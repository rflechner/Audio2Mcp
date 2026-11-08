namespace Audio2Mcp.Core;

/// <summary>A separate native WAV positioned on a conversation's approximate common timeline.</summary>
/// <remarks>Offsets use host startup timing, not hardware clock synchronization.</remarks>
public sealed record SourceRecording(
    AudioSource Source,
    string Path,
    TimeSpan Offset,
    TimeSpan Duration,
    double Peak);
