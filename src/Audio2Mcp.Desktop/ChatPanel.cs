using Audio2Mcp.Chat;
using Audio2Mcp.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System.ClientModel;

namespace Audio2Mcp.Desktop;

/// <summary>Chat presentation and request lifecycle. All methods run on the Avalonia UI thread.</summary>
public sealed class ChatPanel : Grid
{
    private readonly StackPanel messages = new() { Spacing = 16 };
    private readonly StackPanel attachments = new() { Spacing = 4 };
    private readonly TextBox input = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 85,
        MaxHeight = 180,
        PlaceholderText = "Votre message…"
    };

    private readonly Button send = new() { Content = "Envoyer" };
    private readonly Button cancel = new() { Content = "Interrompre", IsEnabled = false };
    private readonly Button attach = new() { Content = "Ajouter des images" };
    private readonly Button reset = new() { Content = "Nouvelle discussion" };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly List<ChatImage> pendingImages = [];
    private readonly ScrollViewer scroll;
    private ChatConversation? conversation;
    private CancellationTokenSource? cancellation;
    private Task? requestTask;
    private bool stopped;
    private readonly Func<IReadOnlyList<TextAction>> getActions;
    private readonly ClientTranscriptInbox transcriptInbox = new();
    private readonly CheckBox automaticMode = new() { Content = "Envoyer les demandes entendues sur le PC au LLM" };
    private readonly TextBlock automaticStatus = new() { TextWrapping = TextWrapping.Wrap };
    private bool uiBusy;
    private bool processingTranscripts;
    private bool automaticSending;

    /// <summary>Whether a generation must be cancelled before closing the window.</summary>
    public bool IsSending => requestTask is { IsCompleted: false };

    /// <summary>Creates the composer, attachment controls and streaming conversation view.</summary>
    public ChatPanel(Func<IReadOnlyList<TextAction>>? getActions = null)
    {
        this.getActions = getActions ?? (() => []);
        Margin = new Thickness(24);
        RowDefinitions = new RowDefinitions("Auto,*,Auto");
        RowSpacing = 16;
        scroll = new ScrollViewer { Content = messages };
        BuildLayout();
        send.Click += async (_, _) =>
        {
            requestTask = SendAsync();
            await requestTask;
        };
        cancel.Click += (_, _) =>
        {
            automaticMode.IsChecked = false;
            cancellation?.Cancel();
        };
        automaticMode.IsCheckedChanged += (_, _) =>
        {
            transcriptInbox.Enabled = automaticMode.IsChecked == true && !stopped;
            if (!transcriptInbox.Enabled && automaticSending)
            {
                cancellation?.Cancel();
            }

            automaticStatus.Text = transcriptInbox.Enabled
                ? "Mode actif : démarrez la capture dans Transcription. Seule la source PC est transmise."
                : "Mode désactivé ; demandes en attente supprimées.";
        };
        attach.Click += async (_, _) => await AttachImagesAsync();
        reset.Click += (_, _) => ResetConversation();
        ResetConversation();
    }

    /// <summary>Cancels generation and waits for cleanup before the window releases its controls.</summary>
    public async Task CancelAndWaitAsync()
    {
        stopped = true;
        automaticMode.IsChecked = false;
        transcriptInbox.Enabled = false;
        cancellation?.Cancel();
        if (requestTask is not null)
        {
            await requestTask;
        }
    }

    /// <summary>
    /// Offers live recognized text on the UI thread. Never waits for the LLM; overflow is displayed.
    /// Microphone text is excluded by the inbox, independently of the capture configuration.
    /// </summary>
    public void AcceptTranscript(TranscriptSegment segment)
    {
        if (stopped)
        {
            return;
        }

        var acceptance = transcriptInbox.Offer(segment);
        if (acceptance == TranscriptAcceptance.Overflow)
        {
            AddMessage("Audio PC — non envoyé", "File du LLM pleine : ce segment reste dans la transcription.\n" + segment.Text);
        }
        else if (acceptance == TranscriptAcceptance.Queued)
        {
            automaticStatus.Text = $"Demandes PC en attente : {transcriptInbox.Count}";
            TryProcessTranscripts();
        }
    }

    private void TryProcessTranscripts()
    {
        if (stopped || uiBusy || processingTranscripts || transcriptInbox.Count == 0)
        {
            return;
        }

        requestTask = ProcessTranscriptsAsync();
    }

    private async Task ProcessTranscriptsAsync()
    {
        processingTranscripts = true;
        try
        {
            while (!stopped && transcriptInbox.Take() is { } segment)
            {
                automaticStatus.Text = $"Demande PC en cours • {transcriptInbox.Count} en attente";
                if (!await SendAsync(segment))
                {
                    automaticMode.IsChecked = false;
                    automaticStatus.Text = "Mode désactivé après interruption ou erreur. Aucun nouvel essai automatique.";
                    break;
                }
            }

            if (transcriptInbox.Enabled)
            {
                automaticStatus.Text = "En attente d’une transcription PC…";
            }
        }
        finally
        {
            processingTranscripts = false;
        }
    }

    private void BuildLayout()
    {
        var header = new StackPanel { Spacing = 8 };
        header.Children.Add(new TextBlock { Text = "Chat avec le modèle local", FontSize = 24 });
        header.Children.Add(status);
        header.Children.Add(reset);
        header.Children.Add(automaticMode);
        header.Children.Add(new TextBlock
        {
            Text = "Le texte reconnu est envoyé au LLM configuré et peut déclencher les actions activées. Le micro est exclu ; tous les sons du PC peuvent être capturés.",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        });
        header.Children.Add(automaticStatus);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(attach);
        actions.Children.Add(send);
        actions.Children.Add(cancel);

        var composer = new StackPanel { Spacing = 8 };
        composer.Children.Add(attachments);
        composer.Children.Add(input);
        composer.Children.Add(actions);
        composer.Children.Add(new TextBlock
        {
            Text = "PNG, JPEG ou WebP • 4 images maximum, 10 Mo par image • modèle compatible vision requis",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        });

        Grid.SetRow(scroll, 1);
        Grid.SetRow(composer, 2);
        Children.Add(header);
        Children.Add(scroll);
        Children.Add(composer);
    }

    private void ResetConversation()
    {
        automaticMode.IsChecked = false;
        transcriptInbox.Enabled = false;
        conversation = null;
        messages.Children.Clear();
        pendingImages.Clear();
        RefreshAttachments();
        input.Text = "";
        status.Text = "Discussion en mémoire • configuration dans appsettings.json";
    }

    private async Task AttachImagesAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null || !storage.CanOpen)
        {
            status.Text = "La sélection de fichiers n’est pas disponible.";
            return;
        }

        SetBusy(true);
        cancel.IsEnabled = false;
        try
        {
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choisir des images",
                AllowMultiple = true,
                FileTypeFilter = [new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp"] }]
            });

            foreach (var file in files)
            {
                using (file)
                {
                    if (stopped)
                    {
                        continue;
                    }

                    if (pendingImages.Count >= 4)
                    {
                        status.Text = "Maximum 4 images par message.";
                        continue;
                    }

                    await using var stream = await file.OpenReadAsync();
                    pendingImages.Add(await ChatImage.ReadAsync(file.Name, stream));
                }
            }
        }
        catch (InvalidOperationException ex)
        {
            status.Text = ex.Message;
        }
        catch (Exception)
        {
            status.Text = "Impossible de lire l’image sélectionnée.";
        }
        finally
        {
            RefreshAttachments();
            SetBusy(false);
        }
    }

    private void RefreshAttachments()
    {
        attachments.Children.Clear();
        foreach (var image in pendingImages.ToArray())
        {
            var remove = new Button { Content = $"Retirer : {image.Name}" };
            remove.Click += (_, _) =>
            {
                pendingImages.Remove(image);
                RefreshAttachments();
            };
            attachments.Children.Add(remove);
        }
    }

    private async Task<bool> SendAsync(TranscriptSegment? clientSegment = null)
    {
        var text = clientSegment is null ? input.Text ?? "" : ClientTranscriptInbox.FormatMessage(clientSegment);
        var images = clientSegment is null ? pendingImages.ToArray() : [];
        if (stopped || cancellation is not null || (string.IsNullOrWhiteSpace(text) && images.Length == 0))
        {
            return false;
        }

        automaticSending = clientSegment is not null;
        SetBusy(true);
        using var requestCancellation = new CancellationTokenSource();
        cancellation = requestCancellation;
        TextBlock? answer = null;
        try
        {
            if (conversation is null)
            {
                var settings = LlmSettings.Load(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
                conversation = new ChatConversation(settings.CreateClient(), new TextActionExecutor());
                conversation.ActionCompleted += result => AddMessage("Action", result);
            }

            var imageNames = string.Join("\n", images.Select(image => $"[Image : {image.Name}]"));
            var author = clientSegment is null ? "Vous" : $"Client · audio PC [{clientSegment.Start:mm\\:ss}]";
            AddMessage(author, string.Join("\n", new[] { text, imageNames }.Where(value => value.Length > 0)));
            answer = AddMessage("Assistant", "");
            status.Text = "Réponse en cours…";

            await foreach (var delta in conversation.SendAsync(text, images, requestCancellation.Token, getActions()))
            {
                answer.Text += delta;
                scroll.ScrollToEnd();
            }

            if (clientSegment is null)
            {
                input.Text = "";
                pendingImages.Clear();
                RefreshAttachments();
            }

            status.Text = "Réponse terminée.";
            return true;
        }
        catch (OperationCanceledException)
        {
            status.Text = "Réponse interrompue. Le brouillon est conservé. Les actions déjà exécutées ne sont pas annulées ; réessayer peut les répéter.";
            MarkIncomplete(answer);
        }
        catch (ClientResultException ex)
        {
            // Do not show raw server responses: they can echo credentials or request content.
            status.Text = $"Erreur du serveur (HTTP {ex.Status}). Vérifiez l’URL, la clé, le modèle et sa prise en charge des images.";
            MarkIncomplete(answer);
        }
        catch (InvalidOperationException ex)
        {
            status.Text = ex.Message;
            MarkIncomplete(answer);
        }
        catch (Exception)
        {
            status.Text = "Connexion impossible. Vérifiez appsettings.json et que le serveur local est démarré.";
            MarkIncomplete(answer);
        }
        finally
        {
            cancellation = null;
            automaticSending = false;
            SetBusy(false);
        }

        return false;
    }

    private TextBlock AddMessage(string author, string text)
    {
        var body = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        var message = new StackPanel { Spacing = 4 };
        message.Children.Add(new TextBlock { Text = author, FontWeight = FontWeight.Bold });
        message.Children.Add(body);
        messages.Children.Add(message);
        scroll.ScrollToEnd();
        return body;
    }

    private static void MarkIncomplete(TextBlock? answer)
    {
        if (answer is not null)
        {
            answer.Text += "\n[Réponse incomplète — échange non ajouté à l’historique du modèle. Les actions déjà exécutées restent effectuées ; réessayer peut les répéter.]";
        }
    }

    private void SetBusy(bool busy)
    {
        uiBusy = busy;
        send.IsEnabled = attach.IsEnabled = reset.IsEnabled = input.IsEnabled = attachments.IsEnabled = !busy;
        cancel.IsEnabled = busy;
        if (!busy && !stopped)
        {
            // Posting avoids replacing the tracked request task before its caller has assigned it.
            Dispatcher.UIThread.Post(TryProcessTranscripts);
        }
    }
}
