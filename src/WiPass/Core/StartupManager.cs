using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WiPass.Core
{
    /// <summary>
    /// "Start with Windows". GitHub installer / portable copy: the current user's Run key (no administrator
    /// rights needed). Store package: the package's startup task (Windows → 작업 관리자 → 시작 앱).
    /// </summary>
    internal static class StartupManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "WiPass";
        public const string StartupArgument = "--startup";
        /// <summary>Must match TaskId in the package manifest.</summary>
        public const string StartupTaskId = "WiPassStartup";

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

        /// <summary>
        /// Registers (with the current exe path) or removes the startup entry.
        /// <paramref name="blockedByUser"/> is called (on a background thread) when the Store version may not
        /// turn itself on because the user switched it off in Windows' startup apps list.
        /// </summary>
        public static void Apply(bool enable, Action blockedByUser = null)
        {
            if (AppPackage.IsPackaged)
            {
                Task.Factory.StartNew(() => ApplyPackaged(enable, blockedByUser));
                return;
            }
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

        // Windows.ApplicationModel.StartupTask through reflection (no extra packages on .NET Framework 4.8).
        private static void ApplyPackaged(bool enable, Action blockedByUser)
        {
            try
            {
                Type taskType = Type.GetType("Windows.ApplicationModel.StartupTask, Windows.ApplicationModel, ContentType=WindowsRuntime", true);
                Type ext = Type.GetType("System.WindowsRuntimeSystemExtensions, System.Runtime.WindowsRuntime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089", true);
                MethodInfo asTask = ext.GetMethods(BindingFlags.Public | BindingFlags.Static).First(m =>
                    m.Name == "AsTask" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType.Name == "IAsyncOperation`1");

                object op = taskType.GetMethod("GetAsync", new[] { typeof(string) }).Invoke(null, new object[] { StartupTaskId });
                object task = Await(asTask.MakeGenericMethod(taskType).Invoke(null, new[] { op }));
                string state = taskType.GetProperty("State").GetValue(task, null).ToString();

                if (enable)
                {
                    if (state == "Disabled")
                    {
                        object req = taskType.GetMethod("RequestEnableAsync", Type.EmptyTypes).Invoke(task, null);
                        Type stateType = taskType.GetProperty("State").PropertyType;
                        state = Await(asTask.MakeGenericMethod(stateType).Invoke(null, new[] { req })).ToString();
                        Log.Info("윈도우 시작 시 자동 실행 켬 (" + state + ")");
                    }
                    if (state == "DisabledByUser" || state == "DisabledByPolicy")
                    {
                        Log.Warn("윈도우 시작 앱 목록에서 꺼져 있어 자동 실행을 켜지 못함 (" + state + ")");
                        if (blockedByUser != null) blockedByUser();
                    }
                }
                else if (state == "Enabled")
                {
                    taskType.GetMethod("Disable", Type.EmptyTypes).Invoke(task, null);
                    Log.Info("윈도우 시작 시 자동 실행 해제");
                }
            }
            catch (Exception ex)
            {
                while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
                Log.Warn("자동 실행 설정 실패: " + ex.Message);
            }
        }

        private static object Await(object task)
        {
            var t = (Task)task;
            t.Wait(TimeSpan.FromSeconds(15));
            return t.GetType().GetProperty("Result").GetValue(t, null);
        }
    }
}
