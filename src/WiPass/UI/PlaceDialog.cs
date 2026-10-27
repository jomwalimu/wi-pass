using System;
using System.Drawing;
using System.Windows.Forms;
using WiPass.Wifi;

namespace WiPass.UI
{
    /// <summary>Small "add place" / "edit place" window: name + password.</summary>
    internal sealed class PlaceDialog : Form
    {
        private readonly bool editing;
        private readonly Func<string, bool> nameTaken;
        private readonly float scale;
        private TextBox txtName;
        private TextBox txtPassword;

        public string PlaceName { get { return txtName.Text.Trim(); } }
        /// <summary>Empty when editing and the user left the password unchanged.</summary>
        public string Password { get { return txtPassword.Text; } }

        /// <param name="currentName">null to add a new place.</param>
        /// <param name="nameTaken">Returns true if another place already uses the name.</param>
        public PlaceDialog(string currentName, Func<string, bool> nameTaken)
        {
            editing = currentName != null;
            this.nameTaken = nameTaken;

            Font = new Font("Malgun Gothic", 9.5f);
            using (Graphics g = CreateGraphics()) scale = g.DpiX / 96f;
            Text = editing ? "장소 수정" : "장소 추가";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var root = new TableLayoutPanel
            {
                ColumnCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(S(16), S(14), S(16), S(12))
            };
            Controls.Add(root);

            root.Controls.Add(Caption("장소 이름"), 0, 0);
            txtName = new TextBox { Width = S(260), MaxLength = 40, Text = currentName ?? "" };
            root.Controls.Add(txtName, 1, 0);
            root.Controls.Add(Hint("예: ○○초, 본교, 분교"), 1, 1);

            root.Controls.Add(Caption("비밀번호"), 0, 2);
            // Passwords are Latin letters, digits and symbols: Malgun Gothic spaces those unevenly,
            // so the password box uses the Windows UI Latin font instead.
            txtPassword = new TextBox
            {
                Width = S(260),
                MaxLength = 64,
                UseSystemPasswordChar = true,
                Font = new Font("Segoe UI", 10f)
            };
            root.Controls.Add(txtPassword, 1, 2);

            var chkShow = new CheckBox { Text = "비밀번호 보기", AutoSize = true, Margin = new Padding(0, S(2), 0, 0) };
            chkShow.CheckedChanged += (s, e) => txtPassword.UseSystemPasswordChar = !chkShow.Checked;
            root.Controls.Add(chkShow, 1, 3);

            root.Controls.Add(Hint(editing
                ? "비밀번호를 바꾸지 않으려면 비워 두세요.\r\n지금 저장된 비밀번호가 그대로 쓰입니다."
                : "이 장소 와이파이의 비밀번호 (8~63자)"), 1, 4);

            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, S(12), 0, 0)
            };
            var btnCancel = new Button { Text = "취소", AutoSize = true, MinimumSize = new Size(S(80), 0), DialogResult = DialogResult.Cancel };
            var btnOk = new Button { Text = editing ? "수정" : "추가", AutoSize = true, MinimumSize = new Size(S(80), 0) };
            btnOk.Click += (s, e) => Confirm();
            buttons.Controls.Add(btnCancel);
            buttons.Controls.Add(btnOk);
            root.Controls.Add(buttons, 0, 5);
            root.SetColumnSpan(buttons, 2);

            AcceptButton = btnOk;
            CancelButton = btnCancel;
            Shown += (s, e) => { if (editing) txtPassword.Focus(); else txtName.Focus(); };
        }

        private int S(int px) { return (int)Math.Round(px * scale); }

        private Label Caption(string text)
        {
            return new Label { Text = text, AutoSize = true, Margin = new Padding(0, S(6), S(10), 0) };
        }

        private Label Hint(string text)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, S(2), 0, S(6)) };
        }

        private void Confirm()
        {
            if (PlaceName.Length == 0) { Warn("장소 이름을 입력해 주세요."); txtName.Focus(); return; }
            if (nameTaken != null && nameTaken(PlaceName)) { Warn("같은 이름의 장소가 이미 있습니다."); txtName.Focus(); return; }
            if (!editing || Password.Length > 0)
            {
                string err = ProfileBuilder.ValidatePassword(Password);
                if (err != null) { Warn(err); txtPassword.Focus(); return; }
            }
            DialogResult = DialogResult.OK;
            Close();
        }

        private void Warn(string msg)
        {
            MessageBox.Show(this, msg, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
