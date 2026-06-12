namespace DisKeyboard;

/// <summary>
/// Represents a single keyboard device discovered on the system.
/// </summary>
public sealed class KeyboardDevice
{
    /// <summary>Human readable name (friendly name or device description).</summary>
    public string Name { get; init; } = "Unknown keyboard";

    /// <summary>Stable device instance id, e.g. "HID\VID_046D&amp;PID_C31C\6&amp;...".</summary>
    public string InstanceId { get; init; } = string.Empty;

    /// <summary>True when the device is currently enabled.</summary>
    public bool IsEnabled { get; set; }

    public override string ToString() => Name;
}
