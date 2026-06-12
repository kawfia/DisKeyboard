using System.ComponentModel;
using System.Runtime.InteropServices;
using static DisKeyboard.NativeMethods;

namespace DisKeyboard;

/// <summary>
/// Enumerates keyboard devices and toggles their enabled state through SetupAPI.
///
/// Many laptop keyboards expose a keyboard-class node (e.g. "Standard PS/2
/// Keyboard" or "HID Keyboard Device") that Windows marks as non-disableable.
/// In that case the effective hardware device is the node's parent in the
/// device tree, so this manager resolves and operates on that parent instead.
/// </summary>
public static class DeviceManager
{
    /// <summary>
    /// Returns every keyboard currently present, with the node that should be
    /// toggled already resolved.
    /// </summary>
    public static List<KeyboardDevice> GetKeyboards()
    {
        var result = new List<KeyboardDevice>();
        Guid classGuid = GUID_DEVCLASS_KEYBOARD;

        IntPtr deviceInfoSet = SetupDiGetClassDevs(
            ref classGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT);

        if (deviceInfoSet == INVALID_HANDLE_VALUE)
            throw LastError("SetupDiGetClassDevs");

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

                string nodeId = GetInstanceId(deviceInfoSet, ref devInfo);
                bool nodeDisableable = (GetStatus(devInfo.DevInst).status & DN_DISABLEABLE) != 0;

                // Resolve which node we actually toggle.
                string targetId = nodeId;
                bool targetDisableable = nodeDisableable;
                bool targetEnabled = !IsDisabled(devInfo.DevInst);

                if (!nodeDisableable)
                {
                    string? parentId = GetParentInstanceId(devInfo.DevInst);
                    if (parentId is not null)
                    {
                        var parent = ReadStatus(parentId);
                        if (parent is { } p)
                        {
                            targetId = parentId;
                            targetDisableable = (p.status & DN_DISABLEABLE) != 0;
                            targetEnabled = !((p.status & DN_HAS_PROBLEM) != 0 && p.problem == CM_PROB_DISABLED);
                            name = $"{name} (через родителя)";
                        }
                    }
                }

                result.Add(new KeyboardDevice
                {
                    Name = name,
                    InstanceId = nodeId,
                    TargetInstanceId = targetId,
                    IsEnabled = targetEnabled,
                    CanDisable = targetDisableable,
                });

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

        MergeDisabledStore(result);
        return result;
    }

    /// <summary>
    /// Re-adds keyboards we disabled whose keyboard-class child node has
    /// disappeared from enumeration (because we disabled the parent), so they
    /// can still be re-enabled from the UI.
    /// </summary>
    private static void MergeDisabledStore(List<KeyboardDevice> discovered)
    {
        var store = LoadDisabledStore();
        if (store.Count == 0)
            return;

        var presentTargets = new HashSet<string>(
            discovered.Select(d => d.TargetInstanceId), StringComparer.OrdinalIgnoreCase);

        bool storeChanged = false;
        foreach (var (targetId, name) in store.ToList())
        {
            if (presentTargets.Contains(targetId))
            {
                // Already shown by normal enumeration — no longer our concern.
                store.Remove(targetId);
                storeChanged = true;
                continue;
            }

            var status = ReadStatus(targetId);
            if (status is not { } s)
                continue; // Device currently unplugged; keep it for later.

            bool disabled = (s.status & DN_HAS_PROBLEM) != 0 && s.problem == CM_PROB_DISABLED;
            if (!disabled)
            {
                // It got re-enabled elsewhere; drop it.
                store.Remove(targetId);
                storeChanged = true;
                continue;
            }

            discovered.Add(new KeyboardDevice
            {
                Name = name,
                InstanceId = targetId,
                TargetInstanceId = targetId,
                IsEnabled = false,
                CanDisable = (s.status & DN_DISABLEABLE) != 0,
            });
        }

        if (storeChanged)
            SaveDisabledStore(store);
    }

    /// <summary>
    /// Enables or disables the keyboard's resolved target node. Requires
    /// elevation. Remembers disabled devices so they can be re-enabled even
    /// after their keyboard-class node disappears.
    /// </summary>
    public static void SetEnabled(KeyboardDevice device, bool enable)
    {
        string targetInstanceId = device.TargetInstanceId;
        if (string.IsNullOrEmpty(targetInstanceId))
            throw new ArgumentException("Instance id is required.", nameof(device));

        IntPtr set = SetupDiCreateDeviceInfoList(IntPtr.Zero, IntPtr.Zero);
        if (set == INVALID_HANDLE_VALUE)
            throw LastError("SetupDiCreateDeviceInfoList");

        try
        {
            var devInfo = new SP_DEVINFO_DATA();
            devInfo.cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>();

            if (!SetupDiOpenDeviceInfo(set, targetInstanceId, IntPtr.Zero, 0, ref devInfo))
                throw LastError("SetupDiOpenDeviceInfo");

            ChangeState(set, ref devInfo, enable);
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        UpdateDisabledStore(targetInstanceId, device.Name, disabled: !enable);
    }

    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DisKeyboard", "disabled.tsv");

    private static Dictionary<string, string> LoadDisabledStore()
    {
        var store = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(StorePath))
                return store;

            foreach (string line in File.ReadAllLines(StorePath))
            {
                int tab = line.IndexOf('\t');
                if (tab <= 0)
                    continue;
                store[line[..tab]] = line[(tab + 1)..];
            }
        }
        catch { /* повреждённый кэш не критичен */ }

        return store;
    }

    private static void SaveDisabledStore(Dictionary<string, string> store)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllLines(StorePath, store.Select(kv => $"{kv.Key}\t{kv.Value}"));
        }
        catch { /* запись кэша не критична */ }
    }

    private static void UpdateDisabledStore(string targetId, string name, bool disabled)
    {
        var store = LoadDisabledStore();
        if (disabled)
            store[targetId] = name;
        else
            store.Remove(targetId);
        SaveDisabledStore(store);
    }

    private static void ChangeState(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA devInfo, bool enable)
    {
        if (!enable && (GetStatus(devInfo.DevInst).status & DN_DISABLEABLE) == 0)
        {
            throw new InvalidOperationException(
                "Это устройство нельзя отключить: Windows помечает его как не " +
                "отключаемое. У встроенной PS/2-клавиатуры родителем часто служит " +
                "контроллер i8042, который также обслуживает тачпад, поэтому " +
                "Windows запрещает его отключение.");
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

    private static bool IsDisabled(uint devInst)
    {
        var s = GetStatus(devInst);
        return (s.status & DN_HAS_PROBLEM) != 0 && s.problem == CM_PROB_DISABLED;
    }

    private static (uint status, uint problem) GetStatus(uint devInst)
    {
        if (CM_Get_DevNode_Status(out uint status, out uint problem, devInst, 0) != CR_SUCCESS)
            return (0, 0);
        return (status, problem);
    }

    private static (uint status, uint problem)? ReadStatus(string instanceId)
    {
        IntPtr set = SetupDiCreateDeviceInfoList(IntPtr.Zero, IntPtr.Zero);
        if (set == INVALID_HANDLE_VALUE)
            return null;

        try
        {
            var devInfo = new SP_DEVINFO_DATA();
            devInfo.cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>();
            if (!SetupDiOpenDeviceInfo(set, instanceId, IntPtr.Zero, 0, ref devInfo))
                return null;
            return GetStatus(devInfo.DevInst);
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
    }

    private static string? GetParentInstanceId(uint devInst)
    {
        if (CM_Get_Parent(out uint parent, devInst, 0) != CR_SUCCESS)
            return null;
        if (CM_Get_Device_ID_Size(out uint len, parent, 0) != CR_SUCCESS || len == 0)
            return null;

        var buffer = new char[len + 1];
        if (CM_Get_Device_ID(parent, buffer, len + 1, 0) != CR_SUCCESS)
            return null;

        return new string(buffer).TrimEnd('\0');
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

    /// <summary>
    /// Builds an exception that preserves the raw Win32 error code, the
    /// system's description and a short hint, and logs it to %TEMP% so failures
    /// are easy to capture.
    /// </summary>
    private static Win32Exception LastError(string apiName)
    {
        int code = Marshal.GetLastWin32Error();
        string system = new Win32Exception(code).Message;
        string hint = code switch
        {
            5 => " Похоже, приложение запущено БЕЗ прав администратора.",
            13 => " Неверные данные (ERROR_INVALID_DATA) — проблема со структурой вызова.",
            87 => " Неверный параметр (ERROR_INVALID_PARAMETER).",
            _ => string.Empty,
        };

        string message = $"{apiName} failed (код {code}: {system}).{hint}";
        try
        {
            string log = Path.Combine(Path.GetTempPath(), "DisKeyboard.log");
            File.AppendAllText(log, $"{DateTime.Now:O}  {message}{Environment.NewLine}");
        }
        catch { /* логирование не критично */ }

        return new Win32Exception(code, message);
    }
}
