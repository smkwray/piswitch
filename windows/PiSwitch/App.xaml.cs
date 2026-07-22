using System.IO;
using System.Threading;
using System.Windows;
using PiSwitch.Models;
using PiSwitch.Services;

namespace PiSwitch;

public partial class App : Application
{
    private PieMenuWindow _menuWindow = null!;
    private ConfigService _config = null!;
    private InstanceManager _instanceManager = null!;
    private TriggerWatcher? _triggerWatcher;
    private HotkeyService? _hotkeyService;
    private TrayIcon? _trayIcon;
    private EventWaitHandle? _showEvent;
    private Thread? _eventThread;
    private volatile bool _exiting;
    private string _instanceName = "default";
    private bool _triggerOnly;

    private List<AppConfig>? _cachedAppConfigs;
    private DateTime _configLastModified;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global crash handler so we can diagnose silent failures
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            CrashLog($"UnhandledException: {ex}");
        };
        DispatcherUnhandledException += (_, args) =>
        {
            CrashLog($"DispatcherUnhandledException: {args.Exception}");
            args.Handled = true;
        };

        try
        {
            ParseArguments(e.Args);

            // Fast path: --trigger just touches the trigger file and exits.
            // No WPF window, no config loading — designed for hotkey tools.
            if (_triggerOnly)
            {
                TriggerAndExit();
                return;
            }

            var appHome = GetAppHome();
            _config = new ConfigService(appHome) { InstanceName = _instanceName };

            Directory.CreateDirectory(_config.RunDir);
            Directory.CreateDirectory(_config.ConfigDir);

            Logger.Initialize(_config.RunDir, _instanceName);
            Logger.Bootstrap("main:start");
            Logger.Bootstrap($"main:parsed instance={_instanceName} home={appHome}");

            var ns = Environment.GetEnvironmentVariable("PISWITCH_NAMESPACE") ?? "piswitch-win";
            if (string.IsNullOrWhiteSpace(ns)) ns = "piswitch-win";

            Logger.Bootstrap("main:instance-manager");
            _instanceManager = new InstanceManager(_config.RunDir, _instanceName, ns);

            Logger.Bootstrap("main:try-acquire");
            if (!_instanceManager.TryAcquire())
            {
                // Another instance holds the mutex — trigger it and exit
                Logger.Bootstrap("main:trigger-existing");
                _instanceManager.TriggerExisting();
                Shutdown();
                return;
            }

            Logger.Bootstrap("main:create-window");
            _menuWindow = new PieMenuWindow();
            _menuWindow.OnSelect += LaunchApp;
            _menuWindow.OnCancel += HideMenu;
            _menuWindow.Visibility = Visibility.Hidden;
            _menuWindow.Show();

            Logger.Bootstrap("main:load-config");
            RefreshConfigIfNeeded();

            Logger.Bootstrap("main:setup-tray");
            SetupTrayIcon();

            Logger.Bootstrap("main:setup-trigger");
            SetupTriggerWatch();
            SetupEventTrigger();

            Logger.Bootstrap("main:ready");
        }
        catch (Exception ex)
        {
            CrashLog($"OnStartup crash: {ex}");
            Shutdown();
        }
    }

    private static void CrashLog(string message)
    {
        try
        {
            var crashPath = Path.Combine(
                Path.GetDirectoryName(Environment.ProcessPath ?? ".") ?? ".",
                "piswitch-crash.log");
            File.AppendAllText(crashPath,
                $"{DateTime.UtcNow:o} {message}\n");

            // Also try writing to the run dir
            var runDir = Path.Combine(
                Path.GetDirectoryName(Path.GetDirectoryName(
                    Path.GetDirectoryName(Environment.ProcessPath ?? ".") ?? ".") ?? ".") ?? ".",
                "run");
            if (Directory.Exists(runDir))
                File.AppendAllText(Path.Combine(runDir, "piswitch-crash.log"),
                    $"{DateTime.UtcNow:o} {message}\n");
        }
        catch { }
    }

    private void ParseArguments(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--instance" && i + 1 < args.Length)
                _instanceName = args[i + 1];
            else if (args[i] is "--trigger" or "-t")
                _triggerOnly = true;
        }
    }

    /// <summary>
    /// Ultra-fast trigger: signal the named event, exit.
    /// Falls back to trigger file if the event doesn't exist.
    /// </summary>
    private void TriggerAndExit()
    {
        try
        {
            // Try the named event first (instant, no filesystem)
            if (EventWaitHandle.TryOpenExisting(EventName, out var evt))
            {
                evt.Set();
                evt.Dispose();
                Shutdown();
                return;
            }

            // Fallback: write trigger file
            var appHome = GetAppHome();
            var runDir = Path.Combine(appHome, "run");
            var ns = Environment.GetEnvironmentVariable("PISWITCH_NAMESPACE") ?? "piswitch-win";
            if (string.IsNullOrWhiteSpace(ns)) ns = "piswitch-win";

            var triggerPath = _instanceName == "default"
                ? Path.Combine(runDir, $"{ns}-trigger")
                : Path.Combine(runDir, $"{ns}-trigger-{_instanceName}");

            Directory.CreateDirectory(runDir);
            File.WriteAllText(triggerPath, DateTime.UtcNow.Ticks.ToString());
        }
        catch { }

        Shutdown();
    }

    private string GetAppHome()
    {
        // 1. Explicit env var (set by setup-windows.ps1)
        var envHome = Environment.GetEnvironmentVariable("PISWITCH_HOME");
        if (!string.IsNullOrWhiteSpace(envHome) && Directory.Exists(envHome))
            return envHome;

        // 2. Running from dist/bin-windows/ inside the repo
        var exePath = Environment.ProcessPath ?? AppContext.BaseDirectory;
        var exeDir = Path.GetDirectoryName(exePath);
        if (exeDir != null)
        {
            var binDir = Path.GetFileName(exeDir);
            if (binDir is "bin-windows" or "bin")
            {
                var distDir = Path.GetDirectoryName(exeDir);
                if (distDir != null && Path.GetFileName(distDir) == "dist")
                {
                    var root = Path.GetDirectoryName(distDir);
                    if (root != null) return root;
                }
            }

            // 3. piswitch-home.txt written by setup-windows.ps1 next to the installed exe
            var homeFile = Path.Combine(exeDir, "piswitch-home.txt");
            if (File.Exists(homeFile))
            {
                var home = File.ReadAllText(homeFile).Trim();
                if (!string.IsNullOrWhiteSpace(home) && Directory.Exists(home))
                    return home;
            }
        }

        // 4. PISWITCH_HOME set but path not yet available (e.g. Google Drive still mounting) —
        //    still use it so the app can create the dirs and wait for configs to appear
        if (!string.IsNullOrWhiteSpace(envHome))
            return envHome;

        return Directory.GetCurrentDirectory();
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new TrayIcon();
        _trayIcon.ShowMenuRequested += ShowMenu;
        _trayIcon.ReloadRequested += () => { _cachedAppConfigs = null; ShowMenu(); };
        _trayIcon.ExitRequested += ExitApplication;
        _trayIcon.Initialize(_menuWindow, $"PiSwitch ({_instanceName})");
    }

    private void SetupTriggerWatch()
    {
        _triggerWatcher = new TriggerWatcher();
        _triggerWatcher.Triggered += () =>
        {
            Logger.Event("watch-fired");
            ShowMenu();
        };
        _triggerWatcher.Start(_instanceManager.TriggerPath);
        Logger.Event($"watch-start trigger={_instanceManager.TriggerPath}");
    }

    private string EventName => $"Local\\PiSwitch_show_{_instanceName}";

    private void SetupEventTrigger()
    {
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        _eventThread = new Thread(() =>
        {
            while (!_exiting)
            {
                if (_showEvent.WaitOne(500))
                {
                    if (_exiting) break;
                    Dispatcher.Invoke(() =>
                    {
                        Logger.Event("event-fired");
                        ShowMenu();
                    });
                }
            }
        })
        {
            IsBackground = true,
            Name = "PiSwitch-EventTrigger"
        };
        _eventThread.Start();
        Logger.Event($"event-listen name={EventName}");
    }

    private void RefreshConfigIfNeeded()
    {
        var configPath = _config.GetActiveConfigPath();
        if (configPath == null)
        {
            if (_cachedAppConfigs == null)
            {
                var appNames = _config.LoadConfig();
                _cachedAppConfigs = _config.CreateAppConfigs(appNames);
                _menuWindow.Rebuild(_cachedAppConfigs);
            }
            return;
        }

        var lastWrite = File.GetLastWriteTimeUtc(configPath);
        if (_cachedAppConfigs != null && lastWrite == _configLastModified)
            return;

        _configLastModified = lastWrite;
        var names = _config.LoadConfig();
        _cachedAppConfigs = _config.CreateAppConfigs(names);
        _menuWindow.Rebuild(_cachedAppConfigs);
    }

    public void ShowMenu()
    {
        RefreshConfigIfNeeded();
        Logger.Event($"show-menu apps={_cachedAppConfigs?.Count ?? 0}");

        try
        {
            _menuWindow.ShowAtCursor();
        }
        catch (InvalidOperationException)
        {
            // Window was closed (e.g. by the system during hibernate/resume) — recreate it
            Logger.Event("window-recreate");
            _menuWindow = new PieMenuWindow();
            _menuWindow.OnSelect += LaunchApp;
            _menuWindow.OnCancel += HideMenu;
            _menuWindow.Visibility = Visibility.Hidden;
            _menuWindow.Show();
            if (_cachedAppConfigs != null)
                _menuWindow.Rebuild(_cachedAppConfigs);
            _menuWindow.ShowAtCursor();
        }

        Logger.Event("menu-visible");
    }

    private void HideMenu()
    {
        _menuWindow.HideMenu();
        Logger.Event("menu-hidden");
    }

    private void LaunchApp(int index)
    {
        if (_cachedAppConfigs == null || index >= _cachedAppConfigs.Count) return;
        var appName = _cachedAppConfigs[index].Name;
        var configPath = _config.PathForApp(appName);
        Logger.Event($"launch-app name={appName} path={configPath ?? "(auto)"}");

        // Order matters: activate the target while PiSwitch still holds the foreground
        // input lock (the click that just landed inside our window). If we Hide() first,
        // we release foreground and Windows' focus-stealing-prevention can silently
        // demote SetForegroundWindow(targetHwnd) to a taskbar-flash, which is what
        // caused intermittent "had to click 2-3 times" reports.
        _menuWindow.PrepareForLaunch();
        AppLauncher.Launch(appName, _config.AppHome, configPath);
        HideMenu();
    }

    private void ExitApplication()
    {
        if (_exiting) return;
        _exiting = true;

        // Unblock the event thread, then dispose the handle
        try { _showEvent?.Set(); } catch { }
        _showEvent?.Dispose();
        _showEvent = null;

        _triggerWatcher?.Dispose();
        _hotkeyService?.Dispose();
        _trayIcon?.Dispose();
        _instanceManager.Cleanup();
        _instanceManager.Dispose();

        _menuWindow.ForceClose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (!_exiting)
        {
            _exiting = true;
            try { _showEvent?.Set(); } catch { }
            _showEvent?.Dispose();
            _showEvent = null;
            _instanceManager?.Cleanup();
            _instanceManager?.Dispose();
            _trayIcon?.Dispose();
            _triggerWatcher?.Dispose();
            _hotkeyService?.Dispose();
        }

        base.OnExit(e);
    }
}
