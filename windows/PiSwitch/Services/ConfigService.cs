using System.IO;
using System.Text.Json;
using System.Windows.Media;
using PiSwitch.Models;

namespace PiSwitch.Services;

public class ConfigService
{
    private static readonly Dictionary<string, Color> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["red"]         = Color.FromRgb(255, 59, 48),
        ["orange"]      = Color.FromRgb(255, 149, 0),
        ["yellow"]      = Color.FromRgb(255, 204, 0),
        ["green"]       = Color.FromRgb(52, 199, 89),
        ["mint"]        = Color.FromRgb(0, 199, 190),
        ["teal"]        = Color.FromRgb(48, 176, 199),
        ["cyan"]        = Color.FromRgb(50, 173, 230),
        ["blue"]        = Color.FromRgb(0, 122, 255),
        ["indigo"]      = Color.FromRgb(88, 86, 214),
        ["purple"]      = Color.FromRgb(175, 82, 222),
        ["pink"]        = Color.FromRgb(255, 45, 85),
        ["brown"]       = Color.FromRgb(162, 132, 94),
        ["white"]       = Colors.White,
        ["black"]       = Colors.Black,
        ["gray"]        = Color.FromRgb(142, 142, 147),
        ["grey"]        = Color.FromRgb(142, 142, 147),
        ["systemred"]       = Color.FromRgb(255, 59, 48),
        ["systemorange"]    = Color.FromRgb(255, 149, 0),
        ["systemyellow"]    = Color.FromRgb(255, 204, 0),
        ["systemgreen"]     = Color.FromRgb(52, 199, 89),
        ["systemmint"]      = Color.FromRgb(0, 199, 190),
        ["systemteal"]      = Color.FromRgb(48, 176, 199),
        ["systemcyan"]      = Color.FromRgb(50, 173, 230),
        ["systemblue"]      = Color.FromRgb(0, 122, 255),
        ["systemindigo"]    = Color.FromRgb(88, 86, 214),
        ["systempurple"]    = Color.FromRgb(175, 82, 222),
        ["systempink"]      = Color.FromRgb(255, 45, 85),
        ["systemgray"]      = Color.FromRgb(142, 142, 147),
        ["systemgrey"]      = Color.FromRgb(142, 142, 147),
    };

    private static readonly Dictionary<string, (Color Color, string DisplayName)> DefaultAppConfigs =
        new(StringComparer.OrdinalIgnoreCase)
    {
        // Cross-platform apps
        ["Codex"]               = (NamedColors["blue"], "Codex"),
        ["Claude"]              = (NamedColors["orange"], "Claude"),
        ["Claude Code"]         = (NamedColors["orange"], "Claude Code"),
        ["Claude Desktop"]      = (NamedColors["orange"], "Claude"),
        ["Visual Studio Code"]  = (NamedColors["blue"], "VS Code"),
        ["Code"]                = (NamedColors["blue"], "VS Code"),
        ["Safari"]              = (NamedColors["cyan"], "Safari"),
        ["Firefox"]             = (NamedColors["orange"], "Firefox"),
        ["Chrome"]              = (NamedColors["yellow"], "Chrome"),
        ["Vivaldi"]             = (NamedColors["red"], "Vivaldi"),
        ["Mail"]                = (NamedColors["blue"], "Mail"),
        ["Messages"]            = (NamedColors["green"], "Messages"),
        ["Slack"]               = (NamedColors["purple"], "Slack"),
        ["Discord"]             = (NamedColors["indigo"], "Discord"),
        ["Spotify"]             = (Color.FromRgb(29, 185, 84), "Spotify"),
        ["Music"]               = (NamedColors["pink"], "Music"),
        ["Photos"]              = (NamedColors["yellow"], "Photos"),
        ["Notes"]               = (NamedColors["yellow"], "Notes"),
        ["Reminders"]           = (NamedColors["orange"], "Reminders"),
        ["Calendar"]            = (NamedColors["red"], "Calendar"),
        ["Maps"]                = (NamedColors["green"], "Maps"),
        ["Telegram"]            = (NamedColors["blue"], "Telegram"),
        ["SyncTrayzor"]         = (NamedColors["teal"], "Syncthing"),
        ["Antigravity"]         = (NamedColors["purple"], "Antigravity"),
        ["T3 Code"]             = (NamedColors["purple"], "T3 Code"),

        // macOS apps (kept for shared config compatibility)
        ["iTerm"]               = (NamedColors["green"], "iTerm"),
        ["iTerm2"]              = (NamedColors["green"], "iTerm"),
        ["Terminal"]            = (NamedColors["green"], "Terminal"),
        ["Finder"]              = (NamedColors["gray"], "Finder"),
        ["System Settings"]     = (NamedColors["gray"], "Settings"),
        ["Activity Monitor"]    = (NamedColors["green"], "Activity"),
        ["Console"]             = (NamedColors["gray"], "Console"),

        // Windows-specific apps
        ["Explorer"]            = (NamedColors["yellow"], "Explorer"),
        ["File Explorer"]       = (NamedColors["yellow"], "Explorer"),
        ["Windows Terminal"]    = (Color.FromRgb(45, 45, 45), "Terminal"),
        ["cmd"]                 = (NamedColors["gray"], "CMD"),
        ["PowerShell"]          = (Color.FromRgb(1, 36, 86), "PowerShell"),
        ["Outlook"]             = (NamedColors["blue"], "Outlook"),
        ["Teams"]               = (NamedColors["purple"], "Teams"),
        ["Notepad"]             = (NamedColors["blue"], "Notepad"),
        ["Edge"]                = (NamedColors["cyan"], "Edge"),
        ["Task Manager"]        = (NamedColors["green"], "Task Mgr"),
        ["Settings"]            = (NamedColors["gray"], "Settings"),
        ["Visual Studio"]       = (NamedColors["purple"], "VS"),
    };

    private static readonly List<string> DefaultApps = ["Chrome", "Visual Studio Code", "Windows Terminal", "Slack", "Spotify"];

    private Dictionary<string, Color> _colorOverrides = [];
    private Dictionary<string, string> _labelOverrides = [];
    private Dictionary<string, string> _pathOverrides = [];

    public string AppHome { get; }
    public string InstanceName { get; set; } = "default";

    public ConfigService(string appHome)
    {
        AppHome = appHome;
    }

    public string ConfigDir => Path.Combine(AppHome, "config", "instances");
    public string RunDir => Path.Combine(AppHome, "run");

    public static string CanonicalAppName(string appName)
    {
        if (appName.Contains('/') || appName.Contains('\\'))
            return Path.GetFileNameWithoutExtension(appName);
        if (appName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return appName[..^4];
        if (appName.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            return appName[..^4];
        return appName;
    }

    public static string NormalizedAppKey(string appName)
        => CanonicalAppName(appName).ToLowerInvariant();

    public static Color? ParseHexColor(string spec)
    {
        var trimmed = spec.Trim();
        var hex = trimmed.StartsWith('#') ? trimmed[1..] : trimmed;
        if (string.IsNullOrEmpty(hex)) return null;

        string expanded;
        if (hex.Length == 3)
            expanded = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
        else if (hex.Length == 6 || hex.Length == 8)
            expanded = hex;
        else
            return null;

        if (!ulong.TryParse(expanded, System.Globalization.NumberStyles.HexNumber, null, out var value))
            return null;

        if (expanded.Length == 6)
        {
            var r = (byte)((value >> 16) & 0xff);
            var g = (byte)((value >> 8) & 0xff);
            var b = (byte)(value & 0xff);
            return Color.FromRgb(r, g, b);
        }

        {
            var r = (byte)((value >> 24) & 0xff);
            var g = (byte)((value >> 16) & 0xff);
            var b = (byte)((value >> 8) & 0xff);
            var a = (byte)(value & 0xff);
            return Color.FromArgb(a, r, g, b);
        }
    }

    public static Color? ParseColorSpec(string spec)
    {
        if (ParseHexColor(spec) is { } hexColor)
            return hexColor;

        var key = spec.Trim().ToLowerInvariant()
            .Replace(" ", "").Replace("-", "").Replace("_", "");

        return NamedColors.GetValueOrDefault(key);
    }

    private List<string> GetConfigPaths()
    {
        var configDir = ConfigDir;
        if (InstanceName == "default")
        {
            return [
                Path.Combine(configDir, "default.json"),
                Path.Combine(configDir, "config.json"),
            ];
        }

        return [
            Path.Combine(configDir, $"{InstanceName}.json"),
            Path.Combine(configDir, $"config-{InstanceName}.json"),
        ];
    }

    /// <summary>
    /// Returns the path of the first config file that exists, or null if none found.
    /// Used for change-detection (checking file modification time).
    /// </summary>
    public string? GetActiveConfigPath()
    {
        foreach (var path in GetConfigPaths())
        {
            if (File.Exists(path)) return path;
        }
        return null;
    }

    public List<string> LoadConfig()
    {
        _colorOverrides = [];
        _labelOverrides = [];
        _pathOverrides = [];

        foreach (var path in GetConfigPaths())
        {
            if (!File.Exists(path)) continue;

            try
            {
                var json = File.ReadAllText(path);
                var config = JsonSerializer.Deserialize<PieSwitcherConfig>(json);
                if (config == null) continue;

                if (config.Labels != null)
                {
                    foreach (var (appName, label) in config.Labels)
                    {
                        var key = NormalizedAppKey(appName);
                        if (!string.IsNullOrEmpty(key))
                            _labelOverrides[key] = label;
                    }
                }

                if (config.Colors != null)
                {
                    foreach (var (appName, spec) in config.Colors)
                    {
                        var key = NormalizedAppKey(appName);
                        if (string.IsNullOrEmpty(key)) continue;
                        if (ParseColorSpec(spec) is { } color)
                            _colorOverrides[key] = color;
                    }
                }

                if (config.Paths != null)
                {
                    foreach (var (appName, appPath) in config.Paths)
                    {
                        var key = NormalizedAppKey(appName);
                        if (!string.IsNullOrEmpty(key) && !string.IsNullOrWhiteSpace(appPath))
                            _pathOverrides[key] = appPath;
                    }
                }

                var count = config.Apps.Count;
                if (count < 2) return DefaultApps;
                if (count > 8) return config.Apps.Take(8).ToList();
                return config.Apps;
            }
            catch
            {
                continue;
            }
        }

        return DefaultApps;
    }

    public string DisplayNameForApp(string appName)
    {
        if (_labelOverrides.TryGetValue(NormalizedAppKey(appName), out var labelOverride))
            return labelOverride;

        if (DefaultAppConfigs.TryGetValue(appName, out var mapped))
            return mapped.DisplayName;

        var baseName = CanonicalAppName(appName);
        if (DefaultAppConfigs.TryGetValue(baseName, out var baseMapped))
            return baseMapped.DisplayName;

        return baseName;
    }

    public Color ColorForApp(string appName)
    {
        if (_colorOverrides.TryGetValue(NormalizedAppKey(appName), out var colorOverride))
            return colorOverride;

        if (DefaultAppConfigs.TryGetValue(appName, out var mapped))
            return mapped.Color;

        var baseName = CanonicalAppName(appName);
        if (DefaultAppConfigs.TryGetValue(baseName, out var baseMapped))
            return baseMapped.Color;

        return NamedColors["gray"];
    }

    public string? PathForApp(string appName)
    {
        if (_pathOverrides.TryGetValue(NormalizedAppKey(appName), out var path))
            return path;
        return null;
    }

    public static List<(double Start, double End)> CalculateSliceAngles(int count)
    {
        if (count <= 0) return [];

        var sliceSize = 360.0 / count;
        var angles = new List<(double, double)>(count);

        for (var i = 0; i < count; i++)
        {
            var midAngle = 90.0 - i * sliceSize;
            var halfSlice = sliceSize / 2.0;
            angles.Add((midAngle - halfSlice, midAngle + halfSlice));
        }

        return angles;
    }

    public List<AppConfig> CreateAppConfigs(List<string> appNames)
    {
        if (appNames.Count == 0) return [];

        var angles = CalculateSliceAngles(appNames.Count);

        return appNames.Select((name, i) => new AppConfig(
            Name: name,
            DisplayName: DisplayNameForApp(name),
            Number: i + 1,
            Color: ColorForApp(name),
            StartAngle: angles[i].Start,
            EndAngle: angles[i].End
        )).ToList();
    }
}
