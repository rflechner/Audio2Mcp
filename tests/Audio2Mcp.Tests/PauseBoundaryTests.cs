using Audio2Mcp.Core;
using Xunit;

public sealed class PauseBoundaryTests
{
    private static readonly TimeSpan FrameDuration = TimeSpan.FromMilliseconds(20);

    [Fact]
    public void PhraseLongerThanFiveSecondsWaitsForQuietPause()
    {
        var detector = new PauseBoundaryDetector();
        ObserveWithoutBoundary(detector, 400, 0.05);
        ObserveWithoutBoundary(detector, 39, 0);

        Assert.True(detector.Observe(FrameDuration, 0));
    }

    [Fact]
    public void ShortPausesDoNotAccumulateAcrossActiveAudio()
    {
        var detector = new PauseBoundaryDetector();
        for (var index = 0; index < 3; index++)
        {
            ObserveWithoutBoundary(detector, 50, 0.05);
            ObserveWithoutBoundary(detector, 30, 0.0001);
        }

        ObserveWithoutBoundary(detector, 9, 0);
        Assert.True(detector.Observe(FrameDuration, 0));

        // The next phrase must not inherit the previous trailing pause.
        ObserveWithoutBoundary(detector, 50, 0.05);
        ObserveWithoutBoundary(detector, 39, 0);
        Assert.True(detector.Observe(FrameDuration, 0));
    }

    [Fact]
    public void ContinuousAudioIsBoundedToTwentySeconds()
    {
        var detector = new PauseBoundaryDetector();
        ObserveWithoutBoundary(detector, 999, 0.05);

        Assert.True(detector.Observe(FrameDuration, 0.05));
        Assert.False(detector.Observe(FrameDuration, 0.05));
    }

    [Fact]
    public void SilenceRemainsBoundedWithoutAnActiveSignal()
    {
        var detector = new PauseBoundaryDetector();
        ObserveWithoutBoundary(detector, 249, 0);

        Assert.True(detector.Observe(FrameDuration, 0));
        ObserveWithoutBoundary(detector, 249, 0);
        Assert.True(detector.Observe(FrameDuration, 0));
    }

    private static void ObserveWithoutBoundary(PauseBoundaryDetector detector, int frames, double rootMeanSquare)
    {
        for (var index = 0; index < frames; index++)
        {
            Assert.False(detector.Observe(FrameDuration, rootMeanSquare));
        }
    }
}
