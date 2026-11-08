namespace Audio2Mcp.Core;

/// <summary>Finds chunk boundaries after quiet pauses, with a fixed upper duration limit.</summary>
/// <remarks>
/// This energy heuristic does not distinguish speech from music or noise. Feed consecutive,
/// short audio frames from one thread. A completed boundary resets the state for the next chunk.
/// No audio is discarded: the caller retains leading quiet audio and the entire trailing pause.
/// </remarks>
public sealed class PauseBoundaryDetector
{
    private static readonly TimeSpan PauseDuration = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan IdleChunkDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaximumChunkDuration = TimeSpan.FromSeconds(20);

    // Approximately -50 dBFS RMS; separate from the near-zero peak filter used before inference.
    private const double QuietRootMeanSquare = 0.003;

    private TimeSpan chunkDuration;
    private TimeSpan quietDuration;
    private bool hasActiveSignal;

    /// <summary>
    /// Observes a frame of up to 20 ms and returns true when its end completes the current chunk.
    /// RMS is calculated over all native channels to avoid treating opposite-phase stereo as quiet.
    /// </summary>
    public bool Observe(TimeSpan duration, double rootMeanSquare)
    {
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromMilliseconds(20))
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        if (!double.IsFinite(rootMeanSquare) || rootMeanSquare < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rootMeanSquare));
        }

        chunkDuration += duration;

        if (rootMeanSquare >= QuietRootMeanSquare)
        {
            hasActiveSignal = true;
            quietDuration = TimeSpan.Zero;
        }
        else
        {
            quietDuration += duration;
        }

        var pauseCompleted = hasActiveSignal && quietDuration >= PauseDuration;
        var idleCompleted = !hasActiveSignal && chunkDuration >= IdleChunkDuration;
        if (!pauseCompleted && !idleCompleted && chunkDuration < MaximumChunkDuration)
        {
            return false;
        }

        chunkDuration = TimeSpan.Zero;
        quietDuration = TimeSpan.Zero;
        hasActiveSignal = false;

        return true;
    }
}
