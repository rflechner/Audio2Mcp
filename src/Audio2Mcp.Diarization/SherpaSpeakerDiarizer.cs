using Audio2Mcp.Core;
using SherpaOnnx;

namespace Audio2Mcp.Diarization;

/// <summary>Runs local offline speaker segmentation and clustering over a whole source recording.</summary>
/// <remarks>
/// Model ownership is confined to each synchronous analysis, which callers must run on a worker.
/// Sherpa's native call is not interruptible: cancellation is checked before and after it, and
/// native resources are never freed while it is executing. No speaker identity is inferred.
/// </remarks>
public sealed class SherpaSpeakerDiarizer
{
    private readonly string segmentationModel;
    private readonly string embeddingModel;

    public SherpaSpeakerDiarizer(string segmentationModel, string embeddingModel)
    {
        this.segmentationModel = ValidateModel(segmentationModel);
        this.embeddingModel = ValidateModel(embeddingModel);
    }

    /// <summary>Analyzes mono 16 kHz samples. Zero speakers means automatic clustering.</summary>
    /// <remarks>A higher clustering threshold merges more groups; it is ignored when the speaker count is fixed.</remarks>
    public IReadOnlyList<SpeakerTurn> Analyze(
        float[] samples, int speakerCount, CancellationToken cancellationToken, float clusteringThreshold = 0.5f)
    {
        if (speakerCount is < 0 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(speakerCount));
        }

        if (!float.IsFinite(clusteringThreshold) || clusteringThreshold <= 0 || clusteringThreshold > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(clusteringThreshold));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (samples.Length == 0)
        {
            return Array.Empty<SpeakerTurn>();
        }

        var config = new OfflineSpeakerDiarizationConfig();
        config.Segmentation.Pyannote.Model = segmentationModel;
        config.Segmentation.NumThreads = 2;
        config.Segmentation.Provider = "cpu";
        config.Embedding.Model = embeddingModel;
        config.Embedding.NumThreads = 2;
        config.Embedding.Provider = "cpu";
        config.Clustering.NumClusters = speakerCount == 0 ? -1 : speakerCount;
        config.Clustering.Threshold = clusteringThreshold;

        using var diarizer = new OfflineSpeakerDiarization(config);
        cancellationToken.ThrowIfCancellationRequested();
        var detected = diarizer.Process(samples);
        cancellationToken.ThrowIfCancellationRequested();

        var speakers = new Dictionary<int, int>();
        var turns = new List<SpeakerTurn>();
        var duration = (double)samples.Length / RecordingAudio.SampleRate;
        foreach (var segment in detected.OrderBy(segment => segment.Start))
        {
            var start = Math.Clamp(segment.Start, 0, duration);
            var end = Math.Clamp(segment.End, start, duration);
            if (end <= start || segment.Speaker < 0)
            {
                continue;
            }

            if (!speakers.TryGetValue(segment.Speaker, out var speaker))
            {
                speaker = speakers.Count + 1;
                speakers.Add(segment.Speaker, speaker);
            }

            turns.Add(new SpeakerTurn(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), speaker));
        }

        return turns.AsReadOnly();
    }

    private static string ValidateModel(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Modèle de diarisation absent. Exécutez scripts/setup-diarization.ps1.", fullPath);
        }

        return fullPath;
    }
}
