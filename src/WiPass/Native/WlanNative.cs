using System;
using System.Runtime.InteropServices;
using System.Text;

namespace WiPass.Native
{
    /// <summary>
    /// Native Wi-Fi API (wlanapi.dll) declarations.
    /// Struct fields that are only read are parsed by byte offset in WifiService
    /// (offsets are fixed by the Windows SDK headers and identical on x86/x64).
    /// </summary>
    internal static class WlanNative
    {
        public const int ERROR_SUCCESS = 0;
        public const int ERROR_ACCESS_DENIED = 5;
        public const int ERROR_INVALID_PARAMETER = 87;
        public const int ERROR_NOT_FOUND = 1168;

        public const uint WLAN_CLIENT_VERSION_2 = 2;
        public const uint WLAN_NOTIFICATION_SOURCE_ACM = 0x00000008;
        public const uint WLAN_PROFILE_USER = 0x00000002;

        // ACM notification codes
        public const uint ACM_SCAN_COMPLETE = 7;
        public const uint ACM_CONNECTION_START = 9;
        public const uint ACM_CONNECTION_COMPLETE = 10;
        public const uint ACM_CONNECTION_ATTEMPT_FAIL = 11;
        public const uint ACM_DISCONNECTED = 21;

        // WLAN_INTERFACE_STATE
        public const int STATE_CONNECTED = 1;

        // WLAN_INTF_OPCODE
        public const int OPCODE_CURRENT_CONNECTION = 7;

        // DOT11_BSS_TYPE
        public const int BSS_TYPE_INFRASTRUCTURE = 1;
        public const int BSS_TYPE_ANY = 3;

        // Struct sizes (bytes)
        public const int SIZE_INTERFACE_INFO = 532;
        public const int SIZE_AVAILABLE_NETWORK = 628;
        public const int SIZE_BSS_ENTRY = 360;
        public const int SIZE_PROFILE_INFO = 516;

        [StructLayout(LayoutKind.Sequential)]
        public struct WLAN_NOTIFICATION_DATA
        {
            public uint NotificationSource;
            public uint NotificationCode;
            public Guid InterfaceGuid;
            public uint dwDataSize;
            public IntPtr pData;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WLAN_CONNECTION_PARAMETERS
        {
            public int wlanConnectionMode;          // 0 = wlan_connection_mode_profile
            [MarshalAs(UnmanagedType.LPWStr)]
            public string strProfile;
            public IntPtr pDot11Ssid;
            public IntPtr pDesiredBssidList;
            public int dot11BssType;
            public uint dwFlags;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void WlanNotificationCallback(ref WLAN_NOTIFICATION_DATA data, IntPtr context);

        [DllImport("wlanapi.dll")]
        public static extern int WlanOpenHandle(uint dwClientVersion, IntPtr pReserved, out uint pdwNegotiatedVersion, out IntPtr phClientHandle);

        [DllImport("wlanapi.dll")]
        public static extern int WlanCloseHandle(IntPtr hClientHandle, IntPtr pReserved);

        [DllImport("wlanapi.dll")]
        public static extern void WlanFreeMemory(IntPtr pMemory);

        [DllImport("wlanapi.dll")]
        public static extern int WlanEnumInterfaces(IntPtr hClientHandle, IntPtr pReserved, out IntPtr ppInterfaceList);

        [DllImport("wlanapi.dll")]
        public static extern int WlanScan(IntPtr hClientHandle, [In] ref Guid pInterfaceGuid, IntPtr pDot11Ssid, IntPtr pIeData, IntPtr pReserved);

        [DllImport("wlanapi.dll")]
        public static extern int WlanGetAvailableNetworkList(IntPtr hClientHandle, [In] ref Guid pInterfaceGuid, uint dwFlags, IntPtr pReserved, out IntPtr ppAvailableNetworkList);

        [DllImport("wlanapi.dll")]
        public static extern int WlanGetNetworkBssList(IntPtr hClientHandle, [In] ref Guid pInterfaceGuid, IntPtr pDot11Ssid, int dot11BssType,
            [MarshalAs(UnmanagedType.Bool)] bool bSecurityEnabled, IntPtr pReserved, out IntPtr ppWlanBssList);

        [DllImport("wlanapi.dll")]
        public static extern int WlanQueryInterface(IntPtr hClientHandle, [In] ref Guid pInterfaceGuid, int OpCode, IntPtr pReserved,
            out int pdwDataSize, out IntPtr ppData, IntPtr pWlanOpcodeValueType);

        [DllImport("wlanapi.dll")]
        public static extern int WlanGetProfileList(IntPtr hClientHandle, [In] ref Guid pInterfaceGuid, IntPtr pReserved, out IntPtr ppProfileList);

        [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
        public static extern int WlanGetProfile(IntPtr hClientHandle, [In] ref Guid pInterfaceGuid, string strProfileName, IntPtr pReserved,
            out IntPtr pstrProfileXml, ref uint pdwFlags, out uint pdwGrantedAccess);

        [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
        public static extern int WlanSetProfile(IntPtr hClientHandle, [In] ref Guid pInterfaceGuid, uint dwFlags, string strProfileXml,
            string strAllUserProfileSecurity, [MarshalAs(UnmanagedType.Bool)] bool bOverwrite, IntPtr pReserved, out uint pdwReasonCode);

        [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
        public static extern int WlanDeleteProfile(IntPtr hClientHandle, [In] ref Guid pInterfaceGuid, string strProfileName, IntPtr pReserved);

        [DllImport("wlanapi.dll")]
        public static extern int WlanConnect(IntPtr hClientHandle, [In] ref Guid pInterfaceGuid, [In] ref WLAN_CONNECTION_PARAMETERS pConnectionParameters, IntPtr pReserved);

        [DllImport("wlanapi.dll")]
        public static extern int WlanDisconnect(IntPtr hClientHandle, [In] ref Guid pInterfaceGuid, IntPtr pReserved);

        [DllImport("wlanapi.dll")]
        public static extern int WlanRegisterNotification(IntPtr hClientHandle, uint dwNotifSource, [MarshalAs(UnmanagedType.Bool)] bool bIgnoreDuplicate,
            WlanNotificationCallback funcCallback, IntPtr pCallbackContext, IntPtr pReserved, out uint pdwPrevNotifSource);

        [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
        public static extern int WlanReasonCodeToString(uint dwReasonCode, int dwBufferSize, StringBuilder pStringBuffer, IntPtr pReserved);
    }
}
