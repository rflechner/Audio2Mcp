namespace Audio2Mcp.Core;

/// <summary>A finalized native-format WAV recording and its measured audio properties.</summary>
/// <param name="Path">Path to the full recording.</param>
/// <param name="Bytes">Audio payload size, excluding the WAV header.</param>
/// <param name="Duration">Duration calculated from the native format and payload size.</param>
/// <param name="Peak">Maximum absolute normalized amplitude across converted chunks.</param>
public sealed record CaptureResult(string Path, long Bytes, TimeSpan Duration, double Peak)
{
    /// <summary>Separate source files when multiple devices were captured; the main properties describe the primary WAV.</summary>
    public IReadOnlyList<SourceRecording> Recordings { get; init; } = Array.Empty<SourceRecording>();

    /// <summary>Whether the recording is near-silent; a non-silent result does not prove speech.</summary>
    public bool IsSilent => AudioSignal.IsSilent(Peak);
}
