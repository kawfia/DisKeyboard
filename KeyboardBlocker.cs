using static DisKeyboard.NativeMethods;

namespace DisKeyboard;

/// <summary>
/// Globally swallows keyboard input with a low-level keyboard hook
/// (<c>WH_KEYBOARD_LL</c>).
///
/// This is the guaranteed fallback for keyboards that Windows refuses to
/// disable at the device level — most notably the built-in PS/2 keyboard,
/// whose i8042 controller also drives the touchpad and is therefore marked
/// non-disableable. Unlike SetupAPI device-disable, the hook does not care
/// what the device is: it simply discards every keystroke before it reaches
/// any application.
///
/// Trade-offs, stated plainly:
/// <list type="bullet">
/// <item>It blocks <b>all</b> keyboards, not a single one, so a mouse is
/// needed to turn it back off (the tray menu and the window both work).</item>
/// <item>As a safety hatch the combo <c>Ctrl+Alt+End</c> is always let
/// through and releases the lock, so a user can recover without a mouse.</item>
/// <item>It only blocks while the app runs; it is not a permanent device
/// change and the secure desktop (UAC / Ctrl+Alt+Del) is never affected.</item>
/// </list>
/// </summary>
internal sealed class KeyboardBlocker : IDisposable
{
    // Keep a managed reference to the delegate for the lifetime of the hook so
    // the GC cannot collect it while Windows still holds the callback pointer.
    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hook = IntPtr.Zero;

    /// <summary>Raised (on the UI thread's message pump) when the escape combo
    /// releases the lock, so the UI can refresh its state.</summary>
    public event Action? Released;

    public KeyboardBlocker()
    {
        _proc = HookCallback;
    }

    public bool IsActive => _hook != IntPtr.Zero;

    public void Start()
    {
        if (IsActive)
            return;

        // For a global low-level hook the module handle may be any loaded
        // module; the main module is the conventional choice.
        IntPtr hMod = GetModuleHandle(null);
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, hMod, 0);
        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException(
                "Не удалось установить перехват клавиатуры (SetWindowsHookEx).");
    }

    public void Stop()
    {
        if (!IsActive)
            return;

        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode != HC_ACTION)
            return CallNextHookEx(_hook, nCode, wParam, lParam);

        uint message = (uint)wParam.ToInt64();
        bool isKeyDown = message is WM_KEYDOWN or WM_SYSKEYDOWN;

        // Escape hatch: let Ctrl+Alt+End through and use it to release the lock,
        // so the keyboard can recover even with no mouse available.
        if (isKeyDown && IsEscapeCombo(lParam))
        {
            Stop();
            Released?.Invoke();
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        // Swallow everything else: returning a non-zero value stops the event
        // from propagating to any other hook or application.
        return new IntPtr(1);
    }

    private static bool IsEscapeCombo(IntPtr lParam)
    {
        var data = System.Runtime.InteropServices.Marshal
            .PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

        if (data.vkCode != VK_END)
            return false;

        // High bit set means the key is currently down.
        bool ctrl = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
        bool alt = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
        return ctrl && alt;
    }

    public void Dispose() => Stop();
}
