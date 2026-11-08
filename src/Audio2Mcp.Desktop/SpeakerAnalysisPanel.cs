using Audio2Mcp.Core;
using Audio2Mcp.Diarization;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Audio2Mcp.Desktop;

/// <summary>Desktop controls and lifecycle for optional microphone capture and offline diarization.</summary>
/// <remarks>All public methods and events run on the UI thread. The analysis owns its transcriber.</remarks>
public sealed class SpeakerAnalysisPanel : StackPanel
{
    private readonly Func<ITranscriber> createTranscriber;
    private readonly CheckBox microphone = new() { Content = "Inclure le microphone (source séparée)" };
    private readonly NumericUpDown speakerCount = new() { Minimum = 0, Maximum = 20, Value = 0, Increment = 1 };
    private readonly NumericUpDown clusteringThreshold = new()
    {
        Minimum = 0.05m,
        Maximum = 1,
        Value = 0.5m,
        Increment = 0.05m,
        FormatString = "0.00"
    };
    private readonly TextBox segmentationModel = new()
    {
        Text = "models/diarization/sherpa-onnx-pyannote-segmentation-3-0/model.onnx"
    };
    private readonly TextBox embeddingModel = new()
    {
        Text = "models/diarization/nemo_en_titanet_small.onnx"
    };
    private readonly Button analyze = new() { Content = "Identifier les interlocuteurs", IsEnabled = false };
    private readonly Button cancel = new() { Content = "Annuler l'analyse", IsEnabled = false };

    private IReadOnlyList<SourceRecording>? recordings;
    private string directory = "";
    private CancellationTokenSource? cancellation;
    private Task? analysisTask;

    public bool IncludeMicrophone => microphone.IsChecked == true;

    public bool IsAnalyzing => analysisTask is { IsCompleted: false };

    public event Action<string>? StatusChanged;

    public event Action<bool>? BusyChanged;

    public event Action<ConversationAnalysisResult>? Completed;

    public SpeakerAnalysisPanel(Func<ITranscriber> createTranscriber)
    {
        this.createTranscriber = createTranscriber;
        Spacing = 8;
        Children.Add(microphone);

        Children.Add(new TextBlock { Text = "Nombre de voix PC connu : 0 = estimation automatique, 2 = interview à deux" });
        Children.Add(speakerCount);
        Children.Add(new TextBlock
        {
            Text = "L'estimation peut compter plusieurs fois une même voix. Si le nombre est connu, indiquez-le avant l'analyse.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12
        });

        var options = new StackPanel { Spacing = 6 };
        options.Children.Add(new TextBlock
        {
            Text = "Seuil automatique : plus élevé = moins de groupes, avec un risque de fusionner deux voix.",
            TextWrapping = TextWrapping.Wrap
        });
        options.Children.Add(clusteringThreshold);
        options.Children.Add(new TextBlock { Text = "Modèle de segmentation" });
        options.Children.Add(segmentationModel);
        options.Children.Add(new TextBlock { Text = "Modèle de comparaison des voix" });
        options.Children.Add(embeddingModel);
        Children.Add(new Expander { Header = "Réglages des interlocuteurs", Content = options });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(analyze);
        buttons.Children.Add(cancel);
        Children.Add(buttons);
        Children.Add(new TextBlock
        {
            Text = "Analyse après Stop • voix anonymes • casque conseillé avec le micro",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        });

        analyze.Click += async (_, _) =>
        {
            analysisTask = RunAnalysisAsync();
            await analysisTask;
        };
        cancel.Click += (_, _) => Cancel();
    }

    /// <summary>Clears the previous result when starting and locks microphone selection during capture.</summary>
    public void SetCapturing(bool capturing)
    {
        microphone.IsEnabled = !capturing;
        if (capturing)
        {
            recordings = null;
        }

        analyze.IsEnabled = !capturing && recordings is not null;
    }

    public void SetRecording(CaptureResult result)
    {
        directory = Path.GetDirectoryName(result.Path)!;
        recordings = result.Recordings.Count > 0
            ? result.Recordings
            : Array.AsReadOnly(new[]
            {
                new SourceRecording(AudioSource.SystemOutput, result.Path, TimeSpan.Zero, result.Duration, result.Peak)
            });
    }

    /// <summary>Requests cancellation and waits for native work before the window can close.</summary>
    public async Task CancelAndWaitAsync()
    {
        Cancel();
        if (analysisTask is not null)
        {
            await analysisTask;
        }
    }

    private void Cancel()
    {
        cancellation?.Cancel();
        if (IsAnalyzing)
        {
            StatusChanged?.Invoke("Annulation demandée ; attente de la fin du calcul natif…");
        }
    }

    private async Task RunAnalysisAsync()
    {
        if (recordings is null || IsAnalyzing)
        {
            return;
        }

        cancellation = new CancellationTokenSource();
        analyze.IsEnabled = false;
        cancel.IsEnabled = true;
        BusyChanged?.Invoke(true);
        speakerCount.IsEnabled = clusteringThreshold.IsEnabled = false;

        try
        {
            var diarizer = new SherpaSpeakerDiarizer(segmentationModel.Text ?? "", embeddingModel.Text ?? "");
            var requestedCount = (int)(speakerCount.Value ?? 0);
            var threshold = (float)(clusteringThreshold.Value ?? 0.5m);
            await using var transcriber = createTranscriber();
            var progress = new Progress<string>(message =>
            {
                if (cancellation is { IsCancellationRequested: false })
                {
                    StatusChanged?.Invoke(message);
                }
            });
            var result = await new ConversationAnalysis(diarizer).AnalyzeAsync(
                recordings, directory, requestedCount, transcriber,
                progress, cancellation.Token, threshold);

            Completed?.Invoke(result);
            var outputCount = result.Segments.Where(segment => segment.Source == AudioSource.SystemOutput)
                .Select(segment => segment.Speaker).Distinct().Count();
            var countMode = requestedCount == 0 ? "estimation automatique" : $"nombre demandé : {requestedCount}";
            StatusChanged?.Invoke($"Analyse terminée • PC : {outputCount} groupes ({countMode}) • {result.TranscriptPath}");
        }
        catch (OperationCanceledException)
        {
            StatusChanged?.Invoke("Analyse annulée ; la transcription initiale est conservée.");
        }
        catch (Exception error)
        {
            StatusChanged?.Invoke($"Analyse impossible : {error.Message}");
        }
        finally
        {
            cancellation.Dispose();
            cancellation = null;
            analyze.IsEnabled = true;
            cancel.IsEnabled = false;
            speakerCount.IsEnabled = clusteringThreshold.IsEnabled = true;
            BusyChanged?.Invoke(false);
        }
    }
}
