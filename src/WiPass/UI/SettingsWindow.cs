using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using WiPass.Core;
using WiPass.UI.Fluent;
using WiPass.Wifi;

namespace WiPass.UI
{
    internal enum SettingChange { Places, Ssid, AutoSwitch, Startup, Hotspot }

    /// <summary>What the settings window needs from the tray app.</summary>
    internal interface ISettingsHost
    {
        bool HotspotAvailable { get; }
        /// <summary>Id of the place we are connected at right now, or null.</summary>
        string ConnectedPlaceId { get; }
        /// <summary>Blocking; called on a background thread.</summary>
        HotspotInfo GetHotspotInfo();
        bool LocationAllowed();
        /// <summary>Called after the settings object was changed and saved.</summary>
        void OnSettingsChanged(SettingChange change);
        void SaveDiagnostics(IWin32Window owner);
    }

    /// <summary>
    /// Settings with a left navigation (장소 / 옵션 / 정보·문의). Every change is saved
    /// immediately, so there is no save button.
    /// </summary>
    internal sealed class SettingsWindow : FWindow
    {
        public const int PagePlaces = 0, PageOptions = 1, PageAbout = 2;

        private readonly WifiService wifi;
        private readonly AppSettings settings;
        private readonly ISettingsHost host;
        private readonly NavItem[] nav = new NavItem[3];
        private readonly Panel[] pages = new Panel[3];
        private readonly int W, H, navW, cx, inner;
        private int page = -1;
        private string placesSignature;
        private bool? locationShown;
        private FLabel hsName, hsClients;
        private bool hotspotPending;
        private string optionsSignature;

        /// <summary>For previews: fixed location answer and hotspot details instead of asking Windows.</summary>
        public bool? LocationOverride;
        public HotspotInfo HotspotOverride;

        public SettingsWindow(WifiService wifi, AppSettings settings, ISettingsHost host)
        {
            this.wifi = wifi;
            this.settings = settings;
            this.host = host;
            Text = "와이패스 설정";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            KeyPreview = true;

            W = Fx.S(820); H = Fx.S(640); navW = Fx.S(224);
            cx = Fx.S(20);
            inner = W - navW - cx - Fx.S(40);
            ClientSize = new Size(W, H);

            // navigation
            Controls.Add(new FIcon(Ico.Logo, p.Accent, Fx.S(26)) { BackColor = p.Window, Location = new Point(Fx.S(22), Fx.S(24)) });
            Controls.Add(new FLabel("와이패스", Fx.Font(16, FontStyle.Bold), p.Text) { BackColor = p.Window, Location = new Point(Fx.S(56), Fx.S(18)) });
            Controls.Add(new FLabel("버전 " + AppInfo.Version, Fx.Font(12), p.SubText) { BackColor = p.Window, Location = new Point(Fx.S(57), Fx.S(41)) });
            string[] names = { "장소", "옵션", "정보·문의" };
            Ico[] icons = { Ico.Building, Ico.Sliders, Ico.Info };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                nav[i] = new NavItem(p, names[i], icons[i]) { BackColor = p.Window, Location = new Point(Fx.S(10), Fx.S(82) + i * Fx.S(44)), Size = new Size(navW - Fx.S(20), Fx.S(40)), TabIndex = i };
                nav[i].Click += (s, e) => ShowPage(idx);
                Controls.Add(nav[i]);
                pages[i] = new Panel { Location = new Point(navW, 0), Size = new Size(W - navW, H), BackColor = p.Window, AutoScroll = true, Visible = false, TabIndex = 10 };
                Controls.Add(pages[i]);
            }
            var credit = new FLabel("jomwalimu 개인 제작", Fx.Font(12), p.SubText) { BackColor = p.Window, Location = new Point(Fx.S(22), H - Fx.S(36)) };
            Controls.Add(credit);

            ShowPage(PagePlaces);
            Activated += (s, e) => OnReactivated();
            Shown += (s, e) => { if (hotspotPending) { hotspotPending = false; LoadHotspotInfo(); } };
        }

        // ------------------------------------------------------------------ public

        public void ShowPage(int i)
        {
            if (i == PagePlaces) BuildPlaces();
            else if (i == PageOptions) BuildOptions();
            else if (pages[i].Controls.Count == 0) BuildAbout();
            page = i;
            for (int k = 0; k < 3; k++) { pages[k].Visible = k == i; nav[k].Selected = k == i; }
        }

        /// <summary>The tray app calls this when something may have changed (connection, learned access points).</summary>
        public void RefreshFromHost()
        {
            if (page == PagePlaces && PlacesSignature() != placesSignature) BuildPlaces();
            else if (page == PageOptions && OptionsSignature() != optionsSignature) BuildOptions();
        }

        private string OptionsSignature()
        {
            return settings.AutoSwitch + "|" + settings.StartWithWindows + "|" + settings.AutoHotspot;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.Tab) { ShowPage((page + (e.Shift ? 2 : 1)) % 3); nav[page].Focus(); e.Handled = true; }
            base.OnKeyDown(e);
        }

        private void OnReactivated()
        {
            // Coming back from the Windows location settings: update the banner.
            if (page == PageOptions && locationShown.HasValue && locationShown.Value != (LocationOverride ?? host.LocationAllowed())) BuildOptions();
            else RefreshFromHost();
        }

        // ------------------------------------------------------------------ helpers

        private static void Clear(Panel pg)
        {
            var list = new List<Control>();
            foreach (Control c in pg.Controls) list.Add(c);
            foreach (Control c in list) { pg.Controls.Remove(c); c.Dispose(); }
            pg.AutoScrollPosition = new Point(0, 0);
        }

        private int Header(Panel pg, string title, string body)
        {
            int y = Fx.S(22);
            pg.Controls.Add(Heading(title, cx, y, inner));
            y += Fx.S(42);
            if (body != null)
            {
                var b = Body(body, cx, y, inner);
                pg.Controls.Add(b);
                y = b.Bottom;
            }
            return y + Fx.S(18);
        }

        private int Caption(Panel pg, string text, int y)
        {
            var c = new FLabel(text, Fx.Font(13, FontStyle.Bold), p.Text) { BackColor = p.Window, Location = new Point(cx + Fx.S(2), y) };
            pg.Controls.Add(c);
            return y + c.Height + Fx.S(8);
        }

        /// <summary>A row with a title, detail and a button on the right.</summary>
        private static FButton AddButtonRow(FCard card, ref int y, string title, string detail, string buttonText, Action action, bool divider)
        {
            Palette p = Palette.Current();
            int rowH = detail != null ? Fx.S(62) : Fx.S(50);
            var btn = new FButton(p, buttonText, FButtonKind.Standard);
            btn.Location = new Point(card.Width - btn.Width - Fx.S(14), y + (rowH - btn.Height) / 2);
            btn.Click += (s, e) => action();
            int textW = btn.Left - Fx.S(28);
            var t = new FLabel(title, Fx.Font(14), p.Text) { Width = textW };
            t.Location = new Point(Fx.S(16), y + (detail != null ? Fx.S(11) : (rowH - t.Height) / 2));
            card.Controls.Add(t);
            if (detail != null)
                card.Controls.Add(new FLabel(detail, Fx.Font(12), p.SubText) { Location = new Point(Fx.S(16), t.Bottom + Fx.S(1)), Width = textW });
            card.Controls.Add(btn);
            y += rowH;
            if (divider) card.Dividers.Add(y);
            return btn;
        }

        private void Changed(SettingChange c)
        {
            settings.Save();
            optionsSignature = OptionsSignature(); // our own change: no need to rebuild the page
            host.OnSettingsChanged(c);
        }

        private static void Open(string uri)
        {
            try { Process.Start(uri); }
            catch (Exception ex) { Log.Warn("열기 실패(" + uri + "): " + ex.Message); }
        }

        internal static string Ago(string utc)
        {
            DateTime t;
            if (string.IsNullOrEmpty(utc) || !DateTime.TryParse(utc, null, System.Globalization.DateTimeStyles.RoundtripKind, out t)) return null;
            TimeSpan d = DateTime.UtcNow - t.ToUniversalTime();
            if (d.TotalMinutes < 1) return "방금";
            if (d.TotalMinutes < 60) return (int)d.TotalMinutes + "분 전";
            if (d.TotalHours < 24) return (int)d.TotalHours + "시간 전";
            if (d.TotalDays < 7) return (int)d.TotalDays + "일 전";
            return t.ToLocalTime().ToString("M월 d일");
        }

        // ------------------------------------------------------------------ 장소

        private string PlacesSignature()
        {
            var sb = new StringBuilder(settings.Ssid).Append('|').Append(host.ConnectedPlaceId);
            foreach (var pl in settings.Places)
                sb.Append('|').Append(pl.Id).Append(pl.Name).Append(pl.Bssids != null ? pl.Bssids.Count : 0).Append(Ago(pl.LastUsedUtc));
            return sb.ToString();
        }

        private void BuildPlaces()
        {
            Panel pg = pages[PagePlaces];
            pg.SuspendLayout();
            Clear(pg);
            placesSignature = PlacesSignature();
            int y = Header(pg, "장소", "이름이 같은 학교 와이파이를 장소마다 다른 비밀번호로 연결합니다. 바꾸면 바로 저장돼요.");

            // network card
            var net = new FCard(p) { Location = new Point(cx, y), Size = new Size(inner, Fx.S(68)) };
            net.Controls.Add(new FIcon(Ico.Wifi, p.Accent, Fx.S(38)) { Circle = p.AccentSoft, Stroke = 2f, Location = new Point(Fx.S(16), Fx.S(15)) });
            net.Controls.Add(new FLabel("학교 와이파이 (모든 장소 공통)", Fx.Font(12), p.SubText) { Location = new Point(Fx.S(66), Fx.S(13)) });
            var change = new FButton(p, "변경", FButtonKind.Standard);
            change.Location = new Point(inner - change.Width - Fx.S(16), (net.Height - change.Height) / 2);
            change.Click += (s, e) => ChangeSsid();
            net.Controls.Add(new FLabel(settings.Ssid ?? "(정하지 않음)", Fx.Font(16, FontStyle.Bold), p.Text) { Location = new Point(Fx.S(66), Fx.S(31)), Width = change.Left - Fx.S(80) });
            net.Controls.Add(change);
            pg.Controls.Add(net);
            y += net.Height + Fx.S(24);

            // list header
            var count = new FLabel("장소 " + settings.Places.Count + "곳", Fx.Font(13, FontStyle.Bold), p.Text) { BackColor = p.Window };
            var add = new FButton(p, "장소 추가", FButtonKind.Standard, Ico.Plus) { BackColor = p.Window };
            add.Location = new Point(cx + inner - add.Width, y);
            add.Click += (s, e) => AddPlace();
            count.Location = new Point(cx + Fx.S(2), y + (add.Height - count.Height) / 2);
            pg.Controls.Add(count);
            pg.Controls.Add(add);
            y += add.Height + Fx.S(10);

            string here = host.ConnectedPlaceId;
            if (settings.Places.Count == 0)
            {
                var empty = new FCard(p) { Location = new Point(cx, y), Size = new Size(inner, Fx.S(72)), Dashed = true };
                empty.Controls.Add(new FLabel("아직 장소가 없어요. [장소 추가]로 다니는 곳과 비밀번호를 넣어 주세요.", Fx.Font(13), p.SubText) { Location = new Point(Fx.S(18), Fx.S(26)), Width = inner - Fx.S(36) });
                pg.Controls.Add(empty);
                y += empty.Height + Fx.S(8);
            }
            foreach (var pl in settings.Places)
            {
                Place place = pl;
                bool now = here != null && place.Id == here;
                var card = new FCard(p) { Location = new Point(cx, y), Size = new Size(inner, Fx.S(68)) };
                if (now) card.Border = p.Dark ? Fx.Blend(p.Accent, p.Card, 0.45f) : Color.FromArgb(153, 204, 197);
                card.Controls.Add(new FIcon(Ico.Building, now ? p.Accent : p.SubText, Fx.S(38)) { Circle = now ? p.AccentSoft : p.Hover, Stroke = 1.8f, Location = new Point(Fx.S(16), Fx.S(15)) });

                var del = new FButton(p, "", FButtonKind.Subtle, Ico.Trash) { AccessibleName = place.Name + " 지우기" };
                del.Location = new Point(inner - del.Width - Fx.S(12), (card.Height - del.Height) / 2);
                del.Click += (s, e) => DeletePlace(place);
                var edit = new FButton(p, "수정", FButtonKind.Subtle, Ico.Pencil) { AccessibleName = place.Name + " 수정" };
                edit.Location = new Point(del.Left - Fx.S(4) - edit.Width, del.Top);
                edit.Click += (s, e) => EditPlace(place);

                var name = new FLabel(place.Name, Fx.Font(15, FontStyle.Bold), p.Text);
                name.Location = new Point(Fx.S(66), Fx.S(12));
                name.Width = Math.Min(name.Width, edit.Left - Fx.S(160));
                card.Controls.Add(name);
                if (now)
                    card.Controls.Add(new Badge(p, "지금 여기") { Location = new Point(name.Right + Fx.S(8), Fx.S(14)) });

                int aps = place.Bssids != null ? place.Bssids.Count : 0;
                string ago = Ago(place.LastUsedUtc);
                string meta;
                if (!place.HasPassword) meta = "비밀번호를 넣어 주세요";
                else if (aps == 0 && ago == null) meta = "비밀번호 저장됨 · 처음 연결하면 공유기를 알아 둡니다";
                else meta = "공유기 " + aps + "대 알아 둠" + (ago != null ? " · " + ago + " 연결" : "");
                card.Controls.Add(new FLabel(meta, Fx.Font(12), place.HasPassword ? p.SubText : p.Danger) { Location = new Point(Fx.S(66), Fx.S(37)), Width = edit.Left - Fx.S(76) });
                card.Controls.Add(edit);
                card.Controls.Add(del);
                pg.Controls.Add(card);
                y += card.Height + Fx.S(8);
            }

            var note = new FLabel("도착하면 알아 둔 공유기가 보이는 장소부터, 그다음 최근에 연결한 순서로 시도합니다.", Fx.Font(12), p.SubText, true)
            { BackColor = p.Window, Location = new Point(cx + Fx.S(2), y + Fx.S(6)), Width = inner };
            note.FitHeight();
            pg.Controls.Add(note);
            pg.Controls.Add(new Panel { BackColor = p.Window, Location = new Point(cx, note.Bottom), Size = new Size(1, Fx.S(24)) }); // bottom margin when scrolling
            pg.ResumeLayout();
        }

        private bool NameTaken(string name, Place except)
        {
            foreach (var pl in settings.Places)
                if (pl != except && string.Equals(pl.Name.Trim(), name.Trim(), StringComparison.CurrentCultureIgnoreCase)) return true;
            return false;
        }

        private void AddPlace()
        {
            using (var d = new PlaceDialog(null, n => NameTaken(n, null)))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                settings.Places.Add(new Place { Id = Guid.NewGuid().ToString("N"), Name = d.PlaceName, ProtectedPassword = SecretStore.Protect(d.Password), Bssids = new List<string>() });
                Log.Info("장소 추가: " + d.PlaceName);
            }
            Changed(SettingChange.Places);
            BuildPlaces();
        }

        private void EditPlace(Place place)
        {
            using (var d = new PlaceDialog(place.Name, n => NameTaken(n, place)))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                place.Name = d.PlaceName;
                if (d.Password.Length > 0) place.ProtectedPassword = SecretStore.Protect(d.Password);
                Log.Info("장소 수정: " + place.Name + (d.Password.Length > 0 ? " (비밀번호 바꿈)" : ""));
            }
            Changed(SettingChange.Places);
            BuildPlaces();
        }

        private void DeletePlace(Place place)
        {
            if (!ConfirmDialog.Ask(this, "‘" + place.Name + "’ 장소를 지울까요?",
                    "저장된 비밀번호와 알아 둔 공유기 정보도 함께 지워집니다. 되돌릴 수 없어요.", "지우기", true)) return;
            settings.Places.Remove(place);
            if (settings.CurrentPlaceId == place.Id) settings.CurrentPlaceId = null;
            Log.Info("장소 삭제: " + place.Name);
            Changed(SettingChange.Places);
            BuildPlaces();
        }

        private void ChangeSsid()
        {
            using (var d = new SsidDialog(wifi, settings.Ssid))
            {
                if (d.ShowDialog(this) != DialogResult.OK || d.SelectedSsid == null) return;
                settings.Ssid = d.SelectedSsid;
                foreach (var pl in settings.Places) if (pl.Bssids != null) pl.Bssids.Clear();
                settings.ProfileTemplate = null;
                settings.CurrentPlaceId = null;
                Log.Info("학교 와이파이 이름 변경");
            }
            Changed(SettingChange.Ssid);
            BuildPlaces();
        }

        // ------------------------------------------------------------------ 옵션

        private void BuildOptions()
        {
            Panel pg = pages[PageOptions];
            pg.SuspendLayout();
            Clear(pg);
            int y = Header(pg, "옵션", null);
            optionsSignature = OptionsSignature();

            bool allowed = LocationOverride ?? host.LocationAllowed();
            locationShown = allowed;
            FBanner banner = allowed
                ? new FBanner(p, BannerKind.Success, "위치 권한이 켜져 있어요 · 도착한 장소를 알아볼 수 있습니다", null, null, null, inner)
                : new FBanner(p, BannerKind.Warning, "위치 권한이 꺼져 있어요", "자동 전환에 필요해요. 위치는 기록하지 않습니다.", "켜러 가기",
                    () => Open("ms-settings:privacy-location"), inner);
            banner.Location = new Point(cx, y);
            pg.Controls.Add(banner);
            y += banner.Height + Fx.S(24);

            y = Caption(pg, "자동으로", y);
            var card = new FCard(p) { Location = new Point(cx, y), Width = inner };
            int cy = 0;
            FToggle tAuto = SetupWizard.AddToggleRow(card, ref cy, "도착하면 자동 전환", "주변 공유기를 보고 어느 장소인지 알아서 판단해요", settings.AutoSwitch, true, true);
            FToggle tStart = SetupWizard.AddToggleRow(card, ref cy, "윈도우를 켤 때 와이패스 실행", "로그인하면 작업표시줄에서 조용히 시작해요", settings.StartWithWindows, true, false);
            card.Height = cy;
            tAuto.CheckedChanged += (s, e) => { settings.AutoSwitch = tAuto.Checked; Log.Info("자동 전환 " + (tAuto.Checked ? "켜짐" : "꺼짐")); Changed(SettingChange.AutoSwitch); };
            tStart.CheckedChanged += (s, e) => { settings.StartWithWindows = tStart.Checked; Changed(SettingChange.Startup); };
            pg.Controls.Add(card);
            y += card.Height + Fx.S(24);

            y = Caption(pg, "스마트폰 연결", y);
            bool hs = host.HotspotAvailable;
            var hcard = new FCard(p) { Location = new Point(cx, y), Width = inner };
            cy = 0;
            FToggle tHs = SetupWizard.AddToggleRow(hcard, ref cy, "학교 와이파이에 연결되면 노트북 핫스팟 켜기",
                hs ? "학교·기관 무선망 보안 지침을 먼저 확인하세요" : "이 PC에서는 핫스팟 기능을 쓸 수 없어요", settings.AutoHotspot && hs, hs, hs);
            tHs.CheckedChanged += (s, e) => { settings.AutoHotspot = tHs.Checked; Log.Info("핫스팟 자동 켜기 " + (tHs.Checked ? "켜짐" : "꺼짐")); Changed(SettingChange.Hotspot); };
            if (hs)
            {
                int rowH = Fx.S(66);
                hcard.Controls.Add(new FIcon(Ico.Phone, p.SubText, Fx.S(22)) { Location = new Point(Fx.S(16), cy + (rowH - Fx.S(22)) / 2) });
                var open = new FButton(p, "윈도우 핫스팟 설정", FButtonKind.Standard);
                open.Location = new Point(inner - open.Width - Fx.S(14), cy + (rowH - open.Height) / 2);
                open.Click += (s, e) => Open("ms-settings:network-mobilehotspot");
                hsName = new FLabel("핫스팟 이름 확인 중…", Fx.Font(14), p.Text) { Location = new Point(Fx.S(50), cy + Fx.S(13)), Width = open.Left - Fx.S(62) };
                hsClients = new FLabel("이름과 비밀번호는 윈도우 설정에서 정해요", Fx.Font(12), p.SubText) { Location = new Point(Fx.S(50), hsName.Bottom + Fx.S(1)), Width = open.Left - Fx.S(62) };
                hcard.Controls.Add(hsName);
                hcard.Controls.Add(hsClients);
                hcard.Controls.Add(open);
                cy += rowH;
                LoadHotspotInfo();
            }
            hcard.Height = cy;
            pg.Controls.Add(hcard);
            y += hcard.Height;
            if (hs)
            {
                var hint = new FLabel("핫스팟 이름과 비밀번호는 [윈도우 핫스팟 설정]에서 정해요. 스마트폰은 한 번만 연결해 두면 됩니다.", Fx.Font(12), p.SubText, true)
                { BackColor = p.Window, Location = new Point(cx + Fx.S(2), y + Fx.S(8)), Width = inner };
                hint.FitHeight();
                pg.Controls.Add(hint);
                y = hint.Bottom;
            }
            pg.Controls.Add(new Panel { BackColor = p.Window, Location = new Point(cx, y), Size = new Size(1, Fx.S(24)) });
            pg.ResumeLayout();
        }

        private void LoadHotspotInfo()
        {
            if (HotspotOverride != null) { ShowHotspotInfo(HotspotOverride); return; }
            if (!IsHandleCreated) { hotspotPending = true; return; } // loaded once the window is shown
            Task.Factory.StartNew(() => host.GetHotspotInfo()).ContinueWith(t =>
            {
                if (t.IsFaulted || IsDisposed || !IsHandleCreated) return;
                try { BeginInvoke((Action)(() => ShowHotspotInfo(t.Result))); } catch { }
            });
        }

        private void ShowHotspotInfo(HotspotInfo info)
        {
            if (hsName == null || hsName.IsDisposed) return;
            hsName.Text = string.IsNullOrEmpty(info.ApSsid) ? "노트북 핫스팟" : "핫스팟 이름: " + info.ApSsid;
            string state;
            if (info.State == HotspotState.On) state = "켜짐" + (info.Clients >= 0 ? " · 기기 " + info.Clients + "대 연결" : "");
            else if (info.State == HotspotState.InTransition) state = "켜는 중";
            else if (info.State == HotspotState.Off) state = "꺼짐";
            else state = "학교 와이파이에 연결되면 켤 수 있어요";
            hsClients.Text = state;
        }

        // ------------------------------------------------------------------ 정보·문의

        private void BuildAbout()
        {
            Panel pg = pages[PageAbout];
            pg.SuspendLayout();
            int y = Fx.S(26);
            pg.Controls.Add(new FIcon(Ico.Logo, p.Accent, Fx.S(56)) { Circle = p.AccentSoft, Stroke = 2.4f, BackColor = p.Window, Location = new Point(cx, y) });
            pg.Controls.Add(new FLabel(AppInfo.DisplayName, Fx.Font(22, FontStyle.Bold), p.Text) { BackColor = p.Window, Location = new Point(cx + Fx.S(72), y + Fx.S(2)) });
            pg.Controls.Add(new FLabel("버전 " + AppInfo.Version + " · " + AppInfo.Tagline, Fx.Font(13), p.SubText) { BackColor = p.Window, Location = new Point(cx + Fx.S(73), y + Fx.S(34)), Width = inner - Fx.S(73) });
            y += Fx.S(56) + Fx.S(22);

            var card = new FCard(p) { Location = new Point(cx, y), Width = inner };
            int cy = 0;
            InfoRow(card, ref cy, "만든 사람", AppInfo.Author + " · 전북특별자치도교육청 소속\n개인 제작 · 교육청 공식 프로그램 아님", true);
            InfoRow(card, ref cy, "개인정보", "인터넷으로 아무 정보도 보내지 않아요.\n비밀번호는 이 PC에만 암호화해 저장합니다.", true);
            InfoRow(card, ref cy, "라이선스", "MIT 라이선스 · 무료\n어떠한 보증도 하지 않으며, 사용 책임은 사용자에게 있습니다.", false);
            card.Height = cy;
            pg.Controls.Add(card);
            y += card.Height + Fx.S(24);

            y = Caption(pg, "문의·문제 해결", y);
            var help = new FCard(p) { Location = new Point(cx, y), Width = inner };
            cy = 0;
            AddButtonRow(help, ref cy, "문의하기 (GitHub)", "버그 신고 · 기능 제안 · 질문", "열기", () => Open(AppInfo.IssuesUrl), true);
            AddButtonRow(help, ref cy, "문의용 진단 정보 저장", "비밀번호는 들어 있지 않아요 · 문의할 때 첨부해 주세요", "저장...", () => host.SaveDiagnostics(this), true);
            AddButtonRow(help, ref cy, "기록 폴더", "와이패스가 남긴 동작 기록", "열기", () => { try { Directory.CreateDirectory(AppPaths.LogDir); } catch { } Open(AppPaths.LogDir); }, true);
            AddButtonRow(help, ref cy, "새 버전 확인", "GitHub 릴리스 페이지를 엽니다", "열기", () => Open(AppInfo.RepoUrl + "/releases"), false);
            help.Height = cy;
            pg.Controls.Add(help);
            y += help.Height;
            pg.Controls.Add(new Panel { BackColor = p.Window, Location = new Point(cx, y), Size = new Size(1, Fx.S(24)) });
            pg.ResumeLayout();
        }

        private void InfoRow(FCard card, ref int y, string title, string text, bool divider)
        {
            int labelW = Fx.S(92);
            var v = new FLabel(text, Fx.Font(13), p.Text, true) { Location = new Point(Fx.S(16) + labelW, y + Fx.S(14)), Width = card.Width - labelW - Fx.S(32) };
            v.FitHeight();
            card.Controls.Add(new FLabel(title, Fx.Font(13), p.SubText) { Location = new Point(Fx.S(16), y + Fx.S(14)) });
            card.Controls.Add(v);
            y += v.Height + Fx.S(28);
            if (divider) card.Dividers.Add(y);
        }

        // ------------------------------------------------------------------ small controls

        /// <summary>Left navigation entry (selected: grey pill with an accent bar, like Windows 11 Settings).</summary>
        private sealed class NavItem : Control
        {
            private readonly Palette p;
            private readonly Ico icon;
            private bool selected, hover;

            public NavItem(Palette palette, string text, Ico icon)
            {
                p = palette;
                this.icon = icon;
                Text = text;
                AccessibleName = text;
                AccessibleRole = AccessibleRole.PageTab;
                Cursor = Cursors.Hand;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                TabStop = true;
            }

            public bool Selected
            {
                get { return selected; }
                set { selected = value; AccessibleDescription = value ? "선택됨" : null; Invalidate(); }
            }

            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
            protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
            protected override void OnMouseDown(MouseEventArgs e) { Focus(); base.OnMouseDown(e); }
            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
                base.OnKeyDown(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(BackColor);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var r = new RectangleF(0, 0, Width, Height);
                if (selected) Fx.FillRound(g, p.NavSelected, r, Fx.S(6));
                else if (hover) Fx.FillRound(g, p.Hover, r, Fx.S(6));
                if (selected) Fx.FillRound(g, p.Accent, new RectangleF(0, Height / 2f - Fx.S(8), Fx.S(3), Fx.S(16)), Fx.S(1.5f));
                int iconSize = Fx.S(18);
                Icons.Draw(g, icon, new RectangleF(Fx.S(14), (Height - iconSize) / 2f, iconSize, iconSize), selected ? p.Accent : p.Text);
                Fx.Text(g, Text, Fx.Font(14, selected ? FontStyle.Bold : FontStyle.Regular), p.Text, new Rectangle(Fx.S(44), 0, Width - Fx.S(50), Height));
                if (Focused && ShowFocusCues) Fx.DrawRound(g, p.Text, new RectangleF(1, 1, Width - 2, Height - 2), Fx.S(6), 1.5f);
            }
        }

        /// <summary>Small pill label ("지금 여기").</summary>
        private sealed class Badge : Control
        {
            private readonly Palette p;
            public Badge(Palette palette, string text)
            {
                p = palette;
                Text = text;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                SetStyle(ControlStyles.Selectable, false);
                Size ts = TextRenderer.MeasureText(text, Fx.Font(11, FontStyle.Bold), Size.Empty, TextFormatFlags.NoPrefix);
                Size = new Size(ts.Width + Fx.S(14), Fx.S(20));
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(BackColor);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                Fx.FillRound(g, p.AccentSoft, new RectangleF(0, 0, Width, Height), Height / 2f);
                TextRenderer.DrawText(g, Text, Fx.Font(11, FontStyle.Bold), ClientRectangle, p.Dark ? p.Accent : p.AccentSoftText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }
        }
    }
}
