using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
    internal sealed class TrayApp : ApplicationContext, ISettingsHost
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
        private bool wizardOpen;
        private string statusText = "상태 확인 중...";
        private TrayState currentState = TrayState.Idle;
        private DateTime autoRetryAfter = DateTime.MinValue;
        private DateTime? windowsConnectingSince;
        private DateTime? lastFailureAt;
        private string lastFailureText, lastFailureSub;
        private string connectedPlaceId;
        private bool? hotspotOn;
        private bool locationWarned;
        private bool hotspotWarned;
        private Notifier notifier;
        private FlyoutForm flyout;
        private SettingsWindow settingsWindow;
        private string flyoutSignature;
        private string flyTitle = "상태 확인 중", flySub = "";
        private HotspotInfo hotspotInfo;
        private bool hotspotInfoLoading;
        private string lastSwitchText;

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
            menu.Opened += (s, e) => WinTheme.RoundCorners(menu.Handle, false);

            tray = new NotifyIcon
            {
                Icon = IconFactory.Get(TrayState.Idle),
                Text = AppInfo.Name,
                ContextMenuStrip = menu,
                Visible = true
            };
            tray.MouseUp += (s, e) => { if (e.Button == MouseButtons.Left) ToggleFlyout(); };
            notifier = new Notifier(tray, Post);

            wifi.AcmNotification += (itf, code, reason, profile) =>
            {
                if (code == WlanNative.ACM_CONNECTION_COMPLETE || code == WlanNative.ACM_DISCONNECTED ||
                    code == WlanNative.ACM_CONNECTION_ATTEMPT_FAIL || code == WlanNative.ACM_SCAN_COMPLETE)
                    Post(() => { if (!busy) { RefreshStatus(); EvaluateSoon(2500); } });
            };
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.UserPreferenceChanged += OnThemeChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;

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
                if (!settings.IsConfigured) OpenSettings();
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
            if (busy || wizardOpen || !settings.IsConfigured) return;
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
            if (busy)
            {
                // The flyout already shows "연결 중"; a toast would only cover it.
                if (!FlyoutVisible) Notify("전환 중입니다", "진행 중인 전환이 끝난 뒤 다시 시도해 주세요.", NoticeKind.Info);
                return;
            }
            if (!place.HasPassword) { OpenSettings(); return; }
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
                    string busyTitle = Josa.WithRo(req.PlaceName) + " 연결 중";
                    string busySub = automatic ? "도착한 장소를 확인해 자동으로 전환하고 있어요" : "저장된 비밀번호로 연결하고 있어요";
                    Post(() => SetState(TrayState.Busy, label, busyTitle, busySub));
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

                // Short lines: the place goes in the title so the message does not wrap mid-word.
                string msg = automatic ? "장소에 맞게 자동으로 전환했습니다." : "와이파이에 연결했습니다.";
                if (o.HotspotTried)
                {
                    hotspotOn = o.HotspotError == null;
                    hotspotInfo = null;
                    msg += o.HotspotError == null ? "\r\n노트북 핫스팟도 켰습니다." : "\r\n핫스팟은 켜지 못했습니다: " + o.HotspotError;
                }
                lastSwitchText = DateTime.Now.ToString("HH:mm") + (automatic ? " 자동 전환" : " 직접 전환") + " · " + o.PlaceName;
                Notify(o.PlaceName + " 연결됨", msg, NoticeKind.Info);
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
                Notify("자동 전환 실패", message + "\r\n" + (int)AutoRetryDelay.TotalMinutes + "분 뒤 다시 시도합니다.", NoticeKind.Warning,
                    OpenFlyout, PickPlaceButton(), SettingsButton("비밀번호 확인"));
            }
            else
            {
                lastFailureText = "전환 실패 - 메뉴에서 다시 시도";
                Notify("연결하지 못했습니다", message, NoticeKind.Error, OpenFlyout, PickPlaceButton(), SettingsButton("비밀번호 수정"));
            }
            lastFailureSub = automatic ? (int)AutoRetryDelay.TotalMinutes + "분 뒤 다시 시도해요 · 아래에서 장소를 직접 고를 수 있어요" : "장소를 다시 고르거나 설정에서 비밀번호를 확인하세요";
            SetState(TrayState.Failed, lastFailureText, "연결하지 못했어요", lastFailureSub);
        }

        private void WarnLocation()
        {
            if (locationWarned) return;
            locationWarned = true;
            Log.Warn("위치 권한이 꺼져 있어 주변 공유기를 확인하지 못함");
            Action openLocation = () => OpenUri("ms-settings:privacy-location");
            Notify("위치 권한이 필요합니다",
                "자동 전환을 하려면 윈도우 위치 권한을 켜 주세요.\r\n위치 서비스와 '데스크톱 앱이 위치에 액세스하도록 허용'을 켜면 됩니다.",
                NoticeKind.Warning, openLocation, new NoticeButton("위치 설정 열기", openLocation));
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
                if (flyout != null && flyout.Visible) LoadHotspotInfo();
                if (err != null && !hotspotWarned)
                {
                    hotspotWarned = true;
                    Action openHotspot = () => OpenUri("ms-settings:network-mobilehotspot");
                    Notify("핫스팟을 켜지 못했습니다", err + "\r\n윈도우 모바일 핫스팟 설정에서 직접 켜 보세요.",
                        NoticeKind.Warning, openHotspot, new NoticeButton("핫스팟 설정 열기", openHotspot));
                }
                else if (err == null) hotspotWarned = false;
                RefreshStatus();
            }));
        }

        private void StopHotspot()
        {
            if (!hotspot.Available || string.IsNullOrEmpty(settings.Ssid)) return;
            string ssid = settings.Ssid;
            Task.Factory.StartNew(() => hotspot.Stop(ssid)).ContinueWith(t => Post(() => { hotspotOn = null; hotspotInfo = null; RefreshStatus(); }));
        }

        // ------------------------------------------------------------------ menu

        private void BuildMenu()
        {
            MenuPalette palette = MenuPalette.Current();
            var renderer = new ModernMenuRenderer(palette);
            menu.Renderer = renderer;
            menu.BackColor = palette.Background;
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = true;
            menu.Padding = new Padding(0, 4, 0, 4);
            menu.Items.Clear();

            // Header: where we are, then a quiet summary of the options.
            menu.Items.Add(new ToolStripLabel(statusText)
            {
                Font = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold),
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 6, 0, 0)
            });
            menu.Items.Add(new ToolStripLabel(StatusSummary())
            {
                Tag = "subtle",
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 0, 0, 6)
            });
            menu.Items.Add(new ToolStripSeparator());

            if (settings.IsConfigured)
            {
                Place current = settings.FindPlace(settings.CurrentPlaceId);
                foreach (var p in settings.Places)
                {
                    Place place = p;
                    var item = Styled(new ToolStripMenuItem(Josa.WithRo(place.Name) + " 전환")
                    {
                        Checked = current != null && current.Id == place.Id,
                        Enabled = !busy
                    });
                    item.Click += (s, e) => SwitchTo(place);
                    menu.Items.Add(item);
                }
                menu.Items.Add(new ToolStripSeparator());

                var auto = Styled(new ToolStripMenuItem("도착하면 자동 전환") { Checked = settings.AutoSwitch });
                auto.Click += (s, e) => SetAutoSwitch(!settings.AutoSwitch);
                menu.Items.Add(auto);

                var hs = Styled(new ToolStripMenuItem("핫스팟 자동 켜기 (스마트폰 연결용)") { Checked = settings.AutoHotspot, Enabled = hotspot.Available });
                hs.Click += (s, e) => SetAutoHotspot(!settings.AutoHotspot);
                menu.Items.Add(hs);
                menu.Items.Add(new ToolStripSeparator());
            }

            var settingsItem = Styled(new ToolStripMenuItem(settings.IsConfigured ? "설정..." : "처음 설정하기..."));
            settingsItem.Click += (s, e) => OpenSettings();
            menu.Items.Add(settingsItem);

            var help = Styled(new ToolStripMenuItem("도움말"));
            help.DropDownItems.Add(Styled(MakeItem("위치 권한 설정 열기", () => OpenUri("ms-settings:privacy-location"))));
            help.DropDownItems.Add(Styled(MakeItem("모바일 핫스팟 설정 열기", () => OpenUri("ms-settings:network-mobilehotspot"))));
            help.DropDownItems.Add(new ToolStripSeparator());
            help.DropDownItems.Add(Styled(MakeItem("문의용 진단 정보 저장...", SaveDiagnostics)));
            help.DropDownItems.Add(Styled(MakeItem("기록 폴더 열기", () => { Directory.CreateDirectory(AppPaths.LogDir); OpenUri(AppPaths.LogDir); })));
            help.DropDownItems.Add(Styled(MakeItem("문의하기 (GitHub)", () => OpenUri(AppInfo.IssuesUrl))));
            help.DropDownItems.Add(new ToolStripSeparator());
            help.DropDownItems.Add(Styled(MakeItem("와이패스 정보", () => OpenSettings(SettingsWindow.PageAbout))));
            var helpDrop = (ToolStripDropDownMenu)help.DropDown;
            helpDrop.Renderer = renderer;
            helpDrop.BackColor = palette.Background;
            helpDrop.ShowImageMargin = false;
            helpDrop.ShowCheckMargin = false;
            helpDrop.Padding = new Padding(0, 4, 0, 4);
            helpDrop.Opened += (s, e) => WinTheme.RoundCorners(helpDrop.Handle, false);
            menu.Items.Add(help);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Styled(MakeItem("종료", Quit)));
        }

        /// <summary>Taller items with breathing room, like Windows 11 menus.</summary>
        private static ToolStripMenuItem Styled(ToolStripMenuItem item)
        {
            item.Padding = new Padding(0, 5, 0, 5);
            return item;
        }

        private string StatusSummary()
        {
            if (!settings.IsConfigured) return "작업표시줄 아이콘 → 설정에서 시작하세요";
            string text = "자동 전환 " + OnOff(settings.AutoSwitch);
            if (settings.AutoHotspot)
                text += " · 핫스팟 " + (hotspotOn == true ? "켜짐" : hotspotOn == false ? "꺼짐" : "대기");
            return text;
        }

        private static ToolStripMenuItem MakeItem(string text, Action onClick)
        {
            var item = new ToolStripMenuItem(text);
            item.Click += (s, e) => onClick();
            return item;
        }

        private void SetAutoSwitch(bool on)
        {
            if (settings.AutoSwitch != on) { settings.AutoSwitch = on; settings.Save(); }
            Log.Info("자동 전환 " + OnOff(on));
            AfterAutoSwitchChanged();
        }

        private void AfterAutoSwitchChanged()
        {
            if (settings.AutoSwitch) { autoRetryAfter = DateTime.MinValue; EvaluateSoon(500); }
            RefreshStatus();
            UpdateWindowsSoon();
        }

        private void SetAutoHotspot(bool on)
        {
            if (settings.AutoHotspot != on) { settings.AutoHotspot = on; settings.Save(); }
            Log.Info("핫스팟 자동 켜기 " + OnOff(on));
            AfterHotspotChanged(true);
        }

        private void AfterHotspotChanged(bool notify)
        {
            hotspotWarned = false;
            if (settings.AutoHotspot)
            {
                if (notify)
                    Notify("핫스팟 자동 켜기", "학교 와이파이에 연결되면 노트북 핫스팟을 켭니다.\r\n학교·기관의 무선망 보안 지침을 먼저 확인해 주세요.", NoticeKind.Info);
                EvaluateSoon(500);
            }
            else StopHotspot();
            UpdateWindowsSoon();
        }

        // ------------------------------------------------------------------ status

        private void RefreshStatus()
        {
            if (busy) return;
            try
            {
                connectedPlaceId = null;
                WifiInterface itf = wifi.GetPrimaryInterface();
                if (itf == null) { SetState(TrayState.Idle, "무선랜 장치 없음", "무선랜 장치 없음", "와이파이 어댑터가 켜져 있는지 확인해 주세요"); return; }
                if (!settings.IsConfigured) { SetState(TrayState.Idle, "설정이 필요합니다", "설정이 필요해요", "학교 와이파이와 장소를 정해 주세요"); return; }

                CurrentConnection cur = wifi.GetCurrent(itf.Id);
                bool connected = cur != null && cur.State == WlanNative.STATE_CONNECTED;
                if (connected && IsSchool(cur))
                {
                    Place p = settings.FindPlaceByBssid(cur.Bssid) ?? settings.FindPlace(settings.CurrentPlaceId);
                    string text = (p != null ? p.Name : "학교 와이파이") + " 연결됨";
                    if (settings.AutoHotspot && hotspotOn.HasValue) text += " · 핫스팟 " + (hotspotOn.Value ? "켜짐" : "꺼짐");
                    connectedPlaceId = p != null ? p.Id : null;
                    SetState(TrayState.Connected, text, p != null ? p.Name : "학교 와이파이", settings.Ssid + " 연결됨");
                    return;
                }
                else if (lastFailureAt.HasValue && DateTime.UtcNow - lastFailureAt.Value < AutoRetryDelay)
                {
                    SetState(TrayState.Failed, lastFailureText, "연결하지 못했어요", lastFailureSub);
                }
                else if (connected)
                {
                    SetState(TrayState.Idle, "다른 와이파이 사용 중" + (cur.Ssid.Length > 0 ? " (" + cur.Ssid + ")" : ""), "다른 와이파이 사용 중",
                        (cur.Ssid.Length > 0 ? cur.Ssid + " · " : "") + "이 연결은 건드리지 않아요");
                }
                else
                {
                    SetState(TrayState.Idle, "학교 와이파이 연결 안 됨", "연결 안 됨",
                        settings.AutoSwitch ? "장소에 도착하면 자동으로 연결해요" : "아래에서 지금 있는 장소를 고르세요");
                }
            }
            catch (Exception ex)
            {
                Log.Warn("상태 확인 오류: " + ex.Message);
            }
        }

        // The tray glyph follows the taskbar colour (light/dark) and the display scale.
        private void OnThemeChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.VisualStyle || e.Category == UserPreferenceCategory.Color)
                Post(RedrawIcon);
        }

        private void OnDisplayChanged(object sender, EventArgs e)
        {
            Post(RedrawIcon);
        }

        private void RedrawIcon()
        {
            IconFactory.Invalidate();
            tray.Icon = IconFactory.Get(currentState);
        }

        private void SetState(TrayState state, string text, string title = null, string sub = null)
        {
            currentState = state;
            statusText = text;
            flyTitle = title ?? text;
            flySub = sub ?? "";
            UpdateWindowsSoon();
            tray.Icon = IconFactory.Get(state);
            string tip = AppInfo.Name + ": " + text;
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        }

        private bool FlyoutVisible { get { return flyout != null && !flyout.IsDisposed && flyout.Visible; } }

        private void Notify(string title, string text, NoticeKind kind, Action onClick = null, params NoticeButton[] buttons)
        {
            // While the flyout is open it shows the same news, and a toast would pop up right on top of it.
            if (kind == NoticeKind.Info && FlyoutVisible) return;
            notifier.Show(title, text, kind, onClick, buttons);
        }

        private NoticeButton PickPlaceButton() { return new NoticeButton("장소 고르기", OpenFlyout); }
        private NoticeButton SettingsButton(string label) { return new NoticeButton(label, () => OpenSettings()); }

        // ------------------------------------------------------------------ dialogs

        /// <summary>First run (nothing set up yet) opens the wizard; otherwise the settings window (one at a time).</summary>
        private void OpenSettings(int page = SettingsWindow.PagePlaces)
        {
            if (flyout != null) flyout.HideFlyout();
            if (settingsWindow != null && !settingsWindow.IsDisposed)
            {
                if (settingsWindow.WindowState == FormWindowState.Minimized) settingsWindow.WindowState = FormWindowState.Normal;
                settingsWindow.ShowPage(page);
                settingsWindow.Activate();
                return;
            }
            if (wizardOpen) return;
            if (!settings.IsConfigured && page == SettingsWindow.PagePlaces) { RunWizard(); return; }

            settingsWindow = new SettingsWindow(wifi, settings, this);
            settingsWindow.FormClosed += (s, e) =>
            {
                settingsWindow = null;
                RefreshStatus();
                EvaluateSoon(1500);
            };
            settingsWindow.ShowPage(page);
            settingsWindow.Show();
            settingsWindow.Activate();
        }

        private void RunWizard()
        {
            if (wizardOpen || busy) return;
            wizardOpen = true;
            bool hotspotBefore = settings.AutoHotspot;
            try
            {
                using (var f = new SetupWizard(wifi, settings, hotspot.Available))
                {
                    if (f.ShowDialog() == DialogResult.OK && f.Result != null)
                    {
                        settings = f.Result;
                        settings.Save();
                        StartupManager.Apply(settings.StartWithWindows);
                        Log.Info("처음 설정 완료: 장소 " + settings.Places.Count + "개, 자동 전환 " + OnOff(settings.AutoSwitch) +
                                 ", 자동 실행 " + OnOff(settings.StartWithWindows) + ", 핫스팟 " + OnOff(settings.AutoHotspot));
                        autoRetryAfter = DateTime.MinValue;
                        lastFailureAt = null;
                        hotspotWarned = false;
                        if (hotspotBefore && !settings.AutoHotspot) StopHotspot();
                        Notify("준비됐어요",
                            settings.AutoSwitch
                                ? "이제 장소에 도착하면 와이패스가 알아서 연결합니다.\r\n작업표시줄 아이콘을 눌러 직접 고를 수도 있어요."
                                : "작업표시줄의 와이패스 아이콘을 눌러\r\n지금 있는 장소를 고르면 그 비밀번호로 연결합니다.",
                            NoticeKind.Info);
                    }
                }
            }
            finally
            {
                wizardOpen = false;
                RefreshStatus();
                EvaluateSoon(1500);
            }
        }

        // ------------------------------------------------------------------ settings window host

        bool ISettingsHost.HotspotAvailable { get { return hotspot.Available; } }

        string ISettingsHost.ConnectedPlaceId { get { return currentState == TrayState.Connected ? connectedPlaceId : null; } }

        HotspotInfo ISettingsHost.GetHotspotInfo() { return hotspot.GetInfo(settings.Ssid); }

        bool ISettingsHost.LocationAllowed()
        {
            try
            {
                WifiInterface itf = wifi.GetPrimaryInterface();
                if (itf != null) wifi.GetVisibleNetworks(itf.Id);
                locationWarned = false;
                return true;
            }
            catch (LocationDeniedException) { return false; }
            catch { return true; }
        }

        void ISettingsHost.SaveDiagnostics(IWin32Window owner) { SaveDiagnostics(owner); }

        void ISettingsHost.OnSettingsChanged(SettingChange change)
        {
            switch (change)
            {
                case SettingChange.Places:
                case SettingChange.Ssid:
                    Log.Info("설정 변경: 장소 " + settings.Places.Count + "개");
                    autoRetryAfter = DateTime.MinValue;
                    lastFailureAt = null;
                    if (change == SettingChange.Ssid) { hotspotOn = null; hotspotInfo = null; }
                    RefreshStatus();
                    EvaluateSoon(1500);
                    break;
                case SettingChange.AutoSwitch:
                    AfterAutoSwitchChanged();
                    break;
                case SettingChange.Startup:
                    StartupManager.Apply(settings.StartWithWindows);
                    Log.Info("윈도우 시작 시 실행 " + OnOff(settings.StartWithWindows));
                    break;
                case SettingChange.Hotspot:
                    AfterHotspotChanged(false);
                    break;
            }
            UpdateWindowsSoon();
        }

        // ------------------------------------------------------------------ flyout

        private void EnsureFlyout()
        {
            if (flyout != null && !flyout.IsDisposed) return;
            flyout = new FlyoutForm();
            flyout.SwitchRequested += id =>
            {
                Place place = settings.FindPlace(id);
                if (place != null) SwitchTo(place);
            };
            flyout.AutoSwitchChanged += on => Post(() => SetAutoSwitch(on));
            flyout.HotspotChanged += on => Post(() => SetAutoHotspot(on));
            flyout.SettingsRequested += () => Post(() => OpenSettings());
        }

        private void ToggleFlyout()
        {
            EnsureFlyout();
            if (flyout.Visible) { flyout.HideFlyout(); return; }
            if (flyout.JustHidden) return; // the click that closed it was on the tray icon
            OpenFlyout();
        }

        private void OpenFlyout()
        {
            EnsureFlyout();
            RefreshStatus();
            FlyoutModel m = BuildFlyoutModel();
            flyoutSignature = Signature(m);
            flyout.ShowFlyout(m);
            LoadHotspotInfo();
        }

        private FlyoutModel BuildFlyoutModel()
        {
            var m = new FlyoutModel
            {
                Configured = settings.IsConfigured,
                State = currentState,
                Title = flyTitle,
                Subtitle = flySub,
                Busy = busy,
                AutoSwitch = settings.AutoSwitch,
                AutoHotspot = settings.AutoHotspot,
                HotspotAvailable = hotspot.Available,
                Footer = lastSwitchText ?? ("와이패스 " + AppInfo.Version)
            };
            string here = currentState == TrayState.Connected ? connectedPlaceId : null;
            foreach (var pl in settings.Places)
                m.Places.Add(new FlyoutPlace { Id = pl.Id, Name = pl.Name, Current = pl.Id == here });

            if (!hotspot.Available) m.HotspotDetail = "이 PC에서는 쓸 수 없어요";
            else if (!settings.AutoHotspot) m.HotspotDetail = "켜면 학교 와이파이에 연결될 때 자동으로 켜요";
            else if (hotspotOn == true || (hotspotInfo != null && hotspotInfo.State == HotspotState.On))
            {
                m.HotspotDetail = "켜짐";
                if (hotspotInfo != null && !string.IsNullOrEmpty(hotspotInfo.ApSsid)) m.HotspotDetail += " · " + hotspotInfo.ApSsid;
            }
            else m.HotspotDetail = currentState == TrayState.Connected ? "켜는 중이거나 꺼져 있어요" : "학교 와이파이에 연결되면 켜요";
            return m;
        }

        private static string Signature(FlyoutModel m)
        {
            var sb = new StringBuilder();
            sb.Append(m.Configured).Append(m.State).Append(m.Title).Append('|').Append(m.Subtitle).Append(m.Busy)
              .Append(m.AutoSwitch).Append(m.AutoHotspot).Append(m.HotspotDetail).Append(m.Footer);
            foreach (var pl in m.Places) sb.Append('|').Append(pl.Id).Append(pl.Name).Append(pl.Current);
            return sb.ToString();
        }

        /// <summary>Hotspot name and connected devices for the flyout (slow WinRT call, so in the background).</summary>
        private void LoadHotspotInfo()
        {
            if (hotspotInfoLoading || !hotspot.Available || !settings.AutoHotspot || string.IsNullOrEmpty(settings.Ssid)) return;
            hotspotInfoLoading = true;
            string ssid = settings.Ssid;
            Task.Factory.StartNew(() => hotspot.GetInfo(ssid)).ContinueWith(t => Post(() =>
            {
                hotspotInfoLoading = false;
                if (!t.IsFaulted) hotspotInfo = t.Result;
                UpdateWindowsSoon();
            }));
        }

        /// <summary>Refresh an open flyout / settings window after the current event has finished.</summary>
        private void UpdateWindowsSoon()
        {
            Post(() =>
            {
                if (flyout != null && !flyout.IsDisposed && flyout.Visible)
                {
                    FlyoutModel m = BuildFlyoutModel();
                    string sig = Signature(m);
                    if (sig != flyoutSignature) { flyoutSignature = sig; flyout.UpdateModel(m); }
                }
                if (settingsWindow != null && !settingsWindow.IsDisposed) settingsWindow.RefreshFromHost();
            });
        }

        private void SaveDiagnostics() { SaveDiagnostics(null); }

        private void SaveDiagnostics(IWin32Window owner)
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
                if (dlg.ShowDialog(owner) != DialogResult.OK) return;
                File.WriteAllText(dlg.FileName, report, new UTF8Encoding(true));
                MessageBox.Show(owner, "저장했습니다.\r\n\r\n비밀번호는 들어 있지 않습니다. 기록에 장소 이름이 나올 수 있으니, 올리기 전에 한 번 열어 확인해 주세요.\r\n" +
                                "GitHub 문의(Issues)에 이 파일을 첨부해 주시면 원인을 찾는 데 큰 도움이 됩니다.",
                                AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                try { Process.Start("explorer.exe", "/select,\"" + dlg.FileName + "\""); } catch { }
            }
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
            SystemEvents.UserPreferenceChanged -= OnThemeChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            checkTimer.Stop();
            evaluateSoon.Stop();
            tray.Visible = false;
            tray.Dispose();
            invoker.Dispose();
            ExitThread();
        }
    }
}
