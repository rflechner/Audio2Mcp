namespace Audio2Mcp.Core;

/// <summary>A saved PCM16 mono 16 kHz chunk, positioned relative to the start of capture.</summary>
/// <param name="Path">Path to the saved WAV file.</param>
/// <param name="Start">Offset from the beginning of the recording.</param>
/// <param name="Duration">Duration represented by this chunk.</param>
/// <param name="Peak">Maximum absolute normalized sample amplitude.</param>
public sealed record AudioChunk(string Path, TimeSpan Start, TimeSpan Duration, double Peak)
{
    /// <summary>Whether the peak is below the near-silence threshold; this is not speech detection.</summary>
    public bool IsSilent => AudioSignal.IsSilent(Peak);
}
