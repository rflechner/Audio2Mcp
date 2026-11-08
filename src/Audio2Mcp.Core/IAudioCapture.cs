namespace Audio2Mcp.Core;

/// <summary>A single-use capture adapter owned and disposed by its recording session.</summary>
public interface IAudioCapture : IAsyncDisposable
{
    /// <summary>Raised when a chunk is saved, potentially on a capture thread. Handlers must not block.</summary>
    event Action<AudioChunk>? ChunkReady;

    /// <summary>Reports device failures from the capture thread; the host should initiate stop.</summary>
    event Action<Exception>? Failed;

    /// <summary>Starts saving audio into the output directory. Cancellation may prevent startup.</summary>
    Task StartAsync(string directory, CancellationToken cancellationToken);

    /// <summary>Stops capture, emits any final chunk, and finalizes the WAV before returning.</summary>
    Task<CaptureResult> StopAsync();
}
