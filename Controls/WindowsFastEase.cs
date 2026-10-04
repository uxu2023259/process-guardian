using System.Windows;
using System.Windows.Media.Animation;

namespace ProcessGuardian.Controls;

/// <summary>Matches the Windows direct-motion curve cubic-bezier(0, 0, 0, 1).</summary>
public sealed class WindowsFastEase : EasingFunctionBase
{
    protected override double EaseInCore(double normalizedTime)
    {
        // With x1=x2=0, the Bezier x-coordinate is t^3, so invert it directly.
        var t = Math.Pow(normalizedTime, 1d / 3d);
        return (3 * t * t) - (2 * t * t * t);
    }

    protected override Freezable CreateInstanceCore() => new WindowsFastEase();
}
