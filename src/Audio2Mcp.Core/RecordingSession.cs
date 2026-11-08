using System.Threading.Channels;

namespace Audio2Mcp.Core;

/// <summary>Coordinates one recording with a bounded, sequential transcription queue.</summary>
/// <remarks>
/// Owns the capture adapter. Call lifecycle methods from one host thread; event handlers must
/// marshal to the UI thread themselves. Stop drains queued chunks, while disposal cancels inference.
/// </remarks>
public sealed class RecordingSession(IAudioCapture capture, ITranscriber transcriber) : IAsyncDisposable
{
    private const int TranscriptionQueueCapacity = 8;

    private readonly Channel<AudioChunk> queue = Channel.CreateBounded<AudioChunk>(TranscriptionQueueCapacity);
    private readonly CancellationTokenSource abort = new();

    private Task? worker;
    private Task<CaptureResult>? stopping;
    private string directory = "";

    /// <summary>Raised by the transcription worker after recognized text is saved.</summary>
    public event Action<TranscriptSegment>? SegmentReady;

    /// <summary>Reports overflow or inference errors from capture or worker threads.</summary>
    public event Action<string>? StatusChanged;

    /// <summary>Forwards capture failures; the host is responsible for initiating stop.</summary>
    public event Action<Exception>? CaptureFailed;

    /// <summary>Starts this single-use session and creates its output directory.</summary>
    public async Task StartAsync(string outputDirectory)
    {
        if (worker is not null)
        {
            throw new InvalidOperationException("Session déjà démarrée.");
        }

        directory = outputDirectory;
        Directory.CreateDirectory(directory);

        capture.ChunkReady += OnChunk;
        capture.Failed += OnFailure;

        worker = ConsumeAsync();

        try
        {
            await capture.StartAsync(directory, abort.Token);
        }
        catch
        {
            queue.Writer.TryComplete();
            await worker;
            throw;
        }
    }

    private void OnFailure(Exception ex) => CaptureFailed?.Invoke(ex);

    private void OnChunk(AudioChunk chunk)
    {
        if (!queue.Writer.TryWrite(chunk))
            StatusChanged?.Invoke($"Transcription en retard : segment {chunk.Start} conservé sur disque mais non transcrit.");
    }

    private async Task ConsumeAsync()
    {
        try
        {
            await foreach (var chunk in queue.Reader.ReadAllAsync(abort.Token))
            {
                if (chunk.IsSilent)
                {
                    continue;
                }

                try
                {
                    await TranscribeChunkAsync(chunk);
                }
                catch (OperationCanceledException) when (abort.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke($"Transcription impossible : {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException) when (abort.IsCancellationRequested)
        {
            // Disposal interrupts the queue wait as well as active inference.
        }
    }

    private async Task TranscribeChunkAsync(AudioChunk chunk)
    {
        var text = (await transcriber.TranscribeAsync(chunk.Path, abort.Token)).Trim();
        if (text.Length == 0)
        {
            return;
        }

        var segment = new TranscriptSegment(chunk.Start, chunk.Duration, text);

        await File.AppendAllTextAsync(Path.Combine(directory, "transcript.txt"),
            $"[{chunk.Start:hh\\:mm\\:ss}] {text}{Environment.NewLine}", abort.Token);

        SegmentReady?.Invoke(segment);
    }

    /// <summary>Finalizes capture and drains pending transcriptions; repeated calls share the result.</summary>
    public Task<CaptureResult> StopAsync() => stopping ??= StopCoreAsync();

    private async Task<CaptureResult> StopCoreAsync()
    {
        try
        {
            return await capture.StopAsync();
        }
        finally
        {
            capture.ChunkReady -= OnChunk;
            capture.Failed -= OnFailure;

            queue.Writer.TryComplete();

            if (worker is not null)
            {
                await worker;
            }

        }
    }

    /// <summary>Cancels pending inference and releases the capture adapter after finalizing audio.</summary>
    public async ValueTask DisposeAsync()
    {
        abort.Cancel();

        try
        {
            if (worker is not null)
            {
                await StopAsync();
            }

        }
        finally
        {
            try
            {
                await capture.DisposeAsync();
            }
            finally
            {
                abort.Dispose();
            }
        }
    }
}
