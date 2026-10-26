using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using WiPass.Core;
using WiPass.Wifi;

namespace WiPass.UI
{
    /// <summary>Network name + places/passwords. Works on a copy; the caller saves <see cref="Result"/>.</summary>
    internal sealed class SettingsForm : Form
    {
        private readonly WifiService wifi;
        private readonly AppSettings original;
        private readonly AppSettings work;
        private readonly bool firstRun;
        private readonly float scale;

        private List<VisibleNetwork> visible = new List<VisibleNetwork>();

        private ComboBox cboSsid;
        private Label lblSecurity;
        private LinkLabel lnkLocation;
        private ListView lvPlaces;
        private TextBox txtName;
        private TextBox txtPassword;
        private CheckBox chkShow;
        private Button btnAdd, btnUpdate, btnDelete;
        private Timer rescanTimer;

        public AppSettings Result { get; private set; }

        public SettingsForm(WifiService wifi, AppSettings settings, bool firstRun)
        {
            this.wifi = wifi;
            this.original = settings;
            this.work = settings.Clone();
            this.firstRun = firstRun;

            Font = new Font("Malgun Gothic", 9.5f);
            using (Graphics g = CreateGraphics()) scale = g.DpiX / 96f;

            Text = AppInfo.DisplayName + " 설정";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            TopMost = firstRun;

            BuildLayout();
            LoadPlaces();
            cboSsid.Text = work.Ssid ?? "";
            UpdateSecurityLabel();

            Shown += (s, e) => RefreshNetworks(true);
            FormClosed += (s, e) => { if (rescanTimer != null) rescanTimer.Dispose(); };
        }

        private int S(int px) { return (int)Math.Round(px * scale); }

        // ------------------------------------------------------------------ layout

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(S(16), S(12), S(16), S(12)),
                Dock = DockStyle.Fill
            };
            Controls.Add(root);

            if (firstRun)
            {
                root.Controls.Add(MakeLabel(
                    "WiPass는 여러 장소(학교)의 와이파이 이름이 모두 같고 비밀번호만 다를 때,\r\n" +
                    "장소에 맞는 비밀번호로 바꿔 접속해 주는 프로그램입니다.", false, Color.FromArgb(60, 60, 60)));
                root.Controls.Add(Spacer(6));
            }

            // 1. network name
            root.Controls.Add(MakeLabel("1. 학교 와이파이 이름", true, SystemColors.ControlText));
            root.Controls.Add(MakeLabel("모든 장소에서 이름이 똑같이 보이는 와이파이를 고르세요.", false, SystemColors.GrayText));

            var ssidRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, S(4), 0, 0) };
            cboSsid = new ComboBox { Width = S(300), DropDownStyle = ComboBoxStyle.DropDown };
            cboSsid.TextChanged += (s, e) => UpdateSecurityLabel();
            var btnRefresh = new Button { Text = "새로고침", AutoSize = true };
            btnRefresh.Click += (s, e) => RefreshNetworks(true);
            ssidRow.Controls.Add(cboSsid);
            ssidRow.Controls.Add(btnRefresh);
            root.Controls.Add(ssidRow);

            lblSecurity = MakeLabel("", false, SystemColors.GrayText);
            root.Controls.Add(lblSecurity);

            lnkLocation = new LinkLabel
            {
                AutoSize = true,
                Text = "주변 와이파이 목록을 읽으려면 윈도우 위치 권한이 필요합니다. [위치 설정 열기]",
                Visible = false,
                Margin = new Padding(0, S(2), 0, 0)
            };
            lnkLocation.LinkArea = new LinkArea(lnkLocation.Text.IndexOf('['), lnkLocation.Text.Length - lnkLocation.Text.IndexOf('['));
            lnkLocation.LinkClicked += (s, e) => OpenLocationSettings();
            root.Controls.Add(lnkLocation);

            root.Controls.Add(Spacer(10));

            // 2. places
            root.Controls.Add(MakeLabel("2. 장소와 비밀번호", true, SystemColors.ControlText));
            root.Controls.Add(MakeLabel("장소 이름(예: ○○초)과 그곳의 와이파이 비밀번호를 하나씩 추가하세요.", false, SystemColors.GrayText));

            lvPlaces = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                Width = S(440),
                Height = S(150),
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                Margin = new Padding(0, S(4), 0, S(4))
            };
            lvPlaces.Columns.Add("장소", S(200));
            lvPlaces.Columns.Add("비밀번호", S(90));
            lvPlaces.Columns.Add("기억한 공유기", S(120));
            lvPlaces.SelectedIndexChanged += (s, e) => OnPlaceSelected();
            root.Controls.Add(lvPlaces);

            var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0) };
            grid.Controls.Add(MakeLabel("장소 이름", false, SystemColors.ControlText, true), 0, 0);
            txtName = new TextBox { Width = S(300), MaxLength = 40 };
            grid.Controls.Add(txtName, 1, 0);
            grid.Controls.Add(MakeLabel("비밀번호", false, SystemColors.ControlText, true), 0, 1);
            txtPassword = new TextBox { Width = S(300), UseSystemPasswordChar = true, MaxLength = 64 };
            grid.Controls.Add(txtPassword, 1, 1);
            chkShow = new CheckBox { Text = "비밀번호 보기", AutoSize = true };
            chkShow.CheckedChanged += (s, e) => txtPassword.UseSystemPasswordChar = !chkShow.Checked;
            grid.Controls.Add(chkShow, 1, 2);
            root.Controls.Add(grid);

            var placeButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, S(2), 0, 0) };
            btnAdd = new Button { Text = "추가", AutoSize = true };
            btnAdd.Click += (s, e) => AddPlace();
            btnUpdate = new Button { Text = "선택한 장소 수정", AutoSize = true, Enabled = false };
            btnUpdate.Click += (s, e) => UpdatePlace();
            btnDelete = new Button { Text = "삭제", AutoSize = true, Enabled = false };
            btnDelete.Click += (s, e) => DeletePlace();
            placeButtons.Controls.Add(btnAdd);
            placeButtons.Controls.Add(btnUpdate);
            placeButtons.Controls.Add(btnDelete);
            root.Controls.Add(placeButtons);

            root.Controls.Add(MakeLabel("수정할 때 비밀번호 칸을 비워 두면 기존 비밀번호가 그대로 유지됩니다.", false, SystemColors.GrayText));
            root.Controls.Add(MakeLabel("비밀번호는 이 PC에만 암호화되어 저장되며, 어디로도 전송되지 않습니다.", false, Color.FromArgb(34, 120, 70)));

            root.Controls.Add(Spacer(10));

            var bottom = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, WrapContents = false };
            var btnCancel = new Button { Text = "취소", AutoSize = true, DialogResult = DialogResult.Cancel, MinimumSize = new Size(S(90), 0) };
            var btnSave = new Button { Text = "저장", AutoSize = true, MinimumSize = new Size(S(90), 0) };
            btnSave.Click += (s, e) => Save();
            bottom.Controls.Add(btnCancel);
            bottom.Controls.Add(btnSave);
            root.Controls.Add(bottom);
            CancelButton = btnCancel;
        }

        private Label MakeLabel(string text, bool bold, Color color, bool middle = false)
        {
            var l = new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = color,
                Margin = new Padding(0, S(middle ? 6 : 2), S(6), 0)
            };
            if (bold) l.Font = new Font(Font, FontStyle.Bold);
            return l;
        }

        private Control Spacer(int h)
        {
            return new Panel { Height = S(h), Width = 1, Margin = new Padding(0) };
        }

        // ------------------------------------------------------------------ networks

        private void RefreshNetworks(bool rescan)
        {
            lnkLocation.Visible = false;
            try
            {
                WifiInterface itf = wifi.GetPrimaryInterface();
                if (itf == null)
                {
                    lblSecurity.Text = "무선랜 장치를 찾지 못했습니다. 와이파이가 켜져 있는지 확인해 주세요.";
                    return;
                }
                if (rescan)
                {
                    wifi.Scan(itf.Id);
                    // Windows takes a few seconds to finish a scan; read the list again shortly.
                    if (rescanTimer == null)
                    {
                        rescanTimer = new Timer { Interval = 4000 };
                        rescanTimer.Tick += (s, e) => { rescanTimer.Stop(); RefreshNetworks(false); };
                    }
                    rescanTimer.Stop();
                    rescanTimer.Start();
                }

                visible = wifi.GetVisibleNetworks(itf.Id);
                visible.Sort((a, b) => b.Signal.CompareTo(a.Signal));
                string typed = cboSsid.Text;
                cboSsid.BeginUpdate();
                cboSsid.Items.Clear();
                var seen = new HashSet<string>();
                foreach (var n in visible)
                    if (n.Ssid.Length > 0 && seen.Add(n.Ssid)) cboSsid.Items.Add(n.Ssid);
                cboSsid.EndUpdate();
                cboSsid.Text = typed;
                cboSsid.SelectionStart = cboSsid.Text.Length;
            }
            catch (LocationDeniedException)
            {
                lnkLocation.Visible = true;
            }
            catch (Exception ex)
            {
                Log.Warn("설정 창 와이파이 목록 오류: " + ex.Message);
                lblSecurity.Text = ex.Message;
            }
            UpdateSecurityLabel();
        }

        private VisibleNetwork FindVisible(string ssid)
        {
            foreach (var n in visible) if (n.Ssid == ssid) return n;
            return null;
        }

        private void UpdateSecurityLabel()
        {
            string ssid = cboSsid.Text.Trim();
            if (ssid.Length == 0) { lblSecurity.Text = ""; return; }
            VisibleNetwork n = FindVisible(ssid);
            if (n == null)
            {
                lblSecurity.ForeColor = SystemColors.GrayText;
                lblSecurity.Text = "지금 주변에서 보이지 않는 이름입니다. 철자를 확인해 주세요.";
                return;
            }
            SecurityKind kind = ProfileBuilder.Classify(n.Auth);
            string desc = "보안 방식: " + ProfileBuilder.Describe(n.Auth);
            switch (kind)
            {
                case SecurityKind.Personal:
                    lblSecurity.ForeColor = Color.FromArgb(34, 120, 70);
                    lblSecurity.Text = desc + "  ✔ 지원합니다";
                    break;
                case SecurityKind.Enterprise:
                    lblSecurity.ForeColor = Color.FromArgb(190, 40, 40);
                    lblSecurity.Text = desc + "  ✖ 아이디+비밀번호 방식은 지원하지 않습니다";
                    break;
                case SecurityKind.Open:
                    lblSecurity.ForeColor = Color.FromArgb(190, 40, 40);
                    lblSecurity.Text = desc + "  ✖ 비밀번호가 없는 와이파이에는 필요 없습니다";
                    break;
                default:
                    lblSecurity.ForeColor = SystemColors.GrayText;
                    lblSecurity.Text = desc;
                    break;
            }
        }

        private static void OpenLocationSettings()
        {
            try { Process.Start("ms-settings:privacy-location"); } catch { }
        }

        // ------------------------------------------------------------------ places

        private void LoadPlaces()
        {
            lvPlaces.BeginUpdate();
            lvPlaces.Items.Clear();
            foreach (var p in work.Places)
            {
                var item = new ListViewItem(p.Name) { Tag = p };
                item.SubItems.Add(p.HasPassword ? "저장됨" : "없음");
                item.SubItems.Add(p.Bssids.Count == 0 ? "아직 없음" : p.Bssids.Count + "개");
                lvPlaces.Items.Add(item);
            }
            lvPlaces.EndUpdate();
            OnPlaceSelected();
        }

        private Place SelectedPlace
        {
            get { return lvPlaces.SelectedItems.Count == 1 ? (Place)lvPlaces.SelectedItems[0].Tag : null; }
        }

        private void OnPlaceSelected()
        {
            Place p = SelectedPlace;
            btnUpdate.Enabled = p != null;
            btnDelete.Enabled = p != null;
            if (p != null)
            {
                txtName.Text = p.Name;
                txtPassword.Text = "";
            }
        }

        private bool NameTaken(string name, Place except)
        {
            foreach (var p in work.Places)
                if (p != except && string.Equals(p.Name.Trim(), name, StringComparison.CurrentCultureIgnoreCase)) return true;
            return false;
        }

        private void AddPlace()
        {
            string name = txtName.Text.Trim();
            string pw = txtPassword.Text;
            if (name.Length == 0) { Warn("장소 이름을 입력해 주세요."); txtName.Focus(); return; }
            if (NameTaken(name, null)) { Warn("같은 이름의 장소가 이미 있습니다."); txtName.Focus(); return; }
            string err = ProfileBuilder.ValidatePassword(pw);
            if (err != null) { Warn(err); txtPassword.Focus(); return; }

            work.Places.Add(new Place
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                ProtectedPassword = SecretStore.Protect(pw),
                Bssids = new List<string>()
            });
            txtName.Text = "";
            txtPassword.Text = "";
            LoadPlaces();
            txtName.Focus();
        }

        private void UpdatePlace()
        {
            Place p = SelectedPlace;
            if (p == null) return;
            string name = txtName.Text.Trim();
            if (name.Length == 0) { Warn("장소 이름을 입력해 주세요."); return; }
            if (NameTaken(name, p)) { Warn("같은 이름의 장소가 이미 있습니다."); return; }
            if (txtPassword.Text.Length > 0)
            {
                string err = ProfileBuilder.ValidatePassword(txtPassword.Text);
                if (err != null) { Warn(err); txtPassword.Focus(); return; }
                p.ProtectedPassword = SecretStore.Protect(txtPassword.Text);
            }
            p.Name = name;
            txtPassword.Text = "";
            LoadPlaces();
        }

        private void DeletePlace()
        {
            Place p = SelectedPlace;
            if (p == null) return;
            if (MessageBox.Show(this, "'" + p.Name + "' 장소를 삭제할까요?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            work.Places.Remove(p);
            if (work.CurrentPlaceId == p.Id) work.CurrentPlaceId = null;
            txtName.Text = "";
            txtPassword.Text = "";
            LoadPlaces();
        }

        // ------------------------------------------------------------------ save

        private void Save()
        {
            string ssid = cboSsid.Text.Trim();
            if (ssid.Length == 0) { Warn("학교 와이파이 이름을 고르거나 입력해 주세요."); cboSsid.Focus(); return; }

            // An unsaved entry in the boxes is a common slip: offer to add it.
            if (SelectedPlace == null && txtName.Text.Trim().Length > 0)
            {
                if (MessageBox.Show(this, "입력 중인 장소 '" + txtName.Text.Trim() + "'을(를) 추가할까요?", Text,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    int before = work.Places.Count;
                    AddPlace();
                    if (work.Places.Count == before) return;
                }
            }

            VisibleNetwork n = FindVisible(ssid);
            if (n != null)
            {
                SecurityKind kind = ProfileBuilder.Classify(n.Auth);
                if (kind == SecurityKind.Enterprise)
                {
                    Warn("'" + ssid + "'은(는) 아이디와 비밀번호를 함께 쓰는 방식(" + ProfileBuilder.Describe(n.Auth) + ")입니다.\r\nWiPass는 비밀번호만 입력하는 와이파이만 지원합니다.");
                    return;
                }
                if (kind == SecurityKind.Open)
                {
                    Warn("'" + ssid + "'은(는) 비밀번호가 없는 와이파이라 WiPass가 필요하지 않습니다.");
                    return;
                }
            }
            else if (ssid != original.Ssid)
            {
                if (MessageBox.Show(this, "'" + ssid + "'은(는) 지금 주변에서 보이지 않는 이름입니다.\r\n철자가 맞다면 그대로 저장할까요?", Text,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            }

            if (work.Places.Count == 0) { Warn("장소를 하나 이상 추가해 주세요."); txtName.Focus(); return; }
            foreach (var p in work.Places)
                if (!p.HasPassword) { Warn("'" + p.Name + "'의 비밀번호를 입력해 주세요."); return; }

            if (!string.IsNullOrEmpty(original.Ssid) && ssid != original.Ssid)
            {
                if (MessageBox.Show(this, "와이파이 이름을 바꾸면 장소마다 기억한 공유기 정보가 초기화됩니다. 계속할까요?", Text,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                foreach (var p in work.Places) p.Bssids.Clear();
                work.ProfileTemplate = null;
                work.CurrentPlaceId = null;
            }
            if (string.IsNullOrEmpty(original.Ssid) || ssid != original.Ssid) work.ProfileTemplate = null;

            work.Ssid = ssid;
            Result = work;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void Warn(string msg)
        {
            MessageBox.Show(this, msg, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
