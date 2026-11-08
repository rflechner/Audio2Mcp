using System.Text.Json;
using System.Text.Json.Serialization;
using Audio2Mcp.Core;

namespace Audio2Mcp.Diarization;

/// <summary>Offline analysis result; the original live transcript is never overwritten.</summary>
public sealed record ConversationAnalysisResult(IReadOnlyList<SpeakerTranscript> Segments, string TranscriptPath);

/// <summary>Diarizes each saved source, then transcribes its turns with a caller-owned transcriber.</summary>
public sealed class ConversationAnalysis(SherpaSpeakerDiarizer diarizer)
{
    /// <summary>
    /// Runs on a worker; reports source and stage through progress. Speaker labels are scoped to each
    /// source. Cancellation waits for a running native clustering call, then prevents subsequent work.
    /// </summary>
    public Task<ConversationAnalysisResult> AnalyzeAsync(
        IReadOnlyList<SourceRecording> recordings,
        string outputDirectory,
        int outputSpeakerCount,
        ITranscriber transcriber,
        IProgress<string>? progress,
        CancellationToken cancellationToken,
        float clusteringThreshold = 0.5f)
    {
        return Task.Run(async () =>
        {
            var analysisDirectory = Path.Combine(outputDirectory, $"diarization-{Guid.NewGuid():N}");
            Directory.CreateDirectory(analysisDirectory);
            var segments = new List<SpeakerTranscript>();

            foreach (var recording in recordings)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report($"Analyse des voix : {recording.Source}…");
                var samples = RecordingAudio.Read(recording.Path, cancellationToken);
                var count = recording.Source == AudioSource.SystemOutput ? outputSpeakerCount : 0;
                var turns = diarizer.Analyze(samples, count, cancellationToken, clusteringThreshold);

                foreach (var turn in turns)
                {
                    var hasOverlap = turns.Any(other => turn.Overlaps(other));
                    var context = AddQuietContext(turn, turns, samples.Length, hasOverlap);
                    await TranscribeTurnAsync(
                        recording, samples, context, hasOverlap, analysisDirectory,
                        transcriber, segments, progress, cancellationToken);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            var ordered = segments.OrderBy(segment => segment.Start).ToArray();
            var transcriptPath = Path.Combine(analysisDirectory, "transcript-speakers.txt");
            var lines = ordered.Select(segment =>
                $"[{segment.Start:hh\\:mm\\:ss}] [{segment.Source} / Speaker {segment.Speaker}] " +
                (segment.HasOverlap ? "[overlapping voices] " : "") + segment.Text);
            await File.WriteAllLinesAsync(transcriptPath, lines, cancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(analysisDirectory, "speakers.json"),
                JsonSerializer.Serialize(ordered, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Converters = { new JsonStringEnumConverter() }
                }),
                cancellationToken);

            return new ConversationAnalysisResult(Array.AsReadOnly(ordered), transcriptPath);
        }, cancellationToken);
    }

    private static SpeakerTurn AddQuietContext(
        SpeakerTurn turn, IReadOnlyList<SpeakerTurn> turns, int sampleCount, bool hasOverlap)
    {
        if (hasOverlap)
        {
            return turn;
        }

        // Small leading/trailing margins protect syllables without crossing another detected turn.
        var previousEnd = turns.Where(other => other.End <= turn.Start)
            .Select(other => other.End).DefaultIfEmpty(TimeSpan.Zero).Max();
        var nextStart = turns.Where(other => other.Start >= turn.End)
            .Select(other => other.Start)
            .DefaultIfEmpty(TimeSpan.FromSeconds((double)sampleCount / RecordingAudio.SampleRate)).Min();
        var start = turn.Start - TimeSpan.FromMilliseconds(150);
        var end = turn.End + TimeSpan.FromMilliseconds(150);

        return new SpeakerTurn(
            start < previousEnd ? previousEnd : start,
            end > nextStart ? nextStart : end,
            turn.Speaker);
    }

    private static async Task TranscribeTurnAsync(
        SourceRecording recording,
        float[] samples,
        SpeakerTurn turn,
        bool hasOverlap,
        string directory,
        ITranscriber transcriber,
        List<SpeakerTranscript> segments,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        // Bound inference even when a speaker talks for minutes without changing turns.
        var start = (int)(turn.Start.TotalSeconds * RecordingAudio.SampleRate);
        var end = Math.Min(samples.Length, (int)Math.Ceiling(turn.End.TotalSeconds * RecordingAudio.SampleRate));
        while (start < end)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(20 * RecordingAudio.SampleRate, end - start);
            var path = Path.Combine(directory, $"turn-{segments.Count:D5}-{Guid.NewGuid():N}.wav");
            RecordingAudio.WriteTurn(path, samples, start, count);
            progress?.Report($"Transcription : {recording.Source}, interlocuteur {turn.Speaker}…");
            var text = await transcriber.TranscribeAsync(path, cancellationToken);
            segments.Add(new SpeakerTranscript(
                recording.Offset + TimeSpan.FromSeconds((double)start / RecordingAudio.SampleRate),
                TimeSpan.FromSeconds((double)count / RecordingAudio.SampleRate),
                recording.Source,
                turn.Speaker,
                text.Trim(),
                hasOverlap));
            start += count;
        }
    }
}
