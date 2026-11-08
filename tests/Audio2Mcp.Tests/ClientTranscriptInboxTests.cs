using Audio2Mcp.Chat;
using Audio2Mcp.Core;
using Xunit;

namespace Audio2Mcp.Tests;

public sealed class ClientTranscriptInboxTests
{
    [Fact]
    public void ModeIsOptInAndExcludesMicrophoneAndEmptyText()
    {
        var inbox = new ClientTranscriptInbox();
        Assert.Equal(TranscriptAcceptance.Ignored, inbox.Offer(Segment("Search")));

        inbox.Enabled = true;
        Assert.Equal(TranscriptAcceptance.Ignored, inbox.Offer(Segment("Search") with { Source = AudioSource.Microphone }));
        Assert.Equal(TranscriptAcceptance.Ignored, inbox.Offer(Segment("  ")));
        Assert.Null(inbox.Take());
        Assert.Equal(TranscriptAcceptance.Queued, inbox.Offer(Segment("Search")));
        Assert.Equal("Search", inbox.Take()!.Text);
    }

    [Fact]
    public void OverflowPreservesAcceptedMessagesInOrder()
    {
        var inbox = new ClientTranscriptInbox { Enabled = true };
        for (var index = 0; index < ClientTranscriptInbox.Capacity; index++)
        {
            Assert.Equal(TranscriptAcceptance.Queued, inbox.Offer(Segment($"Request {index}")));
        }

        Assert.Equal(TranscriptAcceptance.Overflow, inbox.Offer(Segment("Overflow")));
        for (var index = 0; index < ClientTranscriptInbox.Capacity; index++)
        {
            Assert.Equal($"Request {index}", inbox.Take()!.Text);
        }

        Assert.Null(inbox.Take());
    }

    [Fact]
    public void DisablingDiscardsPendingMessagesWithoutReplayingOnEnable()
    {
        var inbox = new ClientTranscriptInbox { Enabled = true };
        inbox.Offer(Segment("Old request"));
        inbox.Enabled = false;
        Assert.Equal(0, inbox.Count);
        inbox.Enabled = true;
        Assert.Null(inbox.Take());
        inbox.Offer(Segment("New request"));
        Assert.Equal("New request", inbox.Take()!.Text);
    }

    private static TranscriptSegment Segment(string text) => new(TimeSpan.Zero, TimeSpan.FromSeconds(2), text);
}
