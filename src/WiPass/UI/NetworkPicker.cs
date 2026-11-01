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
    /// <summary>
    /// Chooses the shared network name: nearby networks as radio cards (with their security type),
    /// a refresh link and a field to type the name by hand.
    /// </summary>
    internal sealed class NetworkPicker : Panel
    {
        private const int MaxCards = 5;
        private readonly Palette p;
        private readonly WifiService wifi;
        private readonly Panel list;
        private readonly FButton refresh, manualLink;
        private readonly FTextBox manualBox;
        private readonly Timer rescanTimer;
        private List<VisibleNetwork> networks = new List<VisibleNetwork>();
        private string selected;
        private bool locationDenied;

        public event EventHandler Changed;

        public NetworkPicker(Palette palette, WifiService wifi, string current, int width, int height)
        {
            p = palette;
            this.wifi = wifi;
            selected = current;
            Size = new Size(width, height);
            BackColor = p.Window;

            list = new Panel { Location = new Point(0, 0), Size = new Size(width, height - Fx.S(84)), BackColor = p.Window, AutoScroll = true };
            Controls.Add(list);

            int y = list.Bottom + Fx.S(8);
            refresh = new FButton(p, "목록 새로고침", FButtonKind.Link, Ico.Refresh) { BackColor = p.Window, Height = Fx.S(30), Location = new Point(-Fx.S(6), y) };
            refresh.AutoSizeToText();
            refresh.Click += (s, e) => Scan(true);
            manualLink = new FButton(p, "이름 직접 입력", FButtonKind.Link, Ico.Pencil) { BackColor = p.Window, Height = Fx.S(30) };
            manualLink.AutoSizeToText();
            manualLink.Location = new Point(refresh.Right + Fx.S(6), y);
            manualLink.Click += (s, e) => { manualBox.Visible = true; manualBox.Box.Focus(); };
            Controls.Add(refresh);
            Controls.Add(manualLink);

            manualBox = new FTextBox(p, "와이파이 이름 직접 입력", false, "와이파이 이름 (예: JBEDU_WiFi_T)")
            {
                BackColor = p.Window,
                Location = new Point(0, y + Fx.S(36)),
                Width = Math.Min(width, Fx.S(320)),
                Visible = false
            };
            manualBox.Box.TextChanged += (s, e) =>
            {
                if (manualBox.Text.Trim().Length > 0) { selected = manualBox.Text.Trim(); Rebuild(); Raise(); }
            };
            Controls.Add(manualBox);

            rescanTimer = new Timer { Interval = 4000 };
            rescanTimer.Tick += (s, e) => { rescanTimer.Stop(); Scan(false); };
            Disposed += (s, e) => rescanTimer.Dispose();
        }

        public string SelectedSsid { get { return string.IsNullOrEmpty(selected) ? null : selected; } }

        /// <summary>Security of the selected network if it is visible (null when typed by hand / not seen).</summary>
        public VisibleNetwork SelectedNetwork
        {
            get
            {
                foreach (var n in networks) if (n.Ssid == selected) return n;
                return null;
            }
        }

        public bool LocationDenied { get { return locationDenied; } }

        /// <summary>Reads nearby networks (and asks Windows to scan again when <paramref name="rescan"/>).</summary>
        public void Scan(bool rescan)
        {
            locationDenied = false;
            try
            {
                WifiInterface itf = wifi != null ? wifi.GetPrimaryInterface() : null;
                if (itf != null)
                {
                    if (rescan) { wifi.Scan(itf.Id); rescanTimer.Stop(); rescanTimer.Start(); }
                    var seen = new Dictionary<string, VisibleNetwork>();
                    foreach (var n in wifi.GetVisibleNetworks(itf.Id))
                    {
                        if (n.Ssid.Length == 0) continue;
                        VisibleNetwork old;
                        if (!seen.TryGetValue(n.Ssid, out old) || n.Signal > old.Signal) seen[n.Ssid] = n;
                    }
                    networks = new List<VisibleNetwork>(seen.Values);
                    networks.Sort((a, b) => b.Signal.CompareTo(a.Signal));
                }
            }
            catch (LocationDeniedException) { locationDenied = true; }
            catch (Exception ex) { Log.Warn("와이파이 목록 오류: " + ex.Message); }
            Rebuild();
        }

        /// <summary>For previews and tests: show the given networks instead of scanning.</summary>
        public void SetNetworks(List<VisibleNetwork> list, bool denied)
        {
            networks = list;
            locationDenied = denied;
            Rebuild();
        }

        private void Rebuild()
        {
            list.SuspendLayout();
            foreach (Control c in new List<Control>(Each(list.Controls))) { list.Controls.Remove(c); c.Dispose(); }
            int y = 0;
            int w = list.Width - Fx.S(2);

            if (locationDenied)
            {
                var banner = new FBanner(p, BannerKind.Warning, "주변 와이파이를 보려면 위치 권한이 필요해요",
                    "켜 두면 도착한 장소도 알아볼 수 있어요. 위치는 기록하지 않습니다.", "켜러 가기",
                    () => { try { Process.Start("ms-settings:privacy-location"); } catch { } }, w)
                { Location = new Point(0, y) };
                list.Controls.Add(banner);
                y += banner.Height + Fx.S(8);
            }

            var shown = new List<VisibleNetwork>();
            foreach (var n in networks) { if (shown.Count >= MaxCards) break; shown.Add(n); }
            bool selectedShown = false;
            foreach (var n in shown) if (n.Ssid == selected) selectedShown = true;
            if (!selectedShown && !string.IsNullOrEmpty(selected))
                shown.Insert(0, new VisibleNetwork { Ssid = selected, Auth = 0, Signal = 0 });

            foreach (var n in shown)
            {
                SecurityKind kind = n.Auth == 0 ? SecurityKind.Unknown : ProfileBuilder.Classify(n.Auth);
                bool ok = kind == SecurityKind.Personal || kind == SecurityKind.Unknown;
                string sub;
                Color subColor;
                if (n.Auth == 0) { sub = "지금 주변에서 보이지 않아요 · 이름을 확인해 주세요"; subColor = p.SubText; }
                else if (kind == SecurityKind.Personal) { sub = ProfileBuilder.Describe(n.Auth) + " · 지원됩니다"; subColor = p.Dark ? p.Accent : p.AccentSoftText; }
                else if (kind == SecurityKind.Enterprise) { sub = "아이디+비밀번호 방식 · 지원하지 않습니다"; subColor = p.Danger; }
                else if (kind == SecurityKind.Open) { sub = "비밀번호 없는 와이파이 · 필요하지 않습니다"; subColor = p.SubText; }
                else { sub = ProfileBuilder.Describe(n.Auth); subColor = p.SubText; }

                var card = new FRadioCard(p, n.Ssid, sub, subColor, ok)
                {
                    Location = new Point(0, y),
                    Width = w,
                    BackColor = p.Window,
                    Bars = n.Signal > 66 ? 3 : n.Signal > 33 ? 2 : n.Signal > 0 ? 1 : 0,
                    Selected = ok && n.Ssid == selected
                };
                string ssid = n.Ssid;
                card.Picked += (s, e) => { selected = ssid; foreach (Control c in list.Controls) { var rc = c as FRadioCard; if (rc != null) rc.Selected = rc.Title == ssid; } Raise(); };
                list.Controls.Add(card);
                y += card.Height + Fx.S(8);
            }

            if (shown.Count == 0 && !locationDenied)
            {
                var empty = new FLabel("주변에서 와이파이를 찾지 못했어요. 새로고침하거나 이름을 직접 입력해 주세요.", Fx.Font(13), p.SubText, true)
                { BackColor = p.Window, Location = new Point(0, y), Width = w };
                empty.FitHeight();
                list.Controls.Add(empty);
            }
            list.ResumeLayout();
        }

        private static IEnumerable<Control> Each(Control.ControlCollection cc)
        {
            foreach (Control c in cc) yield return c;
        }

        private void Raise()
        {
            var h = Changed;
            if (h != null) h(this, EventArgs.Empty);
        }
    }
}
