using Audio2Mcp.Core;
using Xunit;

public sealed class DualSourceCaptureTests
{
    [Fact]
    public async Task SourcesRemainSeparateAndBothDevicesStopBeforeDraining()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        var output = new FakeCapture();
        var microphone = new FakeCapture();
        await using var capture = new DualSourceCapture(output, microphone);
        var chunks = new List<AudioChunk>();
        capture.ChunkReady += chunks.Add;

        try
        {
            await capture.StartAsync(directory, CancellationToken.None);
            output.Emit();
            microphone.Emit();
            Assert.Equal(AudioSource.SystemOutput, chunks[0].Source);
            Assert.Equal(AudioSource.Microphone, chunks[1].Source);
            Assert.True(chunks[1].Start >= chunks[0].Start);
            Assert.NotEqual(output.Directory, microphone.Directory);

            var stopping = capture.StopAsync();
            Assert.Equal(1, output.Stops);
            Assert.Equal(1, microphone.Stops);
            Assert.False(stopping.IsCompleted);
            output.Stopped.SetResult();
            microphone.Stopped.SetResult();
            var result = await stopping;

            Assert.Equal(2, result.Recordings.Count);
            Assert.Equal(AudioSource.Microphone, result.Recordings[1].Source);
            Assert.Equal(result.Recordings[1].Offset, chunks[1].Start);
            Assert.True(File.Exists(Path.Combine(directory, "sources.json")));
            Assert.Same(result, await capture.StopAsync());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task MicrophoneStartupFailureReleasesBothAdaptersOnce()
    {
        var output = new FakeCapture();
        var microphone = new FakeCapture { FailStart = true };
        var capture = new DualSourceCapture(output, microphone);

        await Assert.ThrowsAsync<IOException>(() => capture.StartAsync("unused", CancellationToken.None));
        await capture.DisposeAsync();
        Assert.Equal(1, output.Disposals);
        Assert.Equal(1, microphone.Disposals);
    }

    [Fact]
    public async Task FailureOfOneDisposalStillReleasesTheOtherDevice()
    {
        var output = new FakeCapture { FailDispose = true };
        var microphone = new FakeCapture();
        var capture = new DualSourceCapture(output, microphone);

        await Assert.ThrowsAsync<IOException>(() => capture.DisposeAsync().AsTask());
        Assert.Equal(1, microphone.Disposals);
    }

    private sealed class FakeCapture : IAudioCapture
    {
        public string Directory = "";
        public bool FailStart;
        public bool FailDispose;
        public int Stops;
        public int Disposals;
        public TaskCompletionSource Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event Action<AudioChunk>? ChunkReady;

        public event Action<Exception>? Failed { add { } remove { } }

        public Task StartAsync(string directory, CancellationToken cancellationToken)
        {
            Directory = directory;
            return FailStart ? Task.FromException(new IOException("test")) : Task.CompletedTask;
        }

        public void Emit()
        {
            ChunkReady?.Invoke(new AudioChunk(Path.Combine(Directory, "chunk.wav"), TimeSpan.Zero, TimeSpan.FromSeconds(1), 0.1));
        }

        public async Task<CaptureResult> StopAsync()
        {
            Stops++;
            await Stopped.Task;
            return new CaptureResult(Path.Combine(Directory, "capture.wav"), 32000, TimeSpan.FromSeconds(1), 0.1);
        }

        public ValueTask DisposeAsync()
        {
            Disposals++;
            return FailDispose ? ValueTask.FromException(new IOException("test")) : ValueTask.CompletedTask;
        }
    }
}
