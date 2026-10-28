using System.Runtime.InteropServices;

namespace SnippingToolPlus;

internal enum CaptureMode { Copy, Save }

internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private readonly uint showMessage;
    private AppSettings? registered;
    public event Action<CaptureMode>? CaptureRequested;
    public event Action<AppAction>? ActionRequested;
    public event Action? SettingsRequested;
    public event Action? QuitRequested;
    public event Action? ThemeChanged;

    public HotkeyWindow(string messageName)
    {
        showMessage = NativeMethods.RegisterWindowMessage(messageName);
        CreateHandle(new CreateParams { Caption = "Snipping Tool+ shortcuts" });
    }

    public void Apply(AppSettings settings)
    {
        settings.Validate();
        AppSettings? previous = registered;
        Unregister();
        try
        {
            foreach (var pair in settings.Shortcuts()) Register(pair.Value, (int)pair.Key + 1, ActionNames.Label(pair.Key));
            registered = settings;
        }
        catch (Exception failure)
        {
            Unregister();
            if (previous != null)
            {
                try
                {
                    foreach (var pair in previous.Shortcuts()) Register(pair.Value, (int)pair.Key + 1, ActionNames.Label(pair.Key));
                    registered = previous;
                }
                catch
                {
                    Unregister();
                    throw new InvalidOperationException(failure.Message +
                        "\nThe previous shortcuts could not be restored. Choose two available shortcuts and save again.");
                }
            }
            throw;
        }
    }

    private void Register(Hotkey hotkey, int id, string action)
    {
        if (!NativeMethods.RegisterHotKey(Handle, id, (uint)hotkey.Modifiers | 0x4000, (uint)hotkey.Key))
        {
            int error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(error == 1409
                ? $"{action}: {hotkey} is already used by Windows or another app. Choose a different shortcut."
                : $"{action}: couldn't register {hotkey} (Windows error {error}). Choose a different shortcut.");
        }
    }

    private void Unregister()
    {
        foreach (AppAction action in Enum.GetValues<AppAction>()) NativeMethods.UnregisterHotKey(Handle, (int)action + 1);
        registered = null;
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0312)
        {
            var action = (AppAction)(message.WParam.ToInt32() - 1);
            ActionRequested?.Invoke(action);
            if (action is AppAction.CopySnip or AppAction.SaveSnip)
                CaptureRequested?.Invoke(action == AppAction.CopySnip ? CaptureMode.Copy : CaptureMode.Save);
        }
        else if (message.Msg is 0x001a or 0x031a or 0x007e or 0x02e0) ThemeChanged?.Invoke();
        else if ((uint)message.Msg == showMessage)
        {
            if (message.WParam == new IntPtr(1)) QuitRequested?.Invoke();
            else SettingsRequested?.Invoke();
        }
        base.WndProc(ref message);
    }

    public void Dispose()
    {
        Unregister();
        DestroyHandle();
    }
}

