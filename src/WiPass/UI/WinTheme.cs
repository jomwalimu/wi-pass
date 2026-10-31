using System;
using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WiPass.UI
{
    /// <summary>Windows theme information and small window-chrome helpers (Windows 11 rounding).</summary>
    internal static class WinTheme
    {
        private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        /// <summary>True when apps use the light theme (Settings → Personalization → Colors → app mode).</summary>
        public static bool AppsLight
        {
            get { return ReadFlag("AppsUseLightTheme", true); }
        }

        /// <summary>True when the taskbar uses the light theme.</summary>
        public static bool TaskbarLight
        {
            get { return ReadFlag("SystemUsesLightTheme", false); }
        }

        private static bool ReadFlag(string name, bool fallback)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(PersonalizeKey))
                {
                    object v = k != null ? k.GetValue(name) : null;
                    return v is int ? (int)v == 1 : fallback;
                }
            }
            catch { return fallback; }
        }

        public static bool IsWindows11
        {
            get { return Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 22000; }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;
        private const int DWMWCP_ROUNDSMALL = 3;

        /// <summary>Asks Windows 11 to round the window's corners (ignored on Windows 10).</summary>
        public static void RoundCorners(IntPtr hwnd, bool small)
        {
            if (!IsWindows11 || hwnd == IntPtr.Zero) return;
            try
            {
                int pref = small ? DWMWCP_ROUNDSMALL : DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
            }
            catch { }
        }

        public static readonly Color Brand = Color.FromArgb(15, 118, 110);      // #0F766E
        public static readonly Color BrandLight = Color.FromArgb(94, 234, 212); // #5EEAD4
    }
}
