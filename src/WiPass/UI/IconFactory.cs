using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WiPass.UI
{
    internal enum TrayState { Idle, Busy, Connected, Failed }

    /// <summary>
    /// Tray icons in the Windows 11 style: a single-colour WiPass glyph (white on a dark taskbar,
    /// near-black on a light one) with a small status dot in the bottom-right corner.
    /// Drawn at the system's small-icon size so it stays sharp at any display scale.
    /// </summary>
    internal static class IconFactory
    {
        private static readonly Dictionary<string, Icon> Cache = new Dictionary<string, Icon>();
        private static Icon appIcon;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        /// <summary>The program icon (embedded in WiPass.exe), for windows.</summary>
        public static Icon AppIcon
        {
            get
            {
                if (appIcon == null)
                {
                    try { appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
                    catch { appIcon = Get(TrayState.Connected); }
                }
                return appIcon;
            }
        }

        /// <summary>True when the taskbar uses the light theme.</summary>
        public static bool LightTaskbar
        {
            get
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    {
                        object v = k != null ? k.GetValue("SystemUsesLightTheme") : null;
                        return v is int && (int)v == 1;
                    }
                }
                catch { return false; }
            }
        }

        /// <summary>Forget cached icons (call after the Windows theme or display scale changes).</summary>
        public static void Invalidate()
        {
            foreach (var icon in Cache.Values) icon.Dispose();
            Cache.Clear();
        }

        public static Icon Get(TrayState state)
        {
            bool light = LightTaskbar;
            int size = Math.Max(16, SystemInformation.SmallIconSize.Width);
            string key = state + "|" + light + "|" + size;
            Icon icon;
            if (Cache.TryGetValue(key, out icon)) return icon;
            icon = Draw(state, light, size);
            Cache[key] = icon;
            return icon;
        }

        private static Color DotColor(TrayState s, bool light)
        {
            switch (s)
            {
                case TrayState.Connected: return light ? Color.FromArgb(22, 163, 74) : Color.FromArgb(34, 197, 94);
                case TrayState.Busy: return light ? Color.FromArgb(217, 119, 6) : Color.FromArgb(245, 158, 11);
                case TrayState.Failed: return light ? Color.FromArgb(220, 38, 38) : Color.FromArgb(239, 68, 68);
                default: return Color.Empty;
            }
        }

        private static Icon Draw(TrayState state, bool light, int size)
        {
            using (Bitmap bmp = Render(state, light, size))
            {
                IntPtr h = bmp.GetHicon();
                Icon tmp = Icon.FromHandle(h);
                Icon owned = (Icon)tmp.Clone();
                tmp.Dispose();
                DestroyIcon(h);
                return owned;
            }
        }

        /// <summary>The tray image as a bitmap (separate from icon creation so it can be previewed).</summary>
        internal static Bitmap Render(TrayState state, bool light, int size)
        {
            Color glyph = light ? Color.FromArgb(31, 31, 31) : Color.White;
            var bmp = new Bitmap(size, size);
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.Clear(Color.Transparent);
                    DrawGlyph(g, size, glyph);

                    Color dot = DotColor(state, light);
                    if (!dot.IsEmpty)
                    {
                        float d = size * 0.40f;               // dot diameter
                        float ring = Math.Max(1f, size / 12f); // gap around the dot
                        float x = size - d, y = size - d;
                        // Cut a transparent ring so the dot reads clearly on top of the glyph.
                        g.CompositingMode = CompositingMode.SourceCopy;
                        using (var clear = new SolidBrush(Color.Transparent))
                            g.FillEllipse(clear, x - ring, y - ring, d + ring * 2, d + ring * 2);
                        g.CompositingMode = CompositingMode.SourceOver;
                        using (var b = new SolidBrush(dot)) g.FillEllipse(b, x, y, d, d);
                    }
                }
            }
            return bmp;
        }

        /// <summary>
        /// The WiPass mark (logo A): three arcs opening to the right around a dot,
        /// defined in a 64-unit box (see docs/brand). Thicker strokes at tray size.
        /// </summary>
        private static void DrawGlyph(Graphics g, int size, Color color)
        {
            float k = size / 64f;
            float cx = 17f * k, cy = 32f * k;
            float stroke = (size <= 20 ? 7.5f : 7f) * k;
            using (var pen = new Pen(color, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                foreach (float r0 in new[] { 14f, 24f, 34f })
                {
                    float r = r0 * k;
                    g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, -45f, 90f);
                }
            }
            float dr = 5.5f * k;
            using (var b = new SolidBrush(color)) g.FillEllipse(b, cx - dr, cy - dr, dr * 2, dr * 2);
        }
    }
}
