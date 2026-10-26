using System;
using System.Collections.Generic;
using System.Threading;
using WiPass.Core;
using WiPass.Native;

namespace WiPass.Wifi
{
    internal sealed class SwitchRequest
    {
        public string Ssid;
        public string PlaceName;
        public string ProtectedPassword;
        public string Template;
    }

    internal sealed class SwitchResult
    {
        public bool Success;
        public string Message;
        public string NewTemplate;
        public List<string> SeenBssids = new List<string>();
        public bool LocationDenied;
    }

    /// <summary>
    /// Does what people used to do by hand: forget the saved network, re-add it with
    /// this place's password, connect, and wait for the result.
    /// Runs on a background thread; never touches AppSettings directly.
    /// </summary>
    internal sealed class Switcher
    {
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(25);
        private readonly WifiService wifi;

        public Switcher(WifiService wifi) { this.wifi = wifi; }

        public SwitchResult Run(SwitchRequest req)
        {
            var res = new SwitchResult();
            Log.Info("전환 시작: " + req.PlaceName);

            WifiInterface itf;
            try { itf = wifi.GetPrimaryInterface(); }
            catch (Exception ex) { return Fail(res, ex.Message); }
            if (itf == null) return Fail(res, "무선랜 장치를 찾지 못했습니다. 와이파이가 켜져 있는지 확인해 주세요.");
            Guid g = itf.Id;

            string password = SecretStore.Unprotect(req.ProtectedPassword);
            if (string.IsNullOrEmpty(password))
                return Fail(res, "저장된 비밀번호를 읽지 못했습니다. 설정에서 이 장소의 비밀번호를 다시 입력해 주세요.");

            // 1. Saved profiles for this network name (there may be more than one, e.g. "이름 2")
            List<SavedProfile> existing;
            try { existing = wifi.FindProfilesBySsid(g, req.Ssid); }
            catch (Exception ex) { return Fail(res, ex.Message); }
            Log.Info("같은 이름의 저장된 와이파이 설정 " + existing.Count + "개");

            // 2. Template: copy the security settings Windows already uses for this network
            string template = req.Template;
            if (string.IsNullOrEmpty(template))
            {
                foreach (var p in existing)
                {
                    string t = ProfileBuilder.MakeTemplate(p.Xml);
                    if (t != null) { template = t; res.NewTemplate = t; Log.Info("기존 와이파이 설정의 보안 방식을 템플릿으로 저장"); break; }
                }
            }

            // 3. Build the new profile
            string xml = null;
            if (!string.IsNullOrEmpty(template)) xml = ProfileBuilder.FromTemplate(template, req.Ssid, password);
            if (xml == null)
            {
                VisibleNetwork seen = null;
                try
                {
                    foreach (var n in wifi.GetVisibleNetworks(g))
                        if (n.Ssid == req.Ssid) { seen = n; break; }
                }
                catch (LocationDeniedException) { res.LocationDenied = true; }
                catch (Exception ex) { Log.Warn(ex.Message); }

                int auth = 7, cipher = 4; // WPA2-Personal / AES by default
                if (seen != null)
                {
                    SecurityKind kind = ProfileBuilder.Classify(seen.Auth);
                    if (kind == SecurityKind.Enterprise)
                        return Fail(res, "이 와이파이는 아이디와 비밀번호를 함께 쓰는 방식(" + ProfileBuilder.Describe(seen.Auth) + ")이라 WiPass가 지원하지 않습니다.");
                    if (kind == SecurityKind.Open)
                        return Fail(res, "이 와이파이는 비밀번호가 없는 방식이라 WiPass가 필요하지 않습니다.");
                    auth = seen.Auth;
                    cipher = seen.Cipher;
                    Log.Info("보안 방식: " + ProfileBuilder.Describe(auth));
                }
                else
                {
                    Log.Info("보안 방식을 확인하지 못해 WPA2-개인으로 시도");
                }
                xml = ProfileBuilder.Create(req.Ssid, auth, cipher, password);
            }

            // 4. Forget the old setting(s)
            foreach (var p in existing)
            {
                int r = wifi.DeleteProfile(g, p.Name);
                if (r == 0) Log.Info("기존 설정 삭제: " + p.Name);
                else if (r != WlanNative.ERROR_NOT_FOUND) Log.Warn("기존 설정 삭제 실패(" + p.Name + "): " + WifiService.ErrorText(r));
            }

            // 5. Register with this place's password
            uint reason;
            int sr = wifi.SetProfile(g, xml, false, out reason);
            if (sr == WlanNative.ERROR_ACCESS_DENIED)
            {
                Log.Info("모든 사용자용 등록 권한이 없어 현재 사용자용으로 등록");
                sr = wifi.SetProfile(g, xml, true, out reason);
            }
            if (sr != 0)
            {
                Log.Warn("설정 등록 실패: " + WifiService.ErrorText(sr) + " / " + WifiService.ReasonText(reason));
                return Fail(res, "와이파이 설정을 등록하지 못했습니다. " + WifiService.ReasonText(reason));
            }
            Log.Info("새 설정 등록 완료");

            // 6. Connect and wait for Windows to report the result
            bool completed = false;
            uint finalReason = 0, lastFailReason = 0;
            using (var done = new ManualResetEventSlim(false))
            {
                Action<Guid, uint, uint, string> handler = (ig, code, rc, profile) =>
                {
                    if (ig != g) return;
                    if (profile != null && profile != req.Ssid) return;
                    if (code == WlanNative.ACM_CONNECTION_ATTEMPT_FAIL) lastFailReason = rc;
                    else if (code == WlanNative.ACM_CONNECTION_COMPLETE) { finalReason = rc; completed = true; done.Set(); }
                };
                wifi.AcmNotification += handler;
                try
                {
                    int cr = wifi.Connect(g, req.Ssid);
                    if (cr != 0) return Fail(res, "접속 요청을 보내지 못했습니다. " + WifiService.ErrorText(cr));
                    done.Wait(ConnectTimeout);
                }
                finally { wifi.AcmNotification -= handler; }
            }

            CurrentConnection cur = wifi.GetCurrent(g);
            bool connected = cur != null && cur.State == WlanNative.STATE_CONNECTED && cur.ProfileName == req.Ssid;
            bool ok = completed ? (finalReason == 0) : connected;
            if (!ok)
            {
                uint why = finalReason != 0 ? finalReason : lastFailReason;
                Log.Warn("접속 실패: " + (completed ? "" : "시간 초과 ") + WifiService.ReasonText(why));
                string detail = WifiService.ReasonText(why);
                return Fail(res, req.PlaceName + " 비밀번호로 접속하지 못했습니다. 비밀번호가 맞는지, 지금 " + req.PlaceName + "에 있는지 확인해 주세요."
                                 + (detail.Length > 0 ? " (" + detail + ")" : ""));
            }

            // 7. Remember which access points belong to this place (used for automatic detection)
            Thread.Sleep(1500);
            cur = wifi.GetCurrent(g);
            if (cur != null && cur.Bssid != null) res.SeenBssids.Add(cur.Bssid);
            try
            {
                foreach (string b in wifi.GetBssids(g, req.Ssid))
                    if (!res.SeenBssids.Contains(b)) res.SeenBssids.Add(b);
            }
            catch (LocationDeniedException) { res.LocationDenied = true; }
            catch (Exception ex) { Log.Warn(ex.Message); }

            Log.Info("접속 성공: " + req.PlaceName + " (공유기 " + res.SeenBssids.Count + "개 확인)");
            res.Success = true;
            res.Message = req.PlaceName + " 와이파이에 연결했습니다.";
            return res;
        }

        private static SwitchResult Fail(SwitchResult res, string message)
        {
            Log.Warn("실패: " + message);
            res.Success = false;
            res.Message = message;
            return res;
        }
    }
}
