using System.ClientModel;
using System.Text.Json;
using OpenAI;
using OpenAI.Chat;

namespace Audio2Mcp.Chat;

/// <summary>Connection settings containing only the name of the secret environment variable.</summary>
public sealed class LlmSettings
{
    /// <summary>OpenAI-compatible API base URL, including its version path.</summary>
    public string Endpoint { get; init; } = "";

    /// <summary>Exact model identifier advertised by the local server.</summary>
    public string Model { get; init; } = "";

    /// <summary>Name of the process environment variable containing the API key.</summary>
    public string ApiKeyEnvironmentVariable { get; init; } = "";

    /// <summary>Loads the Llm section from the supplied application settings file.</summary>
    public static LlmSettings Load(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var settings = document.RootElement.GetProperty("Llm").Deserialize<LlmSettings>()
            ?? throw new InvalidOperationException("La section Llm est absente de appsettings.json.");
        settings.Validate();
        return settings;
    }

    /// <summary>Validates configuration before reading credentials or making requests.</summary>
    public void Validate()
    {
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != "http" && endpoint.Scheme != "https")
            || !string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment)
            || string.IsNullOrWhiteSpace(Model)
            || string.IsNullOrWhiteSpace(ApiKeyEnvironmentVariable))
        {
            throw new InvalidOperationException("Vérifiez Endpoint, Model et ApiKeyEnvironmentVariable dans appsettings.json.");
        }
    }

    /// <summary>Reads the secret at connection time; it is never persisted in configuration.</summary>
    public ChatClient CreateClient()
    {
        Validate();
        var apiKey = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException($"La variable d’environnement {ApiKeyEnvironmentVariable} est absente ou vide. Redémarrez l’application après l’avoir définie.");
        }

        return new ChatClient(Model, new ApiKeyCredential(apiKey), new OpenAIClientOptions
        {
            Endpoint = new Uri(Endpoint)
        });
    }
}
