namespace Audio2Mcp.Core;

/// <summary>Converts saved audio chunks to text without owning capture or session state.</summary>
public interface ITranscriber
{
    /// <summary>Transcribes a PCM16 mono 16 kHz WAV; cancellation interrupts the inference.</summary>
    Task<string> TranscribeAsync(string wavPath, CancellationToken cancellationToken);
}
