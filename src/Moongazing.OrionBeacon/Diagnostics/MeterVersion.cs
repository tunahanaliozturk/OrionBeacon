namespace Moongazing.OrionBeacon.Diagnostics;

using System.Reflection;

/// <summary>
/// Resolves the diagnostics <see cref="System.Diagnostics.Metrics.Meter"/> version once from the
/// assembly's informational version so it tracks the package version automatically and never drifts
/// from a hardcoded literal.
/// </summary>
internal static class MeterVersion
{
    /// <summary>The meter version derived from the assembly informational version.</summary>
    public static string Value { get; } = Resolve();

    private static string Resolve()
    {
        var asm = typeof(MeterVersion).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(info))
        {
            var plus = info.IndexOf('+');
            return plus >= 0 ? info[..plus] : info;
        }

        return asm.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
