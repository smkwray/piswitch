using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using PiSwitch.Interop;

namespace PiSwitch.Services;

public class TrayIcon : IDisposable
{
    private NativeMethods.NOTIFYICONDATA _nid;
    private HwndSource? _hwndSource;
    private bool _added;

    private const uint ID_SHOW = 1;
    private const uint ID_RELOAD = 2;
    private const uint ID_EXIT = 3;

    public event Action? ShowMenuRequested;
    public event Action? ReloadRequested;
    public event Action? ExitRequested;

    public void Initialize(Window ownerWindow, string tooltip)
    {
        var helper = new WindowInteropHelper(ownerWindow);
        if (helper.Handle == IntPtr.Zero) helper.EnsureHandle();
        _hwndSource = HwndSource.FromHwnd(helper.Handle);
        _hwndSource?.AddHook(WndProc);

        _nid = new NativeMethods.NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
            hWnd = helper.Handle,
            uID = 1,
            uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP,
            uCallbackMessage = NativeMethods.WM_TRAYICON,
            hIcon = GetDefaultIcon(),
            szTip = tooltip.Length > 127 ? tooltip[..127] : tooltip
        };

        _added = NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref _nid);
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);

    private static IntPtr GetDefaultIcon()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (exePath != null)
            {
                var icon = ExtractIcon(IntPtr.Zero, exePath, 0);
                if (icon != IntPtr.Zero) return icon;
            }
        }
        catch { }
        return IntPtr.Zero;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_TRAYICON)
        {
            var mouseMsg = lParam.ToInt32();
            if (mouseMsg == NativeMethods.WM_LBUTTONDBLCLK)
            {
                ShowMenuRequested?.Invoke();
                handled = true;
            }
            else if (mouseMsg == NativeMethods.WM_RBUTTONUP)
            {
                ShowContextMenu(hwnd);
                handled = true;
            }
        }
        else if (msg == NativeMethods.WM_COMMAND)
        {
            var id = (uint)(wParam.ToInt32() & 0xFFFF);
            switch (id)
            {
                case ID_SHOW: ShowMenuRequested?.Invoke(); handled = true; break;
                case ID_RELOAD: ReloadRequested?.Invoke(); handled = true; break;
                case ID_EXIT: ExitRequested?.Invoke(); handled = true; break;
            }
        }

        return IntPtr.Zero;
    }

    private void ShowContextMenu(IntPtr hwnd)
    {
        var hMenu = NativeMethods.CreatePopupMenu();
        NativeMethods.AppendMenu(hMenu, NativeMethods.MF_STRING, ID_SHOW, "Show Menu");
        NativeMethods.AppendMenu(hMenu, NativeMethods.MF_STRING, ID_RELOAD, "Reload Config");
        NativeMethods.AppendMenu(hMenu, NativeMethods.MF_SEPARATOR, 0, "");
        NativeMethods.AppendMenu(hMenu, NativeMethods.MF_STRING, ID_EXIT, "Exit");

        NativeMethods.GetCursorPos(out var pt);
        NativeMethods.SetForegroundWindow(hwnd);

        var cmd = NativeMethods.TrackPopupMenu(hMenu,
            NativeMethods.TPM_RETURNCMD | NativeMethods.TPM_NONOTIFY,
            pt.X, pt.Y, 0, hwnd, IntPtr.Zero);

        NativeMethods.DestroyMenu(hMenu);

        if (cmd > 0)
        {
            switch ((uint)cmd)
            {
                case ID_SHOW: ShowMenuRequested?.Invoke(); break;
                case ID_RELOAD: ReloadRequested?.Invoke(); break;
                case ID_EXIT: ExitRequested?.Invoke(); break;
            }
        }
    }

    public void Dispose()
    {
        if (_added)
        {
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref _nid);
            _added = false;
        }
        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }
    }
}
