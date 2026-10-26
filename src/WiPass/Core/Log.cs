using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WiPass.Core
{
    /// <summary>Simple local log file. Passwords and profile XML are never written here.</summary>
    internal static class Log
    {
        private static readonly object Sync = new object();
        private const long MaxBytes = 1024 * 1024;

        public static string FilePath { get { return Path.Combine(AppPaths.LogDir, "wipass.log"); } }

        public static void Info(string msg) { Write("정보", msg); }
        public static void Warn(string msg) { Write("주의", msg); }
        public static void Error(string msg) { Write("오류", msg); }

        private static void Write(string level, string msg)
        {
            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(AppPaths.LogDir);
                    var fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > MaxBytes)
                    {
                        string old = FilePath + ".old";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(FilePath, old);
                    }
                    File.AppendAllText(FilePath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "] " + msg + Environment.NewLine,
                        new UTF8Encoding(false));
                }
            }
            catch { }
        }

        public static List<string> Tail(int lines)
        {
            var result = new List<string>();
            try
            {
                lock (Sync)
                {
                    if (!File.Exists(FilePath)) return result;
                    string[] all = File.ReadAllLines(FilePath, Encoding.UTF8);
                    for (int i = Math.Max(0, all.Length - lines); i < all.Length; i++) result.Add(all[i]);
                }
            }
            catch { }
            return result;
        }
    }

    internal static class AppPaths
    {
        public static string DataDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WiPass"); }
        }

        public static string LogDir { get { return Path.Combine(DataDir, "logs"); } }
        public static string SettingsFile { get { return Path.Combine(DataDir, "settings.json"); } }
    }
}
