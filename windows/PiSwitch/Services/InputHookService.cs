using System.Runtime.InteropServices;
using PiSwitch.Interop;

namespace PiSwitch.Services;

/// <summary>
/// Global low-level keyboard + mouse hooks running on a dedicated message-pump thread.
///
/// When "armed" (the pie menu is visible) the keyboard hook captures the next 1-8 / Esc
/// keystroke BEFORE it is dispatched to whichever window currently owns focus, and posts
/// the selection straight to the menu window. This decouples selection from OS keyboard
/// focus entirely, which is the only reliable way on Windows to make "press the number
/// immediately after the hotkey" always land — the previous SetForegroundWindow approach
/// lost the race because the daemon never received the triggering input.
///
/// The mouse hook provides outside-click dismissal (the overlay is now a no-activate
/// window, so it no longer gets a Deactivated event to cancel on).
///
/// Callback discipline (must stay under LowLevelHooksTimeout, ~300ms-1s): no file I/O,
/// no config, no logging, no locks, minimal allocation. It only branches on a volatile
/// flag, reads the hook struct, and PostMessages.
/// </summary>
public sealed class InputHookService : IDisposable
{
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _kbHook;
    private IntPtr _mouseHook;
    private NativeMethods.HookProc? _kbProc;     // kept alive so the GC can't collect the delegate
    private NativeMethods.HookProc? _mouseProc;
    private volatile IntPtr _target;
    private volatile bool _armed;
    private readonly bool[] _pendingUp = new bool[256]; // touched only on the hook thread
    private readonly ManualResetEventSlim _ready = new(false);

    public void Start(IntPtr targetHwnd)
    {
        _target = targetHwnd;
        _thread = new Thread(ThreadProc) { IsBackground = true, Name = "PiSwitch-InputHook" };
        _thread.Start();
        _ready.Wait(2000);
    }

    /// <summary>Point the hooks at a freshly-recreated menu window.</summary>
    public void Retarget(IntPtr targetHwnd) => _target = targetHwnd;

    public void Arm() => _armed = true;
    public void Disarm() => _armed = false;

    private void ThreadProc()
    {
        _threadId = NativeMethods.GetCurrentThreadId();
        _kbProc = KeyboardCallback;
        _mouseProc = MouseCallback;

        var hMod = NativeMethods.GetModuleHandle(null);
        _kbHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _kbProc, hMod, 0);
        _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc, hMod, 0);
        _ready.Set();

        while (NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessage(ref msg);
        }

        if (_kbHook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_kbHook);
        if (_mouseHook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_mouseHook);
    }

    private IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var msg = wParam.ToInt32();

            if (msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN)
            {
                if (_armed)
                {
                    var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                    var action = MapVk(data.vkCode);
                    if (action != ActionNone)
                    {
                        if (data.vkCode < 256) _pendingUp[data.vkCode] = true; // also swallow its key-up
                        _armed = false;
                        var target = _target;
                        if (action == ActionCancel)
                            NativeMethods.PostMessage(target, NativeMethods.WM_PISWITCH_CANCEL, IntPtr.Zero, IntPtr.Zero);
                        else
                            NativeMethods.PostMessage(target, NativeMethods.WM_PISWITCH_SELECT, (IntPtr)action, IntPtr.Zero);
                        return (IntPtr)1; // swallow
                    }
                }
            }
            else if (msg is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP)
            {
                var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                if (data.vkCode < 256 && _pendingUp[data.vkCode])
                {
                    _pendingUp[data.vkCode] = false;
                    return (IntPtr)1; // swallow the key-up of an already-consumed selection key
                }
            }
        }

        return NativeMethods.CallNextHookEx(_kbHook, nCode, wParam, lParam);
    }

    private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _armed)
        {
            var msg = wParam.ToInt32();
            if (msg is NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_RBUTTONDOWN or NativeMethods.WM_MBUTTONDOWN)
            {
                var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                var target = _target;
                // Ask the OS which window is under the click instead of doing rect/DPI math —
                // robust across monitors and display scaling. Anything that isn't our overlay
                // counts as "outside" and dismisses the pie. Don't swallow: let the click also
                // reach whatever is underneath.
                if (target != IntPtr.Zero && NativeMethods.WindowFromPoint(data.pt) != target)
                    NativeMethods.PostMessage(target, NativeMethods.WM_PISWITCH_CANCEL, IntPtr.Zero, IntPtr.Zero);
            }
        }

        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private const int ActionNone = -2;
    private const int ActionCancel = -1;

    private static int MapVk(uint vk)
    {
        if (vk >= 0x31 && vk <= 0x38) return (int)(vk - 0x31); // '1'..'8' -> 0..7
        if (vk >= 0x61 && vk <= 0x68) return (int)(vk - 0x61); // NumPad1..8 -> 0..7
        if (vk == 0x1B) return ActionCancel;                   // Esc
        return ActionNone;
    }

    public void Dispose()
    {
        if (_threadId != 0)
            NativeMethods.PostThreadMessage(_threadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        try { _thread?.Join(1000); } catch { }
        _ready.Dispose();
    }
}
