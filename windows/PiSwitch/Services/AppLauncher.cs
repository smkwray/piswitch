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
    private const int SW_SHOW = 5;

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

            // First pass: find a process with a visible main window
            foreach (var proc in procs)
            {
                var hwnd = proc.MainWindowHandle;
                if (hwnd == IntPtr.Zero) continue;

                if (IsIconic(hwnd))
                {
                    ShowWindow(hwnd, SW_RESTORE);
                }
                else if (!IsWindowVisible(hwnd))
                {
                    // Window is hidden (e.g. tray-minimized WPF app calling Window.Hide()).
                    // Skip — fall through to the re-launch path so the app's single-instance
                    // handler can restore its UI.
                    continue;
                }

                ForceForeground(hwnd);
                return true;
            }

            // Second pass: tray-only apps (MainWindowHandle == Zero).
            // Use EnumWindows to find any window owned by the process.
            foreach (var proc in procs)
            {
                var hwnd = FindWindowByProcessId((uint)proc.Id);
                if (hwnd != IntPtr.Zero)
                {
                    ShowWindow(hwnd, SW_SHOW);
                    if (IsIconic(hwnd))
                        ShowWindow(hwnd, SW_RESTORE);
                    ForceForeground(hwnd);
                    return true;
                }
            }

            // Third pass: for tray apps that truly have no discoverable window,
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
    /// Enumerates all top-level windows to find a VISIBLE one belonging to the given process ID.
    /// Returns IntPtr.Zero if the process has no visible top-level window. For tray apps
    /// minimized to the system tray (e.g. SyncTrayzor calling Window.Hide()), the caller
    /// should fall through to re-launching the exe — the app's single-instance handler
    /// will then restore the main window. Picking a hidden support window here causes the
    /// wrong window (tooltip/popup/balloon) to be shown via SW_SHOW.
    /// </summary>
    private static IntPtr FindWindowByProcessId(uint targetPid)
    {
        IntPtr bestHwnd = IntPtr.Zero;

        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != targetPid) return true; // continue

            if (IsWindowVisible(hwnd))
            {
                bestHwnd = hwnd;
                return false; // stop — found a visible window
            }

            return true; // keep looking for visible
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
