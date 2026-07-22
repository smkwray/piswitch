using System.Collections.Concurrent;
using System.IO;

namespace PiSwitch.Services;

/// <summary>
/// Asynchronous logger. The hot path (event-fired → menu-visible) only enqueues a
/// formatted line; all file I/O happens on a dedicated background thread. This keeps
/// the show path free of synchronous filesystem writes even when the run dir lives on
/// a cloud/synced drive (Google Drive / Syncthing), which is what caused the open-lag tail.
/// </summary>
public static class Logger
{
    private static string _runDir = "";
    private static string _instanceName = "default";
    private static readonly BlockingCollection<(string Path, string Line)> _queue =
        new(new ConcurrentQueue<(string, string)>());
    private static Thread? _writer;
    private static volatile bool _started;

    /// <summary>Event-level telemetry. On by default but now off the hot path (async).</summary>
    public static bool EventLoggingEnabled { get; set; } = true;

    public static void Initialize(string runDir, string instanceName)
    {
        _runDir = runDir;
        _instanceName = instanceName;
        try { Directory.CreateDirectory(runDir); } catch { }

        if (_started) return;
        _started = true;
        _writer = new Thread(WriterLoop) { IsBackground = true, Name = "PiSwitch-LogWriter" };
        _writer.Start();
    }

    private static void WriterLoop()
    {
        foreach (var (path, line) in _queue.GetConsumingEnumerable())
        {
            try { File.AppendAllText(path, line); } catch { }
        }
    }

    public static void Bootstrap(string message)
        => Enqueue(Path.Combine(_runDir, "piswitch-bootstrap.log"),
            $"{DateTime.UtcNow:o} pid={Environment.ProcessId} {message}\n");

    public static void Event(string message)
    {
        if (!EventLoggingEnabled) return;
        Enqueue(Path.Combine(_runDir, "piswitch-events.log"),
            $"{DateTime.UtcNow:o} instance={_instanceName} {message}\n");
    }

    private static void Enqueue(string path, string line)
    {
        // Formatting is cheap and happens on the caller; the blocking file write is deferred.
        try { if (_started && !_queue.IsAddingCompleted) _queue.Add((path, line)); } catch { }
    }

    public static void Shutdown()
    {
        try { _queue.CompleteAdding(); } catch { }
    }
}
