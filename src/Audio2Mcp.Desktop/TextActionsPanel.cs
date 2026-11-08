using Audio2Mcp.Chat;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Audio2Mcp.Desktop;

/// <summary>Edits the local action catalog on the UI thread; chat requests consume immutable snapshots.</summary>
public sealed class TextActionsPanel : ScrollViewer
{
    private readonly TextActionStore store = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Audio2Mcp", "actions.json"));
    private readonly ListBox list = new() { Height = 150 };
    private readonly TextBox name = new() { PlaceholderText = "search_google" };
    private readonly TextBox description = new() { AcceptsReturn = true, MinHeight = 80, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox url = new() { PlaceholderText = "https://www.google.com/search?q={query}" };
    private readonly ComboBox kind = new()
    {
        ItemsSource = new[] { "Ouvrir dans le navigateur", "Requête HTTP GET" },
        SelectedIndex = 0
    };
    private readonly CheckBox enabled = new() { Content = "Exposer cette action au chat", IsChecked = true };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private IReadOnlyList<TextAction> catalog = [];
    private int editingIndex = -1;

    /// <summary>Returns a stable snapshot of saved actions for the next message.</summary>
    public IReadOnlyList<TextAction> Actions => catalog;

    /// <summary>Builds the editor and loads saved definitions without overwriting unreadable files.</summary>
    public TextActionsPanel()
    {
        var layout = new StackPanel { Margin = new Thickness(24), Spacing = 10 };
        layout.Children.Add(new TextBlock { Text = "Actions du chat", FontSize = 24 });
        layout.Children.Add(new TextBlock
        {
            Text = "Décrivez quand utiliser l’action. Le LLM résume la demande dans query. Les actions activées s’exécutent automatiquement et transmettent query au site choisi.",
            TextWrapping = TextWrapping.Wrap
        });
        layout.Children.Add(list);
        AddField(layout, "Nom de l’outil (lettres ASCII, chiffres, _ ou -)", name);
        AddField(layout, "Description et circonstances d’utilisation pour le LLM", description);
        AddField(layout, "Exécution", kind);
        AddField(layout, "URL HTTP(S) — {query} sera remplacé par le texte encodé", url);
        layout.Children.Add(enabled);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        AddButton(buttons, "Nouvelle", () => Edit(-1));
        AddButton(buttons, "Exemple Google", SetExample);
        AddButton(buttons, "Enregistrer", Save);
        AddButton(buttons, "Supprimer", Delete);
        layout.Children.Add(buttons);
        layout.Children.Add(status);
        Content = layout;
        list.SelectionChanged += (_, _) => Edit(list.SelectedIndex);
        try
        {
            catalog = store.Load();
            Refresh();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        {
            status.Text = "Impossible de charger les actions. Vérifiez le fichier local actions.json. L’éditeur est désactivé pour préserver ce fichier.";
            buttons.IsEnabled = false;
        }
    }

    private static void AddField(StackPanel layout, string label, Control control)
    {
        layout.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
        layout.Children.Add(control);
    }

    private static void AddButton(StackPanel layout, string label, Action action)
    {
        var button = new Button { Content = label };
        button.Click += (_, _) => action();
        layout.Children.Add(button);
    }

    private void Edit(int index)
    {
        editingIndex = index;
        var action = index >= 0 && index < catalog.Count ? catalog[index] : null;
        name.Text = action?.Name ?? "";
        description.Text = action?.Description ?? "";
        url.Text = action?.UrlTemplate ?? "";
        kind.SelectedIndex = (int)(action?.Kind ?? TextActionKind.OpenBrowser);
        enabled.IsChecked = action?.Enabled ?? true;
    }

    private void SetExample()
    {
        list.SelectedIndex = -1;
        Edit(-1);
        name.Text = "search_google";
        description.Text = "Rechercher sur Google lorsque l’utilisateur demande une recherche sur le Web. Résumer sa demande en mots-clés pertinents dans query.";
        url.Text = "https://www.google.com/search?q={query}";
    }

    private void Save()
    {
        var action = new TextAction(name.Text?.Trim() ?? "", description.Text?.Trim() ?? "",
            url.Text?.Trim() ?? "", (TextActionKind)kind.SelectedIndex, enabled.IsChecked == true);
        var updated = catalog.ToList();
        if (editingIndex >= 0)
        {
            updated[editingIndex] = action;
        }
        else
        {
            updated.Add(action);
        }

        Persist(updated);
    }

    private void Delete()
    {
        if (editingIndex < 0)
        {
            return;
        }

        var updated = catalog.ToList();
        updated.RemoveAt(editingIndex);
        Persist(updated);
    }

    private void Persist(List<TextAction> updated)
    {
        try
        {
            store.Save(updated);
            catalog = updated.AsReadOnly();
            Refresh();
            status.Text = "Actions enregistrées. Elles seront disponibles dès le prochain message du chat.";
        }
        catch (InvalidOperationException ex)
        {
            status.Text = ex.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            status.Text = "Impossible d’enregistrer les actions sur le disque.";
        }
    }

    private void Refresh()
    {
        list.ItemsSource = catalog.Select(action => $"{action.Name} · {(action.Enabled ? "activée" : "désactivée")}").ToArray();
        list.SelectedIndex = -1;
        Edit(-1);
    }
}
