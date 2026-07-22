using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using PiSwitch.Interop;
using PiSwitch.Models;

namespace PiSwitch;

public partial class PieMenuWindow : Window
{
    private List<AppConfig> _apps = [];
    private PieMenuView? _pieView;
    private bool _allowClose;
    private HwndSource? _source;

    public event Action<int>? OnSelect;
    public event Action? OnCancel;

    /// <summary>The window's native handle (the keyboard/mouse hook posts selections here).</summary>
    public IntPtr Hwnd { get; private set; }

    /// <summary>RegisterWindowMessage id broadcast between instances to enforce one pie at a time.</summary>
    public uint HideOthersMessageId { get; set; }

    public PieMenuWindow()
    {
        InitializeComponent();
    }

    public void Rebuild(List<AppConfig> apps)
    {
        _apps = apps;
        RootGrid.Children.Clear();
        _pieView = new PieMenuView(apps);
        _pieView.OnSelect += index => OnSelect?.Invoke(index);
        _pieView.OnCancel += () => OnCancel?.Invoke();
        RootGrid.Children.Add(_pieView);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Hwnd = new WindowInteropHelper(this).Handle;

        // Turn the overlay into a no-activate, tool-window, topmost layer. Showing it then
        // never steals foreground or keyboard focus from the app the user is in, so the
        // immediate number keypress (captured globally by InputHookService) is never lost,
        // and there is no taskbar flash / deactivate churn / focus-guard war to manage.
        var ex = NativeMethods.GetWindowLongPtr(Hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        ex |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST;
        NativeMethods.SetWindowLongPtr(Hwnd, NativeMethods.GWL_EXSTYLE, (IntPtr)ex);

        _source = HwndSource.FromHwnd(Hwnd);
        _source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Another instance is showing its pie — hide ours (ignore our own broadcast).
        if (HideOthersMessageId != 0 && (uint)msg == HideOthersMessageId)
        {
            if (wParam.ToInt32() != Environment.ProcessId)
            {
                handled = true;
                OnCancel?.Invoke();
            }
            return IntPtr.Zero;
        }

        switch ((uint)msg)
        {
            case NativeMethods.WM_MOUSEACTIVATE:
                // A click on the pie must not activate it (keeps it a true overlay).
                handled = true;
                return (IntPtr)NativeMethods.MA_NOACTIVATE;

            case NativeMethods.WM_PISWITCH_SELECT:
                handled = true;
                OnSelect?.Invoke(wParam.ToInt32());
                return IntPtr.Zero;

            case NativeMethods.WM_PISWITCH_CANCEL:
                handled = true;
                OnCancel?.Invoke();
                return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    public void ShowAtCursor()
    {
        if (!NativeMethods.GetCursorPos(out var cursorPos))
            return;

        // Work area of the monitor under the cursor.
        var hMonitor = NativeMethods.MonitorFromPoint(cursorPos, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var monitorInfo = new NativeMethods.MONITORINFO
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>()
        };
        NativeMethods.GetMonitorInfo(hMonitor, ref monitorInfo);
        var workArea = monitorInfo.rcWork;

        var source = PresentationSource.FromVisual(this);
        var dpiX = source?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
        var dpiY = source?.CompositionTarget?.TransformFromDevice.M22 ?? 1.0;

        var screenLeft = workArea.Left * dpiX;
        var screenTop = workArea.Top * dpiY;
        var screenRight = workArea.Right * dpiX;
        var screenBottom = workArea.Bottom * dpiY;

        var mouseX = cursorPos.X * dpiX;
        var mouseY = cursorPos.Y * dpiY;

        var winX = mouseX - Width / 2;
        var winY = mouseY - Height / 2;

        const double margin = 60;
        winX = Math.Clamp(winX, screenLeft + margin, screenRight - Width - margin);
        winY = Math.Clamp(winY, screenTop + margin, screenBottom - Height - margin);

        Left = winX;
        Top = winY;

        _pieView?.Highlight(null);
        Visibility = Visibility.Visible;

        // Bring topmost + ensure shown, WITHOUT activating (no focus steal).
        if (Hwnd != IntPtr.Zero)
        {
            NativeMethods.SetWindowPos(Hwnd, NativeMethods.HWND_TOPMOST,
                0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
                NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_NOACTIVATE);
        }

        Services.Logger.Event($"window-pos left={Left:F0} top={Top:F0} hwnd={Hwnd}");
    }

    public void HideMenu()
    {
        Visibility = Visibility.Hidden;
    }

    /// <summary>
    /// Prevents the window from being closed by the system (e.g. during hibernate).
    /// Use <see cref="ForceClose"/> when actually exiting the application.
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            HideMenu();
            return;
        }

        base.OnClosing(e);
    }

    public void ForceClose()
    {
        _allowClose = true;
        if (_source != null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }
        Close();
    }
}
