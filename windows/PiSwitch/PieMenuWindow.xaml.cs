using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using PiSwitch.Interop;
using PiSwitch.Models;

namespace PiSwitch;

public partial class PieMenuWindow : Window
{
    private const int FocusGuardDurationMs = 900;
    private const int FocusGuardTickMs = 45;

    private List<AppConfig> _apps = [];
    private PieMenuView? _pieView;
    private bool _suppressDeactivate;
    private bool _allowClose;
    private readonly DispatcherTimer _suppressTimer;
    private readonly DispatcherTimer _focusGuardTimer;
    private DateTime _focusGuardUntilUtc = DateTime.MinValue;

    public event Action<int>? OnSelect;
    public event Action? OnCancel;

    public PieMenuWindow()
    {
        InitializeComponent();
        _suppressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _suppressTimer.Tick += (_, _) =>
        {
            _suppressTimer.Stop();
            _suppressDeactivate = false;
        };

        _focusGuardTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FocusGuardTickMs) };
        _focusGuardTimer.Tick += (_, _) =>
        {
            if (!IsFocusGuardActive)
            {
                _focusGuardTimer.Stop();
                return;
            }

            EnsureForeground();
        };
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

    public void ShowAtCursor()
    {
        _suppressDeactivate = true;
        _suppressTimer.Stop();

        if (!NativeMethods.GetCursorPos(out var cursorPos))
        {
            _suppressDeactivate = false;
            return;
        }

        // Get work area of the monitor containing the cursor
        var hMonitor = NativeMethods.MonitorFromPoint(cursorPos, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var monitorInfo = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        NativeMethods.GetMonitorInfo(hMonitor, ref monitorInfo);
        var workArea = monitorInfo.rcWork;

        // Convert to WPF device-independent pixels
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
        EnsureForeground();
        _focusGuardUntilUtc = DateTime.UtcNow.AddMilliseconds(FocusGuardDurationMs);
        _focusGuardTimer.Stop();
        _focusGuardTimer.Start();

        var hwnd = new WindowInteropHelper(this).Handle;

        Services.Logger.Event($"window-pos left={Left:F0} top={Top:F0} w={Width:F0} h={Height:F0} hwnd={hwnd}");

        // Use a timer to clear the suppress flag — Deactivated fires asynchronously
        // after this method returns, so we can't clear it synchronously here.
        _suppressTimer.Start();
    }

    public void HideMenu()
    {
        _suppressDeactivate = true;
        _suppressTimer.Stop();
        _focusGuardTimer.Stop();
        _focusGuardUntilUtc = DateTime.MinValue;
        Visibility = Visibility.Hidden;
    }

    /// <summary>
    /// Called by the app immediately before activating a target app, while the pie
    /// menu is still visible. Stops the focus-guard so it can't race the launcher's
    /// SetForegroundWindow call, and arms the deactivate suppressor so the
    /// inevitable focus loss to the target window doesn't fire OnCancel.
    /// The window stays visible — App.LaunchApp() calls HideMenu() afterwards.
    /// </summary>
    public void PrepareForLaunch()
    {
        _focusGuardTimer.Stop();
        _focusGuardUntilUtc = DateTime.MinValue;
        _suppressDeactivate = true;
        _suppressTimer.Stop();
        _suppressTimer.Start();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        var appCount = _apps.Count;

        switch (e.Key)
        {
            case Key.Escape:
                OnCancel?.Invoke();
                e.Handled = true;
                return;
            case Key.D1 or Key.NumPad1: if (appCount > 0) { OnSelect?.Invoke(0); e.Handled = true; } return;
            case Key.D2 or Key.NumPad2: if (appCount > 1) { OnSelect?.Invoke(1); e.Handled = true; } return;
            case Key.D3 or Key.NumPad3: if (appCount > 2) { OnSelect?.Invoke(2); e.Handled = true; } return;
            case Key.D4 or Key.NumPad4: if (appCount > 3) { OnSelect?.Invoke(3); e.Handled = true; } return;
            case Key.D5 or Key.NumPad5: if (appCount > 4) { OnSelect?.Invoke(4); e.Handled = true; } return;
            case Key.D6 or Key.NumPad6: if (appCount > 5) { OnSelect?.Invoke(5); e.Handled = true; } return;
            case Key.D7 or Key.NumPad7: if (appCount > 6) { OnSelect?.Invoke(6); e.Handled = true; } return;
            case Key.D8 or Key.NumPad8: if (appCount > 7) { OnSelect?.Invoke(7); e.Handled = true; } return;
        }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (IsFocusGuardActive)
        {
            Dispatcher.BeginInvoke(EnsureForeground, DispatcherPriority.Input);
            return;
        }

        if (!_suppressDeactivate && Visibility == Visibility.Visible)
            OnCancel?.Invoke();
    }

    private bool IsFocusGuardActive =>
        Visibility == Visibility.Visible && DateTime.UtcNow < _focusGuardUntilUtc;

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
        Close();
    }

    private void EnsureForeground()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST,
                0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
            NativeMethods.SetForegroundWindow(hwnd);
        }

        Activate();
        Focus();
        Keyboard.Focus(this);
    }
}
