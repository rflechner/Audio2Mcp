using System.Text;
using System.Text.Json;
using OpenAI.Chat;

namespace Audio2Mcp.Chat;

/// <summary>Collects fragmented streaming tool calls and dispatches only configured actions.</summary>
internal sealed class ToolCallBatch
{
    private readonly SortedDictionary<int, PendingCall> calls = [];

    public void Append(StreamingChatToolCallUpdate update)
    {
        if (!calls.TryGetValue(update.Index, out var call))
        {
            call = new PendingCall();
            calls.Add(update.Index, call);
        }

        call.Id = update.ToolCallId ?? call.Id;
        call.Name = update.FunctionName ?? call.Name;
        if (update.FunctionArgumentsUpdate is not null)
        {
            call.Arguments.Append(update.FunctionArgumentsUpdate.ToString());
        }
    }

    public IReadOnlyList<ChatToolCall> Complete() => calls.Values.Select(call =>
        ChatToolCall.CreateFunctionToolCall(call.Id, call.Name, BinaryData.FromString(call.Arguments.ToString()))).ToArray();

    public static async Task<string> ExecuteAsync(
        ChatToolCall call,
        IReadOnlyList<TextAction> actions,
        ITextActionExecutor executor,
        CancellationToken cancellationToken)
    {
        try
        {
            var action = actions.SingleOrDefault(action => action.Name == call.FunctionName);
            if (action is null)
            {
                return "Erreur : action inconnue ou désactivée.";
            }

            using var arguments = JsonDocument.Parse(call.FunctionArguments);
            var root = arguments.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1
                || !root.TryGetProperty("query", out var query) || query.ValueKind != JsonValueKind.String)
            {
                return "Erreur : un unique paramètre query de type texte est requis.";
            }

            var text = query.GetString()!;
            action.BuildUri(text);
            return await executor.ExecuteAsync(action, text, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or HttpRequestException
            or OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            return "Erreur : paramètres invalides ou exécution de l’action impossible.";
        }
    }

    private sealed class PendingCall
    {
        public string Id { get; set; } = "";

        public string Name { get; set; } = "";

        public StringBuilder Arguments { get; } = new();
    }
}
