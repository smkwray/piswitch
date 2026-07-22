using System.Diagnostics;
using System.IO;

namespace PiSwitch.Services;

public class InstanceManager : IDisposable
{
    private Mutex? _mutex;
    private string? _pidFilePath;

    public string NamespacePrefix { get; }
    public string InstanceName { get; }
    public string RunDir { get; }

    public InstanceManager(string runDir, string instanceName, string namespacePrefix = "piswitch-win")
    {
        RunDir = runDir;
        InstanceName = instanceName;
        NamespacePrefix = namespacePrefix;
    }

    public string PidFilePath
    {
        get
        {
            if (_pidFilePath != null) return _pidFilePath;
            _pidFilePath = InstanceName == "default"
                ? Path.Combine(RunDir, $"{NamespacePrefix}.pid")
                : Path.Combine(RunDir, $"{NamespacePrefix}-{InstanceName}.pid");
            return _pidFilePath;
        }
    }

    public string TriggerPath
    {
        get
        {
            return InstanceName == "default"
                ? Path.Combine(RunDir, $"{NamespacePrefix}-trigger")
                : Path.Combine(RunDir, $"{NamespacePrefix}-trigger-{InstanceName}");
        }
    }

    public bool TryAcquire()
    {
        var mutexName = $"Global\\PiSwitch_{InstanceName}";
        _mutex = new Mutex(true, mutexName, out var createdNew);
        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;
            return false;
        }

        // PID file is informational only — the mutex is the real single-instance guard.
        // Writing may fail if the home directory is on a cloud drive that isn't mounted yet
        // (e.g. Google Drive after restart). Retry in the background.
        try
        {
            Directory.CreateDirectory(RunDir);
            WritePidFile();
        }
        catch (IOException)
        {
            var timer = new System.Threading.Timer(_ => RetryWritePidFile(), null, 5000, 5000);
            _pidRetryTimer = timer;
        }

        return true;
    }

    private System.Threading.Timer? _pidRetryTimer;

    private void RetryWritePidFile()
    {
        try
        {
            Directory.CreateDirectory(RunDir);
            WritePidFile();
            _pidRetryTimer?.Dispose();
            _pidRetryTimer = null;
        }
        catch
        {
            // Still not ready — timer will retry
        }
    }

    public void TriggerExisting()
    {
        // Try named event first (instant, reliable)
        var eventName = $"Local\\PiSwitch_show_{InstanceName}";
        if (System.Threading.EventWaitHandle.TryOpenExisting(eventName, out var evt))
        {
            evt.Set();
            evt.Dispose();
            return;
        }

        // Fallback: write trigger file
        File.WriteAllText(TriggerPath, DateTime.UtcNow.Ticks.ToString());
    }

    public void CheckAndKillExisting()
    {
        if (!File.Exists(PidFilePath)) return;

        try
        {
            var pidStr = File.ReadAllText(PidFilePath).Trim();
            if (int.TryParse(pidStr, out var pid) && pid != Environment.ProcessId)
            {
                var proc = Process.GetProcessById(pid);
                proc.Kill();
                proc.WaitForExit(1000);
            }
        }
        catch
        {
            // Process already gone or inaccessible
        }
    }

    private void WritePidFile()
    {
        File.WriteAllText(PidFilePath, Environment.ProcessId.ToString());
    }

    public void Cleanup()
    {
        try { File.Delete(PidFilePath); } catch { }
        try { File.Delete(TriggerPath); } catch { }
    }

    public void Dispose()
    {
        _pidRetryTimer?.Dispose();
        _pidRetryTimer = null;
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        _mutex = null;
    }
}
