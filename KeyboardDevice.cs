namespace DisKeyboard;

/// <summary>
/// Represents a single keyboard discovered on the system, together with the
/// device node that is actually toggled (which may be the keyboard's parent
/// hardware device when the keyboard-class node itself is not disableable).
/// </summary>
public sealed class KeyboardDevice
{
    /// <summary>Human readable name (friendly name or device description).</summary>
    public string Name { get; init; } = "Unknown keyboard";

    /// <summary>Instance id of the keyboard-class node itself.</summary>
    public string InstanceId { get; init; } = string.Empty;

    /// <summary>
    /// Instance id of the node that enable/disable operates on. Equals
    /// <see cref="InstanceId"/> when the keyboard node is directly disableable,
    /// otherwise the parent hardware device.
    /// </summary>
    public string TargetInstanceId { get; init; } = string.Empty;

    /// <summary>True when the target node is currently enabled.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>False when Windows reports the target as non-disableable.</summary>
    public bool CanDisable { get; init; } = true;

    /// <summary>True when the toggled node differs from the keyboard node.</summary>
    public bool UsesParent => !string.Equals(
        InstanceId, TargetInstanceId, StringComparison.OrdinalIgnoreCase);

    public string DisplayName
    {
        get
        {
            string state = IsEnabled ? "вкл" : "откл";
            string suffix = CanDisable ? string.Empty : ", нельзя отключить";
            return $"{Name}  [{state}{suffix}]";
        }
    }

    public override string ToString() => DisplayName;
}
