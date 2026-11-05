using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace WiPass.Core
{
    /// <summary>
    /// Whether WiPass runs as the Microsoft Store (MSIX) package or as the GitHub installer / portable copy.
    /// The Store version has a package identity: Windows handles start-up, notifications and AppData for it.
    /// </summary>
    internal static class AppPackage
    {
        private const int APPMODEL_ERROR_NO_PACKAGE = 15700;
        private const int ERROR_INSUFFICIENT_BUFFER = 122;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetCurrentPackageFamilyName(ref int length, StringBuilder name);

        private static bool checkedOnce;
        private static string familyName;

        /// <summary>Package family name (e.g. "12345jomwalimu.WiPass_abcd1234"), or null when not packaged.</summary>
        public static string FamilyName
        {
            get
            {
                if (checkedOnce) return familyName;
                checkedOnce = true;
                try
                {
                    int len = 0;
                    int r = GetCurrentPackageFamilyName(ref len, null);
                    if (r == ERROR_INSUFFICIENT_BUFFER && len > 0)
                    {
                        var sb = new StringBuilder(len);
                        if (GetCurrentPackageFamilyName(ref len, sb) == 0) familyName = sb.ToString();
                    }
                }
                catch { } // older Windows / not Windows (previews)
                return familyName;
            }
        }

        public static bool IsPackaged { get { return FamilyName != null; } }

        /// <summary>
        /// Where a folder under %APPDATA% really is. Files a packaged app writes to AppData go to a private
        /// copy; Explorer (outside the package) only sees them there.
        /// </summary>
        public static string RealAppDataPath(string virtualPath)
        {
            if (!IsPackaged) return virtualPath;
            try
            {
                string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData).TrimEnd('\\');
                if (!virtualPath.StartsWith(roaming, StringComparison.OrdinalIgnoreCase)) return virtualPath;
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string redirected = Path.Combine(local, "Packages", FamilyName, "LocalCache", "Roaming") + virtualPath.Substring(roaming.Length);
                return Directory.Exists(redirected) ? redirected : virtualPath;
            }
            catch { return virtualPath; }
        }

        /// <summary>True when the GitHub installer version (Inno Setup) is still installed for this user.</summary>
        public static bool LegacyInstallPresent
        {
            get
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{8B4E2A61-3C7D-4F0B-9E57-6A1D2C9F4B10}_is1"))
                        return k != null;
                }
                catch { return false; }
            }
        }
    }
}
