using System.IO;
using System.Windows;

namespace PiSwitch.Services;

public class TriggerWatcher : IDisposable
{
    private FileSystemWatcher? _watcher;

    public event Action? Triggered;

    public void Start(string triggerFilePath)
    {
        var dir = Path.GetDirectoryName(triggerFilePath);
        var file = Path.GetFileName(triggerFilePath);
        if (dir == null || file == null) return;

        Directory.CreateDirectory(dir);
        if (!File.Exists(triggerFilePath))
            File.WriteAllText(triggerFilePath, "");

        _watcher = new FileSystemWatcher(dir, file)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Attributes | NotifyFilters.Size,
            EnableRaisingEvents = true,
            InternalBufferSize = 64 * 1024
        };

        _watcher.Changed += (_, _) =>
            Application.Current?.Dispatcher.Invoke(() => Triggered?.Invoke());
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
    }
}
