using Audio2Mcp.Core;

namespace Audio2Mcp.Chat;

/// <summary>Outcome of offering a transcript to the optional client-message queue.</summary>
public enum TranscriptAcceptance
{
    Ignored,
    Queued,
    Overflow
}

/// <summary>
/// Bounded FIFO for system-output transcripts. Owned by one host thread; never blocks capture.
/// Disabling clears pending messages. Audio source identifies a device, not a person or proven speech.
/// </summary>
public sealed class ClientTranscriptInbox
{
    /// <summary>Maximum pending messages, excluding the request already being processed.</summary>
    public const int Capacity = 8;

    private readonly Queue<TranscriptSegment> pending = new();
    private bool enabled;

    /// <summary>Whether new system-output transcripts may be queued. Defaults to false.</summary>
    public bool Enabled
    {
        get => enabled;
        set
        {
            enabled = value;
            if (!value)
            {
                pending.Clear();
            }
        }
    }

    /// <summary>Number of pending messages.</summary>
    public int Count => pending.Count;

    /// <summary>Rejects microphone and empty text even while the mode is enabled.</summary>
    public TranscriptAcceptance Offer(TranscriptSegment segment)
    {
        if (!enabled || segment.Source != AudioSource.SystemOutput || string.IsNullOrWhiteSpace(segment.Text))
        {
            return TranscriptAcceptance.Ignored;
        }

        if (pending.Count == Capacity)
        {
            return TranscriptAcceptance.Overflow;
        }

        pending.Enqueue(segment);
        return TranscriptAcceptance.Queued;
    }

    /// <summary>Removes the oldest pending transcript, or returns null if none remains.</summary>
    public TranscriptSegment? Take() => pending.TryDequeue(out var segment) ? segment : null;

    /// <summary>Formats recognized text as a client request without claiming speaker identification.</summary>
    public static string FormatMessage(TranscriptSegment segment) =>
        "Le client a demandé ceci (transcription automatique de la sortie audio du PC, " +
        "identité du locuteur non vérifiée) :\n\n" + segment.Text.Trim() +
        "\n\nRéponds à cette demande et utilise les outils disponibles si elle le nécessite. " +
        "Si le texte est incomplet ou ne contient pas de demande claire, n’exécute pas d’action et demande une précision.";
}
