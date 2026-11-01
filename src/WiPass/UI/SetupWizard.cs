using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using WiPass.Core;
using WiPass.UI.Fluent;
using WiPass.Wifi;

namespace WiPass.UI
{
    /// <summary>First-run setup in three steps: network → places → finishing touches.</summary>
    internal sealed class SetupWizard : FWindow
    {
        private readonly WifiService wifi;
        private readonly AppSettings work;
        private readonly bool hotspotAvailable;
        private readonly FSteps steps;
        private readonly Panel[] pages = new Panel[3];
        private readonly FFooter footer;
        private readonly FButton back, next;
        private readonly FLabel footInfo;
        private readonly int W, H, pad;
        private int page;

        private NetworkPicker picker;
        private Panel placeList;
        private FTextBox addName, addPassword;
        private FLabel addError;
        private FToggle tAuto, tStartup, tHotspot;

        /// <summary>For previews: pretend location access is on/off instead of asking Windows.</summary>
        public bool? LocationOverride;

        public AppSettings Result { get; private set; }

        public SetupWizard(WifiService wifi, AppSettings settings, bool hotspotAvailable)
        {
            this.wifi = wifi;
            this.hotspotAvailable = hotspotAvailable;
            work = settings.Clone();
            Text = "와이패스 시작하기";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;

            W = Fx.S(600); H = Fx.S(560); pad = Fx.S(36);
            ClientSize = new Size(W, H);

            steps = new FSteps(p, "와이파이", "장소", "마무리") { BackColor = p.Window, Location = new Point(pad, Fx.S(18)), Width = W - pad * 2 };
            Controls.Add(steps);

            int footH = Fx.S(66);
            footer = new FFooter(p) { Location = new Point(0, H - footH), Size = new Size(W, footH) };
            next = new FButton(p, "다음", FButtonKind.Accent);
            back = new FButton(p, "취소", FButtonKind.Standard);
            next.Location = new Point(W - Fx.S(24) - next.Width, (footH - next.Height) / 2);
            back.Location = new Point(next.Left - Fx.S(8) - back.Width, next.Top);
            next.Click += (s, e) => Next();
            back.Click += (s, e) => Back();
            footInfo = new FLabel("", Fx.Font(13), p.SubText) { Location = new Point(Fx.S(24), (footH - Fx.S(20)) / 2), Width = Fx.S(200) };
            footer.Controls.Add(footInfo);
            footer.Controls.Add(back);
            footer.Controls.Add(next);
            Controls.Add(footer);

            for (int i = 0; i < 3; i++)
            {
                pages[i] = new Panel { Location = new Point(0, Fx.S(56)), Size = new Size(W, H - Fx.S(56) - footH), BackColor = p.Window, Visible = false };
                Controls.Add(pages[i]);
            }
            BuildPage1();
            BuildPage2();
            ShowPage(0);
            Shown += (s, e) => { if (picker != null) picker.Scan(true); };
        }

        /// <summary>For previews: show these networks instead of scanning.</summary>
        public void PreviewNetworks(List<VisibleNetwork> networks, bool denied)
        {
            picker.SetNetworks(networks, denied);
        }

        public void PreviewPage(int i) { ShowPage(i); }

        // ------------------------------------------------------------------ pages

        private void BuildPage1()
        {
            Panel pg = pages[0];
            int y = Fx.S(8);
            pg.Controls.Add(Heading("학교 와이파이를 골라 주세요", pad, y, W - pad * 2));
            y += Fx.S(40);
            var body = Body("여러 장소에서 이름이 똑같이 보이는 와이파이를 고르면 됩니다.", pad, y, W - pad * 2);
            pg.Controls.Add(body);
            y = body.Bottom + Fx.S(18);
            picker = new NetworkPicker(p, wifi, work.Ssid, W - pad * 2, pg.Height - y - Fx.S(8)) { Location = new Point(pad, y) };
            picker.Changed += (s, e) => UpdateFooter();
            pg.Controls.Add(picker);
        }

        private void BuildPage2()
        {
            Panel pg = pages[1];
            int y = Fx.S(8);
            pg.Controls.Add(Heading("다니는 장소를 추가해 주세요", pad, y, W - pad * 2));
            y += Fx.S(40);
            var body = Body("장소 이름과 그곳의 와이파이 비밀번호를 넣으세요. 비밀번호는 이 PC에만 암호화해서 저장합니다.", pad, y, W - pad * 2);
            pg.Controls.Add(body);
            y = body.Bottom + Fx.S(16);

            int addH = Fx.S(124);
            placeList = new Panel { Location = new Point(pad, y), Size = new Size(W - pad * 2, pg.Height - y - addH - Fx.S(16)), BackColor = p.Window, AutoScroll = true };
            pg.Controls.Add(placeList);

            var add = new FCard(p) { Location = new Point(pad, pg.Height - addH - Fx.S(8)), Size = new Size(W - pad * 2, addH), Dashed = true, Border = p.Dark ? Fx.Blend(p.Accent, p.Card, 0.5f) : Color.FromArgb(168, 201, 197) };
            int half = (add.Width - Fx.S(16) * 2 - Fx.S(12)) / 2;
            add.Controls.Add(new FLabel("장소 이름", Fx.Font(12), p.SubText) { Location = new Point(Fx.S(16), Fx.S(12)) });
            add.Controls.Add(new FLabel("와이파이 비밀번호", Fx.Font(12), p.SubText) { Location = new Point(Fx.S(16) + half + Fx.S(12), Fx.S(12)) });
            addName = new FTextBox(p, "새 장소 이름", false, "예: 흥왕초") { Location = new Point(Fx.S(16), Fx.S(32)), Width = half };
            addName.Box.MaxLength = 40;
            addPassword = new FTextBox(p, "새 장소 와이파이 비밀번호", true) { Location = new Point(Fx.S(16) + half + Fx.S(12), Fx.S(32)), Width = half };
            addPassword.Box.MaxLength = 64;
            add.Controls.Add(addName);
            add.Controls.Add(addPassword);
            addError = new FLabel("8~63자", Fx.Font(12), p.SubText) { Location = new Point(Fx.S(16), Fx.S(80)), Width = add.Width - Fx.S(150) };
            add.Controls.Add(addError);
            var addBtn = new FButton(p, "추가", FButtonKind.Standard, Ico.Plus) { Height = Fx.S(32) };
            addBtn.AutoSizeToText();
            addBtn.Location = new Point(add.Width - Fx.S(16) - addBtn.Width, Fx.S(76));
            addBtn.Click += (s, e) => TryAdd(true);
            add.Controls.Add(addBtn);
            addPassword.Box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { TryAdd(true); e.SuppressKeyPress = true; } };
            pg.Controls.Add(add);
            RefreshPlaces();
        }

        private void BuildPage3()
        {
            Panel pg = pages[2];
            foreach (Control c in new List<Control>(Each(pg.Controls))) { pg.Controls.Remove(c); c.Dispose(); }
            int y = Fx.S(8);
            int w = W - pad * 2;
            pg.Controls.Add(Heading("거의 다 됐어요", pad, y, w));
            y += Fx.S(40);
            var body = Body("자동 전환에 필요한 윈도우 설정을 확인하고, 원하는 옵션을 골라 주세요.", pad, y, w);
            pg.Controls.Add(body);
            y = body.Bottom + Fx.S(16);

            bool allowed = LocationOverride ?? LocationAllowed();
            FBanner banner = allowed
                ? new FBanner(p, BannerKind.Success, "위치 권한이 켜져 있어요", null, null, null, w)
                : new FBanner(p, BannerKind.Warning, "위치 권한이 꺼져 있어요", "켜야 도착한 장소를 알아볼 수 있습니다. 위치는 기록하지 않아요.", "켜러 가기",
                    () => { try { Process.Start("ms-settings:privacy-location"); } catch { } }, w);
            banner.Location = new Point(pad, y);
            pg.Controls.Add(banner);
            y += banner.Height + Fx.S(12);

            var card = new FCard(p) { Location = new Point(pad, y), Width = w };
            int cy = 0;
            tAuto = AddToggleRow(card, ref cy, "도착하면 자동 전환", "주변 공유기를 보고 장소를 알아서 판단해요", work.AutoSwitch, true, true);
            tStartup = AddToggleRow(card, ref cy, "윈도우를 켤 때 와이패스 실행", null, work.StartWithWindows, true, true);
            tHotspot = AddToggleRow(card, ref cy, "노트북 핫스팟 켜기 (스마트폰 연결용)",
                hotspotAvailable ? "학교·기관 무선망 보안 지침을 먼저 확인하세요" : "이 PC에서는 쓸 수 없어요", work.AutoHotspot && hotspotAvailable, hotspotAvailable, false);
            card.Height = cy;
            pg.Controls.Add(card);
        }

        /// <summary>A row with a title, optional detail and a toggle on the right; adds a divider below unless last.</summary>
        internal static FToggle AddToggleRow(FCard card, ref int y, string title, string detail, bool value, bool enabled, bool divider)
        {
            Palette p = Palette.Current();
            int rowH = detail != null ? Fx.S(62) : Fx.S(48);
            var t = new FLabel(title, Fx.Font(14), enabled ? p.Text : p.Disabled) { Width = card.Width - Fx.S(90) };
            t.Location = new Point(Fx.S(16), y + (detail != null ? Fx.S(11) : (rowH - t.Height) / 2));
            card.Controls.Add(t);
            if (detail != null)
            {
                var d = new FLabel(detail, Fx.Font(12), p.SubText) { Location = new Point(Fx.S(16), t.Bottom + Fx.S(1)), Width = card.Width - Fx.S(90) };
                card.Controls.Add(d);
            }
            var tg = new FToggle(p, title) { Checked = value, Enabled = enabled };
            tg.Location = new Point(card.Width - tg.Width - Fx.S(14), y + (rowH - tg.Height) / 2);
            card.Controls.Add(tg);
            y += rowH;
            if (divider) card.Dividers.Add(y);
            return tg;
        }

        private bool LocationAllowed()
        {
            try
            {
                WifiInterface itf = wifi != null ? wifi.GetPrimaryInterface() : null;
                if (itf == null) return true;
                wifi.GetVisibleNetworks(itf.Id);
                return true;
            }
            catch (LocationDeniedException) { return false; }
            catch { return true; }
        }

        // ------------------------------------------------------------------ places

        private void RefreshPlaces()
        {
            placeList.SuspendLayout();
            foreach (Control c in new List<Control>(Each(placeList.Controls))) { placeList.Controls.Remove(c); c.Dispose(); }
            int y = 0;
            int w = placeList.Width - Fx.S(2);
            foreach (var pl in work.Places)
            {
                Place place = pl;
                var row = new FCard(p) { Location = new Point(0, y), Size = new Size(w, Fx.S(52)) };
                row.Controls.Add(new FIcon(Ico.Building, p.Accent, Fx.S(20)) { Location = new Point(Fx.S(16), Fx.S(16)) });
                row.Controls.Add(new FLabel(place.Name, Fx.Font(15), p.Text) { Location = new Point(Fx.S(48), Fx.S(15)), Width = w - Fx.S(220) });
                var meta = new FLabel("비밀번호 저장됨", Fx.Font(12), p.SubText);
                meta.Location = new Point(w - Fx.S(52) - meta.Width, Fx.S(17));
                row.Controls.Add(meta);
                var del = new FButton(p, "", FButtonKind.Subtle, Ico.Close) { AccessibleName = place.Name + " 삭제", Size = new Size(Fx.S(34), Fx.S(34)) };
                del.Location = new Point(w - Fx.S(44), Fx.S(9));
                del.Click += (s, e) => { work.Places.Remove(place); RefreshPlaces(); UpdateFooter(); };
                row.Controls.Add(del);
                placeList.Controls.Add(row);
                y += row.Height + Fx.S(6);
            }
            placeList.ResumeLayout();
        }

        /// <summary>Adds the place typed in the add box. Returns false if something is wrong (and says what).</summary>
        private bool TryAdd(bool fromButton)
        {
            string name = addName.Text.Trim(), pw = addPassword.Text;
            if (name.Length == 0 && pw.Length == 0) return !fromButton;
            Func<string, bool> err = msg => { addError.Text = msg; addError.Color = p.Danger; addError.Invalidate(); return false; };
            if (name.Length == 0) { addName.Box.Focus(); return err("장소 이름을 입력해 주세요."); }
            foreach (var pl in work.Places)
                if (string.Equals(pl.Name.Trim(), name, StringComparison.CurrentCultureIgnoreCase)) { addName.Box.Focus(); return err("같은 이름의 장소가 이미 있어요."); }
            string v = ProfileBuilder.ValidatePassword(pw);
            if (v != null) { addPassword.Box.Focus(); return err(v); }
            work.Places.Add(new Place { Id = Guid.NewGuid().ToString("N"), Name = name, ProtectedPassword = SecretStore.Protect(pw), Bssids = new List<string>() });
            addName.Text = ""; addPassword.Text = "";
            addError.Text = "8~63자"; addError.Color = p.SubText; addError.Invalidate();
            addName.Box.Focus();
            RefreshPlaces();
            UpdateFooter();
            return true;
        }

        // ------------------------------------------------------------------ navigation

        private void ShowPage(int i)
        {
            page = i;
            if (i == 2) BuildPage3();
            for (int k = 0; k < pages.Length; k++) pages[k].Visible = k == i;
            steps.Current = i;
            back.Text = i == 0 ? "취소" : "이전";
            next.Text = i == 2 ? "시작하기" : "다음";
            back.AutoSizeToText(); next.AutoSizeToText();
            next.Left = W - Fx.S(24) - next.Width;
            back.Left = next.Left - Fx.S(8) - back.Width;
            UpdateFooter();
            if (i == 1) addName.Box.Focus();
        }

        private void UpdateFooter()
        {
            if (page == 0) footInfo.Text = picker.SelectedSsid != null ? "선택: " + picker.SelectedSsid : "";
            else if (page == 1) footInfo.Text = "장소 " + work.Places.Count + "곳";
            else footInfo.Text = "";
        }

        private void Back()
        {
            if (page == 0) { DialogResult = DialogResult.Cancel; Close(); return; }
            ShowPage(page - 1);
        }

        private void Next()
        {
            if (page == 0)
            {
                string ssid = picker.SelectedSsid;
                if (ssid == null) { Warn("학교 와이파이를 골라 주세요."); return; }
                if (ssid != work.Ssid)
                {
                    foreach (var pl in work.Places) pl.Bssids.Clear();
                    work.ProfileTemplate = null;
                    work.CurrentPlaceId = null;
                }
                work.Ssid = ssid;
                ShowPage(1);
            }
            else if (page == 1)
            {
                if (!TryAdd(false)) return;
                if (work.Places.Count == 0) { addError.Text = "장소를 하나 이상 추가해 주세요."; addError.Color = p.Danger; addError.Invalidate(); addName.Box.Focus(); return; }
                ShowPage(2);
            }
            else
            {
                work.AutoSwitch = tAuto.Checked;
                work.StartWithWindows = tStartup.Checked;
                work.AutoHotspot = tHotspot.Checked && hotspotAvailable;
                Result = work;
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        private void Warn(string msg)
        {
            MessageBox.Show(this, msg, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static IEnumerable<Control> Each(Control.ControlCollection cc)
        {
            foreach (Control c in cc) yield return c;
        }
    }
}
