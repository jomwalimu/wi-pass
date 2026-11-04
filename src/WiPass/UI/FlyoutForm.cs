using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using WiPass.UI.Fluent;

namespace WiPass.UI
{
    internal sealed class FlyoutPlace
    {
        public string Id;
        public string Name;
        public bool Current;
    }

    /// <summary>What the flyout shows; built by the tray app.</summary>
    internal sealed class FlyoutModel
    {
        public bool Configured;
        public TrayState State;
        public string Title;
        public string Subtitle;
        public List<FlyoutPlace> Places = new List<FlyoutPlace>();
        public bool Busy;
        public bool AutoSwitch;
        public bool AutoHotspot;
        public bool HotspotAvailable;
        public string HotspotDetail;
        public string Footer;
    }

    /// <summary>
    /// The small window above the clock (like the Windows network / OneDrive flyouts):
    /// current place, one-tap place switching, quick toggles and a link to settings.
    /// Closes when it loses focus.
    /// </summary>
    internal sealed class FlyoutForm : Form
    {
        private Palette p;
        private FlyoutModel model;
        private DateTime hiddenAt = DateTime.MinValue;

        // Controls that change while the flyout is open (updated in place, never rebuilt,
        // so a click on a tile or toggle keeps focus and the flyout stays open).
        private FIcon statusBadge;
        private FLabel statusTitle, statusSub, hotspotSub, footText;
        private FCard statusCard;
        private readonly List<FPlaceTile> tiles = new List<FPlaceTile>();
        private FToggle autoToggle, hotspotToggle;
        private bool updating;

        public event Action<string> SwitchRequested;
        public event Action<bool> AutoSwitchChanged;
        public event Action<bool> HotspotChanged;
        public event Action SettingsRequested;

        public FlyoutForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            KeyPreview = true;
            Text = "와이패스";
            Icon = IconFactory.AppIcon;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80;        // WS_EX_TOOLWINDOW: not in Alt+Tab
                cp.ClassStyle |= 0x20000;  // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            WinTheme.RoundCorners(Handle, false);
        }

        /// <summary>True for a moment after the flyout closed by losing focus (so a tray click toggles it).</summary>
        public bool JustHidden { get { return (DateTime.UtcNow - hiddenAt).TotalMilliseconds < 400; } }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            // Close only when the user really moved on (clicked elsewhere, another window came up).
            // Check a moment later: Windows may briefly report deactivation while we update ourselves.
            BeginInvoke((Action)(() =>
            {
                if (!Visible) return;
                IntPtr fg = IntPtr.Zero;
                try { fg = GetForegroundWindow(); } catch { }
                if (fg == Handle) return;
                HideFlyout();
            }));
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { HideFlyout(); e.Handled = true; }
            base.OnKeyDown(e);
        }

        public void HideFlyout()
        {
            if (!Visible) return;
            hiddenAt = DateTime.UtcNow;
            Hide();
        }

        public void ShowFlyout(FlyoutModel m)
        {
            Build(m);
            PlaceNearTray();
            Show();
            Activate();
        }

        /// <summary>Refresh the content while open (e.g. after a switch finished).</summary>
        public void UpdateModel(FlyoutModel m)
        {
            if (!Visible) return;
            if (SameLayout(model, m)) { Apply(m); return; }

            // The list of places changed: rebuild, then keep the flyout active with the same focus.
            Control focused = ActiveControl;
            string focusName = focused != null ? focused.AccessibleName : null;
            Build(m);
            PlaceNearTray();
            Activate();
            if (focusName != null)
                foreach (Control c in AllControls(this))
                    if (c.CanFocus && c.AccessibleName == focusName) { c.Focus(); break; }
        }

        private static bool SameLayout(FlyoutModel a, FlyoutModel b)
        {
            if (a == null || a.Configured != b.Configured || a.HotspotAvailable != b.HotspotAvailable) return false;
            if (string.IsNullOrEmpty(a.HotspotDetail) != string.IsNullOrEmpty(b.HotspotDetail)) return false;
            if (a.Places.Count != b.Places.Count) return false;
            for (int i = 0; i < a.Places.Count; i++)
                if (a.Places[i].Id != b.Places[i].Id || a.Places[i].Name != b.Places[i].Name) return false;
            return true;
        }

        /// <summary>Updates texts, icons, checks and toggles without recreating controls.</summary>
        private void Apply(FlyoutModel m)
        {
            model = m;
            Ico icon; Color iconColor, circle;
            StatusLook(m.State, out icon, out iconColor, out circle);
            statusBadge.Icon = icon; statusBadge.Color = iconColor; statusBadge.Circle = circle; statusBadge.Invalidate();
            statusTitle.Text = m.Title ?? "";
            statusSub.Text = m.Subtitle ?? "";
            statusCard.AccessibleName = (m.Title ?? "") + " " + (m.Subtitle ?? "");
            for (int i = 0; i < tiles.Count && i < m.Places.Count; i++)
            {
                FPlaceTile t = tiles[i];
                t.Current = m.Places[i].Current;
                t.AccessibleName = m.Places[i].Name + (t.Current ? " (현재 장소)" : "");
                t.Invalidate();
            }
            updating = true;
            try
            {
                if (autoToggle != null) autoToggle.Checked = m.AutoSwitch;
                if (hotspotToggle != null) hotspotToggle.Checked = m.AutoHotspot;
            }
            finally { updating = false; }
            if (hotspotSub != null) hotspotSub.Text = m.HotspotDetail ?? "";
            if (footText != null) footText.Text = m.Footer ?? "";
        }

        private void StatusLook(TrayState state, out Ico icon, out Color iconColor, out Color circle)
        {
            switch (state)
            {
                case TrayState.Connected: icon = Ico.Check; iconColor = p.Accent; circle = p.AccentSoft; break;
                case TrayState.Busy: icon = Ico.Refresh; iconColor = p.WarnText; circle = p.WarnBg; break;
                case TrayState.Failed: icon = Ico.Warning; iconColor = p.Danger; circle = Fx.Blend(p.Card, p.Danger, 0.12f); break;
                default: icon = Ico.Wifi; iconColor = p.SubText; circle = p.Hover; break;
            }
        }

        private static IEnumerable<Control> AllControls(Control root)
        {
            foreach (Control c in root.Controls)
            {
                yield return c;
                foreach (Control d in AllControls(c)) yield return d;
            }
        }

        private void PlaceNearTray()
        {
            Screen screen = Screen.FromPoint(Cursor.Position);
            Rectangle wa = screen.WorkingArea, b = screen.Bounds;
            int m = Fx.S(12);
            int x = wa.Right - Width - m, y = wa.Bottom - Height - m;
            if (wa.Top > b.Top) y = wa.Top + m;          // taskbar at the top
            if (wa.Left > b.Left) x = wa.Left + m;        // taskbar on the left
            Location = new Point(x, y);
        }

        // ------------------------------------------------------------------ layout

        private void Build(FlyoutModel m)
        {
            model = m;
            p = Palette.Current();
            SuspendLayout();
            foreach (Control c in new List<Control>(AllTop())) { Controls.Remove(c); c.Dispose(); }
            BackColor = p.Window;

            int W = Fx.S(360);
            int pad = Fx.S(12);
            int y = Fx.S(10);

            // header
            var logo = new FIcon(Ico.Logo, p.Accent, Fx.S(18)) { BackColor = p.Window, Location = new Point(Fx.S(18), y + Fx.S(9)) };
            var appName = new FLabel("와이패스", Fx.Font(13, FontStyle.Bold), p.SubText) { BackColor = p.Window, Location = new Point(Fx.S(42), y + Fx.S(7)) };
            var gear = new FButton(p, "", FButtonKind.Subtle, Ico.Gear) { AccessibleName = "설정 열기", BackColor = p.Window };
            gear.Size = new Size(Fx.S(36), Fx.S(36));
            gear.Location = new Point(W - Fx.S(14) - gear.Width, y);
            gear.Click += (s, e) => { HideFlyout(); Raise(SettingsRequested); };
            Controls.Add(logo); Controls.Add(appName); Controls.Add(gear);
            y += Fx.S(44);

            // status card
            var status = new FCard(p) { Location = new Point(pad, y), Size = new Size(W - pad * 2, Fx.S(76)) };
            Ico icon; Color iconColor, circle;
            StatusLook(m.State, out icon, out iconColor, out circle);
            var badge = new FIcon(icon, iconColor, Fx.S(44)) { Circle = circle, Stroke = 2.2f, Location = new Point(Fx.S(16), Fx.S(16)) };
            var title = new FLabel(m.Title ?? "", Fx.Font(20, FontStyle.Bold), p.Text) { Location = new Point(Fx.S(74), Fx.S(13)), Width = status.Width - Fx.S(90) };
            var sub = new FLabel(m.Subtitle ?? "", Fx.Font(13), p.SubText) { Location = new Point(Fx.S(74), Fx.S(44)), Width = status.Width - Fx.S(90) };
            status.Controls.Add(badge); status.Controls.Add(title); status.Controls.Add(sub);
            status.AccessibleName = (m.Title ?? "") + " " + (m.Subtitle ?? "");
            statusCard = status; statusBadge = badge; statusTitle = title; statusSub = sub;
            tiles.Clear(); autoToggle = null; hotspotToggle = null; hotspotSub = null;
            Controls.Add(status);
            y += status.Height + Fx.S(16);

            if (m.Configured)
            {
                var caption = new FLabel("장소 바꾸기", Fx.Font(12, FontStyle.Bold), p.SubText) { BackColor = p.Window, Location = new Point(Fx.S(18), y) };
                Controls.Add(caption);
                y += caption.Height + Fx.S(6);

                int gap = Fx.S(8);
                int tileW = (W - pad * 2 - gap) / 2;
                for (int i = 0; i < m.Places.Count; i++)
                {
                    FlyoutPlace place = m.Places[i];
                    var tile = new FPlaceTile(p, place.Name, place.Current)
                    {
                        BackColor = p.Window,
                        Width = tileW,
                        Location = new Point(pad + (i % 2) * (tileW + gap), y + (i / 2) * (Fx.S(44) + gap))
                    };
                    string id = place.Id;
                    tile.Click += (s, e) => { var h = SwitchRequested; if (h != null) h(id); };
                    Controls.Add(tile);
                    tiles.Add(tile);
                }
                int rows = (m.Places.Count + 1) / 2;
                y += rows * Fx.S(44) + Math.Max(0, rows - 1) * gap + Fx.S(14);

                // quick toggles
                var toggles = new FCard(p) { Location = new Point(pad, y), Width = W - pad * 2 };
                int rowH1 = Fx.S(48);
                var autoLbl = new FLabel("도착하면 자동 전환", Fx.Font(14), p.Text) { Location = new Point(Fx.S(14), (rowH1 - Fx.S(20)) / 2) };
                var autoTgl = new FToggle(p, "도착하면 자동 전환") { Checked = m.AutoSwitch };
                autoTgl.Location = new Point(toggles.Width - autoTgl.Width - Fx.S(12), (rowH1 - autoTgl.Height) / 2);
                autoTgl.CheckedChanged += (s, e) => { var h = AutoSwitchChanged; if (h != null && !updating) h(autoTgl.Checked); };
                autoToggle = autoTgl;
                toggles.Controls.Add(autoLbl); toggles.Controls.Add(autoTgl);
                toggles.Dividers.Add(rowH1);

                bool hasDetail = !string.IsNullOrEmpty(m.HotspotDetail);
                int rowH2 = hasDetail ? Fx.S(58) : Fx.S(48);
                int y2 = rowH1;
                var hsLbl = new FLabel("노트북 핫스팟", Fx.Font(14), m.HotspotAvailable ? p.Text : p.Disabled);
                hsLbl.Location = new Point(Fx.S(14), y2 + (hasDetail ? Fx.S(9) : (rowH2 - hsLbl.Height) / 2));
                toggles.Controls.Add(hsLbl);
                if (hasDetail)
                {
                    var hsSub = new FLabel(m.HotspotDetail, Fx.Font(12), p.SubText) { Location = new Point(Fx.S(14), hsLbl.Bottom + Fx.S(1)), Width = toggles.Width - Fx.S(90) };
                    toggles.Controls.Add(hsSub);
                    hotspotSub = hsSub;
                }
                var hsTgl = new FToggle(p, "노트북 핫스팟") { Checked = m.AutoHotspot, Enabled = m.HotspotAvailable };
                hsTgl.Location = new Point(toggles.Width - hsTgl.Width - Fx.S(12), y2 + (rowH2 - hsTgl.Height) / 2);
                hsTgl.CheckedChanged += (s, e) => { var h = HotspotChanged; if (h != null && !updating) h(hsTgl.Checked); };
                hotspotToggle = hsTgl;
                toggles.Controls.Add(hsTgl);
                toggles.Height = rowH1 + rowH2;
                Controls.Add(toggles);
                y += toggles.Height + Fx.S(14);
            }
            else
            {
                var msg = new FLabel("학교 와이파이와 장소를 먼저 설정해 주세요.", Fx.Font(14), p.Text, true) { BackColor = p.Window, Location = new Point(Fx.S(18), y), Width = W - Fx.S(36) };
                msg.FitHeight();
                Controls.Add(msg);
                y += msg.Height + Fx.S(12);
                var start = new FButton(p, "처음 설정하기", FButtonKind.Accent) { BackColor = p.Window, Location = new Point(Fx.S(18), y) };
                start.Click += (s, e) => { HideFlyout(); Raise(SettingsRequested); };
                Controls.Add(start);
                y += start.Height + Fx.S(18);
            }

            // footer
            var footer = new FooterPanel(p) { Location = new Point(0, y), Size = new Size(W, Fx.S(50)) };
            var footText = new FLabel(m.Footer ?? "", Fx.Font(12), p.SubText) { BackColor = p.Footer, Location = new Point(Fx.S(18), Fx.S(15)), Width = W - Fx.S(150) };
            var open = new FButton(p, "설정 열기", FButtonKind.Link) { BackColor = p.Footer, Height = Fx.S(32) };
            open.AutoSizeToText();
            open.Location = new Point(W - open.Width - Fx.S(12), (footer.Height - open.Height) / 2);
            open.Click += (s, e) => { HideFlyout(); Raise(SettingsRequested); };
            footer.Controls.Add(footText); footer.Controls.Add(open);
            this.footText = footText;
            Controls.Add(footer);

            ClientSize = new Size(W, y + footer.Height);
            ResumeLayout();
            Invalidate();
        }

        private IEnumerable<Control> AllTop()
        {
            foreach (Control c in Controls) yield return c;
        }

        private static void Raise(Action a) { if (a != null) a(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // Windows 11 draws its own rounded border; give Windows 10 a thin one.
            if (!WinTheme.IsWindows11 && p != null)
                using (var pen = new Pen(p.CardBorder)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        private sealed class FooterPanel : Panel
        {
            private readonly Palette p;
            public FooterPanel(Palette palette)
            {
                p = palette;
                BackColor = p.Footer;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(p.Footer);
                using (var pen = new Pen(p.CardBorder)) e.Graphics.DrawLine(pen, 0, 0, Width, 0);
            }
        }
    }
}
