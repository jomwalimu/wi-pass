using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using WiPass.Core;

namespace WiPass.Wifi
{
    internal enum HotspotState { Unavailable, Off, On, InTransition }

    /// <summary>
    /// Windows "Mobile hotspot" (tethering) control through the WinRT
    /// NetworkOperatorTetheringManager API, called by reflection so the app
    /// needs no extra packages and still runs on .NET Framework 4.8.
    /// All methods block; call them from a background thread.
    /// </summary>
    internal sealed class HotspotService
    {
        private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(30);

        private readonly Type networkInformation;
        private readonly Type tetheringManager;
        private readonly Type operationResult;
        private readonly MethodInfo asTaskGeneric;

        public bool Available { get; private set; }

        public HotspotService()
        {
            try
            {
                networkInformation = Type.GetType("Windows.Networking.Connectivity.NetworkInformation, Windows.Networking.Connectivity, ContentType=WindowsRuntime", true);
                tetheringManager = Type.GetType("Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager, Windows.Networking.NetworkOperators, ContentType=WindowsRuntime", true);
                operationResult = Type.GetType("Windows.Networking.NetworkOperators.NetworkOperatorTetheringOperationResult, Windows.Networking.NetworkOperators, ContentType=WindowsRuntime", true);
                Type ext = Type.GetType("System.WindowsRuntimeSystemExtensions, System.Runtime.WindowsRuntime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089", true);
                asTaskGeneric = ext.GetMethods(BindingFlags.Public | BindingFlags.Static).First(m =>
                    m.Name == "AsTask" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType.Name == "IAsyncOperation`1");
                Available = true;
            }
            catch (Exception ex)
            {
                Log.Warn("핫스팟 기능을 사용할 수 없습니다: " + ex.Message);
                Available = false;
            }
        }

        /// <summary>The tethering manager bound to the school network connection, or null.</summary>
        private object GetManager(string ssid)
        {
            object profile = networkInformation.GetMethod("GetInternetConnectionProfile").Invoke(null, null);
            if (profile == null || !IsWlanProfileFor(profile, ssid))
            {
                // Right after connecting Windows may not have confirmed internet access yet:
                // look for the school network's connection directly.
                profile = null;
                var all = networkInformation.GetMethod("GetConnectionProfiles").Invoke(null, null) as IEnumerable;
                if (all != null)
                    foreach (object p in all)
                        if (IsWlanProfileFor(p, ssid) && IsConnected(p)) { profile = p; break; }
            }
            if (profile == null) return null;

            MethodInfo create = tetheringManager.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "CreateFromConnectionProfile" && m.GetParameters().Length == 1);
            return create.Invoke(null, new[] { profile });
        }

        private static bool IsWlanProfileFor(object profile, string ssid)
        {
            Type t = profile.GetType();
            bool isWlan = (bool)t.GetProperty("IsWlanConnectionProfile").GetValue(profile, null);
            if (!isWlan) return false;
            string name = (string)t.GetProperty("ProfileName").GetValue(profile, null);
            return name != null && (name == ssid || name.StartsWith(ssid + " ", StringComparison.Ordinal));
        }

        private static bool IsConnected(object profile)
        {
            try
            {
                object level = profile.GetType().GetMethod("GetNetworkConnectivityLevel").Invoke(profile, null);
                return level != null && level.ToString() != "None";
            }
            catch { return false; }
        }

        public HotspotState GetState(string ssid)
        {
            if (!Available) return HotspotState.Unavailable;
            try
            {
                object tm = GetManager(ssid);
                if (tm == null) return HotspotState.Unavailable;
                string s = tm.GetType().GetProperty("TetheringOperationalState").GetValue(tm, null).ToString();
                if (s == "On") return HotspotState.On;
                if (s == "InTransition") return HotspotState.InTransition;
                return HotspotState.Off;
            }
            catch (Exception ex)
            {
                Log.Warn("핫스팟 상태 확인 오류: " + Inner(ex).Message);
                return HotspotState.Unavailable;
            }
        }

        /// <summary>Turns the hotspot on (restarting it first if <paramref name="restart"/>). Returns an error message or null.</summary>
        public string Start(string ssid, bool restart)
        {
            if (!Available) return "이 PC에서는 핫스팟 기능을 쓸 수 없습니다.";
            try
            {
                object tm = GetManager(ssid);
                if (tm == null) return "학교 와이파이 연결을 찾지 못해 핫스팟을 켜지 못했습니다.";
                string state = tm.GetType().GetProperty("TetheringOperationalState").GetValue(tm, null).ToString();
                if (state == "InTransition") return null;
                if (state == "On")
                {
                    if (!restart) return null;
                    // After changing networks the hotspot must be restarted to share the new connection.
                    Run(tm, "StopTetheringAsync");
                    System.Threading.Thread.Sleep(1500);
                    tm = GetManager(ssid);
                    if (tm == null) return "학교 와이파이 연결을 찾지 못해 핫스팟을 켜지 못했습니다.";
                }
                string err = Run(tm, "StartTetheringAsync");
                if (err == null) Log.Info("핫스팟 켜짐");
                else Log.Warn("핫스팟 켜기 실패: " + err);
                return err;
            }
            catch (Exception ex)
            {
                Log.Warn("핫스팟 켜기 오류: " + Inner(ex).Message);
                return Inner(ex).Message;
            }
        }

        public void Stop(string ssid)
        {
            if (!Available) return;
            try
            {
                object tm = GetManager(ssid);
                if (tm == null) return;
                if (tm.GetType().GetProperty("TetheringOperationalState").GetValue(tm, null).ToString() != "On") return;
                string err = Run(tm, "StopTetheringAsync");
                if (err == null) Log.Info("핫스팟 꺼짐");
                else Log.Warn("핫스팟 끄기 실패: " + err);
            }
            catch (Exception ex)
            {
                Log.Warn("핫스팟 끄기 오류: " + Inner(ex).Message);
            }
        }

        /// <summary>Runs Start/StopTetheringAsync and waits. Returns an error message or null on success.</summary>
        private string Run(object tm, string method)
        {
            object op = tm.GetType().GetMethod(method, Type.EmptyTypes).Invoke(tm, null);
            var task = (Task)asTaskGeneric.MakeGenericMethod(operationResult).Invoke(null, new[] { op });
            if (!task.Wait(OperationTimeout)) return "응답 시간 초과";
            object result = task.GetType().GetProperty("Result").GetValue(task, null);
            string status = result.GetType().GetProperty("Status").GetValue(result, null).ToString();
            if (status == "Success") return null;
            string extra = Convert.ToString(result.GetType().GetProperty("AdditionalErrorMessage").GetValue(result, null));
            return status + (string.IsNullOrEmpty(extra) ? "" : " - " + extra);
        }

        private static Exception Inner(Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
            if (ex is AggregateException && ex.InnerException != null) ex = ex.InnerException;
            return ex;
        }
    }
}
