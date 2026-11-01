using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WiPass.UI.Fluent
{
    /// <summary>Colours for the Windows 11 style windows (light or dark, following the app theme).</summary>
    internal sealed class Palette
    {
        public bool Dark;
        public Color Window, Card, CardBorder, Divider, Text, SubText, Disabled;
        public Color Accent, OnAccent, AccentHover, AccentSoft, AccentSoftText;
        public Color Control, ControlBorder, ControlHover, Hover, Pressed, Input, Footer;
        public Color ToggleOff, NavSelected;
        public Color WarnBg, WarnBorder, WarnText, OkBg, OkBorder, OkText, Danger;

        /// <summary>Set by preview tools to force a theme; null = follow Windows.</summary>
        public static bool? Override;

        public static Palette Current()
        {
            bool light = Override.HasValue ? !Override.Value : WinTheme.AppsLight;
            return light ? Light() : DarkPalette();
        }

        private static Palette Light()
        {
            return new Palette
            {
                Dark = false,
                Window = Color.FromArgb(243, 243, 243),
                Card = Color.White,
                CardBorder = Color.FromArgb(229, 229, 229),
                Divider = Color.FromArgb(237, 237, 237),
                Text = Color.FromArgb(26, 26, 26),
                SubText = Color.FromArgb(95, 95, 95),
                Disabled = Color.FromArgb(160, 160, 160),
                Accent = Color.FromArgb(15, 118, 110),
                OnAccent = Color.White,
                AccentHover = Color.FromArgb(17, 94, 89),
                AccentSoft = Color.FromArgb(230, 244, 242),
                AccentSoftText = Color.FromArgb(11, 94, 88),
                Control = Color.FromArgb(251, 251, 251),
                ControlBorder = Color.FromArgb(208, 208, 208),
                ControlHover = Color.FromArgb(243, 243, 243),
                Hover = Color.FromArgb(234, 234, 234),
                Pressed = Color.FromArgb(222, 222, 222),
                Input = Color.White,
                Footer = Color.FromArgb(238, 238, 238),
                ToggleOff = Color.FromArgb(110, 110, 110),
                NavSelected = Color.FromArgb(232, 232, 232),
                WarnBg = Color.FromArgb(255, 248, 230),
                WarnBorder = Color.FromArgb(242, 213, 138),
                WarnText = Color.FromArgb(92, 61, 0),
                OkBg = Color.FromArgb(233, 245, 238),
                OkBorder = Color.FromArgb(184, 222, 199),
                OkText = Color.FromArgb(14, 77, 44),
                Danger = Color.FromArgb(164, 38, 44)
            };
        }

        private static Palette DarkPalette()
        {
            return new Palette
            {
                Dark = true,
                Window = Color.FromArgb(32, 32, 32),
                Card = Color.FromArgb(43, 43, 43),
                CardBorder = Color.FromArgb(58, 58, 58),
                Divider = Color.FromArgb(53, 53, 53),
                Text = Color.White,
                SubText = Color.FromArgb(197, 197, 197),
                Disabled = Color.FromArgb(120, 120, 120),
                Accent = Color.FromArgb(94, 234, 212),
                OnAccent = Color.FromArgb(6, 43, 40),
                AccentHover = Color.FromArgb(153, 246, 228),
                AccentSoft = Color.FromArgb(18, 48, 45),
                AccentSoftText = Color.FromArgb(153, 246, 228),
                Control = Color.FromArgb(50, 50, 50),
                ControlBorder = Color.FromArgb(70, 70, 70),
                ControlHover = Color.FromArgb(60, 60, 60),
                Hover = Color.FromArgb(55, 55, 55),
                Pressed = Color.FromArgb(48, 48, 48),
                Input = Color.FromArgb(45, 45, 45),
                Footer = Color.FromArgb(28, 28, 28),
                ToggleOff = Color.FromArgb(197, 197, 197),
                NavSelected = Color.FromArgb(50, 50, 50),
                WarnBg = Color.FromArgb(67, 53, 25),
                WarnBorder = Color.FromArgb(110, 87, 37),
                WarnText = Color.FromArgb(255, 226, 160),
                OkBg = Color.FromArgb(27, 58, 40),
                OkBorder = Color.FromArgb(44, 92, 64),
                OkText = Color.FromArgb(178, 236, 199),
                Danger = Color.FromArgb(255, 153, 164)
            };
        }
    }

    /// <summary>Drawing and sizing helpers shared by the Windows 11 style controls.</summary>
    internal static class Fx
    {
        public static readonly float Scale;
        private static readonly Dictionary<string, Font> Fonts = new Dictionary<string, Font>();
        public static string FontFamily = "Malgun Gothic";

        static Fx()
        {
            try { using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) Scale = g.DpiX / 96f; }
            catch { Scale = 1f; }
            if (Scale < 1f) Scale = 1f;
        }

        /// <summary>Design pixels (96 DPI) to device pixels.</summary>
        public static int S(float px) { return (int)Math.Round(px * Scale); }

        /// <summary>A font by size in design pixels (cached).</summary>
        public static Font Font(float px, FontStyle style = FontStyle.Regular)
        {
            string key = px + "|" + style;
            Font f;
            if (!Fonts.TryGetValue(key, out f))
            {
                f = new Font(FontFamily, px * 0.75f, style, GraphicsUnit.Point);
                Fonts[key] = f;
            }
            return f;
        }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Max(1f, radius * 2);
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRound(Graphics g, Color c, RectangleF r, float radius)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Round(r, radius))
            using (var b = new SolidBrush(c)) g.FillPath(b, p);
        }

        public static void DrawRound(Graphics g, Color c, RectangleF r, float radius, float width = 1f)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float h = width / 2f;
            var rr = new RectangleF(r.X + h, r.Y + h, r.Width - width, r.Height - width);
            using (GraphicsPath p = Round(rr, radius))
            using (var pen = new Pen(c, width)) g.DrawPath(pen, p);
        }

        /// <summary>Left-aligned single-line text, vertically centred in the rectangle, ellipsis if too long.</summary>
        public static void Text(Graphics g, string text, Font font, Color color, Rectangle r,
                                TextFormatFlags extra = TextFormatFlags.Default)
        {
            TextRenderer.DrawText(g, text, font, r, color,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | extra);
        }

        public static Color Blend(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        /// <summary>Dark title bar for dark mode (Windows 10 2004+ / 11).</summary>
        public static void DarkTitleBar(IntPtr hwnd, bool dark)
        {
            try
            {
                int v = dark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, 20, ref v, sizeof(int));
            }
            catch { }
        }
    }
}
