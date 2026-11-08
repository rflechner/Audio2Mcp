namespace Audio2Mcp.Core;

/// <summary>Converts saved audio chunks to text; owned and disposed by one recording session.</summary>
/// <remarks>Calls are sequential. Implementations may retain model resources until disposal.</remarks>
public interface ITranscriber : IAsyncDisposable
{
    /// <summary>Transcribes a PCM16 mono 16 kHz WAV; cancellation requests interruption of inference.</summary>
    /// <remarks>Native implementations must finish cooperative cancellation before releasing resources.</remarks>
    Task<string> TranscribeAsync(string wavPath, CancellationToken cancellationToken);
}
