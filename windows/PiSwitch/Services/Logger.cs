using System.IO;

namespace PiSwitch.Services;

public static class Logger
{
    private static string _runDir = "";
    private static string _instanceName = "default";

    public static void Initialize(string runDir, string instanceName)
    {
        _runDir = runDir;
        _instanceName = instanceName;
        Directory.CreateDirectory(runDir);
    }

    public static void Bootstrap(string message)
    {
        var path = Path.Combine(_runDir, "piswitch-bootstrap.log");
        var ts = DateTime.UtcNow.ToString("o");
        var line = $"{ts} pid={Environment.ProcessId} {message}\n";
        AppendLine(path, line);
    }

    public static void Event(string message)
    {
        var path = Path.Combine(_runDir, "piswitch-events.log");
        var ts = DateTime.UtcNow.ToString("o");
        var line = $"{ts} instance={_instanceName} {message}\n";
        AppendLine(path, line);
    }

    private static void AppendLine(string path, string line)
    {
        try
        {
            File.AppendAllText(path, line);
        }
        catch
        {
            // Silently ignore logging failures
        }
    }
}
