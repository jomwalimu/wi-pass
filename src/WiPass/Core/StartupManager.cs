using System;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WiPass.Core
{
    /// <summary>"Start with Windows" via the current user's Run key (no administrator rights needed).</summary>
    internal static class StartupManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "WiPass";
        public const string StartupArgument = "--startup";

        private static string Command
        {
            get { return "\"" + Application.ExecutablePath + "\" " + StartupArgument; }
        }

        /// <summary>True when the program runs from a temporary folder (e.g. opened directly inside a zip file).</summary>
        public static bool RunningFromTemp
        {
            get
            {
                try
                {
                    string exe = Path.GetFullPath(Application.ExecutablePath).TrimEnd('\\') + "\\";
                    string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\') + "\\";
                    return exe.StartsWith(temp, StringComparison.OrdinalIgnoreCase);
                }
                catch { return false; }
            }
        }

        /// <summary>Registers (with the current exe path) or removes the startup entry.</summary>
        public static void Apply(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (key == null) return;
                    if (enable && !RunningFromTemp)
                    {
                        if (!string.Equals(Convert.ToString(key.GetValue(ValueName)), Command, StringComparison.OrdinalIgnoreCase))
                        {
                            key.SetValue(ValueName, Command);
                            Log.Info("윈도우 시작 시 자동 실행 등록: " + Application.ExecutablePath);
                        }
                    }
                    else if (key.GetValue(ValueName) != null)
                    {
                        key.DeleteValue(ValueName, false);
                        Log.Info("윈도우 시작 시 자동 실행 해제");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("자동 실행 설정 실패: " + ex.Message);
            }
        }
    }
}
