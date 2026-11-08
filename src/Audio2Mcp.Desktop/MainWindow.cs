using Audio2Mcp.Capture.Windows;
using Audio2Mcp.Core;
using Audio2Mcp.Transcription.Whisper;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Audio2Mcp.Desktop;

/// <summary>Desktop composition root and presentation of a local recording session.</summary>
public sealed class MainWindow : Window
{
    private readonly ComboBox engine = new()
    {
        ItemsSource = new[] { "Whisper.net (en mémoire)", "whisper-cli (processus externe)" },
        SelectedIndex = 0,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private readonly StackPanel executableSettings = new() { Spacing = 8, IsVisible = false };

    private readonly TextBox executable = new()
    {
        Text = Environment.GetEnvironmentVariable("WHISPER_CLI") ?? "tools/whisper/whisper-cli.exe"
    };

    private readonly TextBox model = new()
    {
        Text = Environment.GetEnvironmentVariable("WHISPER_MODEL") ?? "models/ggml-base.bin"
    };

    private readonly TextBox language = new()
    {
        Text = "fr",
        Width = 90,
        HorizontalAlignment = HorizontalAlignment.Left
    };

    private readonly Button start = new() { Content = "Start", MinWidth = 110 };
    private readonly Button stop = new() { Content = "Stop", MinWidth = 110, IsEnabled = false };

    private readonly TextBlock status = new()
    {
        Text = "Prêt • sortie audio Windows par défaut",
        TextWrapping = TextWrapping.Wrap
    };

    private readonly TextBox transcript = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        PlaceholderText = "La transcription apparaîtra après une pause sonore (20 s maximum par segment)…"
    };

    private RecordingSession? session;
    private readonly SpeakerAnalysisPanel speakerAnalysis;
    private readonly TextActionsPanel textActions = new();
    private readonly ChatPanel chat;
    private bool busy;
    private bool closing;

    /// <summary>Creates the recording controls and connects the session lifecycle actions.</summary>
    public MainWindow()
    {
        chat = new ChatPanel(() => textActions.Actions);
        speakerAnalysis = new SpeakerAnalysisPanel(CreateTranscriber);
        BuildLayout();
        BindActions();

        if (!OperatingSystem.IsWindows())
        {
            start.IsEnabled = false;
            status.Text = "Interface portable ; capture disponible uniquement sous Windows.";
        }
    }

    private void BuildLayout()
    {
        Title = "Audio2Mcp — transcription et chat local";
        Width = 780;
        Height = 850;
        MinWidth = 600;
        MinHeight = 550;

        var settings = new StackPanel { Spacing = 8 };
        settings.Children.Add(new TextBlock
        {
            Text = "Audio2Mcp",
            FontSize = 28,
            FontWeight = FontWeight.Bold
        });
        settings.Children.Add(new TextBlock
        {
            Text = "Capturez le son du PC et transcrivez-le localement.",
            FontSize = 15
        });
        settings.Children.Add(new TextBlock { Text = "Moteur de transcription" });
        settings.Children.Add(engine);

        executableSettings.Children.Add(new TextBlock { Text = "Exécutable whisper.cpp" });
        executableSettings.Children.Add(executable);
        settings.Children.Add(executableSettings);

        settings.Children.Add(new TextBlock { Text = "Modèle GGML multilingue" });
        settings.Children.Add(model);

        settings.Children.Add(new TextBlock { Text = "Langue (fr, en, auto…)" });
        settings.Children.Add(language);
        settings.Children.Add(speakerAnalysis);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Margin = new Thickness(0, 8)
        };
        buttons.Children.Add(start);
        buttons.Children.Add(stop);

        settings.Children.Add(buttons);
        settings.Children.Add(status);

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Margin = new Thickness(24),
            RowSpacing = 16
        };
        Grid.SetRow(settings, 0);
        Grid.SetRow(transcript, 1);

        var footer = new TextBlock
        {
            Text = "Son du PC et micro optionnel • traitement local • WAV et texte dans captures/",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12
        };

        Grid.SetRow(footer, 2);
        layout.Children.Add(new ScrollViewer { Content = settings, MaxHeight = 650 });
        layout.Children.Add(transcript);
        layout.Children.Add(footer);

        Content = new TabControl
        {
            ItemsSource = new[]
            {
                new TabItem { Header = "Transcription", Content = layout },
                new TabItem { Header = "Chat", Content = chat },
                new TabItem { Header = "Actions", Content = textActions }
            }
        };
    }

    private void BindActions()
    {
        speakerAnalysis.StatusChanged += message => status.Text = message;
        speakerAnalysis.BusyChanged += active => start.IsEnabled = !active && OperatingSystem.IsWindows();
        speakerAnalysis.Completed += result =>
        {
            transcript.Text = string.Join(Environment.NewLine, result.Segments.Select(segment =>
                $"[{segment.Start:mm\\:ss}] [{SourceLabel(segment.Source)} · Interlocuteur {segment.Speaker}] " +
                (segment.HasOverlap ? "[voix superposées] " : "") + segment.Text));
        };
        engine.SelectionChanged += (_, _) => executableSettings.IsVisible = engine.SelectedIndex == 1;
        start.Click += async (_, _) => await StartAsync();
        stop.Click += async (_, _) => await StopAsync();
        Closing += OnClosing;
        Closed += async (_, _) => await chat.CancelAndWaitAsync();
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (closing || (session is null && !speakerAnalysis.IsAnalyzing && !chat.IsSending))
        {
            return;
        }

        e.Cancel = true;
        if (busy)
        {
            status.Text = "Arrêt en cours : attendez avant de fermer.";
            return;
        }

        closing = true;
        busy = true;

        try
        {
            await chat.CancelAndWaitAsync();
            await speakerAnalysis.CancelAndWaitAsync();
            if (session is not null)
            {
                await session.DisposeAsync();
            }
        }
        catch (Exception ex)
        {
            status.Text = ex.Message;
        }
        finally
        {
            session = null;
            Close();
        }
    }

    private async Task StartAsync()
    {
        if (busy || session is not null || speakerAnalysis.IsAnalyzing || !OperatingSystem.IsWindows())
        {
            return;
        }

        busy = true;
        start.IsEnabled = false;

        try
        {
            var transcriber = CreateTranscriber();
            IAudioCapture capture = new WindowsAudioCapture();
            if (speakerAnalysis.IncludeMicrophone)
            {
                capture = new DualSourceCapture(capture, new WindowsAudioCapture(AudioSource.Microphone));
            }

            session = new RecordingSession(capture, transcriber);
            speakerAnalysis.SetCapturing(true);
            BindSessionEvents(session);

            transcript.Text = "";
            var directory = Path.GetFullPath(Path.Combine("captures", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")));
            await session.StartAsync(directory);

            status.Text = $"Capture active • {directory}";
            stop.IsEnabled = true;
            engine.IsEnabled = executable.IsEnabled = model.IsEnabled = language.IsEnabled = false;
        }
        catch (Exception ex)
        {
            status.Text = $"Démarrage impossible : {ex.Message}";
            await DisposeSessionAsync();
            speakerAnalysis.SetCapturing(false);

            start.IsEnabled = true;
        }
        finally
        {
            busy = false;
        }
    }

    private void BindSessionEvents(RecordingSession recordingSession)
    {
        recordingSession.SegmentReady += segment => Dispatcher.UIThread.Post(() =>
            {
                transcript.Text += $"[{segment.Start:mm\\:ss}] [{SourceLabel(segment.Source)}] {segment.Text}{Environment.NewLine}";
                transcript.CaretIndex = transcript.Text.Length;
                chat.AcceptTranscript(segment);
            });

        recordingSession.StatusChanged += message => Dispatcher.UIThread.Post(() => status.Text = message);

        recordingSession.CaptureFailed += ex => Dispatcher.UIThread.Post(async () =>
        {
            status.Text = ex.Message;
            await StopAsync();
        });
    }

    private async Task DisposeSessionAsync()
    {
        if (session is null)
        {
            return;
        }

        try
        {
            await session.DisposeAsync();
        }
        catch (Exception ex)
        {
            // Cleanup failures must not escape an async UI event handler.
            status.Text += $" • Nettoyage impossible : {ex.Message}";
        }
        finally
        {
            session = null;
        }
    }

    private async Task StopAsync()
    {
        if (busy || session is null)
        {
            return;
        }

        busy = true;
        stop.IsEnabled = false;
        status.Text = "Capture arrêtée ; finalisation des transcriptions en attente…";

        try
        {
            var result = await session.StopAsync();
            speakerAnalysis.SetRecording(result);

            status.Text = $"Terminé • {result.Duration.TotalSeconds:0.0} s • " +
                "enregistrement sauvegardé • " + result.Path;
        }
        catch (Exception ex)
        {
            status.Text = $"Erreur : {ex.Message}";
        }
        finally
        {
            await DisposeSessionAsync();

            speakerAnalysis.SetCapturing(false);
            busy = false;
            start.IsEnabled = true;
            engine.IsEnabled = executable.IsEnabled = model.IsEnabled = language.IsEnabled = true;
        }
    }

    private ITranscriber CreateTranscriber()
    {
        return engine.SelectedIndex == 0
            ? new WhisperNetTranscriber(model.Text ?? "", language.Text ?? "fr")
            : new WhisperCliTranscriber(executable.Text ?? "", model.Text ?? "", language.Text ?? "fr");
    }

    private static string SourceLabel(AudioSource source)
    {
        return source == AudioSource.Microphone ? "Micro" : "PC";
    }
}
