using System.Runtime.Versioning;
using Audio2Mcp.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Audio2Mcp.Capture.Windows;

/// <summary>Saves the Windows output mix in its native format and emits transcription-ready chunks.</summary>
/// <remarks>
/// One instance per recording. Chunk callbacks run on the recorder thread, or on the stopping
/// thread for the final chunk. Native audio is retained in full; quiet pauses delimit chunks.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsLoopbackCapture(string? deviceId = null, bool testTone = false) : IAudioCapture
{
    private MMDeviceEnumerator? enumerator;
    private MMDevice? device;
    private WasapiRecorder? recorder;
    private WasapiPlayer? playback;
    private WaveFileWriter? writer;
    private FileStream? stream;
    private PauseBasedChunkWriter? chunks;

    private readonly TaskCompletionSource<Exception?> stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private string directory = "";
    private long bytes;
    private bool running;
    private CaptureResult? result;

    /// <inheritdoc />
    public event Action<AudioChunk>? ChunkReady;

    /// <inheritdoc />
    public event Action<Exception>? Failed;

    /// <inheritdoc />
    public Task StartAsync(string outputDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        directory = outputDirectory;
        Directory.CreateDirectory(directory);

        enumerator = new MMDeviceEnumerator();
        device = deviceId is null
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
            : enumerator.GetDevice(deviceId);

        if (device.DataFlow != DataFlow.Render || device.State != DeviceState.Active)
        {
            throw new InvalidOperationException("La sortie audio choisie n'est pas active.");
        }

        recorder = new WasapiRecorderBuilder().WithDevice(device).WithLoopbackCapture().Build();
        stream = new FileStream(Path.Combine(directory, "capture.wav"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        writer = new WaveFileWriter(stream, recorder.WaveFormat);
        chunks = new PauseBasedChunkWriter(directory, recorder.WaveFormat, chunk => ChunkReady?.Invoke(chunk));

        recorder.DataAvailable += (buffer, flags, position, timestamp) =>
        {
            writer.Write(buffer);
            bytes += buffer.Length;
            chunks.Append(buffer);
        };

        recorder.RecordingStopped += (_, e) =>
        {
            stopped.TrySetResult(e.Exception);
            if (e.Exception is not null)
            {
                Failed?.Invoke(e.Exception);
            }

        };

        InitializePlayback();
        recorder.StartRecording();
        running = true;
        playback!.Play();

        return Task.CompletedTask;
    }

    private void InitializePlayback()
    {
        // A silent playback stream keeps loopback packets flowing during pauses.
        playback = new WasapiPlayerBuilder().WithDevice(device!).WithSharedMode().WithLatency(100).Build();
        var signal = new SignalGenerator(recorder!.WaveFormat.SampleRate, recorder.WaveFormat.Channels)
        {
            Gain = testTone ? 0.05 : 0,
            Frequency = 440,
            Type = SignalGeneratorType.Sin
        };

        playback.Init(signal.ToWaveProvider());
    }

    /// <inheritdoc />
    public async Task<CaptureResult> StopAsync()
    {
        if (result is not null)
        {
            return result;
        }

        Exception? error = null;

        try
        {
            if (running)
            {
                recorder!.StopRecording();
                error = await stopped.Task.WaitAsync(TimeSpan.FromSeconds(10));
                running = false;
                chunks!.Complete();
            }
        }
        finally
        {
            playback?.Stop();
            writer?.Dispose();
            writer = null;
            stream?.Dispose();
            stream = null;
        }

        var duration = TimeSpan.FromSeconds(
            recorder is null ? 0 : (double)bytes / recorder.WaveFormat.AverageBytesPerSecond);
        result = new CaptureResult(
            Path.Combine(directory, "capture.wav"),
            bytes,
            duration,
            chunks?.Peak ?? 0);

        if (error is not null)
        {
            throw new IOException("Capture interrompue par le périphérique.", error);
        }

        return result;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync();
        }
        finally
        {
            playback?.Dispose();
            recorder?.Dispose();
            device?.Dispose();
            enumerator?.Dispose();
            chunks?.Dispose();
        }
    }
}
