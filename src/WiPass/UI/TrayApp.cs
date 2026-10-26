using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using WiPass.Core;
using WiPass.Native;
using WiPass.Wifi;

namespace WiPass.UI
{
    /// <summary>The taskbar (notification area) application.</summary>
    internal sealed class TrayApp : ApplicationContext
    {
        private readonly WifiService wifi;
        private readonly Switcher switcher;
        private readonly Control invoker;
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip menu;
        private readonly Timer statusTimer;

        private AppSettings settings;
        private bool busy;
        private bool settingsOpen;
        private string statusText = "상태 확인 중...";
        private bool locationWarned;

        public TrayApp(WifiService wifi)
        {
            this.wifi = wifi;
            switcher = new Switcher(wifi);
            settings = AppSettings.Load();

            invoker = new Control();
            invoker.CreateControl();
            IntPtr force = invoker.Handle; // make sure BeginInvoke works before the message loop runs

            menu = new ContextMenuStrip { Font = new System.Drawing.Font("Malgun Gothic", 9.5f) };
            menu.Opening += (s, e) => { BuildMenu(); e.Cancel = false; };

            tray = new NotifyIcon
            {
                Icon = IconFactory.Get(TrayState.Idle),
                Text = AppInfo.Name,
                ContextMenuStrip = menu,
                Visible = true
            };
            tray.MouseUp += (s, e) => { if (e.Button == MouseButtons.Left) ShowMenuAtCursor(); };

            wifi.AcmNotification += (itf, code, reason, profile) =>
            {
                if (code == WlanNative.ACM_CONNECTION_COMPLETE || code == WlanNative.ACM_DISCONNECTED)
                    Post(RefreshStatus);
            };

            statusTimer = new Timer { Interval = 5000 };
            statusTimer.Tick += (s, e) => RefreshStatus();
            statusTimer.Start();

            Log.Info("WiPass " + AppInfo.Version + " 시작");
            Post(() =>
            {
                RefreshStatus();
                if (!settings.IsConfigured) OpenSettings(true);
            });
        }

        private void Post(Action a)
        {
            try { if (!invoker.IsDisposed) invoker.BeginInvoke(a); } catch { }
        }

        // ------------------------------------------------------------------ menu

        private void BuildMenu()
        {
            menu.Items.Clear();

            var status = new ToolStripMenuItem(statusText) { Enabled = false };
            menu.Items.Add(status);
            menu.Items.Add(new ToolStripSeparator());

            if (settings.IsConfigured)
            {
                Place current = CurrentPlace();
                foreach (var p in settings.Places)
                {
                    Place place = p;
                    var item = new ToolStripMenuItem(place.Name + "(으)로 전환")
                    {
                        Checked = current != null && current.Id == place.Id,
                        Enabled = !busy
                    };
                    item.Click += (s, e) => SwitchTo(place);
                    menu.Items.Add(item);
                }
                menu.Items.Add(new ToolStripSeparator());
            }

            var settingsItem = new ToolStripMenuItem(settings.IsConfigured ? "장소·비밀번호 설정..." : "처음 설정하기...") { Enabled = !busy };
            settingsItem.Click += (s, e) => OpenSettings(false);
            menu.Items.Add(settingsItem);

            var help = new ToolStripMenuItem("도움말");
            help.DropDownItems.Add(MakeItem("위치 권한 설정 열기", () => OpenUri("ms-settings:privacy-location")));
            help.DropDownItems.Add(MakeItem("문의용 진단 정보 저장...", SaveDiagnostics));
            help.DropDownItems.Add(MakeItem("기록 폴더 열기", () => { Directory.CreateDirectory(AppPaths.LogDir); OpenUri(AppPaths.LogDir); }));
            help.DropDownItems.Add(MakeItem("문의하기 (GitHub)", () => OpenUri(AppInfo.IssuesUrl)));
            help.DropDownItems.Add(new ToolStripSeparator());
            help.DropDownItems.Add(MakeItem(AppInfo.Name + " 정보", ShowAbout));
            menu.Items.Add(help);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(MakeItem("종료", Quit));
        }

        private static ToolStripMenuItem MakeItem(string text, Action onClick)
        {
            var item = new ToolStripMenuItem(text);
            item.Click += (s, e) => onClick();
            return item;
        }

        private void ShowMenuAtCursor()
        {
            // NotifyIcon only opens its menu on right click; reuse its internal method for left click.
            MethodInfo mi = typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);
            if (mi != null) mi.Invoke(tray, null);
        }

        // ------------------------------------------------------------------ status

        private Place CurrentPlace()
        {
            return settings.FindPlace(settings.CurrentPlaceId);
        }

        private void RefreshStatus()
        {
            if (busy) return;
            try
            {
                WifiInterface itf = wifi.GetPrimaryInterface();
                if (itf == null) { SetState(TrayState.Idle, "무선랜 장치 없음"); return; }
                if (!settings.IsConfigured) { SetState(TrayState.Idle, "설정이 필요합니다"); return; }

                CurrentConnection cur = wifi.GetCurrent(itf.Id);
                bool onSchool = cur != null && cur.State == WlanNative.STATE_CONNECTED &&
                                (cur.Ssid == settings.Ssid || cur.ProfileName == settings.Ssid);
                if (onSchool)
                {
                    Place p = settings.FindPlaceByBssid(cur.Bssid) ?? CurrentPlace();
                    if (cur.Ssid.Length == 0 && !locationWarned)
                    {
                        locationWarned = true;
                        Balloon("위치 권한이 필요합니다", "와이파이 정보를 읽으려면 설정 > 개인 정보 및 보안 > 위치에서 위치 서비스와 '데스크톱 앱이 위치에 액세스하도록 허용'을 켜 주세요.", ToolTipIcon.Warning);
                    }
                    SetState(TrayState.Connected, (p != null ? p.Name : "학교 와이파이") + " 연결됨");
                }
                else if (cur != null && cur.State == WlanNative.STATE_CONNECTED)
                {
                    SetState(TrayState.Idle, "다른 와이파이 사용 중" + (cur.Ssid.Length > 0 ? " (" + cur.Ssid + ")" : ""));
                }
                else
                {
                    SetState(TrayState.Idle, "학교 와이파이 연결 안 됨");
                }
            }
            catch (Exception ex)
            {
                Log.Warn("상태 확인 오류: " + ex.Message);
            }
        }

        private void SetState(TrayState state, string text)
        {
            statusText = text;
            tray.Icon = IconFactory.Get(state);
            string tip = AppInfo.Name + ": " + text;
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        }

        private void Balloon(string title, string text, ToolTipIcon icon)
        {
            tray.ShowBalloonTip(5000, title, text, icon);
        }

        // ------------------------------------------------------------------ switching

        private void SwitchTo(Place place)
        {
            if (busy) { Balloon("전환 중입니다", "진행 중인 전환이 끝난 뒤 다시 시도해 주세요.", ToolTipIcon.Info); return; }
            if (!place.HasPassword) { OpenSettings(false); return; }

            busy = true;
            SetState(TrayState.Busy, place.Name + " 비밀번호로 접속 중...");
            var req = new SwitchRequest
            {
                Ssid = settings.Ssid,
                PlaceName = place.Name,
                ProtectedPassword = place.ProtectedPassword,
                Template = settings.ProfileTemplate
            };
            string placeId = place.Id;

            Task.Factory.StartNew(() => switcher.Run(req)).ContinueWith(t => Post(() =>
            {
                busy = false;
                SwitchResult r;
                if (t.IsFaulted)
                {
                    Exception ex = t.Exception.GetBaseException();
                    Log.Error("전환 중 예외: " + ex);
                    r = new SwitchResult { Success = false, Message = "예상하지 못한 오류가 발생했습니다: " + ex.Message };
                }
                else r = t.Result;

                if (r.NewTemplate != null) settings.ProfileTemplate = r.NewTemplate;
                Place p = settings.FindPlace(placeId);
                if (r.Success && p != null)
                {
                    settings.CurrentPlaceId = p.Id;
                    p.LastUsedUtc = DateTime.UtcNow.ToString("o");
                    settings.Learn(p, r.SeenBssids);
                }
                settings.Save();

                if (r.Success)
                {
                    Balloon("와이파이 연결됨", r.Message, ToolTipIcon.Info);
                    SetState(TrayState.Connected, (p != null ? p.Name : place.Name) + " 연결됨");
                }
                else
                {
                    Balloon("연결하지 못했습니다", r.Message, ToolTipIcon.Error);
                    SetState(TrayState.Failed, "전환 실패 - 메뉴에서 다시 시도");
                }
                if (r.LocationDenied && !locationWarned)
                {
                    locationWarned = true;
                    Log.Warn("위치 권한이 꺼져 있어 공유기 정보를 기억하지 못함");
                }
            }));
        }

        // ------------------------------------------------------------------ dialogs

        private void OpenSettings(bool firstRun)
        {
            if (settingsOpen || busy) return;
            settingsOpen = true;
            try
            {
                using (var f = new SettingsForm(wifi, settings, firstRun))
                {
                    if (f.ShowDialog() == DialogResult.OK && f.Result != null)
                    {
                        settings = f.Result;
                        settings.Save();
                        Log.Info("설정 저장: 장소 " + settings.Places.Count + "개");
                        if (firstRun)
                            Balloon("설정을 저장했습니다", "작업표시줄의 WiPass 아이콘을 눌러 지금 있는 장소를 고르면 그 비밀번호로 접속합니다.", ToolTipIcon.Info);
                    }
                }
            }
            finally
            {
                settingsOpen = false;
                RefreshStatus();
            }
        }

        private void SaveDiagnostics()
        {
            string report;
            try { report = Diagnostics.Build(wifi, settings); }
            catch (Exception ex) { report = "진단 정보 생성 실패: " + ex; }

            using (var dlg = new SaveFileDialog
            {
                Title = "문의용 진단 정보 저장",
                Filter = "텍스트 파일 (*.txt)|*.txt",
                FileName = "WiPass-진단-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".txt",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                File.WriteAllText(dlg.FileName, report, new UTF8Encoding(true));
                MessageBox.Show("저장했습니다.\r\n\r\n비밀번호는 들어 있지 않습니다. 기록에 장소 이름이 나올 수 있으니, 올리기 전에 한 번 열어 확인해 주세요.\r\n" +
                                "GitHub 문의(Issues)에 이 파일을 첨부해 주시면 원인을 찾는 데 큰 도움이 됩니다.",
                                AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                try { Process.Start("explorer.exe", "/select,\"" + dlg.FileName + "\""); } catch { }
            }
        }

        private void ShowAbout()
        {
            MessageBox.Show(
                AppInfo.DisplayName + "  " + AppInfo.Version + "\r\n" +
                AppInfo.Tagline + "\r\n\r\n" +
                "제작: " + AppInfo.Author + " (" + AppInfo.AuthorNote + ")\r\n" +
                AppInfo.RepoUrl + "\r\n\r\n" +
                "MIT 라이선스로 무료 배포되는 프로그램이며, 어떠한 보증도 하지 않습니다.\r\n" +
                "사용에 따른 책임은 사용자에게 있습니다.\r\n" +
                "WiPass는 인터넷으로 아무 정보도 보내지 않습니다.",
                AppInfo.Name + " 정보", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static void OpenUri(string uri)
        {
            try { Process.Start(uri); }
            catch (Exception ex) { Log.Warn("열기 실패(" + uri + "): " + ex.Message); }
        }

        private void Quit()
        {
            Log.Info("WiPass 종료");
            statusTimer.Stop();
            tray.Visible = false;
            tray.Dispose();
            invoker.Dispose();
            ExitThread();
        }
    }
}
