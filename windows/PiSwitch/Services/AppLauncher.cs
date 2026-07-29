using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace PiSwitch.Services;

public static class AppLauncher
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr hWnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    private const int SW_RESTORE = 9;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOPMOST = 0x00000008L;
    private const long WS_EX_TOOLWINDOW = 0x00000080L;
    private const long WS_EX_APPWINDOW = 0x00040000L;
    private const long WS_EX_NOACTIVATE = 0x08000000L;
    private const uint GW_OWNER = 4;
    private const int DWMWA_CLOAKED = 14;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>
    /// Reliably brings <paramref name="hwnd"/> to the foreground. A background process'
    /// bare SetForegroundWindow is demoted by Windows' foreground lock, so we temporarily
    /// attach to the current foreground thread's input queue to borrow its foreground rights.
    /// </summary>
    private static void ForceForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        var foreground = GetForegroundWindow();
        var targetThread = GetWindowThreadProcessId(foreground, out _);
        var thisThread = GetCurrentThreadId();

        var attached = false;
        try
        {
            if (foreground != IntPtr.Zero && targetThread != thisThread)
                attached = AttachThreadInput(thisThread, targetThread, true);

            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
                AttachThreadInput(thisThread, targetThread, false);
        }
    }

    public static void Launch(string appName, string appHome, string? configPath = null)
    {
        // First try to activate an existing window for this app
        if (TryActivateExisting(appName))
            return;

        // Not running — launch it (prefer config path if provided)
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            try { StartProcess(configPath); return; } catch { }
        }

        LaunchNew(appName, appHome);
    }

    private static bool TryActivateExisting(string appName)
    {
        var processName = ResolveProcessName(appName);
        if (processName == null) return false;

        try
        {
            var procs = Process.GetProcessesByName(processName);
            if (procs.Length == 0) return false;

            // Process.MainWindowHandle is not reliable for multi-window apps. Electron can
            // report a voice bubble or other overlay as the main window, so enumerate every
            // top-level window for every matching process and select the best app window.
            var processIds = procs.Select(proc => (uint)proc.Id).ToHashSet();
            var hwnd = FindBestWindowByProcessIds(processIds);
            if (hwnd != IntPtr.Zero)
            {
                if (IsIconic(hwnd))
                    ShowWindow(hwnd, SW_RESTORE);

                ForceForeground(hwnd);
                return true;
            }

            // For tray apps that truly have no discoverable app window,
            // re-launching the exe typically causes the existing instance to show
            // its main window. Return false so the caller falls through to launch.
        }
        catch
        {
            // Fall through to launch
        }

        return false;
    }

    /// <summary>
    /// Finds the best visible top-level app window across the matching process IDs.
    /// Tool/no-activate overlays and DWM-cloaked windows are not app surfaces. Among
    /// the remaining candidates, prefer taskbar windows, ownerless windows, titled
    /// windows, and finally the largest surface.
    /// </summary>
    private static IntPtr FindBestWindowByProcessIds(HashSet<uint> targetPids)
    {
        IntPtr bestHwnd = IntPtr.Zero;
        long bestScore = long.MinValue;

        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (!targetPids.Contains(pid) || !IsWindowVisible(hwnd))
                return true;

            var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            if ((exStyle & (WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE)) != 0)
                return true;

            if (DwmGetWindowAttribute(
                    hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0
                && cloaked != 0)
                return true;

            long score = 0;
            if ((exStyle & WS_EX_APPWINDOW) != 0) score += 4_000_000_000L;
            if (GetWindow(hwnd, GW_OWNER) == IntPtr.Zero) score += 2_000_000_000L;
            if (GetWindowTextLength(hwnd) > 0) score += 1_000_000_000L;
            if ((exStyle & WS_EX_TOPMOST) == 0) score += 500_000_000L;

            if (GetWindowRect(hwnd, out var rect))
            {
                var width = Math.Max(0L, (long)rect.Right - rect.Left);
                var height = Math.Max(0L, (long)rect.Bottom - rect.Top);
                score += Math.Min(width * height, 499_999_999L);
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestHwnd = hwnd;
            }

            return true;
        }, IntPtr.Zero);

        return bestHwnd;
    }

    /// <summary>
    /// Maps an app name to the process name (without .exe) to search for.
    /// </summary>
    private static string? ResolveProcessName(string appName)
    {
        // If it ends with .exe, strip it
        var name = appName;
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        // If it's an absolute path, use the filename
        if (Path.IsPathRooted(name))
            name = Path.GetFileNameWithoutExtension(name);

        // Known app-name to process-name mappings
        // (app display names that differ from their process names)
        var mapped = name.ToLowerInvariant() switch
        {
            "chrome" => "chrome",
            "google chrome" => "chrome",
            "visual studio code" or "code" or "vs code" => "Code",
            "windows terminal" => "WindowsTerminal",
            "slack" => "slack",
            "spotify" => "Spotify",
            "discord" => "Discord",
            "firefox" => "firefox",
            "edge" or "microsoft edge" => "msedge",
            "explorer" or "file explorer" => "explorer",
            "outlook" => "OUTLOOK",
            "teams" or "microsoft teams" => "ms-teams",
            "notepad" => "notepad",
            "notepad++" => "notepad++",
            "telegram" => "Telegram",
            "synctrayzor" => "SyncTrayzor",
            "antigravity" => "Antigravity",
            "codex" or "chatgpt" => "ChatGPT",
            "vivaldi" => "vivaldi",
            "brave" => "brave",
            "visual studio" or "vs" => "devenv",
            "cmd" => "cmd",
            "powershell" => "powershell",
            "task manager" => "Taskmgr",
            "settings" => "SystemSettings",
            "calculator" or "calc" => "CalculatorApp",
            "mail" => "MailClient",
            "calendar" => "olk",
            "photos" => "Microsoft.Photos",
            "maps" => "Maps",
            "music" => "Spotify",
            _ => name // Use as-is
        };

        return mapped;
    }

    private static void LaunchNew(string appName, string appHome)
    {
        // 1. Absolute path
        if (Path.IsPathRooted(appName) && File.Exists(appName))
        {
            StartProcess(appName);
            return;
        }

        // 2. Relative path from app home
        var relativePath = Path.Combine(appHome, appName);
        if (File.Exists(relativePath))
        {
            StartProcess(relativePath);
            return;
        }

        // 3. Explorer-groups PowerShell scripts
        var groupScript = Path.Combine(appHome, "assets", "explorer-groups", $"{appName}.ps1");
        if (File.Exists(groupScript))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{groupScript}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            return;
        }

        // 4. Search Start Menu shortcuts
        var shortcut = FindStartMenuShortcut(appName);
        if (shortcut != null)
        {
            StartProcess(shortcut);
            return;
        }

        // 5. Fallback: shell execute by name (handles "notepad", "calc", etc.)
        try
        {
            StartProcess(appName);
        }
        catch
        {
            // Log failure silently
        }
    }

    private static void StartProcess(string target)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = target,
            UseShellExecute = true
        });
    }

    private static string? FindStartMenuShortcut(string appName)
    {
        var searchDirs = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu)
        };

        foreach (var dir in searchDirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var lnk in Directory.GetFiles(dir, "*.lnk", SearchOption.AllDirectories))
            {
                if (Path.GetFileNameWithoutExtension(lnk)
                    .Equals(appName, StringComparison.OrdinalIgnoreCase))
                    return lnk;
            }
        }

        return null;
    }
}
