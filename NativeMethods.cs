using System.Runtime.InteropServices;

namespace DisKeyboard;

/// <summary>
/// P/Invoke declarations for the Windows SetupAPI and Configuration Manager
/// used to enumerate and enable/disable device nodes.
/// </summary>
internal static class NativeMethods
{
    // Device class GUID for keyboards: {4D36E96B-E325-11CE-BFC1-08002BE10318}
    public static readonly Guid GUID_DEVCLASS_KEYBOARD =
        new("4D36E96B-E325-11CE-BFC1-08002BE10318");

    // SetupDiGetClassDevs flags
    public const uint DIGCF_PRESENT = 0x00000002;
    public const uint DIGCF_PROFILE = 0x00000008;

    // SetupDiGetDeviceRegistryProperty properties
    public const uint SPDRP_DEVICEDESC = 0x00000000;
    public const uint SPDRP_FRIENDLYNAME = 0x0000000C;
    public const uint SPDRP_UPPERFILTERS = 0x00000011;

    // Class install function
    public const uint DIF_PROPERTYCHANGE = 0x00000012;

    // State change for SP_PROPCHANGE_PARAMS
    public const uint DICS_ENABLE = 0x00000001;
    public const uint DICS_DISABLE = 0x00000002;

    // Scope for SP_PROPCHANGE_PARAMS
    public const uint DICS_FLAG_GLOBAL = 0x00000001;
    public const uint DICS_FLAG_CONFIGSPECIFIC = 0x00000002;

    // CM_Get_DevNode_Status results
    public const uint CR_SUCCESS = 0x00000000;
    public const uint DN_HAS_PROBLEM = 0x00000400;
    public const uint DN_DISABLEABLE = 0x00002000;
    public const uint CM_PROB_DISABLED = 22;

    public const int ERROR_NO_MORE_ITEMS = 259;
    public const int ERROR_INSUFFICIENT_BUFFER = 122;

    public static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_CLASSINSTALL_HEADER
    {
        public uint cbSize;
        public uint InstallFunction;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_PROPCHANGE_PARAMS
    {
        public SP_CLASSINSTALL_HEADER ClassInstallHeader;
        public uint StateChange;
        public uint Scope;
        public uint HwProfile;
    }

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern IntPtr SetupDiGetClassDevs(
        ref Guid ClassGuid,
        IntPtr Enumerator,
        IntPtr hwndParent,
        uint Flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiEnumDeviceInfo(
        IntPtr DeviceInfoSet,
        uint MemberIndex,
        ref SP_DEVINFO_DATA DeviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiGetDeviceRegistryProperty(
        IntPtr DeviceInfoSet,
        ref SP_DEVINFO_DATA DeviceInfoData,
        uint Property,
        out uint PropertyRegDataType,
        byte[]? PropertyBuffer,
        uint PropertyBufferSize,
        out uint RequiredSize);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiGetDeviceInstanceId(
        IntPtr DeviceInfoSet,
        ref SP_DEVINFO_DATA DeviceInfoData,
        char[]? DeviceInstanceId,
        uint DeviceInstanceIdSize,
        out uint RequiredSize);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiSetDeviceRegistryProperty(
        IntPtr DeviceInfoSet,
        ref SP_DEVINFO_DATA DeviceInfoData,
        uint Property,
        byte[]? PropertyBuffer,
        uint PropertyBufferSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiSetClassInstallParams(
        IntPtr DeviceInfoSet,
        ref SP_DEVINFO_DATA DeviceInfoData,
        ref SP_PROPCHANGE_PARAMS ClassInstallParams,
        uint ClassInstallParamsSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiCallClassInstaller(
        uint InstallFunction,
        IntPtr DeviceInfoSet,
        ref SP_DEVINFO_DATA DeviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern IntPtr SetupDiCreateDeviceInfoList(
        IntPtr ClassGuid,
        IntPtr hwndParent);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiOpenDeviceInfo(
        IntPtr DeviceInfoSet,
        string DeviceInstanceId,
        IntPtr hwndParent,
        uint Flags,
        ref SP_DEVINFO_DATA DeviceInfoData);

    [DllImport("cfgmgr32.dll", SetLastError = true)]
    public static extern uint CM_Get_DevNode_Status(
        out uint pulStatus,
        out uint pulProblemNumber,
        uint dnDevInst,
        uint ulFlags);

    [DllImport("cfgmgr32.dll", SetLastError = true)]
    public static extern uint CM_Get_Parent(
        out uint pdnDevInst,
        uint dnDevInst,
        uint ulFlags);

    [DllImport("cfgmgr32.dll", SetLastError = true)]
    public static extern uint CM_Get_Device_ID_Size(
        out uint pulLen,
        uint dnDevInst,
        uint ulFlags);

    [DllImport("cfgmgr32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CM_Get_Device_IDW")]
    public static extern uint CM_Get_Device_ID(
        uint dnDevInst,
        char[] Buffer,
        uint BufferLen,
        uint ulFlags);

    // --- Low-level keyboard hook (guaranteed input block fallback) ---

    public const int WH_KEYBOARD_LL = 13;
    public const int HC_ACTION = 0;

    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_KEYUP = 0x0101;
    public const uint WM_SYSKEYDOWN = 0x0104;
    public const uint WM_SYSKEYUP = 0x0105;

    // Virtual-key codes used by the escape combo and modifier checks.
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU = 0x12; // Alt
    public const int VK_END = 0x23;

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(
        int idHook,
        LowLevelKeyboardProc lpfn,
        IntPtr hMod,
        uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr CallNextHookEx(
        IntPtr hhk,
        int nCode,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);
}
