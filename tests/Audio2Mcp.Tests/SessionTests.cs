using Audio2Mcp.Core;
using Xunit;

public sealed class SessionTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(0.000099, true)]
    [InlineData(0.0001, false)]
    [InlineData(0.5, false)]
    public async Task SilenceThresholdControlsTranscription(double peak, bool isSilent)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var capture = new FakeCapture();
        var transcriber = new FakeTranscriber();

        try
        {
            await using var session = new RecordingSession(capture, transcriber);
            await session.StartAsync(directory);
            capture.Emit("boundary", peak);
            await session.StopAsync();

            Assert.Equal(isSilent, new AudioChunk("boundary", TimeSpan.Zero, TimeSpan.Zero, peak).IsSilent);
            Assert.Equal(isSilent, new CaptureResult("wav", 0, TimeSpan.Zero, peak).IsSilent);
            Assert.Equal(!isSilent, transcriber.Paths.Contains("boundary"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task StopDrainsFinalChunkAndSkipsSilence()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var capture = new FakeCapture();
        var transcriber = new FakeTranscriber();

        try
        {
            await using var session = new RecordingSession(capture, transcriber);
            var seen = new List<TranscriptSegment>();
            session.SegmentReady += seen.Add;
            await session.StartAsync(directory);
            capture.Emit("silence", 0);
            capture.Emit("first", 0.5);
            await session.StopAsync();
            Assert.Equal(new[] { "first", "last" }, transcriber.Paths);
            Assert.Equal(2, seen.Count);
            Assert.Contains("last", await File.ReadAllTextAsync(Path.Combine(directory, "transcript.txt")));
            await session.StopAsync();
            Assert.Equal(1, capture.Stops);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task TranscriptionFailureDoesNotLoseFollowingChunks()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var capture = new FakeCapture();

        try
        {
            await using var session = new RecordingSession(capture, new FakeTranscriber
            {
                FailFirst = true
            });
            var errors = new List<string>();
            session.StatusChanged += errors.Add;
            await session.StartAsync(directory);
            capture.Emit("bad", 0.5);
            await session.StopAsync();
            Assert.Single(errors);
            Assert.Contains("last", await File.ReadAllTextAsync(Path.Combine(directory, "transcript.txt")));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task SlowTranscriberReportsOverflowAndDisposeCancelsIt()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var capture = new FakeCapture();
        var transcriber = new BlockingTranscriber();
        var session = new RecordingSession(capture, transcriber);

        try
        {
            var messages = new List<string>();
            session.StatusChanged += messages.Add;
            await session.StartAsync(directory);
            capture.Emit("first", 0.5);
            await transcriber.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            for (var i = 0; i < 10; i++)
            {
                capture.Emit($"queued-{i}", 0.5);
            }

            Assert.Equal(2, messages.Count);
            Assert.All(messages, message => Assert.Contains("conservé sur disque", message));
            await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(transcriber.Cancelled);
            Assert.Equal(1, capture.Stops);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private sealed class BlockingTranscriber : ITranscriber
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Cancelled;
        public async Task<string> TranscribeAsync(string path, CancellationToken token)
        {
            Started.TrySetResult();

            try
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            catch (OperationCanceledException)
            {
                Cancelled = true;
                throw;
            }

            return "";
        }
    }

    private sealed class FakeCapture : IAudioCapture
    {
        public event Action<AudioChunk>? ChunkReady;
        public event Action<Exception>? Failed { add { } remove { } }
        public int Stops;
        public void Emit(string path, double peak) => ChunkReady?.Invoke(new(path, TimeSpan.Zero, TimeSpan.FromSeconds(5), peak));
        public Task StartAsync(string directory, CancellationToken token) => Task.CompletedTask;
        public Task<CaptureResult> StopAsync()
        {
            Stops++;
            Emit("last", 0.5);
            return Task.FromResult(new CaptureResult("wav", 10, TimeSpan.FromSeconds(1), 0.5));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeTranscriber : ITranscriber
    {
        public List<string> Paths { get; } = [];
        public bool FailFirst;
        public Task<string> TranscribeAsync(string path, CancellationToken token)
        {
            Paths.Add(path);
            if (FailFirst)
            {
                FailFirst = false;
                throw new IOException("test");
            }

            return Task.FromResult(path);
        }
    }
}
