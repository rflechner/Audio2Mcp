using System.Diagnostics;
using Audio2Mcp.Chat;

namespace Audio2Mcp.Desktop;

/// <summary>Desktop effects for configured tools. Browser launches cannot be undone by cancellation.</summary>
public sealed class TextActionExecutor : ITextActionExecutor
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    /// <inheritdoc />
    public async Task<string> ExecuteAsync(TextAction action, string query, CancellationToken cancellationToken)
    {
        var uri = action.BuildUri(query);
        cancellationToken.ThrowIfCancellationRequested();
        if (action.Kind == TextActionKind.OpenBrowser)
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return $"URL ouverte dans le navigateur : {uri.AbsoluteUri}";
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var reader = new StreamReader(stream);
        var buffer = new char[4001];
        var length = await reader.ReadBlockAsync(buffer.AsMemory(), timeout.Token);
        var body = new string(buffer, 0, Math.Min(length, 4000));
        return $"HTTP {(int)response.StatusCode}\n{body}" + (length > 4000 ? "\n[Résultat tronqué]" : "");
    }
}
