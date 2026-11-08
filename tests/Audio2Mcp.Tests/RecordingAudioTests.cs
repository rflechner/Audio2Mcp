using Audio2Mcp.Diarization;
using NAudio.Wave;
using Xunit;

public sealed class RecordingAudioTests
{
    [Fact]
    public void NativeStereoIsConvertedToMonoWithoutChangingDuration()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.wav");
        try
        {
            using (var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)))
            {
                var samples = new float[48000 * 2];
                for (var frame = 0; frame < 48000; frame++)
                {
                    samples[frame * 2] = (float)(0.2 * Math.Sin(2 * Math.PI * 440 * frame / 48000));
                }

                writer.WriteSamples(samples, 0, samples.Length);
            }

            var mono = RecordingAudio.Read(path, CancellationToken.None);
            Assert.InRange(mono.Length, 15998, 16002);
            Assert.InRange(mono.Max(), 0.095f, 0.105f);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CancellationPreventsReadingTheFile()
    {
        Assert.Throws<OperationCanceledException>(() => RecordingAudio.Read("unused.wav", new CancellationToken(true)));
    }
}
