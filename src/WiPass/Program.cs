using System;
using System.Threading;
using System.Windows.Forms;
using WiPass.Core;
using WiPass.UI;
using WiPass.Wifi;

namespace WiPass
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool createdNew;
            using (var mutex = new Mutex(true, @"Local\WiPass.SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    // Already running: hand this launch over (shows the quick panel / runs a notification button).
                    string[] all = Environment.GetCommandLineArgs();
                    string args = all.Length > 1 ? string.Join(" ", all, 1, all.Length - 1) : "";
                    if (SingleInstance.SendToRunning(args) || args.Contains("id=")) return;
                    if (AppPackage.IsPackaged && AppPackage.LegacyInstallPresent)
                    {
                        MessageBox.Show("예전에 설치한 와이패스(GitHub 설치판)가 실행 중입니다.\r\n\r\n" +
                                        "작업표시줄의 와이패스 아이콘을 오른쪽 클릭 → [종료]한 다음,\r\n" +
                                        "설정 > 앱에서 '와이패스 (WiPass)'를 제거하고 다시 실행해 주세요.\r\n" +
                                        "(데이터 삭제를 물으면 '아니요'를 고르면 장소와 비밀번호가 그대로 남습니다)",
                            AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    MessageBox.Show("WiPass가 이미 실행 중입니다.\r\n작업표시줄 오른쪽(시계 옆, ^ 안쪽)의 와이파이 아이콘을 확인해 주세요.",
                        AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // Same app ID as the Start menu shortcut, so Windows shows WiPass toasts with its icon.
                if (!AppPackage.IsPackaged) Notifier.RegisterProcessAppId(); // the Store package has its own app ID
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) => Log.Error("처리되지 않은 오류: " + e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Error("처리되지 않은 오류: " + e.ExceptionObject);

                if (StartupManager.RunningFromTemp)
                {
                    MessageBox.Show("압축 파일 안에서 바로 실행하신 것 같습니다.\r\n" +
                                    "이대로도 쓸 수 있지만, '윈도우 시작 시 자동 실행'이 되려면\r\n" +
                                    "압축을 원하는 폴더에 푼 다음 그 폴더의 WiPass.exe를 실행해 주세요.",
                        AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                WifiService wifi;
                try
                {
                    wifi = new WifiService();
                }
                catch (Exception ex)
                {
                    Log.Error("무선랜 서비스 연결 실패: " + ex.Message);
                    MessageBox.Show("무선랜 서비스에 연결하지 못했습니다.\r\n이 PC에 무선랜(와이파이) 장치가 있는지, 와이파이가 켜져 있는지 확인해 주세요.\r\n\r\n" + ex.Message,
                        AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                using (wifi)
                {
                    Application.Run(new TrayApp(wifi));
                }
            }
        }
    }
}
