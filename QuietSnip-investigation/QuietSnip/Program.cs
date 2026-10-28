using System.Security.Cryptography;
using System.Text;

namespace QuietSnip;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetDefaultFont(Theme.MakeFont());

        string instanceId = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(AppContext.BaseDirectory.ToUpperInvariant())))[..16];
        string messageName = "QuietSnip.ShowSettings." + instanceId;
        using var mutex = new Mutex(true, @"Local\QuietSnip." + instanceId, out bool first);
        if (!first)
        {
            NativeMethods.PostMessage(new IntPtr(0xffff), NativeMethods.RegisterWindowMessage(messageName),
                args.Contains("--quit") ? new IntPtr(1) : IntPtr.Zero, IntPtr.Zero);
            return;
        }
        if (args.Contains("--quit")) return;

        try
        {
            using var context = new TrayApplication(messageName);
            Application.Run(context);
        }
        catch (Exception exception)
        {
            MessageBox.Show("QuietSnip couldn't start.\n\n" + exception.Message,
                "QuietSnip", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

