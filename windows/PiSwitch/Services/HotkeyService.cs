using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using PiSwitch.Interop;

namespace PiSwitch.Services;

public class HotkeyService : IDisposable
{
    private HwndSource? _source;
    private readonly Dictionary<int, Action> _handlers = [];
    private int _nextId = 1;

    public void Initialize(Window window)
    {
        var helper = new WindowInteropHelper(window);
        if (helper.Handle == IntPtr.Zero)
            helper.EnsureHandle();
        _source = HwndSource.FromHwnd(helper.Handle);
        _source?.AddHook(WndProc);
    }

    public int Register(ModifierKeys modifiers, Key key, Action handler)
    {
        if (_source == null) return -1;

        var id = _nextId++;
        uint mod = ConvertModifiers(modifiers) | NativeMethods.MOD_NOREPEAT;
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);

        if (NativeMethods.RegisterHotKey(_source.Handle, id, mod, vk))
        {
            _handlers[id] = handler;
            return id;
        }

        return -1;
    }

    public void Unregister(int id)
    {
        if (_source == null || !_handlers.ContainsKey(id)) return;
        NativeMethods.UnregisterHotKey(_source.Handle, id);
        _handlers.Remove(id);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if (_handlers.TryGetValue(id, out var handler))
            {
                handler();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private static uint ConvertModifiers(ModifierKeys modifiers)
    {
        uint result = 0;
        if (modifiers.HasFlag(ModifierKeys.Alt)) result |= NativeMethods.MOD_ALT;
        if (modifiers.HasFlag(ModifierKeys.Control)) result |= NativeMethods.MOD_CONTROL;
        if (modifiers.HasFlag(ModifierKeys.Shift)) result |= NativeMethods.MOD_SHIFT;
        if (modifiers.HasFlag(ModifierKeys.Windows)) result |= NativeMethods.MOD_WIN;
        return result;
    }

    public void Dispose()
    {
        if (_source != null)
        {
            foreach (var id in _handlers.Keys.ToList())
                NativeMethods.UnregisterHotKey(_source.Handle, id);
            _handlers.Clear();
            _source.RemoveHook(WndProc);
            _source = null;
        }
    }
}
