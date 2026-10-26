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
                    MessageBox.Show("WiPass가 이미 실행 중입니다.\r\n작업표시줄 오른쪽(시계 옆, ^ 안쪽)의 와이파이 아이콘을 확인해 주세요.",
                        AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) => Log.Error("처리되지 않은 오류: " + e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Error("처리되지 않은 오류: " + e.ExceptionObject);

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
