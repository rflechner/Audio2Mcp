namespace Audio2Mcp.Core;

/// <summary>An anonymous speaker's estimated interval within one source recording.</summary>
/// <remarks>Speaker numbers are local to a recording and are not real-world identities.</remarks>
public sealed record SpeakerTurn
{
    /// <summary>Estimated onset relative to this source's recording.</summary>
    public TimeSpan Start { get; }

    /// <summary>Exclusive estimated end relative to this source's recording.</summary>
    public TimeSpan End { get; }

    /// <summary>Positive anonymous label, numbered by first appearance.</summary>
    public int Speaker { get; }

    /// <summary>Length of the detected interval.</summary>
    public TimeSpan Duration => End - Start;

    /// <summary>Creates a nonempty, nonnegative interval with a positive speaker label.</summary>
    public SpeakerTurn(TimeSpan start, TimeSpan end, int speaker)
    {
        if (start < TimeSpan.Zero || end <= start || speaker < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(start), "Invalid speaker interval.");
        }

        Start = start;
        End = end;
        Speaker = speaker;
    }

    /// <summary>Whether a different speaker is also active during any portion of this interval.</summary>
    public bool Overlaps(SpeakerTurn other)
    {
        return Speaker != other.Speaker && Start < other.End && other.Start < End;
    }
}
