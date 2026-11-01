using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WiPass.UI.Fluent
{
    /// <summary>Toggle switch (Windows 11 style). Space toggles it from the keyboard.</summary>
    internal sealed class FToggle : Control
    {
        private readonly Palette p;
        private bool on, hover;
        public event EventHandler CheckedChanged;

        public FToggle(Palette palette, string accessibleName)
        {
            p = palette;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Size = new Size(Fx.S(44), Fx.S(24));
            TabStop = true;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.CheckButton;
            AccessibleName = accessibleName;
        }

        public bool Checked
        {
            get { return on; }
            set
            {
                if (on == value) return;
                on = value;
                AccessibleDescription = on ? "켜짐" : "꺼짐";
                Invalidate();
                var h = CheckedChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) { if (Enabled) { Focus(); Checked = !Checked; } base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space && Enabled) { Checked = !Checked; e.Handled = true; } base.OnKeyDown(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            float w = Fx.S(40), h = Fx.S(20);
            var r = new RectangleF((Width - w) / 2f, (Height - h) / 2f, w, h);
            Color accent = Enabled ? (hover ? p.AccentHover : p.Accent) : p.Disabled;
            Color off = Enabled ? p.ToggleOff : p.Disabled;
            if (on)
            {
                Fx.FillRound(g, accent, r, h / 2f);
                float d = h - Fx.S(8);
                using (var b = new SolidBrush(p.OnAccent)) g.FillEllipse(b, r.Right - Fx.S(4) - d, r.Top + Fx.S(4), d, d);
            }
            else
            {
                if (hover && Enabled) Fx.FillRound(g, p.ControlHover, r, h / 2f);
                Fx.DrawRound(g, off, r, h / 2f, Math.Max(1f, Fx.S(1.5f)));
                float d = h - Fx.S(10);
                using (var b = new SolidBrush(off)) g.FillEllipse(b, r.Left + Fx.S(5), r.Top + Fx.S(5), d, d);
            }
            if (Focused && ShowFocusCues)
                Fx.DrawRound(g, p.Text, new RectangleF(r.X - Fx.S(3), r.Y - Fx.S(3), r.Width + Fx.S(6), r.Height + Fx.S(6)), h / 2f + Fx.S(3), 1.5f);
        }
    }

    internal enum FButtonKind { Accent, Standard, Subtle, Link }

    /// <summary>Rounded button: accent (main action), standard, subtle (icon/ghost) or link-style.</summary>
    internal sealed class FButton : Button
    {
        private readonly Palette p;
        private bool hover, down;
        public FButtonKind Kind;
        public Ico Icon;
        public bool Bold;

        public FButton(Palette palette, string text, FButtonKind kind, Ico icon = Ico.None)
        {
            p = palette;
            Kind = kind;
            Icon = icon;
            Text = text;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            Font = Fx.Font(14);
            Cursor = Cursors.Hand;
            Height = Fx.S(34);
            if (string.IsNullOrEmpty(text) && icon != Ico.None) Size = new Size(Fx.S(34), Fx.S(34));
            else AutoSizeToText();
        }

        public void AutoSizeToText()
        {
            Size t = TextRenderer.MeasureText(Text ?? "", Font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            int w = t.Width + Fx.S(Kind == FButtonKind.Link ? 16 : 28) + (Icon != Ico.None ? Fx.S(24) : 0);
            Width = Math.Max(Kind == FButtonKind.Accent || Kind == FButtonKind.Standard ? Fx.S(96) : 0, w);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            var r = new RectangleF(0, 0, Width, Height);
            float rad = Fx.S(6);
            Color fg = p.Text;
            switch (Kind)
            {
                case FButtonKind.Accent:
                    Fx.FillRound(g, !Enabled ? p.Disabled : down ? Fx.Blend(p.Accent, p.Window, 0.2f) : hover ? p.AccentHover : p.Accent, r, rad);
                    fg = Enabled ? p.OnAccent : p.Window;
                    break;
                case FButtonKind.Standard:
                    Fx.FillRound(g, down ? p.Pressed : hover ? p.ControlHover : p.Control, r, rad);
                    Fx.DrawRound(g, p.ControlBorder, r, rad);
                    fg = Enabled ? p.Text : p.Disabled;
                    break;
                case FButtonKind.Subtle:
                    if (hover || down) Fx.FillRound(g, down ? p.Pressed : p.Hover, r, rad);
                    fg = Enabled ? p.Text : p.Disabled;
                    break;
                case FButtonKind.Link:
                    if (hover || down) Fx.FillRound(g, p.Hover, r, rad);
                    fg = Enabled ? p.Accent : p.Disabled;
                    if (!p.Dark && Enabled) fg = p.AccentSoftText;
                    break;
            }

            bool hasText = !string.IsNullOrEmpty(Text);
            Font f = Bold ? Fx.Font(Font.SizeInPoints / 0.75f, FontStyle.Bold) : Font;
            Size ts = hasText ? TextRenderer.MeasureText(Text, f, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine) : Size.Empty;
            int iconSize = Fx.S(16);
            int gap = hasText && Icon != Ico.None ? Fx.S(8) : 0;
            int total = (Icon != Ico.None ? iconSize : 0) + gap + ts.Width;
            int x = (Width - total) / 2;
            if (Icon != Ico.None)
            {
                Icons.Draw(g, Icon, new RectangleF(x, (Height - iconSize) / 2f, iconSize, iconSize), fg);
                x += iconSize + gap;
            }
            if (hasText)
                TextRenderer.DrawText(g, Text, f, new Rectangle(x, 0, ts.Width + 2, Height), fg,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            if (Focused && ShowFocusCues)
                Fx.DrawRound(g, p.Text, new RectangleF(1, 1, Width - 2, Height - 2), rad, 1.5f);
        }
    }

    /// <summary>Rounded card. Child controls get the card colour as their background.</summary>
    internal class FCard : Panel
    {
        protected readonly Palette p;
        public Color Fill, Border;
        public readonly List<int> Dividers = new List<int>();
        public float Radius;
        public bool Dashed;

        public FCard(Palette palette)
        {
            p = palette;
            Fill = p.Card;
            Border = p.CardBorder;
            Radius = Fx.S(8);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnControlAdded(ControlEventArgs e)
        {
            e.Control.BackColor = Fill;
            base.OnControlAdded(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : p.Window);
            var r = new RectangleF(0, 0, Width, Height);
            Fx.FillRound(g, Fill, r, Radius);
            if (Dashed)
            {
                using (var pen = new Pen(Border, 1f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                using (var path = Fx.Round(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Radius))
                    g.DrawPath(pen, path);
            }
            else Fx.DrawRound(g, Border, r, Radius);
            using (var pen = new Pen(p.Divider))
                foreach (int y in Dividers) g.DrawLine(pen, 1, y, Width - 2, y);
        }
    }

    /// <summary>Text label drawn with GDI (crisp, theme colours, optional ellipsis/wrap).</summary>
    internal sealed class FLabel : Control
    {
        public Color Color;
        public bool Wrap;

        public FLabel(string text, Font font, Color color, bool wrap = false)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Text = text;
            Font = font;
            Color = color;
            Wrap = wrap;
            AccessibleRole = AccessibleRole.StaticText;
            Size s = TextRenderer.MeasureText(text ?? "", font, Size.Empty, TextFormatFlags.NoPrefix);
            Size = new Size(s.Width + 2, s.Height);
        }

        /// <summary>Height needed to show the text wrapped at the current width.</summary>
        public int FitHeight()
        {
            Size s = TextRenderer.MeasureText(Text ?? "", Font, new Size(Width, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
            Height = s.Height;
            return Height;
        }

        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            TextFormatFlags f = TextFormatFlags.NoPrefix | TextFormatFlags.Left;
            f |= Wrap ? TextFormatFlags.WordBreak : (TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Color, f);
        }
    }

    /// <summary>A small vector icon as a control (decorative unless given an accessible name).</summary>
    internal sealed class FIcon : Control
    {
        public Ico Icon;
        public Color Color;
        public Color? Circle;
        public float Stroke = 1.7f;

        public FIcon(Ico icon, Color color, int size)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Icon = icon;
            Color = color;
            Size = new Size(size, size);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            RectangleF r = ClientRectangle;
            if (Circle.HasValue)
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var b = new SolidBrush(Circle.Value)) g.FillEllipse(b, r);
                float inset = r.Width * 0.25f;
                r = new RectangleF(r.X + inset, r.Y + inset, r.Width - inset * 2, r.Height - inset * 2);
            }
            if (Icon == Ico.Logo) Icons.DrawLogo(g, r, Color);
            else Icons.Draw(g, Icon, r, Color, Stroke);
        }
    }

    /// <summary>Rounded text box with an accent underline when focused; optional password reveal.</summary>
    internal sealed class FTextBox : Panel
    {
        private readonly Palette p;
        public readonly TextBox Box;
        private readonly FButton reveal;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        public FTextBox(Palette palette, string accessibleName, bool password = false, string placeholder = null)
        {
            p = palette;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = Fx.S(34);
            Box = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Font = password ? new Font("Segoe UI", 10.5f) : Fx.Font(14),
                BackColor = p.Input,
                ForeColor = p.Text,
                UseSystemPasswordChar = password,
                AccessibleName = accessibleName
            };
            Controls.Add(Box);
            if (password)
            {
                reveal = new FButton(p, "", FButtonKind.Subtle, Ico.Eye) { AccessibleName = "비밀번호 보기", TabStop = false };
                reveal.Size = new Size(Fx.S(30), Fx.S(28));
                reveal.BackColor = p.Input;
                reveal.Click += (s, e) => { Box.UseSystemPasswordChar = !Box.UseSystemPasswordChar; Box.Focus(); };
                Controls.Add(reveal);
            }
            Box.GotFocus += (s, e) => Invalidate();
            Box.LostFocus += (s, e) => Invalidate();
            if (!string.IsNullOrEmpty(placeholder))
                Box.HandleCreated += (s, e) =>
                {
                    try { SendMessage(Box.Handle, 0x1501, (IntPtr)1, placeholder); } // EM_SETCUEBANNER: grey hint text
                    catch { }
                };
        }

        public override string Text { get { return Box.Text; } set { Box.Text = value; } }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (Box == null) return; // layout can run while the constructor is still building
            int padL = Fx.S(10), right = reveal != null ? Fx.S(34) : Fx.S(10);
            Box.Width = Width - padL - right;
            Box.Left = padL;
            Box.Top = (Height - Box.Height) / 2;
            if (reveal != null) { reveal.Left = Width - reveal.Width - Fx.S(3); reveal.Top = (Height - reveal.Height) / 2; }
        }

        protected override void OnClick(EventArgs e) { Box.Focus(); base.OnClick(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : p.Card);
            var r = new RectangleF(0, 0, Width, Height);
            Fx.FillRound(g, p.Input, r, Fx.S(4));
            Fx.DrawRound(g, p.ControlBorder, r, Fx.S(4));
            if (Box.Focused)
                using (var b = new SolidBrush(p.Accent)) g.FillRectangle(b, Fx.S(2), Height - Fx.S(2), Width - Fx.S(4), Fx.S(2));
        }
    }

    /// <summary>Selectable card with a radio mark, title and subtitle (for choosing the Wi-Fi network).</summary>
    internal sealed class FRadioCard : Control
    {
        private readonly Palette p;
        private bool selected, hover;
        public string Title, Subtitle;
        public Color SubtitleColor;
        public int Bars = 3;
        public event EventHandler Picked;

        public FRadioCard(Palette palette, string title, string subtitle, Color subColor, bool enabled)
        {
            p = palette;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Title = title; Subtitle = subtitle; SubtitleColor = subColor;
            Enabled = enabled;
            Height = Fx.S(62);
            TabStop = enabled;
            Cursor = enabled ? Cursors.Hand : Cursors.Default;
            AccessibleRole = AccessibleRole.RadioButton;
            AccessibleName = title + ", " + subtitle;
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
        protected override void OnClick(EventArgs e) { if (Enabled) { Focus(); Pick(); } base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if ((e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) && Enabled) { Pick(); e.Handled = true; } base.OnKeyDown(e); }

        private void Pick()
        {
            var h = Picked;
            if (h != null) h(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : p.Window);
            var r = new RectangleF(0, 0, Width, Height);
            Fx.FillRound(g, !Enabled ? Fx.Blend(p.Card, p.Window, 0.5f) : hover ? Fx.Blend(p.Card, p.Hover, 0.5f) : p.Card, r, Fx.S(8));
            Fx.DrawRound(g, selected ? p.Accent : p.CardBorder, r, Fx.S(8), selected ? Fx.S(1.5f) : 1f);

            // radio mark
            float d = Fx.S(18), cx = Fx.S(16), cy = (Height - d) / 2f;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if (selected)
            {
                using (var b = new SolidBrush(p.Accent)) g.FillEllipse(b, cx, cy, d, d);
                using (var b = new SolidBrush(p.OnAccent)) g.FillEllipse(b, cx + d * 0.3f, cy + d * 0.3f, d * 0.4f, d * 0.4f);
            }
            else
                using (var pen = new Pen(Enabled ? p.ToggleOff : p.Disabled, Fx.S(1.2f))) g.DrawEllipse(pen, cx, cy, d, d);

            int tx = Fx.S(48);
            int right = Width - Fx.S(48);
            Fx.Text(g, Title, Fx.Font(15, selected ? FontStyle.Bold : FontStyle.Regular), Enabled ? p.Text : p.SubText,
                new Rectangle(tx, Fx.S(10), right - tx, Fx.S(22)));
            Fx.Text(g, Subtitle, Fx.Font(12), SubtitleColor, new Rectangle(tx, Fx.S(32), right - tx, Fx.S(18)));

            // signal bars
            Color ic = Enabled ? p.Text : p.Disabled;
            var box = new RectangleF(Width - Fx.S(38), (Height - Fx.S(22)) / 2f, Fx.S(22), Fx.S(22));
            float k = box.Width / 24f;
            using (var pen = new Pen(ic, Math.Max(1f, 1.8f * k)) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
            {
                float[] radii = { 4.5f, 9f, 13.5f };
                for (int i = 0; i < radii.Length; i++)
                {
                    if (i >= Bars) pen.Color = Fx.Blend(ic, p.Card, 0.7f);
                    float rr = radii[i];
                    g.DrawArc(pen, box.X + (12 - rr) * k, box.Y + (20 - rr) * k, 2 * rr * k, 2 * rr * k, 225, 90);
                }
            }
            using (var b = new SolidBrush(ic)) g.FillEllipse(b, box.X + 10.7f * k, box.Y + 18.1f * k, 2.6f * k, 2.6f * k);

            if (Focused && ShowFocusCues)
                Fx.DrawRound(g, p.Text, new RectangleF(2, 2, Width - 4, Height - 4), Fx.S(7), 1.5f);
        }
    }

    /// <summary>Place button in the flyout grid; the current place is highlighted with a check.</summary>
    internal sealed class FPlaceTile : Button
    {
        private readonly Palette p;
        private bool hover, down;
        public bool Current;

        public FPlaceTile(Palette palette, string name, bool current)
        {
            p = palette;
            Text = name;
            Current = current;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            Cursor = Cursors.Hand;
            Height = Fx.S(44);
            AccessibleName = name + (current ? " (현재 장소)" : "");
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            var r = new RectangleF(0, 0, Width, Height);
            if (Current)
            {
                Fx.FillRound(g, p.AccentSoft, r, Fx.S(6));
                Fx.DrawRound(g, p.Accent, r, Fx.S(6), Fx.S(1.5f));
            }
            else
            {
                Fx.FillRound(g, down ? p.Pressed : hover ? p.ControlHover : p.Card, r, Fx.S(6));
                Fx.DrawRound(g, p.CardBorder, r, Fx.S(6));
            }
            Color fg = !Enabled ? p.Disabled : Current ? p.AccentSoftText : p.Text;
            int pad = Fx.S(12);
            int iconW = Current ? Fx.S(22) : 0;
            Fx.Text(g, Text, Fx.Font(14, Current ? FontStyle.Bold : FontStyle.Regular), fg, new Rectangle(pad, 0, Width - pad * 2 - iconW, Height));
            if (Current) Icons.Draw(g, Ico.Check, new RectangleF(Width - pad - Fx.S(16), (Height - Fx.S(16)) / 2f, Fx.S(16), Fx.S(16)), p.Accent, 2.4f);
            if (Focused && ShowFocusCues) Fx.DrawRound(g, p.Text, new RectangleF(1, 1, Width - 2, Height - 2), Fx.S(6), 1.5f);
        }
    }

    internal enum BannerKind { Warning, Success }

    /// <summary>Status strip (warning / success) with an icon, text and an optional action.</summary>
    internal sealed class FBanner : FCard
    {
        public FBanner(Palette palette, BannerKind kind, string title, string detail, string actionText, Action action, int width)
            : base(palette)
        {
            bool warn = kind == BannerKind.Warning;
            Fill = warn ? p.WarnBg : p.OkBg;
            Border = warn ? p.WarnBorder : p.OkBorder;
            Color text = warn ? p.WarnText : p.OkText;
            Width = width;
            int pad = Fx.S(16);

            var icon = new FIcon(warn ? Ico.Pin : Ico.CircleCheck, text, Fx.S(22));
            Controls.Add(icon);
            FButton btn = null;
            if (actionText != null)
            {
                btn = new FButton(p, actionText, warn ? FButtonKind.Standard : FButtonKind.Link);
                btn.Height = Fx.S(32);
                btn.AutoSizeToText();
                btn.Click += (s, e) => { if (action != null) action(); };
                Controls.Add(btn);
            }
            int textLeft = pad + Fx.S(34);
            int textRight = width - pad - (btn != null ? btn.Width + Fx.S(12) : 0);
            var t1 = new FLabel(title, Fx.Font(14, warn ? FontStyle.Bold : FontStyle.Regular), text) { Width = textRight - textLeft };
            Controls.Add(t1);
            FLabel t2 = null;
            if (!string.IsNullOrEmpty(detail))
            {
                t2 = new FLabel(detail, Fx.Font(12), text, true) { Width = textRight - textLeft };
                t2.FitHeight();
                Controls.Add(t2);
            }
            int contentH = t1.Height + (t2 != null ? Fx.S(2) + t2.Height : 0);
            Height = Math.Max(Fx.S(48), contentH + pad);
            int top = (Height - contentH) / 2;
            t1.Location = new Point(textLeft, top);
            if (t2 != null) t2.Location = new Point(textLeft, t1.Bottom + Fx.S(2));
            icon.Location = new Point(pad, (Height - icon.Height) / 2);
            if (btn != null) btn.Location = new Point(width - pad - btn.Width + Fx.S(4), (Height - btn.Height) / 2);
        }
    }
}
