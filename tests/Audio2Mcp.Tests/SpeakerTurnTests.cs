using Audio2Mcp.Core;
using Xunit;

public sealed class SpeakerTurnTests
{
    [Fact]
    public void OnlySimultaneousDifferentSpeakersAreMarkedAsOverlapping()
    {
        var first = new SpeakerTurn(TimeSpan.Zero, TimeSpan.FromSeconds(2), 1);
        var overlap = new SpeakerTurn(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), 2);
        var adjacent = new SpeakerTurn(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), 2);
        var sameSpeaker = new SpeakerTurn(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), 1);

        Assert.True(first.Overlaps(overlap));
        Assert.True(overlap.Overlaps(first));
        Assert.False(first.Overlaps(adjacent));
        Assert.False(first.Overlaps(sameSpeaker));
    }

    [Theory]
    [InlineData(-1, 1, 1)]
    [InlineData(1, 1, 1)]
    [InlineData(2, 1, 1)]
    [InlineData(0, 1, 0)]
    public void InvalidIntervalsAreRejected(int start, int end, int speaker)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SpeakerTurn(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), speaker));
    }
}
