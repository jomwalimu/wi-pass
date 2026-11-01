using System;
using System.Drawing;
using System.Windows.Forms;
using WiPass.UI.Fluent;
using WiPass.Wifi;

namespace WiPass.UI
{
    /// <summary>Themed yes/no question (used instead of MessageBox so it matches the rest of the app).</summary>
    internal sealed class ConfirmDialog : FWindow
    {
        private ConfirmDialog(string title, string body, string okText, bool danger)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;

            int W = Fx.S(420), pad = Fx.S(24), inner = W - pad * 2;
            int y = Fx.S(20);
            var h = new FLabel(title, Fx.Font(18, FontStyle.Bold), p.Text) { BackColor = p.Window, Location = new Point(pad, y), Width = inner };
            Controls.Add(h);
            y = h.Bottom + Fx.S(10);
            var b = Body(body, pad, y, inner);
            Controls.Add(b);
            y = b.Bottom + Fx.S(22);

            var footer = new FFooter(p) { Location = new Point(0, y), Size = new Size(W, Fx.S(66)) };
            var ok = new FButton(p, okText, FButtonKind.Accent) { DialogResult = DialogResult.OK };
            var cancel = new FButton(p, "취소", FButtonKind.Standard) { DialogResult = DialogResult.Cancel };
            cancel.Location = new Point(W - pad - cancel.Width, (footer.Height - cancel.Height) / 2);
            ok.Location = new Point(cancel.Left - Fx.S(8) - ok.Width, cancel.Top);
            footer.Controls.Add(ok);
            footer.Controls.Add(cancel);
            Controls.Add(footer);
            ClientSize = new Size(W, y + footer.Height);
            AcceptButton = danger ? (IButtonControl)cancel : ok;
            CancelButton = cancel;
        }

        public static bool Ask(IWin32Window owner, string title, string body, string okText, bool danger)
        {
            using (var d = new ConfirmDialog(title, body, okText, danger))
                return d.ShowDialog(owner) == DialogResult.OK;
        }
    }

    /// <summary>Changes the shared network name from settings.</summary>
    internal sealed class SsidDialog : FWindow
    {
        private readonly NetworkPicker picker;
        private readonly FLabel error;
        private readonly string current;

        public string SelectedSsid { get { return picker.SelectedSsid; } }

        public SsidDialog(WifiService wifi, string current)
        {
            this.current = current;
            Text = "학교 와이파이 바꾸기";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;

            int W = Fx.S(520), pad = Fx.S(28), inner = W - pad * 2;
            int y = Fx.S(22);
            Controls.Add(new FLabel("학교 와이파이 바꾸기", Fx.Font(20, FontStyle.Bold), p.Text) { BackColor = p.Window, Location = new Point(pad, y) });
            y += Fx.S(40);
            var body = Body("장소마다 알아 둔 공유기 정보는 새로 배웁니다. 장소와 비밀번호는 그대로 둡니다.", pad, y, inner);
            Controls.Add(body);
            y = body.Bottom + Fx.S(16);

            picker = new NetworkPicker(p, wifi, current, inner, Fx.S(372)) { Location = new Point(pad, y) };
            Controls.Add(picker);
            y = picker.Bottom;

            error = new FLabel("", Fx.Font(13), p.Danger) { BackColor = p.Window, Location = new Point(pad, y), Width = inner, Visible = false };
            Controls.Add(error);
            y += Fx.S(26);

            var footer = new FFooter(p) { Location = new Point(0, y), Size = new Size(W, Fx.S(66)) };
            var ok = new FButton(p, "바꾸기", FButtonKind.Accent);
            var cancel = new FButton(p, "취소", FButtonKind.Standard) { DialogResult = DialogResult.Cancel };
            cancel.Location = new Point(W - Fx.S(24) - cancel.Width, (footer.Height - cancel.Height) / 2);
            ok.Location = new Point(cancel.Left - Fx.S(8) - ok.Width, cancel.Top);
            ok.Click += (s, e) => Confirm();
            footer.Controls.Add(ok);
            footer.Controls.Add(cancel);
            Controls.Add(footer);
            ClientSize = new Size(W, y + footer.Height);
            CancelButton = cancel;
            Shown += (s, e) => picker.Scan(true);
        }

        public void PreviewNetworks(System.Collections.Generic.List<VisibleNetwork> list, bool denied) { picker.SetNetworks(list, denied); }

        private void Confirm()
        {
            if (picker.SelectedSsid == null) { error.Text = "학교 와이파이를 골라 주세요."; error.Visible = true; return; }
            DialogResult = picker.SelectedSsid == current ? DialogResult.Cancel : DialogResult.OK;
            Close();
        }
    }
}
