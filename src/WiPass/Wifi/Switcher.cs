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
        /// <summary>Stopped because the user picked another place meanwhile.</summary>
        public bool Cancelled;
    }

    /// <summary>
    /// Does what people used to do by hand: forget the saved network, re-add it with
    /// this place's password, connect, and wait for the result.
    /// Runs on a background thread; never touches AppSettings directly.
    /// </summary>
    internal sealed class Switcher
    {
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(20);
        private const int MaxAttemptFailures = 3;

        // Some Wi-Fi drivers (seen: Intel Netwaw18.sys, stop code 0xD1) crash Windows when connect /
        // disconnect / profile changes arrive back to back. Keep a quiet gap between radio operations,
        // and never tear down a connection attempt that has only just started.
        private static readonly TimeSpan QuietGap = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan MinAttemptAge = TimeSpan.FromSeconds(1.5);
        private static readonly TimeSpan DisconnectWait = TimeSpan.FromSeconds(5);
        private const int IF_DISCONNECTING = 3, IF_DISCONNECTED = 4, IF_ASSOCIATING = 5, IF_DISCOVERING = 6, IF_AUTHENTICATING = 7;
        private static DateTime quietUntilUtc = DateTime.MinValue;
        private readonly WifiService wifi;

        public Switcher(WifiService wifi) { this.wifi = wifi; }

        public SwitchResult Run(SwitchRequest req) { return Run(req, CancellationToken.None); }

        /// <summary>
        /// <paramref name="cancel"/> stops the attempt (e.g. the user tapped the wrong place and then the right one):
        /// the wait for Windows ends at once and the half-finished connection is dropped.
        /// </summary>
        public SwitchResult Run(SwitchRequest req, CancellationToken cancel)
        {
            var res = new SwitchResult();
            if (cancel.IsCancellationRequested) return Cancelled(res, req, null, DateTime.MinValue);
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

            if (cancel.IsCancellationRequested) return Cancelled(res, req, null, DateTime.MinValue);

            // Let the adapter rest after the previous operation (rapid taps end up here and are cancelled harmlessly).
            TimeSpan rest = quietUntilUtc - DateTime.UtcNow;
            if (rest > TimeSpan.Zero)
            {
                Log.Info("무선랜 장치 안정 대기 " + (int)rest.TotalMilliseconds + "ms");
                if (cancel.WaitHandle.WaitOne(rest)) return Cancelled(res, req, null, DateTime.MinValue);
            }
            // Windows is in the middle of its own connection attempt: stop it cleanly first.
            int st = InterfaceState(g);
            if (st == IF_ASSOCIATING || st == IF_DISCOVERING || st == IF_AUTHENTICATING || st == IF_DISCONNECTING)
            {
                Log.Info("진행 중인 연결을 먼저 정리");
                if (st != IF_DISCONNECTING) wifi.Disconnect(g);
                WaitDisconnected(g);
                if (cancel.WaitHandle.WaitOne(1000)) { MarkQuiet(); return Cancelled(res, req, null, DateTime.MinValue); }
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

            // 6. Connect and wait for Windows to report the result.
            //    A wrong password makes Windows retry for a long time before it gives up,
            //    so the first security (password) failure ends the wait right away.
            bool completed = false, securityFailed = false;
            uint finalReason = 0, lastFailReason = 0;
            int failCount = 0;
            DateTime started = DateTime.UtcNow;
            MarkQuiet();
            using (var done = new ManualResetEventSlim(false))
            {
                Action<Guid, uint, uint, string> handler = (ig, code, rc, profile) =>
                {
                    if (ig != g) return;
                    if (profile != null && profile != req.Ssid) return;
                    if (code == WlanNative.ACM_CONNECTION_ATTEMPT_FAIL)
                    {
                        lastFailReason = rc;
                        failCount++;
                        if (IsSecurityFailure(rc)) securityFailed = true;
                        if (securityFailed || failCount >= MaxAttemptFailures) done.Set();
                    }
                    else if (code == WlanNative.ACM_CONNECTION_COMPLETE)
                    {
                        finalReason = rc;
                        completed = true;
                        if (IsSecurityFailure(rc)) securityFailed = true;
                        done.Set();
                    }
                };
                wifi.AcmNotification += handler;
                try
                {
                    int cr = wifi.Connect(g, req.Ssid);
                    if (cr != 0) return Fail(res, "접속 요청을 보내지 못했습니다. " + WifiService.ErrorText(cr));
                    try { done.Wait(ConnectTimeout, cancel); }
                    catch (OperationCanceledException) { }
                }
                finally { wifi.AcmNotification -= handler; }
            }
            if (cancel.IsCancellationRequested) return Cancelled(res, req, g, started);
            MarkQuiet();

            CurrentConnection cur = wifi.GetCurrent(g);
            bool connected = cur != null && cur.State == WlanNative.STATE_CONNECTED && cur.ProfileName == req.Ssid;
            bool ok = completed ? (finalReason == 0) : (connected && !securityFailed);
            int seconds = (int)(DateTime.UtcNow - started).TotalSeconds;
            if (!ok)
            {
                // Stop Windows from retrying in the background with the wrong password.
                wifi.Disconnect(g);
                WaitDisconnected(g);
                MarkQuiet();
                uint why = finalReason != 0 ? finalReason : lastFailReason;
                string detail = WifiService.ReasonText(why);
                Log.Warn("접속 실패(" + seconds + "초, " + (securityFailed ? "비밀번호 오류 추정" : completed ? "완료 보고" : "시간 초과") + "): " + detail);
                if (securityFailed)
                    return Fail(res, req.PlaceName + " 비밀번호가 맞지 않습니다. 설정에서 이 장소의 비밀번호를 확인해 주세요.");
                return Fail(res, req.PlaceName + " 비밀번호로 접속하지 못했습니다. 비밀번호가 맞는지, 지금 " + req.PlaceName + "에 있는지 확인해 주세요."
                                 + (detail.Length > 0 ? " (" + detail + ")" : ""));
            }
            Log.Info("접속까지 " + seconds + "초");

            // 7. Remember which access points belong to this place (used for automatic detection)
            if (cancel.WaitHandle.WaitOne(1500)) return Cancelled(res, req, null, DateTime.MinValue); // connected, but another place was picked
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

        /// <summary>
        /// Reason codes in the 802.11 security range (L2_REASON_CODE_DOT11_SECURITY_BASE, 0x40000–0x4FFFF)
        /// mean the key exchange failed — in practice, a wrong password.
        /// </summary>
        private static bool IsSecurityFailure(uint reason)
        {
            return reason >= 0x40000 && reason < 0x50000;
        }

        private SwitchResult Cancelled(SwitchResult res, SwitchRequest req, Guid? disconnect, DateTime connectedAt)
        {
            Log.Info("전환 취소: " + req.PlaceName + " (다른 장소를 골라서)");
            if (disconnect.HasValue)
            {
                // Stop Windows' attempt with this password — but not in the very first moments of
                // the attempt, and wait until the adapter has really let go before the next place.
                TimeSpan age = DateTime.UtcNow - connectedAt;
                if (age < MinAttemptAge) Thread.Sleep(MinAttemptAge - age);
                try { wifi.Disconnect(disconnect.Value); WaitDisconnected(disconnect.Value); } catch { }
                MarkQuiet();
            }
            res.Success = false;
            res.Cancelled = true;
            res.Message = "취소됨";
            return res;
        }

        private static void MarkQuiet() { quietUntilUtc = DateTime.UtcNow + QuietGap; }

        private int InterfaceState(Guid g)
        {
            try
            {
                foreach (var i in wifi.GetInterfaces()) if (i.Id == g) return i.State;
            }
            catch { }
            return -1;
        }

        /// <summary>Waits (up to a few seconds) until the adapter reports it is no longer connected or connecting.</summary>
        private void WaitDisconnected(Guid g)
        {
            DateTime until = DateTime.UtcNow + DisconnectWait;
            while (DateTime.UtcNow < until)
            {
                int st = InterfaceState(g);
                if (st == IF_DISCONNECTED || st == 0 || st == -1) return;
                Thread.Sleep(200);
            }
            Log.Warn("연결 해제 확인 시간 초과");
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
