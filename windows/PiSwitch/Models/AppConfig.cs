using System.Windows.Media;

namespace PiSwitch.Models;

public record AppConfig(
    string Name,
    string DisplayName,
    int Number,
    Color Color,
    double StartAngle,
    double EndAngle
);
