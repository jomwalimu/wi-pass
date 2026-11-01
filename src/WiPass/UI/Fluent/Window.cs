using System;
using System.Drawing;
using System.Windows.Forms;

namespace WiPass.UI.Fluent
{
    /// <summary>Base for WiPass windows: theme colours, app icon, dark title bar in dark mode.</summary>
    internal class FWindow : Form
    {
        protected readonly Palette p;

        public FWindow()
        {
            p = Palette.Current();
            BackColor = p.Window;
            ForeColor = p.Text;
            Font = Fx.Font(14);
            Icon = IconFactory.AppIcon;
            AutoScaleMode = AutoScaleMode.None;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Fx.DarkTitleBar(Handle, p.Dark);
        }

        protected FLabel Heading(string text, int x, int y, int width)
        {
            var l = new FLabel(text, Fx.Font(24, FontStyle.Bold), p.Text) { BackColor = p.Window, Location = new Point(x, y), Width = width };
            return l;
        }

        protected FLabel Body(string text, int x, int y, int width)
        {
            var l = new FLabel(text, Fx.Font(14), p.SubText, true) { BackColor = p.Window, Location = new Point(x, y), Width = width };
            l.FitHeight();
            return l;
        }
    }

    /// <summary>Grey footer strip with a top border (buttons go on the right).</summary>
    internal sealed class FFooter : Panel
    {
        private readonly Palette p;
        public FFooter(Palette palette)
        {
            p = palette;
            BackColor = p.Footer;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnControlAdded(ControlEventArgs e)
        {
            e.Control.BackColor = p.Footer;
            base.OnControlAdded(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(p.Footer);
            using (var pen = new Pen(p.CardBorder)) e.Graphics.DrawLine(pen, 0, 0, Width, 0);
        }
    }

    /// <summary>"1 와이파이 — 2 장소 — 3 마무리" step indicator.</summary>
    internal sealed class FSteps : Control
    {
        private readonly Palette p;
        private readonly string[] steps;
        private int current;

        public FSteps(Palette palette, params string[] names)
        {
            p = palette;
            steps = names;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Height = Fx.S(26);
        }

        public int Current
        {
            get { return current; }
            set { current = value; AccessibleName = (value + 1) + "단계 / " + steps.Length + "단계: " + steps[value]; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int x = 0;
            float d = Fx.S(22);
            float cy = (Height - d) / 2f;
            for (int i = 0; i < steps.Length; i++)
            {
                bool done = i < current, now = i == current;
                if (now)
                {
                    using (var b = new SolidBrush(p.Accent)) g.FillEllipse(b, x, cy, d, d);
                    TextRenderer.DrawText(g, (i + 1).ToString(), Fx.Font(12, FontStyle.Bold), new Rectangle(x, (int)cy, (int)d, (int)d), p.OnAccent,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                else if (done)
                {
                    using (var b = new SolidBrush(p.AccentSoft)) g.FillEllipse(b, x, cy, d, d);
                    Icons.Draw(g, Ico.Check, new RectangleF(x + d * 0.2f, cy + d * 0.2f, d * 0.6f, d * 0.6f), p.Accent, 2.8f);
                }
                else
                {
                    using (var pen = new Pen(p.ToggleOff)) g.DrawEllipse(pen, x + 0.5f, cy + 0.5f, d - 1, d - 1);
                    TextRenderer.DrawText(g, (i + 1).ToString(), Fx.Font(12), new Rectangle(x, (int)cy, (int)d, (int)d), p.SubText,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                x += (int)d + Fx.S(6);
                Font f = Fx.Font(13, now ? FontStyle.Bold : FontStyle.Regular);
                Size ts = TextRenderer.MeasureText(steps[i], f);
                TextRenderer.DrawText(g, steps[i], f, new Rectangle(x, 0, ts.Width, Height), now || done ? (p.Dark ? p.Accent : p.AccentSoftText) : p.SubText,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
                x += ts.Width + Fx.S(8);
                if (i < steps.Length - 1)
                {
                    using (var pen = new Pen(done ? p.Accent : p.ControlBorder)) g.DrawLine(pen, x, Height / 2, x + Fx.S(28), Height / 2);
                    x += Fx.S(28) + Fx.S(10);
                }
            }
        }
    }
}
