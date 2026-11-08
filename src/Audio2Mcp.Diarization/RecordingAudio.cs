using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Audio2Mcp.Diarization;

/// <summary>Converts saved native WAV audio to bounded mono 16 kHz samples for offline analysis.</summary>
public static class RecordingAudio
{
    /// <summary>Sample rate expected by the diarization models and transcription adapter.</summary>
    public const int SampleRate = 16000;

    /// <summary>Reads up to 30 minutes; no device access or network I/O occurs.</summary>
    public static float[] Read(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var reader = new WaveFileReader(path);
        if (reader.TotalTime > TimeSpan.FromMinutes(30))
        {
            throw new InvalidOperationException("L'analyse expérimentale est limitée à 30 minutes par source.");
        }

        ISampleProvider source = new MonoProvider(reader.ToSampleProvider());
        source = new WdlResamplingSampleProvider(source, SampleRate);
        var samples = new List<float>((int)(reader.TotalTime.TotalSeconds * SampleRate));
        var buffer = new float[4096];
        int count;

        while ((count = source.Read(buffer)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            samples.AddRange(buffer.AsSpan(0, count));
        }

        return samples.ToArray();
    }

    /// <summary>Saves an exact sample range as PCM16 mono 16 kHz for an ITranscriber.</summary>
    public static void WriteTurn(string path, float[] samples, int start, int count)
    {
        using var writer = new WaveFileWriter(path, new WaveFormat(SampleRate, 16, 1));
        writer.WriteSamples(samples, start, count);
    }

    private sealed class MonoProvider(ISampleProvider source) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);

        public int Read(Span<float> buffer)
        {
            var channels = source.WaveFormat.Channels;
            var input = new float[buffer.Length * channels];
            var frames = source.Read(input) / channels;
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
