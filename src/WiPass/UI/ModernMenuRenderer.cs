using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WiPass.UI
{
    /// <summary>Colours for the tray menu, following the Windows app theme.</summary>
    internal sealed class MenuPalette
    {
        public Color Background, Text, SubtleText, Disabled, Hover, Separator, Border, Check;

        public static MenuPalette Current()
        {
            return WinTheme.AppsLight ? Light() : Dark();
        }

        private static MenuPalette Light()
        {
            return new MenuPalette
            {
                Background = Color.FromArgb(249, 249, 249),
                Text = Color.FromArgb(26, 26, 26),
                SubtleText = Color.FromArgb(96, 96, 96),
                Disabled = Color.FromArgb(150, 150, 150),
                Hover = Color.FromArgb(234, 234, 234),
                Separator = Color.FromArgb(225, 225, 225),
                Border = Color.FromArgb(214, 214, 214),
                Check = WinTheme.Brand
            };
        }

        private static MenuPalette Dark()
        {
            return new MenuPalette
            {
                Background = Color.FromArgb(44, 44, 44),
                Text = Color.FromArgb(255, 255, 255),
                SubtleText = Color.FromArgb(190, 190, 190),
                Disabled = Color.FromArgb(130, 130, 130),
                Hover = Color.FromArgb(61, 61, 61),
                Separator = Color.FromArgb(70, 70, 70),
                Border = Color.FromArgb(80, 80, 80),
                Check = WinTheme.BrandLight
            };
        }
    }

    /// <summary>
    /// Windows 11 style menu drawing: flat background, rounded hover highlight,
    /// thin separators and a simple check mark. Replaces the classic WinForms look.
    /// </summary>
    internal sealed class ModernMenuRenderer : ToolStripRenderer
    {
        private readonly MenuPalette p;

        public ModernMenuRenderer(MenuPalette palette) { p = palette; }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var b = new SolidBrush(p.Background)) e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            // Windows 11 draws a rounded border and shadow itself; draw a plain one on Windows 10.
            if (WinTheme.IsWindows11) return;
            Rectangle r = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            using (var pen = new Pen(p.Border)) e.Graphics.DrawRectangle(pen, r);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            using (var b = new SolidBrush(p.Background)) e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            var r = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
            using (GraphicsPath path = RoundRect(r, 4))
            using (var b = new SolidBrush(p.Hover))
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.FillPath(b, path);
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item.Tag as string == "subtle") e.TextColor = p.SubtleText;
            else e.TextColor = e.Item.Enabled ? p.Text : p.Disabled;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var pen = new Pen(p.Separator)) e.Graphics.DrawLine(pen, 12, y, e.Item.Width - 12, y);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle r = e.ImageRectangle;
            float s = Math.Min(r.Width, r.Height);
            float x = r.Left + (r.Width - s) / 2f, y = r.Top + (r.Height - s) / 2f;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(p.Check, Math.Max(1.6f, s / 9f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                e.Graphics.DrawLines(pen, new[]
                {
                    new PointF(x + s * 0.22f, y + s * 0.52f),
                    new PointF(x + s * 0.42f, y + s * 0.72f),
                    new PointF(x + s * 0.80f, y + s * 0.30f)
                });
            }
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = p.Text;
            base.OnRenderArrow(e);
        }

        private static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
