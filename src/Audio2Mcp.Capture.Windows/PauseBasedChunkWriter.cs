using Audio2Mcp.Core;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Audio2Mcp.Capture.Windows;

/// <summary>Buffers native audio and saves PCM16 mono 16 kHz chunks at quiet boundaries.</summary>
/// <remarks>
/// Owned by one capture adapter. Append on the capture thread, then Complete only after capture
/// callbacks have stopped. Chunk callbacks run synchronously after the file is closed.
/// Disposal releases buffers; callers must explicitly complete to save the final partial chunk.
/// </remarks>
internal sealed class PauseBasedChunkWriter : IDisposable
{
    private const int TranscriptionSampleRate = 16000;

    private readonly string directory;
    private readonly WaveFormat format;
    private readonly Action<AudioChunk> onChunk;
    private readonly PauseBoundaryDetector boundaries = new();
    private readonly MemoryStream pending = new();
    private readonly byte[] analysisFrame;
    private readonly float[] analysisSamples;

    private int analysisBytes;
    private long chunkStart;
    private int chunkIndex;
    private bool completed;

    public PauseBasedChunkWriter(string directory, WaveFormat format, Action<AudioChunk> onChunk)
    {
        this.directory = directory;
        this.format = format;
        this.onChunk = onChunk;

        var framesPerWindow = Math.Max(1, format.SampleRate / 50);
        analysisFrame = new byte[framesPerWindow * format.BlockAlign];
        analysisSamples = new float[framesPerWindow * format.Channels];
    }

    public double Peak { get; private set; }

    public void Append(ReadOnlySpan<byte> buffer)
    {
        if (completed)
        {
            throw new InvalidOperationException("Le découpage audio est terminé.");
        }

        // Carry incomplete analysis windows across callbacks; packet sizes must not affect boundaries.
        while (!buffer.IsEmpty)
        {
            var count = Math.Min(buffer.Length, analysisFrame.Length - analysisBytes);
            buffer[..count].CopyTo(analysisFrame.AsSpan(analysisBytes));
            analysisBytes += count;
            buffer = buffer[count..];

            if (analysisBytes == analysisFrame.Length)
            {
                ProcessAnalysisFrame();
            }
        }
    }

    public void Complete()
    {
        if (completed)
        {
            return;
        }

        completed = true;

        if (analysisBytes > 0)
        {
            ProcessAnalysisFrame();
        }

        FlushChunk();
    }

    private void ProcessAnalysisFrame()
    {
        pending.Write(analysisFrame, 0, analysisBytes);

        using var memory = new MemoryStream(analysisFrame, 0, analysisBytes, writable: false);
        using var raw = new RawSourceWaveStream(memory, format);
        var source = raw.ToSampleProvider();
        var sampleCount = source.Read(analysisSamples);
        double sumOfSquares = 0;

        for (var index = 0; index < sampleCount; index++)
        {
            var sample = analysisSamples[index];
            sumOfSquares += (double)sample * sample;
        }

        var rootMeanSquare = sampleCount == 0 ? 0 : Math.Sqrt(sumOfSquares / sampleCount);
        var duration = TimeSpan.FromSeconds((double)analysisBytes / format.AverageBytesPerSecond);
        analysisBytes = 0;

        if (boundaries.Observe(duration, rootMeanSquare))
        {
            FlushChunk();
        }
    }

    private void FlushChunk()
    {
        if (pending.Length == 0)
        {
            return;
        }

        var length = pending.Length;
        using var raw = new RawSourceWaveStream(new MemoryStream(pending.ToArray()), format);
        ISampleProvider source = new MonoProvider(raw.ToSampleProvider());
        source = new WdlResamplingSampleProvider(source, TranscriptionSampleRate);

        var path = Path.Combine(directory, $"segment-{chunkIndex++:D5}.wav");
        var chunkPeak = WriteTranscriptionChunk(source, path);
        Peak = Math.Max(Peak, chunkPeak);

        var chunk = new AudioChunk(
            path,
            TimeSpan.FromSeconds((double)chunkStart / format.AverageBytesPerSecond),
            TimeSpan.FromSeconds((double)length / format.AverageBytesPerSecond),
            chunkPeak);

        chunkStart += length;
        pending.SetLength(0);
        pending.Position = 0;

        onChunk(chunk);
    }

    private static double WriteTranscriptionChunk(ISampleProvider source, string path)
    {
        using var wav = new WaveFileWriter(path, new WaveFormat(TranscriptionSampleRate, 16, 1));
        var buffer = new float[4096];
        double chunkPeak = 0;
        int read;

        while ((read = source.Read(buffer)) > 0)
        {
            for (var index = 0; index < read; index++)
            {
                chunkPeak = Math.Max(chunkPeak, Math.Abs(buffer[index]));
            }

            wav.WriteSamples(buffer, 0, read);
        }

        return chunkPeak;
    }

    public void Dispose()
    {
        pending.Dispose();
    }

    private sealed class MonoProvider(ISampleProvider source) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);

        public int Read(Span<float> buffer)
        {
            var channels = source.WaveFormat.Channels;
            var input = new float[buffer.Length * channels];
            var read = source.Read(input);
            var frames = read / channels;

            for (var frame = 0; frame < frames; frame++)
            {
                double sum = 0;
                for (var channel = 0; channel < channels; channel++)
                {
                    sum += input[frame * channels + channel];
                }

                buffer[frame] = (float)(sum / channels);
            }

            return frames;
        }
    }
}
