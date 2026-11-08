using Audio2Mcp.Core;
using Whisper.net;

namespace Audio2Mcp.Transcription.Whisper;

/// <summary>Transcribes local WAV chunks through Whisper.net's in-process CPU runtime.</summary>
/// <remarks>
/// Owned by one recording session. Loads the model lazily on a worker thread and retains it
/// until disposal. Calls are serialized; each chunk uses independent decoding state.
/// Cancellation is cooperative: cleanup waits for native inference before freeing its model.
/// Synchronous model loading cannot be interrupted; cancellation is checked immediately afterward.
/// </remarks>
public sealed class WhisperNetTranscriber : ITranscriber
{
    private static readonly TimeSpan InferenceTimeout = TimeSpan.FromMinutes(2);

    private readonly string modelPath;
    private readonly string language;
    private readonly SemaphoreSlim processing = new(1, 1);
    private readonly CancellationTokenSource shutdown = new();
    private readonly object lifecycleLock = new();

    private WhisperFactory? factory;
    private Task? disposal;
    private volatile bool disposing;

    /// <summary>Validates local settings without loading the model or downloading any data.</summary>
    public WhisperNetTranscriber(string modelPath, string language = "fr")
    {
        this.modelPath = Path.GetFullPath(modelPath);
        this.language = language;

        if (!File.Exists(this.modelPath))
        {
            throw new FileNotFoundException("Modèle GGML introuvable.", this.modelPath);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(language);
    }

    /// <inheritdoc />
    public async Task<string> TranscribeAsync(string wavPath, CancellationToken cancellationToken)
    {
        await processing.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            ObjectDisposedException.ThrowIf(disposing, this);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, shutdown.Token);
            timeout.CancelAfter(InferenceTimeout);

            try
            {
                // Model loading and native initialization must not block the capture or UI thread.
                return await Task.Run(() => TranscribeCoreAsync(wavPath, timeout.Token), timeout.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !shutdown.IsCancellationRequested)
            {
                throw new TimeoutException("Whisper.net dépasse 2 minutes par segment.");
            }
        }
        finally
        {
            processing.Release();
        }
    }

    private async Task<string> TranscribeCoreAsync(string wavPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var audio = File.OpenRead(wavPath);
        factory ??= WhisperFactory.FromPath(modelPath);
        cancellationToken.ThrowIfCancellationRequested();

        await using var processor = factory.CreateBuilder()
            .WithLanguage(language)
            .WithThreads(4)
            .WithNoContext()
            .Build();

        var segments = new List<string>();
        await foreach (var segment in processor.ProcessAsync(audio, cancellationToken).ConfigureAwait(false))
        {
            var text = segment.Text.Trim();
            if (text.Length > 0)
            {
                segments.Add(text);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return string.Join(" ", segments);
    }

    /// <summary>Cancels active work, waits for native cleanup, then releases the retained model once.</summary>
    public ValueTask DisposeAsync()
    {
        lock (lifecycleLock)
        {
            return new ValueTask(disposal ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        disposing = true;
        shutdown.Cancel();
        await processing.WaitAsync().ConfigureAwait(false);

        try
        {
            factory?.Dispose();
            factory = null;
            shutdown.Dispose();
        }
        finally
        {
            // Keep the managed semaphore usable so late calls fail with ObjectDisposedException.
            processing.Release();
        }
    }
}
