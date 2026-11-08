namespace Audio2Mcp.Core;

/// <summary>Text transcribed from an estimated speaker turn, on the conversation timeline.</summary>
/// <remarks>Overlapping voices are not separated; text for overlapping turns may contain both voices.</remarks>
public sealed record SpeakerTranscript(
    TimeSpan Start,
    TimeSpan Duration,
    AudioSource Source,
    int Speaker,
    string Text,
    bool HasOverlap);
