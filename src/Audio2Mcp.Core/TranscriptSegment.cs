namespace Audio2Mcp.Core;

/// <summary>Recognized text with the source chunk's offset and duration, not word-level timing.</summary>
public sealed record TranscriptSegment(TimeSpan Start, TimeSpan Duration, string Text);
