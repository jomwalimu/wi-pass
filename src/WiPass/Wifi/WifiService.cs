using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using WiPass.Core;
using WiPass.Native;

namespace WiPass.Wifi
{
    internal sealed class WifiInterface
    {
        public Guid Id;
        public string Description;
        public int State;
    }

    internal sealed class VisibleNetwork
    {
        public string Ssid;
        public string ProfileName;
        public bool Connectable;
        public int Signal;
        public bool SecurityEnabled;
        public int Auth;
        public int Cipher;
    }

    internal sealed class CurrentConnection
    {
        public int State;
        public string ProfileName;
        public string Ssid;
        public string Bssid;
    }

    internal sealed class SavedProfile
    {
        public string Name;
        public string Xml;
    }

    /// <summary>Thrown when Windows refuses Wi-Fi information because location access is off.</summary>
    internal sealed class LocationDeniedException : Exception
    {
        public LocationDeniedException() : base("위치 권한이 꺼져 있어 와이파이 정보를 읽을 수 없습니다.") { }
    }

    internal sealed class WifiException : Exception
    {
        public int Code { get; private set; }
        public WifiException(string message, int code) : base(message + " (" + WifiService.ErrorText(code) + ")") { Code = code; }
    }

    /// <summary>Managed wrapper over the Native Wi-Fi API.</summary>
    internal sealed class WifiService : IDisposable
    {
        private IntPtr handle;
        private readonly WlanNative.WlanNotificationCallback callback; // keep alive (GC)

        /// <summary>(interface, ACM code, reason code, profile name) - raised on a thread-pool thread.</summary>
        public event Action<Guid, uint, uint, string> AcmNotification;

        public WifiService()
        {
            uint negotiated;
            int r = WlanNative.WlanOpenHandle(WlanNative.WLAN_CLIENT_VERSION_2, IntPtr.Zero, out negotiated, out handle);
            if (r != 0) throw new WifiException("무선랜 서비스에 연결하지 못했습니다", r);

            callback = OnNotification;
            uint prev;
            r = WlanNative.WlanRegisterNotification(handle, WlanNative.WLAN_NOTIFICATION_SOURCE_ACM, true, callback, IntPtr.Zero, IntPtr.Zero, out prev);
            if (r != 0) Log.Warn("와이파이 알림 등록 실패: " + ErrorText(r));
        }

        private void OnNotification(ref WlanNative.WLAN_NOTIFICATION_DATA data, IntPtr context)
        {
            try
            {
                uint reason = 0;
                string profile = null;
                if ((data.NotificationCode == WlanNative.ACM_CONNECTION_COMPLETE || data.NotificationCode == WlanNative.ACM_CONNECTION_ATTEMPT_FAIL)
                    && data.pData != IntPtr.Zero && data.dwDataSize >= 564)
                {
                    // WLAN_CONNECTION_NOTIFICATION_DATA: mode(4) profileName WCHAR[256](512) ssid(36) bssType(4) security(4) reasonCode
                    profile = ReadFixedString(data.pData, 4, 256);
                    reason = (uint)Marshal.ReadInt32(data.pData, 560);
                }
                var h = AcmNotification;
                if (h != null) h(data.InterfaceGuid, data.NotificationCode, reason, profile);
            }
            catch (Exception ex)
            {
                Log.Warn("알림 처리 오류: " + ex.Message);
            }
        }

        // ---------------------------------------------------------------- interfaces

        public List<WifiInterface> GetInterfaces()
        {
            var list = new List<WifiInterface>();
            IntPtr p;
            int r = WlanNative.WlanEnumInterfaces(handle, IntPtr.Zero, out p);
            if (r != 0) throw new WifiException("무선랜 장치 목록을 읽지 못했습니다", r);
            try
            {
                int n = Marshal.ReadInt32(p, 0);
                for (int i = 0; i < n; i++)
                {
                    IntPtr e = IntPtr.Add(p, 8 + i * WlanNative.SIZE_INTERFACE_INFO);
                    list.Add(new WifiInterface
                    {
                        Id = ReadGuid(e, 0),
                        Description = ReadFixedString(e, 16, 256),
                        State = Marshal.ReadInt32(e, 16 + 512)
                    });
                }
            }
            finally { WlanNative.WlanFreeMemory(p); }
            return list;
        }

        /// <summary>The adapter to use: a connected one if any, otherwise the first.</summary>
        public WifiInterface GetPrimaryInterface()
        {
            var all = GetInterfaces();
            if (all.Count == 0) return null;
            foreach (var i in all) if (i.State == WlanNative.STATE_CONNECTED) return i;
            return all[0];
        }

        public void Scan(Guid itf)
        {
            int r = WlanNative.WlanScan(handle, ref itf, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (r != 0) Log.Info("주변 와이파이 검색 요청 실패: " + ErrorText(r));
        }

        // ---------------------------------------------------------------- networks

        public List<VisibleNetwork> GetVisibleNetworks(Guid itf)
        {
            var list = new List<VisibleNetwork>();
            IntPtr p;
            int r = WlanNative.WlanGetAvailableNetworkList(handle, ref itf, 0, IntPtr.Zero, out p);
            if (r == WlanNative.ERROR_ACCESS_DENIED) throw new LocationDeniedException();
            if (r != 0) throw new WifiException("주변 와이파이 목록을 읽지 못했습니다", r);
            try
            {
                int n = Marshal.ReadInt32(p, 0);
                for (int i = 0; i < n; i++)
                {
                    IntPtr e = IntPtr.Add(p, 8 + i * WlanNative.SIZE_AVAILABLE_NETWORK);
                    list.Add(new VisibleNetwork
                    {
                        ProfileName = ReadFixedString(e, 0, 256),
                        Ssid = ReadSsid(e, 512),
                        Connectable = Marshal.ReadInt32(e, 556) != 0,
                        Signal = Marshal.ReadInt32(e, 604),
                        SecurityEnabled = Marshal.ReadInt32(e, 608) != 0,
                        Auth = Marshal.ReadInt32(e, 612),
                        Cipher = Marshal.ReadInt32(e, 616)
                    });
                }
            }
            finally { WlanNative.WlanFreeMemory(p); }
            return list;
        }

        /// <summary>BSSIDs (access point MAC addresses) currently visible for the given SSID, lower-case "aa:bb:..".</summary>
        public List<string> GetBssids(Guid itf, string ssid)
        {
            var list = new List<string>();
            IntPtr p;
            int r = WlanNative.WlanGetNetworkBssList(handle, ref itf, IntPtr.Zero, WlanNative.BSS_TYPE_ANY, false, IntPtr.Zero, out p);
            if (r == WlanNative.ERROR_ACCESS_DENIED) throw new LocationDeniedException();
            if (r != 0) throw new WifiException("공유기 목록을 읽지 못했습니다", r);
            try
            {
                int n = Marshal.ReadInt32(p, 4);
                for (int i = 0; i < n; i++)
                {
                    IntPtr e = IntPtr.Add(p, 8 + i * WlanNative.SIZE_BSS_ENTRY);
                    if (ReadSsid(e, 0) != ssid) continue;
                    string mac = ReadMac(e, 40);
                    if (mac != null && !list.Contains(mac)) list.Add(mac);
                }
            }
            finally { WlanNative.WlanFreeMemory(p); }
            return list;
        }

        /// <summary>Current connection, or null if not connected / not readable.</summary>
        public CurrentConnection GetCurrent(Guid itf)
        {
            int size;
            IntPtr p;
            int r = WlanNative.WlanQueryInterface(handle, ref itf, WlanNative.OPCODE_CURRENT_CONNECTION, IntPtr.Zero, out size, out p, IntPtr.Zero);
            if (r != 0 || p == IntPtr.Zero) return null;
            try
            {
                // WLAN_CONNECTION_ATTRIBUTES: state(4) mode(4) profile WCHAR[256] | association: ssid(36) bssType(4) bssid(6)
                return new CurrentConnection
                {
                    State = Marshal.ReadInt32(p, 0),
                    ProfileName = ReadFixedString(p, 8, 256),
                    Ssid = ReadSsid(p, 520),
                    Bssid = ReadMac(p, 560)
                };
            }
            finally { WlanNative.WlanFreeMemory(p); }
        }

        // ---------------------------------------------------------------- profiles

        public List<string> GetProfileNames(Guid itf)
        {
            var list = new List<string>();
            IntPtr p;
            int r = WlanNative.WlanGetProfileList(handle, ref itf, IntPtr.Zero, out p);
            if (r != 0) throw new WifiException("저장된 와이파이 목록을 읽지 못했습니다", r);
            try
            {
                int n = Marshal.ReadInt32(p, 0);
                for (int i = 0; i < n; i++)
                {
                    IntPtr e = IntPtr.Add(p, 8 + i * WlanNative.SIZE_PROFILE_INFO);
                    list.Add(ReadFixedString(e, 0, 256));
                }
            }
            finally { WlanNative.WlanFreeMemory(p); }
            return list;
        }

        public string GetProfileXml(Guid itf, string name)
        {
            IntPtr p;
            uint flags = 0, access;
            int r = WlanNative.WlanGetProfile(handle, ref itf, name, IntPtr.Zero, out p, ref flags, out access);
            if (r != 0 || p == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUni(p); }
            finally { WlanNative.WlanFreeMemory(p); }
        }

        /// <summary>All saved profiles on this adapter whose SSID equals <paramref name="ssid"/>.</summary>
        public List<SavedProfile> FindProfilesBySsid(Guid itf, string ssid)
        {
            var found = new List<SavedProfile>();
            string hex = ProfileBuilder.SsidHex(ssid);
            foreach (string name in GetProfileNames(itf))
            {
                string xml = GetProfileXml(itf, name);
                if (xml == null) continue;
                try
                {
                    var doc = new XmlDocument();
                    doc.LoadXml(xml);
                    bool match = false;
                    foreach (XmlNode s in doc.SelectNodes("//*[local-name()='SSIDConfig']/*[local-name()='SSID']"))
                    {
                        XmlNode nameNode = s.SelectSingleNode("*[local-name()='name']");
                        XmlNode hexNode = s.SelectSingleNode("*[local-name()='hex']");
                        if ((nameNode != null && nameNode.InnerText == ssid) ||
                            (hexNode != null && string.Equals(hexNode.InnerText.Trim(), hex, StringComparison.OrdinalIgnoreCase)))
                        { match = true; break; }
                    }
                    if (match) found.Add(new SavedProfile { Name = name, Xml = xml });
                }
                catch (XmlException) { }
            }
            return found;
        }

        public int DeleteProfile(Guid itf, string name)
        {
            return WlanNative.WlanDeleteProfile(handle, ref itf, name, IntPtr.Zero);
        }

        public int SetProfile(Guid itf, string xml, bool currentUserOnly, out uint reason)
        {
            uint flags = currentUserOnly ? WlanNative.WLAN_PROFILE_USER : 0u;
            return WlanNative.WlanSetProfile(handle, ref itf, flags, xml, null, true, IntPtr.Zero, out reason);
        }

        public int Connect(Guid itf, string profileName)
        {
            var prm = new WlanNative.WLAN_CONNECTION_PARAMETERS
            {
                wlanConnectionMode = 0,
                strProfile = profileName,
                pDot11Ssid = IntPtr.Zero,
                pDesiredBssidList = IntPtr.Zero,
                dot11BssType = WlanNative.BSS_TYPE_INFRASTRUCTURE,
                dwFlags = 0
            };
            return WlanNative.WlanConnect(handle, ref itf, ref prm, IntPtr.Zero);
        }

        public void Disconnect(Guid itf)
        {
            int r = WlanNative.WlanDisconnect(handle, ref itf, IntPtr.Zero);
            if (r != 0) Log.Info("연결 끊기 요청 실패: " + ErrorText(r));
        }

        // ---------------------------------------------------------------- helpers

        public static string ErrorText(int code)
        {
            try { return code + ": " + new Win32Exception(code).Message; }
            catch { return code.ToString(); }
        }

        public static string ReasonText(uint reason)
        {
            if (reason == 0) return "";
            try
            {
                var sb = new StringBuilder(512);
                if (WlanNative.WlanReasonCodeToString(reason, sb.Capacity, sb, IntPtr.Zero) == 0) return sb.ToString();
            }
            catch { }
            return "코드 " + reason;
        }

        private static string ReadFixedString(IntPtr p, int offset, int maxChars)
        {
            byte[] buf = new byte[maxChars * 2];
            Marshal.Copy(IntPtr.Add(p, offset), buf, 0, buf.Length);
            string s = Encoding.Unicode.GetString(buf);
            int z = s.IndexOf('\0');
            return z >= 0 ? s.Substring(0, z) : s;
        }

        /// <summary>DOT11_SSID: ULONG length + UCHAR[32].</summary>
        private static string ReadSsid(IntPtr p, int offset)
        {
            int len = Marshal.ReadInt32(p, offset);
            if (len <= 0 || len > 32) return "";
            byte[] b = new byte[len];
            Marshal.Copy(IntPtr.Add(p, offset + 4), b, 0, len);
            return Encoding.UTF8.GetString(b);
        }

        private static string ReadMac(IntPtr p, int offset)
        {
            byte[] b = new byte[6];
            Marshal.Copy(IntPtr.Add(p, offset), b, 0, 6);
            bool zero = true;
            foreach (byte x in b) if (x != 0) { zero = false; break; }
            if (zero) return null;
            return BitConverter.ToString(b).Replace('-', ':').ToLowerInvariant();
        }

        private static Guid ReadGuid(IntPtr p, int offset)
        {
            byte[] b = new byte[16];
            Marshal.Copy(IntPtr.Add(p, offset), b, 0, 16);
            return new Guid(b);
        }

        public void Dispose()
        {
            if (handle != IntPtr.Zero)
            {
                WlanNative.WlanCloseHandle(handle, IntPtr.Zero);
                handle = IntPtr.Zero;
            }
        }
    }
}
