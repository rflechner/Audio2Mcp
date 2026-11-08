namespace Audio2Mcp.Core;

/// <summary>Shared interpretation of normalized audio sample amplitudes.</summary>
internal static class AudioSignal
{
    private const double SilenceThreshold = 0.0001;

    public static bool IsSilent(double peak) => peak < SilenceThreshold;
}
