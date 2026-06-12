using System.Runtime.InteropServices;

namespace DisKeyboard;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Single-instance guard so the tray icon is never duplicated.
        using var mutex = new Mutex(initiallyOwned: true, "DisKeyboard.SingleInstance", out bool isNew);
        if (!isNew)
            return;

        ApplicationConfiguration.Initialize();

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            MessageBox.Show("DisKeyboard поддерживается только в Windows.",
                "DisKeyboard", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Application.Run(new MainForm());
    }
}
