using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace WiPass.UI
{
    internal enum TrayState { Idle, Busy, Connected, Failed }

    /// <summary>Draws the tray icons (coloured circle + Wi-Fi arcs) so no image files are needed.</summary>
    internal static class IconFactory
    {
        private static readonly Dictionary<TrayState, Icon> Cache = new Dictionary<TrayState, Icon>();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private static Icon appIcon;

        /// <summary>The program icon (embedded in WiPass.exe), for windows.</summary>
        public static Icon AppIcon
        {
            get
            {
                if (appIcon == null)
                {
                    try { appIcon = Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath); }
                    catch { appIcon = Get(TrayState.Connected); }
                }
                return appIcon;
            }
        }

        public static Icon Get(TrayState state)
        {
            Icon icon;
            if (Cache.TryGetValue(state, out icon)) return icon;
            icon = Draw(ColorOf(state));
            Cache[state] = icon;
            return icon;
        }

        private static Color ColorOf(TrayState s)
        {
            switch (s)
            {
                case TrayState.Connected: return Color.FromArgb(34, 150, 84);
                case TrayState.Busy: return Color.FromArgb(222, 150, 0);
                case TrayState.Failed: return Color.FromArgb(205, 55, 55);
                default: return Color.FromArgb(120, 124, 130);
            }
        }

        private static Icon Draw(Color color)
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                using (var fill = new SolidBrush(color))
                using (var pen = new Pen(Color.White, 2.8f))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    g.FillEllipse(fill, 1, 1, 30, 30);
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawArc(pen, 6, 9, 20, 20, 225, 90);
                    g.DrawArc(pen, 10.5f, 13.5f, 11, 11, 225, 90);
                    g.FillEllipse(Brushes.White, 14, 18, 4, 4);
                }
                IntPtr h = bmp.GetHicon();
                Icon tmp = Icon.FromHandle(h);
                Icon owned = (Icon)tmp.Clone();
                tmp.Dispose();
                DestroyIcon(h);
                return owned;
            }
        }
    }
}
