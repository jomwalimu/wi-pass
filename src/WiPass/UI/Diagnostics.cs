using System;
using System.Text;
using Microsoft.Win32;
using WiPass.Core;
using WiPass.Native;
using WiPass.Wifi;

namespace WiPass.UI
{
    /// <summary>
    /// Builds a text report for bug reports. Contains no passwords and only partial
    /// access-point addresses. Place names can appear in the log section.
    /// </summary>
    internal static class Diagnostics
    {
        public static string Build(WifiService wifi, AppSettings settings)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== WiPass 진단 정보 ===");
            sb.AppendLine("만든 시각: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("WiPass 버전: " + AppInfo.Version);
            sb.AppendLine("윈도우: " + WindowsVersion());
            sb.AppendLine("64비트: " + (Environment.Is64BitOperatingSystem ? "예" : "아니오") + " / 관리자 권한 실행: " + (IsAdmin() ? "예" : "아니오"));
            sb.AppendLine();

            sb.AppendLine("[설정 요약]");
            sb.AppendLine("와이파이 이름 설정됨: " + (!string.IsNullOrEmpty(settings.Ssid) ? "예 (" + settings.Ssid.Length + "자)" : "아니오"));
            sb.AppendLine("장소 수: " + settings.Places.Count);
            int i = 1;
            foreach (var p in settings.Places)
                sb.AppendLine("  장소" + (i++) + ": 비밀번호 " + (p.HasPassword ? "있음" : "없음") + ", 기억한 공유기 " + p.Bssids.Count + "개" +
                              (p.Id == settings.CurrentPlaceId ? " (최근 연결)" : ""));
            sb.AppendLine("보안 방식 템플릿: " + (string.IsNullOrEmpty(settings.ProfileTemplate) ? "없음" : "있음"));
            sb.AppendLine();

            sb.AppendLine("[무선랜]");
            try
            {
                var itfs = wifi.GetInterfaces();
                sb.AppendLine("장치 수: " + itfs.Count);
                foreach (var itf in itfs)
                {
                    sb.AppendLine("  장치: " + itf.Description + " / 상태 코드 " + itf.State);
                    CurrentConnection cur = wifi.GetCurrent(itf.Id);
                    if (cur == null) sb.AppendLine("  현재 연결: 없음 또는 읽을 수 없음");
                    else
                    {
                        bool same = !string.IsNullOrEmpty(settings.Ssid) && cur.Ssid == settings.Ssid;
                        sb.AppendLine("  현재 연결: " + (same ? "학교 와이파이" : "다른 와이파이") + " / 프로필 이름 일치 " +
                                      (cur.ProfileName == settings.Ssid ? "예" : "아니오") + " / 공유기 " + Mask(cur.Bssid));
                    }

                    try
                    {
                        var nets = wifi.GetVisibleNetworks(itf.Id);
                        sb.AppendLine("  위치 권한: 허용됨 (주변 와이파이 " + nets.Count + "개)");
                        foreach (var n in nets)
                            if (n.Ssid == settings.Ssid)
                            {
                                sb.AppendLine("  학교 와이파이 보임: 보안 " + ProfileBuilder.Describe(n.Auth) + ", 암호화 코드 " + n.Cipher + ", 신호 " + n.Signal + "%");
                                break;
                            }
                        if (!string.IsNullOrEmpty(settings.Ssid))
                            sb.AppendLine("  학교 와이파이 공유기 보임: " + wifi.GetBssids(itf.Id, settings.Ssid).Count + "개");
                    }
                    catch (LocationDeniedException)
                    {
                        sb.AppendLine("  위치 권한: 거부됨 → 설정 > 개인 정보 및 보안 > 위치 확인 필요");
                    }

                    if (!string.IsNullOrEmpty(settings.Ssid))
                    {
                        var saved = wifi.FindProfilesBySsid(itf.Id, settings.Ssid);
                        sb.AppendLine("  같은 이름의 저장된 와이파이 설정: " + saved.Count + "개");
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("무선랜 정보 오류: " + ex.Message);
            }
            sb.AppendLine();

            sb.AppendLine("[최근 기록]");
            foreach (string line in Log.Tail(300)) sb.AppendLine(line);
            return sb.ToString();
        }

        private static string Mask(string mac)
        {
            if (string.IsNullOrEmpty(mac) || mac.Length < 8) return "알 수 없음";
            return mac.Substring(0, 8) + ":**:**:**";
        }

        private static string WindowsVersion()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (k == null) return Environment.OSVersion.VersionString;
                    string build = Convert.ToString(k.GetValue("CurrentBuild"));
                    string ubr = Convert.ToString(k.GetValue("UBR"));
                    string disp = Convert.ToString(k.GetValue("DisplayVersion"));
                    int b;
                    string name = int.TryParse(build, out b) && b >= 22000 ? "Windows 11" : "Windows 10";
                    return name + " " + disp + " (빌드 " + build + (ubr.Length > 0 ? "." + ubr : "") + ")";
                }
            }
            catch
            {
                return Environment.OSVersion.VersionString;
            }
        }

        private static bool IsAdmin()
        {
            try
            {
                var id = System.Security.Principal.WindowsIdentity.GetCurrent();
                return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }
}
