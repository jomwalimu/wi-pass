using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using WiPass.Core;
using WiPass.Native;
using WiPass.Wifi;

namespace WiPass.UI
{
    /// <summary>The taskbar (notification area) application: status, menu, automatic switching.</summary>
    internal sealed class TrayApp : ApplicationContext
    {
        private static readonly TimeSpan AutoRetryDelay = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan WindowsConnectingGrace = TimeSpan.FromSeconds(20);
        private const int CheckIntervalMs = 15000;

        // WLAN_INTERFACE_STATE values while Windows is in the middle of connecting
        private const int STATE_ASSOCIATING = 5, STATE_DISCOVERING = 6, STATE_AUTHENTICATING = 7;

        private readonly WifiService wifi;
        private readonly Switcher switcher;
        private readonly HotspotService hotspot;
        private readonly Control invoker;
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip menu;
        private readonly Timer checkTimer;
        private readonly Timer evaluateSoon;

        private AppSettings settings;
        private bool busy;
        private bool hotspotBusy;
        private bool settingsOpen;
        private string statusText = "상태 확인 중...";
        private DateTime autoRetryAfter = DateTime.MinValue;
        private DateTime? windowsConnectingSince;
        private DateTime? lastFailureAt;
        private string lastFailureText;
        private bool? hotspotOn;
        private bool locationWarned;
        private bool hotspotWarned;
        private Action balloonClick;

        public TrayApp(WifiService wifi)
        {
            this.wifi = wifi;
            switcher = new Switcher(wifi);
            hotspot = new HotspotService();
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
            tray.BalloonTipClicked += (s, e) => { var a = balloonClick; balloonClick = null; if (a != null) a(); };
            tray.BalloonTipClosed += (s, e) => balloonClick = null;

            wifi.AcmNotification += (itf, code, reason, profile) =>
            {
                if (code == WlanNative.ACM_CONNECTION_COMPLETE || code == WlanNative.ACM_DISCONNECTED ||
                    code == WlanNative.ACM_CONNECTION_ATTEMPT_FAIL || code == WlanNative.ACM_SCAN_COMPLETE)
                    Post(() => { if (!busy) { RefreshStatus(); EvaluateSoon(2500); } });
            };
            SystemEvents.PowerModeChanged += OnPowerModeChanged;

            checkTimer = new Timer { Interval = CheckIntervalMs };
            checkTimer.Tick += (s, e) => { RefreshStatus(); Evaluate(); };
            checkTimer.Start();

            evaluateSoon = new Timer();
            evaluateSoon.Tick += (s, e) => { evaluateSoon.Stop(); Evaluate(); };

            // Keep the startup entry in line with the setting and the current exe location
            // (e.g. after moving from a portable folder to the installed one).
            StartupManager.Apply(settings.StartWithWindows);

            Log.Info("WiPass " + AppInfo.Version + " 시작 (자동 전환 " + OnOff(settings.AutoSwitch) + ", 핫스팟 " + OnOff(settings.AutoHotspot) + ")");
            Post(() =>
            {
                RefreshStatus();
                if (!settings.IsConfigured) OpenSettings(true);
                else EvaluateSoon(1500);
            });
        }

        private static string OnOff(bool b) { return b ? "켜짐" : "꺼짐"; }

        private void Post(Action a)
        {
            try { if (!invoker.IsDisposed) invoker.BeginInvoke(a); } catch { }
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.Resume) return;
            Post(() =>
            {
                Log.Info("절전 모드에서 깨어남");
                autoRetryAfter = DateTime.MinValue;
                EvaluateSoon(5000);
            });
        }

        // ------------------------------------------------------------------ automatic switching

        private void EvaluateSoon(int delayMs)
        {
            evaluateSoon.Stop();
            evaluateSoon.Interval = delayMs;
            evaluateSoon.Start();
        }

        private bool IsSchool(CurrentConnection cur)
        {
            return cur.Ssid == settings.Ssid || cur.ProfileName == settings.Ssid;
        }

        /// <summary>
        /// Decides whether to do anything: runs on a timer and whenever Windows reports
        /// a Wi-Fi change. Never touches another network the user is connected to.
        /// </summary>
        private void Evaluate()
        {
            if (busy || settingsOpen || !settings.IsConfigured) return;
            try
            {
                WifiInterface itf = wifi.GetPrimaryInterface();
                if (itf == null) return;
                CurrentConnection cur = wifi.GetCurrent(itf.Id);

                if (cur != null && cur.State == WlanNative.STATE_CONNECTED)
                {
                    windowsConnectingSince = null;
                    if (IsSchool(cur)) OnSchoolConnected(cur);
                    return; // connected somewhere else (home, phone...): leave it alone
                }

                if (!settings.AutoSwitch) return;

                // Give Windows a chance to finish its own connection attempt first.
                if (cur != null && (cur.State == STATE_ASSOCIATING || cur.State == STATE_DISCOVERING || cur.State == STATE_AUTHENTICATING))
                {
                    if (windowsConnectingSince == null) windowsConnectingSince = DateTime.UtcNow;
                    if (DateTime.UtcNow - windowsConnectingSince.Value < WindowsConnectingGrace) return;
                }
                else windowsConnectingSince = null;

                if (DateTime.UtcNow < autoRetryAfter) return;

                List<string> visible;
                try { visible = wifi.GetBssids(itf.Id, settings.Ssid); }
                catch (LocationDeniedException) { WarnLocation(); return; }
                if (visible.Count == 0) return; // not at any of the places

                List<Place> candidates = settings.RankPlaces(visible);
                if (candidates.Count == 0) return;
                Log.Info("자동 전환 시작: 학교 와이파이 공유기 " + visible.Count + "개 보임, 첫 후보 " + candidates[0].Name);
                StartSwitch(candidates, true);
            }
            catch (Exception ex)
            {
                Log.Warn("자동 점검 오류: " + ex.Message);
            }
        }

        /// <summary>Already connected to the school network: keep "which place" and the hotspot up to date.</summary>
        private void OnSchoolConnected(CurrentConnection cur)
        {
            lastFailureAt = null;
            autoRetryAfter = DateTime.MinValue;
            bool changed = false;
            Place byAp = settings.FindPlaceByBssid(cur.Bssid);
            if (byAp != null)
            {
                if (settings.CurrentPlaceId != byAp.Id) { settings.CurrentPlaceId = byAp.Id; changed = true; }
            }
            else
            {
                // Windows connected with the saved password, so this access point belongs to the current place.
                Place current = settings.FindPlace(settings.CurrentPlaceId);
                if (current != null && cur.Bssid != null)
                {
                    settings.Learn(current, new[] { cur.Bssid });
                    changed = true;
                }
            }
            if (changed) settings.Save();
            EnsureHotspot();
        }

        private void SwitchTo(Place place)
        {
            if (busy) { Balloon("전환 중입니다", "진행 중인 전환이 끝난 뒤 다시 시도해 주세요.", ToolTipIcon.Info); return; }
            if (!place.HasPassword) { OpenSettings(false); return; }
            StartSwitch(new List<Place> { place }, false);
        }

        private sealed class SwitchOutcome
        {
            public SwitchResult Success;
            public SwitchResult LastFailure;
            public string PlaceId;
            public string PlaceName;
            public string NewTemplate;
            public int Tried;
            public bool LocationDenied;
            public bool HotspotTried;
            public string HotspotError;
        }

        /// <summary>Tries the places in order on a background thread until one connects.</summary>
        private void StartSwitch(List<Place> candidates, bool automatic)
        {
            busy = true;
            var requests = new List<SwitchRequest>();
            var ids = new List<string>();
            foreach (var p in candidates)
            {
                requests.Add(new SwitchRequest { Ssid = settings.Ssid, PlaceName = p.Name, ProtectedPassword = p.ProtectedPassword });
                ids.Add(p.Id);
            }
            string template = settings.ProfileTemplate;
            string ssid = settings.Ssid;
            bool withHotspot = settings.AutoHotspot;

            Task.Factory.StartNew(() =>
            {
                var o = new SwitchOutcome();
                string tpl = template;
                for (int i = 0; i < requests.Count; i++)
                {
                    SwitchRequest req = requests[i];
                    string label = (automatic ? "자동 전환: " : "") + req.PlaceName + " 비밀번호로 접속 중...";
                    Post(() => SetState(TrayState.Busy, label));
                    req.Template = tpl;
                    SwitchResult r = switcher.Run(req);
                    o.Tried++;
                    if (r.NewTemplate != null) { tpl = r.NewTemplate; o.NewTemplate = tpl; }
                    if (r.LocationDenied) o.LocationDenied = true;
                    if (r.Success) { o.Success = r; o.PlaceId = ids[i]; o.PlaceName = req.PlaceName; break; }
                    o.LastFailure = r;
                }
                if (o.Success != null && withHotspot)
                {
                    o.HotspotTried = true;
                    o.HotspotError = hotspot.Start(ssid, true);
                }
                return o;
            }).ContinueWith(t => Post(() => FinishSwitch(t, automatic)));
        }

        private void FinishSwitch(Task<SwitchOutcome> t, bool automatic)
        {
            busy = false;
            if (t.IsFaulted)
            {
                Exception ex = t.Exception.GetBaseException();
                Log.Error("전환 중 예외: " + ex);
                Fail("예상하지 못한 오류가 발생했습니다: " + ex.Message, automatic);
                return;
            }

            SwitchOutcome o = t.Result;
            if (o.NewTemplate != null) settings.ProfileTemplate = o.NewTemplate;

            if (o.Success != null)
            {
                Place p = settings.FindPlace(o.PlaceId);
                if (p != null)
                {
                    settings.CurrentPlaceId = p.Id;
                    p.LastUsedUtc = DateTime.UtcNow.ToString("o");
                    settings.Learn(p, o.Success.SeenBssids);
                }
                settings.Save();
                autoRetryAfter = DateTime.MinValue;
                lastFailureAt = null;

                string msg = automatic ? o.PlaceName + " 와이파이로 자동 전환했습니다." : o.Success.Message;
                if (o.HotspotTried)
                {
                    hotspotOn = o.HotspotError == null;
                    msg += o.HotspotError == null ? "\r\n노트북 핫스팟도 켰습니다." : "\r\n핫스팟은 켜지 못했습니다: " + o.HotspotError;
                }
                Balloon("와이파이 연결됨", msg, ToolTipIcon.Info);
                RefreshStatus();
            }
            else
            {
                settings.Save();
                string reason = o.LastFailure != null ? o.LastFailure.Message : "접속하지 못했습니다.";
                if (automatic && o.Tried > 1)
                    reason = "저장된 장소 " + o.Tried + "곳의 비밀번호로 모두 접속하지 못했습니다. 메뉴에서 장소를 직접 고르거나 비밀번호를 확인해 주세요.";
                Fail(reason, automatic);
            }
            if (o.LocationDenied) WarnLocation();
        }

        private void Fail(string message, bool automatic)
        {
            lastFailureAt = DateTime.UtcNow;
            if (automatic)
            {
                autoRetryAfter = DateTime.UtcNow + AutoRetryDelay;
                lastFailureText = "자동 전환 실패 - 메뉴에서 장소 선택";
                Balloon("자동 전환 실패", message + "\r\n(" + (int)AutoRetryDelay.TotalMinutes + "분 뒤 다시 시도합니다)", ToolTipIcon.Warning);
            }
            else
            {
                lastFailureText = "전환 실패 - 메뉴에서 다시 시도";
                Balloon("연결하지 못했습니다", message, ToolTipIcon.Error);
            }
            SetState(TrayState.Failed, lastFailureText);
        }

        private void WarnLocation()
        {
            if (locationWarned) return;
            locationWarned = true;
            Log.Warn("위치 권한이 꺼져 있어 주변 공유기를 확인하지 못함");
            Balloon("위치 권한이 필요합니다",
                "자동 전환을 하려면 설정 > 개인 정보 및 보안 > 위치에서 '위치 서비스'와 '데스크톱 앱이 위치에 액세스하도록 허용'을 켜 주세요.\r\n이 알림을 누르면 설정이 열립니다.",
                ToolTipIcon.Warning, () => OpenUri("ms-settings:privacy-location"));
        }

        // ------------------------------------------------------------------ hotspot

        private void EnsureHotspot()
        {
            if (!settings.AutoHotspot || hotspotBusy || busy || !hotspot.Available) return;
            hotspotBusy = true;
            string ssid = settings.Ssid;
            Task.Factory.StartNew(() =>
            {
                HotspotState st = hotspot.GetState(ssid);
                string err = null;
                if (st == HotspotState.Off) { err = hotspot.Start(ssid, false); if (err == null) st = HotspotState.On; }
                return Tuple.Create(st, err);
            }).ContinueWith(t => Post(() =>
            {
                hotspotBusy = false;
                if (t.IsFaulted) { Log.Warn("핫스팟 확인 오류: " + t.Exception.GetBaseException().Message); return; }
                HotspotState st = t.Result.Item1;
                string err = t.Result.Item2;
                hotspotOn = st == HotspotState.On ? true : st == HotspotState.Off ? false : (bool?)null;
                if (err != null && !hotspotWarned)
                {
                    hotspotWarned = true;
                    Balloon("핫스팟을 켜지 못했습니다", err + "\r\n이 알림을 누르면 윈도우 모바일 핫스팟 설정이 열립니다.",
                        ToolTipIcon.Warning, () => OpenUri("ms-settings:network-mobilehotspot"));
                }
                else if (err == null) hotspotWarned = false;
                RefreshStatus();
            }));
        }

        private void StopHotspot()
        {
            if (!hotspot.Available || string.IsNullOrEmpty(settings.Ssid)) return;
            string ssid = settings.Ssid;
            Task.Factory.StartNew(() => hotspot.Stop(ssid)).ContinueWith(t => Post(() => { hotspotOn = null; RefreshStatus(); }));
        }

        // ------------------------------------------------------------------ menu

        private void BuildMenu()
        {
            menu.Items.Clear();

            menu.Items.Add(new ToolStripMenuItem(statusText) { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());

            if (settings.IsConfigured)
            {
                Place current = settings.FindPlace(settings.CurrentPlaceId);
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

                var auto = new ToolStripMenuItem("도착하면 자동 전환") { Checked = settings.AutoSwitch };
                auto.Click += (s, e) => ToggleAutoSwitch();
                menu.Items.Add(auto);

                var hs = new ToolStripMenuItem("핫스팟 자동 켜기 (스마트폰 연결용)") { Checked = settings.AutoHotspot, Enabled = hotspot.Available };
                hs.Click += (s, e) => ToggleHotspot();
                menu.Items.Add(hs);
                menu.Items.Add(new ToolStripSeparator());
            }

            var settingsItem = new ToolStripMenuItem(settings.IsConfigured ? "설정..." : "처음 설정하기...") { Enabled = !busy };
            settingsItem.Click += (s, e) => OpenSettings(false);
            menu.Items.Add(settingsItem);

            var help = new ToolStripMenuItem("도움말");
            help.DropDownItems.Add(MakeItem("위치 권한 설정 열기", () => OpenUri("ms-settings:privacy-location")));
            help.DropDownItems.Add(MakeItem("모바일 핫스팟 설정 열기", () => OpenUri("ms-settings:network-mobilehotspot")));
            help.DropDownItems.Add(new ToolStripSeparator());
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

        private void ToggleAutoSwitch()
        {
            settings.AutoSwitch = !settings.AutoSwitch;
            settings.Save();
            Log.Info("자동 전환 " + OnOff(settings.AutoSwitch));
            if (settings.AutoSwitch) { autoRetryAfter = DateTime.MinValue; EvaluateSoon(500); }
            RefreshStatus();
        }

        private void ToggleHotspot()
        {
            settings.AutoHotspot = !settings.AutoHotspot;
            settings.Save();
            Log.Info("핫스팟 자동 켜기 " + OnOff(settings.AutoHotspot));
            hotspotWarned = false;
            if (settings.AutoHotspot)
            {
                Balloon("핫스팟 자동 켜기", "학교 와이파이에 연결되면 노트북 핫스팟을 켭니다.\r\n학교·기관의 무선망 보안 지침을 먼저 확인해 주세요.", ToolTipIcon.Info);
                EvaluateSoon(500);
            }
            else StopHotspot();
        }

        // ------------------------------------------------------------------ status

        private void RefreshStatus()
        {
            if (busy) return;
            try
            {
                WifiInterface itf = wifi.GetPrimaryInterface();
                if (itf == null) { SetState(TrayState.Idle, "무선랜 장치 없음"); return; }
                if (!settings.IsConfigured) { SetState(TrayState.Idle, "설정이 필요합니다"); return; }

                CurrentConnection cur = wifi.GetCurrent(itf.Id);
                bool connected = cur != null && cur.State == WlanNative.STATE_CONNECTED;
                if (connected && IsSchool(cur))
                {
                    Place p = settings.FindPlaceByBssid(cur.Bssid) ?? settings.FindPlace(settings.CurrentPlaceId);
                    string text = (p != null ? p.Name : "학교 와이파이") + " 연결됨";
                    if (settings.AutoHotspot && hotspotOn.HasValue) text += " · 핫스팟 " + (hotspotOn.Value ? "켜짐" : "꺼짐");
                    SetState(TrayState.Connected, text);
                }
                else if (lastFailureAt.HasValue && DateTime.UtcNow - lastFailureAt.Value < AutoRetryDelay)
                {
                    SetState(TrayState.Failed, lastFailureText);
                }
                else if (connected)
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

        private void Balloon(string title, string text, ToolTipIcon icon, Action onClick = null)
        {
            balloonClick = onClick;
            tray.ShowBalloonTip(5000, title, text, icon);
        }

        // ------------------------------------------------------------------ dialogs

        private void OpenSettings(bool firstRun)
        {
            if (settingsOpen || busy) return;
            settingsOpen = true;
            bool hotspotBefore = settings.AutoHotspot;
            try
            {
                using (var f = new SettingsForm(wifi, settings, firstRun, hotspot.Available))
                {
                    if (f.ShowDialog() == DialogResult.OK && f.Result != null)
                    {
                        settings = f.Result;
                        settings.Save();
                        StartupManager.Apply(settings.StartWithWindows);
                        Log.Info("설정 저장: 장소 " + settings.Places.Count + "개, 자동 전환 " + OnOff(settings.AutoSwitch) +
                                 ", 자동 실행 " + OnOff(settings.StartWithWindows) + ", 핫스팟 " + OnOff(settings.AutoHotspot));
                        autoRetryAfter = DateTime.MinValue;
                        lastFailureAt = null;
                        hotspotWarned = false;
                        if (hotspotBefore && !settings.AutoHotspot) StopHotspot();
                        if (firstRun)
                            Balloon("설정을 저장했습니다",
                                settings.AutoSwitch
                                    ? "이제 장소에 도착하면 WiPass가 알아서 그 장소 비밀번호로 접속합니다. 아이콘을 눌러 직접 고를 수도 있습니다."
                                    : "작업표시줄의 WiPass 아이콘을 눌러 지금 있는 장소를 고르면 그 비밀번호로 접속합니다.",
                                ToolTipIcon.Info);
                    }
                }
            }
            finally
            {
                settingsOpen = false;
                RefreshStatus();
                EvaluateSoon(1500);
            }
        }

        private void SaveDiagnostics()
        {
            string report;
            try { report = Diagnostics.Build(wifi, settings, hotspot.Available); }
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
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            checkTimer.Stop();
            evaluateSoon.Stop();
            tray.Visible = false;
            tray.Dispose();
            invoker.Dispose();
            ExitThread();
        }
    }
}
