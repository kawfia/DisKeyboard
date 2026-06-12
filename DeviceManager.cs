using System.ComponentModel;
using System.Runtime.InteropServices;
using static DisKeyboard.NativeMethods;

namespace DisKeyboard;

/// <summary>
/// Enumerates keyboard devices and toggles their enabled state through SetupAPI.
/// </summary>
public static class DeviceManager
{
    /// <summary>
    /// Returns every keyboard device class node currently present on the machine.
    /// </summary>
    public static List<KeyboardDevice> GetKeyboards()
    {
        var result = new List<KeyboardDevice>();
        Guid classGuid = GUID_DEVCLASS_KEYBOARD;

        IntPtr deviceInfoSet = SetupDiGetClassDevs(
            ref classGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT);

        if (deviceInfoSet == INVALID_HANDLE_VALUE)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetupDiGetClassDevs failed.");

        try
        {
            var devInfo = new SP_DEVINFO_DATA();
            devInfo.cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>();

            for (uint index = 0; SetupDiEnumDeviceInfo(deviceInfoSet, index, ref devInfo); index++)
            {
                string name =
                    GetStringProperty(deviceInfoSet, ref devInfo, SPDRP_FRIENDLYNAME)
                    ?? GetStringProperty(deviceInfoSet, ref devInfo, SPDRP_DEVICEDESC)
                    ?? "Unknown keyboard";

                result.Add(new KeyboardDevice
                {
                    Name = name,
                    InstanceId = GetInstanceId(deviceInfoSet, ref devInfo),
                    IsEnabled = !IsDisabled(devInfo.DevInst),
                });

                // SP_DEVINFO_DATA is reused across iterations; reset the size field
                // is unnecessary, but keep cbSize valid for safety.
                devInfo.cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>();
            }

            int err = Marshal.GetLastWin32Error();
            if (err != ERROR_NO_MORE_ITEMS && err != 0)
                throw new Win32Exception(err, "SetupDiEnumDeviceInfo failed.");
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        return result;
    }

    /// <summary>
    /// Enables or disables the keyboard identified by its device instance id.
    /// Requires the process to run elevated.
    /// </summary>
    public static void SetEnabled(string instanceId, bool enable)
    {
        if (string.IsNullOrEmpty(instanceId))
            throw new ArgumentException("Instance id is required.", nameof(instanceId));

        Guid classGuid = GUID_DEVCLASS_KEYBOARD;
        IntPtr deviceInfoSet = SetupDiGetClassDevs(
            ref classGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT);

        if (deviceInfoSet == INVALID_HANDLE_VALUE)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetupDiGetClassDevs failed.");

        try
        {
            var devInfo = new SP_DEVINFO_DATA();
            devInfo.cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>();

            for (uint index = 0; SetupDiEnumDeviceInfo(deviceInfoSet, index, ref devInfo); index++)
            {
                string currentId = GetInstanceId(deviceInfoSet, ref devInfo);
                if (!string.Equals(currentId, instanceId, StringComparison.OrdinalIgnoreCase))
                {
                    devInfo.cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>();
                    continue;
                }

                ChangeState(deviceInfoSet, ref devInfo, enable);
                return;
            }

            throw new InvalidOperationException(
                $"Keyboard with instance id '{instanceId}' was not found.");
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    private static void ChangeState(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA devInfo, bool enable)
    {
        // Windows marks some devices (often the built-in PS/2 keyboard) as
        // non-disableable — Device Manager greys out "Disable" for them too.
        if (!enable && !IsDisableable(devInfo.DevInst))
        {
            throw new InvalidOperationException(
                "Эту клавиатуру нельзя отключить: Windows помечает устройство как " +
                "не отключаемое (в Диспетчере устройств пункт «Отключить» для неё " +
                "также недоступен). Для встроенных PS/2-клавиатур обычно требуется " +
                "замена драйвера, а не простое отключение.");
        }

        var propChange = new SP_PROPCHANGE_PARAMS
        {
            ClassInstallHeader = new SP_CLASSINSTALL_HEADER
            {
                cbSize = (uint)Marshal.SizeOf<SP_CLASSINSTALL_HEADER>(),
                InstallFunction = DIF_PROPERTYCHANGE,
            },
            StateChange = enable ? DICS_ENABLE : DICS_DISABLE,
            Scope = DICS_FLAG_GLOBAL,
            HwProfile = 0,
        };

        if (!SetupDiSetClassInstallParams(
                deviceInfoSet, ref devInfo, ref propChange,
                (uint)Marshal.SizeOf<SP_PROPCHANGE_PARAMS>()))
        {
            throw LastError("SetupDiSetClassInstallParams");
        }

        if (!SetupDiCallClassInstaller(DIF_PROPERTYCHANGE, deviceInfoSet, ref devInfo))
        {
            throw LastError("SetupDiCallClassInstaller");
        }
    }

    private static bool IsDisableable(uint devInst)
    {
        if (CM_Get_DevNode_Status(out uint status, out _, devInst, 0) != CR_SUCCESS)
            return true; // Status unknown — let the SetupAPI call decide.

        return (status & DN_DISABLEABLE) != 0;
    }

    /// <summary>
    /// Builds an exception that preserves the raw Win32 error code and the
    /// system's description, so failures are actually diagnosable.
    /// </summary>
    private static Win32Exception LastError(string apiName)
    {
        int code = Marshal.GetLastWin32Error();
        string system = new Win32Exception(code).Message;
        return new Win32Exception(code, $"{apiName} failed (код {code}: {system}).");
    }

    private static bool IsDisabled(uint devInst)
    {
        if (CM_Get_DevNode_Status(out uint status, out uint problem, devInst, 0) != CR_SUCCESS)
            return false;

        return (status & DN_HAS_PROBLEM) != 0 && problem == CM_PROB_DISABLED;
    }

    private static string? GetStringProperty(
        IntPtr deviceInfoSet, ref SP_DEVINFO_DATA devInfo, uint property)
    {
        SetupDiGetDeviceRegistryProperty(
            deviceInfoSet, ref devInfo, property, out _, null, 0, out uint required);

        if (required == 0)
            return null;

        var buffer = new byte[required];
        if (!SetupDiGetDeviceRegistryProperty(
                deviceInfoSet, ref devInfo, property, out _, buffer, required, out _))
        {
            return null;
        }

        string value = System.Text.Encoding.Unicode.GetString(buffer).TrimEnd('\0').Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string GetInstanceId(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA devInfo)
    {
        SetupDiGetDeviceInstanceId(deviceInfoSet, ref devInfo, null, 0, out uint required);
        if (required == 0)
            return string.Empty;

        var buffer = new char[required];
        if (!SetupDiGetDeviceInstanceId(deviceInfoSet, ref devInfo, buffer, required, out _))
            return string.Empty;

        return new string(buffer).TrimEnd('\0');
    }
}
