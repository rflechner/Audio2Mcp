using System.Text.RegularExpressions;
using OpenAI.Chat;

namespace Audio2Mcp.Chat;

/// <summary>The supported effects of a user-configured text action.</summary>
public enum TextActionKind
{
    OpenBrowser,
    HttpGet
}

/// <summary>An immutable tool definition. Only the query value is supplied by the model.</summary>
public sealed record TextAction(string Name, string Description, string UrlTemplate, TextActionKind Kind, bool Enabled = true)
{
    /// <summary>Checks tool naming and restricts substitution to the path or query of an HTTP URL.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || !Regex.IsMatch(Name, "^[a-zA-Z0-9_-]{1,64}$"))
        {
            throw new InvalidOperationException("Le nom doit contenir 1 à 64 lettres ASCII, chiffres, tirets ou underscores.");
        }

        if (string.IsNullOrWhiteSpace(Description) || !Enum.IsDefined(Kind))
        {
            throw new InvalidOperationException("Une description et un type d’action valides sont nécessaires.");
        }

        if (string.IsNullOrWhiteSpace(UrlTemplate) || !UrlTemplate.Contains("{query}", StringComparison.Ordinal)
            || !Uri.TryCreate(UrlTemplate.Replace("{query}", "probe"), UriKind.Absolute, out var uri)
            || (uri.Scheme != "https" && uri.Scheme != "http") || uri.UserInfo.Length != 0
            || HasQueryInAuthority(UrlTemplate))
        {
            throw new InvalidOperationException("Indiquez une URL HTTP(S) avec {query} dans le chemin ou les paramètres, sans identifiants.");
        }
    }

    private static bool HasQueryInAuthority(string template)
    {
        var authorityStart = template.IndexOf("://", StringComparison.Ordinal) + 3;
        var authorityEnd = template.IndexOfAny(['/', '?', '#'], authorityStart);
        return authorityEnd < 0 || template.IndexOf("{query}", StringComparison.Ordinal) < authorityEnd;
    }

    /// <summary>Encodes model text as a URL component; the model cannot choose the destination host.</summary>
    public Uri BuildUri(string query)
    {
        Validate();
        if (string.IsNullOrWhiteSpace(query) || query.Length > 2000)
        {
            throw new InvalidOperationException("La query doit contenir entre 1 et 2 000 caractères.");
        }

        return new Uri(UrlTemplate.Replace("{query}", Uri.EscapeDataString(query)));
    }

    /// <summary>Exposes the description and a single required query argument to the model.</summary>
    public ChatTool ToTool() => ChatTool.CreateFunctionTool(
        Name,
        Description,
        BinaryData.FromString("""
        {"type":"object","properties":{"query":{"type":"string","description":"Résumé concis de la demande de l’utilisateur, adapté à cette action."}},"required":["query"],"additionalProperties":false}
        """));
}

/// <summary>Executes one configured action. Implementations honor cancellation before starting an effect.</summary>
public interface ITextActionExecutor
{
    /// <summary>Returns a bounded textual result suitable for display and model context.</summary>
    Task<string> ExecuteAsync(TextAction action, string query, CancellationToken cancellationToken);
}
