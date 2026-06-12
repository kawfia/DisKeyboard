using System.Security.Principal;

namespace DisKeyboard;

/// <summary>
/// Main window: lists keyboards with an enable/disable checkbox each.
/// Closing the window hides it to the tray; right-clicking the tray icon
/// restores it.
/// </summary>
public sealed class MainForm : Form
{
    private readonly CheckedListBox _list = new();
    private readonly Button _refreshButton = new();
    private readonly Label _hint = new();
    private readonly NotifyIcon _trayIcon = new();

    private List<KeyboardDevice> _devices = new();
    private bool _suppressItemCheck;
    private bool _reallyExit;

    public MainForm()
    {
        BuildUi();
        BuildTray();
        ReloadDevices();
    }

    private void BuildUi()
    {
        Text = IsElevated() ? "DisKeyboard (администратор)" : "DisKeyboard — БЕЗ прав администратора!";
        ClientSize = new Size(460, 320);
        MinimumSize = new Size(360, 240);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = SystemIcons.Application;

        _hint.Text = "Снимите галочку, чтобы отключить клавиатуру; поставьте — чтобы включить.";
        _hint.Dock = DockStyle.Top;
        _hint.Padding = new Padding(8, 8, 8, 4);
        _hint.AutoSize = false;
        _hint.Height = 40;

        _list.Dock = DockStyle.Fill;
        _list.CheckOnClick = true;
        _list.IntegralHeight = false;
        _list.ItemCheck += OnItemCheck;

        _refreshButton.Text = "Обновить список";
        _refreshButton.Dock = DockStyle.Bottom;
        _refreshButton.Height = 36;
        _refreshButton.Click += (_, _) => ReloadDevices();

        // Add in reverse order of docking precedence.
        Controls.Add(_list);
        Controls.Add(_hint);
        Controls.Add(_refreshButton);
    }

    private void BuildTray()
    {
        var menu = new ContextMenuStrip();

        var openItem = new ToolStripMenuItem("Развернуть", null, (_, _) => RestoreWindow())
        {
            Font = new Font(menu.Font, FontStyle.Bold),
        };
        var exitItem = new ToolStripMenuItem("Выход", null, (_, _) => ExitApplication());

        menu.Items.Add(openItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _trayIcon.Icon = SystemIcons.Application;
        _trayIcon.Text = "DisKeyboard";
        _trayIcon.Visible = true;
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => RestoreWindow();
    }

    private void ReloadDevices()
    {
        try
        {
            _devices = DeviceManager.GetKeyboards();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Не удалось получить список клавиатур:\n{ex.Message}",
                "DisKeyboard", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _suppressItemCheck = true;
        try
        {
            _list.Items.Clear();
            foreach (var device in _devices)
                _list.Items.Add(device, device.IsEnabled);
        }
        finally
        {
            _suppressItemCheck = false;
        }
    }

    private void OnItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if (_suppressItemCheck)
            return;

        if (e.Index < 0 || e.Index >= _devices.Count)
            return;

        var device = _devices[e.Index];
        bool enable = e.NewValue == CheckState.Checked;

        try
        {
            DeviceManager.SetEnabled(device.InstanceId, enable);
            device.IsEnabled = enable;
        }
        catch (Exception ex)
        {
            // Revert the visual state to match reality.
            e.NewValue = e.CurrentValue;
            MessageBox.Show(this,
                $"Не удалось {(enable ? "включить" : "отключить")} клавиатуру \"{device.Name}\":\n{ex.Message}",
                "DisKeyboard", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private void RestoreWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        ShowInTaskbar = true;
        Activate();
        // Reflect any external changes since the window was hidden.
        ReloadDevices();
    }

    private void ExitApplication()
    {
        _reallyExit = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Closing via the window's X button only hides to the tray.
        if (!_reallyExit && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            ShowInTaskbar = false;
            return;
        }

        _trayIcon.Visible = false;
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayIcon.Dispose();
            _list.Dispose();
        }
        base.Dispose(disposing);
    }
}
