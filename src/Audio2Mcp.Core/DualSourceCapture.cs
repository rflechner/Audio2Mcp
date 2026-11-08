using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Audio2Mcp.Core;

/// <summary>Owns two capture adapters and preserves output and microphone as separate native WAVs.</summary>
/// <remarks>
/// Single-session lifecycle calls must be serialized by the host. Events can arrive from either
/// capture thread. Startup offsets are approximate; no drift correction or echo cancellation is applied.
/// Stop requests both devices to stop before waiting for either. Disposal always releases both.
/// </remarks>
public sealed class DualSourceCapture(IAudioCapture output, IAudioCapture microphone) : IAudioCapture
{
    private readonly Stopwatch timeline = new();
    private TimeSpan microphoneOffset;
    private string directory = "";
    private bool started;
    private Task<CaptureResult>? stopping;
    private Task? disposal;

    /// <inheritdoc />
    public event Action<AudioChunk>? ChunkReady;

    /// <inheritdoc />
    public event Action<Exception>? Failed;

    /// <inheritdoc />
    public async Task StartAsync(string outputDirectory, CancellationToken cancellationToken)
    {
        if (started || disposal is not null)
        {
            throw new InvalidOperationException("Cette capture ne peut pas être redémarrée.");
        }

        started = true;
        directory = outputDirectory;
        output.ChunkReady += OnOutputChunk;
        microphone.ChunkReady += OnMicrophoneChunk;
        output.Failed += OnFailure;
        microphone.Failed += OnFailure;
        timeline.Start();

        try
        {
            await output.StartAsync(directory, cancellationToken);
            microphoneOffset = timeline.Elapsed;
            await microphone.StartAsync(Path.Combine(directory, "microphone"), cancellationToken);
        }
        catch
        {
            // Roll back a partial start so the output device cannot keep recording unnoticed.
            await DisposeAsync();
            throw;
        }
    }

    private void OnOutputChunk(AudioChunk chunk)
    {
        ChunkReady?.Invoke(chunk with { Source = AudioSource.SystemOutput });
    }

    private void OnMicrophoneChunk(AudioChunk chunk)
    {
        ChunkReady?.Invoke(chunk with
        {
            Source = AudioSource.Microphone,
            Start = chunk.Start + microphoneOffset
        });
    }

    private void OnFailure(Exception error) => Failed?.Invoke(error);

    /// <inheritdoc />
    public Task<CaptureResult> StopAsync() => stopping ??= StopCoreAsync();

    private async Task<CaptureResult> StopCoreAsync()
    {
        var results = await Task.WhenAll(StopDeviceAsync(output), StopDeviceAsync(microphone));
        var recordings = Array.AsReadOnly(new[]
        {
            new SourceRecording(AudioSource.SystemOutput, results[0].Path, TimeSpan.Zero, results[0].Duration, results[0].Peak),
            new SourceRecording(AudioSource.Microphone, results[1].Path, microphoneOffset, results[1].Duration, results[1].Peak)
        });

        if (Directory.Exists(directory))
        {
            await File.WriteAllTextAsync(
                Path.Combine(directory, "sources.json"),
                JsonSerializer.Serialize(recordings, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Converters = { new JsonStringEnumConverter() }
                }));
        }

        return results[0] with { Recordings = recordings };
    }

    private static async Task<CaptureResult> StopDeviceAsync(IAudioCapture device)
    {
        return await device.StopAsync();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => new(disposal ??= DisposeCoreAsync());

    private async Task DisposeCoreAsync()
    {
        try
        {
            await Task.WhenAll(DisposeDeviceAsync(output), DisposeDeviceAsync(microphone));
        }
        finally
        {
            output.ChunkReady -= OnOutputChunk;
            microphone.ChunkReady -= OnMicrophoneChunk;
            output.Failed -= OnFailure;
            microphone.Failed -= OnFailure;
            timeline.Stop();
        }
    }

    private static async Task DisposeDeviceAsync(IAudioCapture device)
    {
        await device.DisposeAsync();
    }
}
