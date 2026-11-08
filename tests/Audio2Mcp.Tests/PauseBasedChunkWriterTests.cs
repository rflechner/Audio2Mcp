using System.Runtime.InteropServices;
using Audio2Mcp.Capture.Windows;
using Audio2Mcp.Core;
using NAudio.Wave;
using Xunit;

public sealed class PauseBasedChunkWriterTests
{
    [Theory]
    [InlineData(44100, 2, true)]
    [InlineData(48000, 2, true)]
    [InlineData(16000, 1, false)]
    public void PauseAndStopPreserveTimingAndOutputFormat(int sampleRate, int channels, bool useFloat)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);

        try
        {
            var format = useFloat
                ? WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels)
                : new WaveFormat(sampleRate, 16, channels);
            var chunks = new List<AudioChunk>();
            using var writer = new PauseBasedChunkWriter(directory, format, chunks.Add);

            // Include leading quiet audio and a short pause inside a phrase longer than five seconds.
            AppendAudio(writer, format, 0.2, 0);
            AppendAudio(writer, format, 3, 0.05);
            AppendAudio(writer, format, 0.4, 0);
            AppendAudio(writer, format, 3, 0.05);
            Assert.Empty(chunks);

            AppendAudio(writer, format, 0.8, 0);
            var first = Assert.Single(chunks);
            Assert.Equal(TimeSpan.Zero, first.Start);
            Assert.Equal(TimeSpan.FromSeconds(7.4), first.Duration);

            // Stop inside an analysis window must still save all remaining native frames.
            AppendAudio(writer, format, 0.11, 0.05);
            writer.Complete();
            writer.Complete();
            Assert.Equal(2, chunks.Count);
            Assert.Equal(first.Duration, chunks[1].Start);
            Assert.Equal(TimeSpan.FromMilliseconds(110), chunks[1].Duration);
            Assert.InRange(writer.Peak, 0.045, 0.055);

            foreach (var chunk in chunks)
            {
                using var wav = new WaveFileReader(chunk.Path);
                Assert.Equal(WaveFormatEncoding.Pcm, wav.WaveFormat.Encoding);
                Assert.Equal(16000, wav.WaveFormat.SampleRate);
                Assert.Equal(16, wav.WaveFormat.BitsPerSample);
                Assert.Equal(1, wav.WaveFormat.Channels);
                Assert.InRange(Math.Abs((wav.TotalTime - chunk.Duration).TotalMilliseconds), 0, 2);
                Assert.False(chunk.IsSilent);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void EmptyCompletionDoesNotCreateAChunk()
    {
        var chunks = new List<AudioChunk>();
        using var writer = new PauseBasedChunkWriter("unused", new WaveFormat(16000, 16, 1), chunks.Add);
        writer.Complete();

        Assert.Empty(chunks);
    }

    private static void AppendAudio(PauseBasedChunkWriter writer, WaveFormat format, double seconds, double amplitude)
    {
        var frames = (int)Math.Round(seconds * format.SampleRate);
        var samples = new float[frames * format.Channels];
        for (var frame = 0; frame < frames; frame++)
        {
            var value = (float)(amplitude * Math.Sin(2 * Math.PI * 440 * frame / format.SampleRate));
            for (var channel = 0; channel < format.Channels; channel++)
            {
                samples[frame * format.Channels + channel] = value;
            }
        }

        var bytes = format.Encoding == WaveFormatEncoding.IeeeFloat
            ? MemoryMarshal.AsBytes(samples.AsSpan()).ToArray()
            : MemoryMarshal.AsBytes(samples.Select(sample => (short)(sample * short.MaxValue)).ToArray().AsSpan()).ToArray();

        // Deliberately split even sample frames across writes to exercise packet-independent buffering.
        for (var offset = 0; offset < bytes.Length; offset += 997)
        {
            writer.Append(bytes.AsSpan(offset, Math.Min(997, bytes.Length - offset)));
        }
    }
}
