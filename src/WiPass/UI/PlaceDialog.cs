using System;
using System.Drawing;
using System.Windows.Forms;
using WiPass.UI.Fluent;
using WiPass.Wifi;

namespace WiPass.UI
{
    /// <summary>Small "add place" / "edit place" window: name + password, errors shown inline.</summary>
    internal sealed class PlaceDialog : FWindow
    {
        private readonly bool editing;
        private readonly Func<string, bool> nameTaken;
        private readonly FTextBox txtName, txtPassword;
        private readonly FLabel error;

        public string PlaceName { get { return txtName.Text.Trim(); } }
        /// <summary>Empty when editing and the user left the password unchanged.</summary>
        public string Password { get { return txtPassword.Text; } }

        /// <param name="currentName">null to add a new place.</param>
        /// <param name="nameTaken">Returns true if another place already uses the name.</param>
        public PlaceDialog(string currentName, Func<string, bool> nameTaken)
        {
            editing = currentName != null;
            this.nameTaken = nameTaken;
            Text = editing ? "장소 수정" : "장소 추가";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;

            int W = Fx.S(440), pad = Fx.S(24), inner = W - pad * 2;
            int y = Fx.S(20);
            Controls.Add(new FLabel(editing ? "장소 수정" : "장소 추가", Fx.Font(20, FontStyle.Bold), p.Text) { BackColor = p.Window, Location = new Point(pad, y) });
            y += Fx.S(40);

            Controls.Add(new FLabel("장소 이름", Fx.Font(12), p.SubText) { BackColor = p.Window, Location = new Point(pad, y) });
            y += Fx.S(20);
            txtName = new FTextBox(p, "장소 이름", false, "예: ○○초, 본교, 분교") { BackColor = p.Window, Location = new Point(pad, y), Width = inner, Text = currentName ?? "" };
            txtName.Box.MaxLength = 40;
            Controls.Add(txtName);
            y += txtName.Height + Fx.S(14);

            Controls.Add(new FLabel("와이파이 비밀번호", Fx.Font(12), p.SubText) { BackColor = p.Window, Location = new Point(pad, y) });
            y += Fx.S(20);
            txtPassword = new FTextBox(p, "와이파이 비밀번호", true) { BackColor = p.Window, Location = new Point(pad, y), Width = inner };
            txtPassword.Box.MaxLength = 64;
            Controls.Add(txtPassword);
            y += txtPassword.Height + Fx.S(6);
            var hint = new FLabel(editing ? "바꾸지 않으려면 비워 두세요. 지금 저장된 비밀번호가 그대로 쓰입니다." : "8~63자 · 이 PC에만 암호화해서 저장합니다.",
                Fx.Font(12), p.SubText, true) { BackColor = p.Window, Location = new Point(pad, y), Width = inner };
            hint.FitHeight();
            Controls.Add(hint);
            y += hint.Height + Fx.S(6);

            error = new FLabel("", Fx.Font(13), p.Danger) { BackColor = p.Window, Location = new Point(pad, y), Width = inner, Visible = false };
            Controls.Add(error);
            y += Fx.S(26);

            var footer = new FFooter(p) { Location = new Point(0, y), Size = new Size(W, Fx.S(66)) };
            var ok = new FButton(p, editing ? "수정" : "추가", FButtonKind.Accent);
            var cancel = new FButton(p, "취소", FButtonKind.Standard) { DialogResult = DialogResult.Cancel };
            cancel.Location = new Point(W - pad - cancel.Width, (footer.Height - cancel.Height) / 2);
            ok.Location = new Point(cancel.Left - Fx.S(8) - ok.Width, cancel.Top);
            ok.Click += (s, e) => Confirm();
            footer.Controls.Add(ok);
            footer.Controls.Add(cancel);
            Controls.Add(footer);

            ClientSize = new Size(W, y + footer.Height);
            AcceptButton = ok;
            CancelButton = cancel;
            Shown += (s, e) => { if (editing) txtPassword.Box.Focus(); else txtName.Box.Focus(); };
        }

        private void ShowError(string msg, Control focus)
        {
            error.Text = msg;
            error.Visible = true;
            if (focus != null) focus.Focus();
        }

        private void Confirm()
        {
            if (PlaceName.Length == 0) { ShowError("장소 이름을 입력해 주세요.", txtName.Box); return; }
            if (nameTaken != null && nameTaken(PlaceName)) { ShowError("같은 이름의 장소가 이미 있어요.", txtName.Box); return; }
            if (!editing || Password.Length > 0)
            {
                string err = ProfileBuilder.ValidatePassword(Password);
                if (err != null) { ShowError(err, txtPassword.Box); return; }
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
