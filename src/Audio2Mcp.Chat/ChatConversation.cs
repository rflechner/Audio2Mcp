using System.Runtime.CompilerServices;
using System.Text;
using OpenAI.Chat;

namespace Audio2Mcp.Chat;

/// <summary>
/// Owns an in-memory conversation. Only completed turns enter history; failed or cancelled
/// responses remain presentation-only. A conversation permits one active request at a time.
/// </summary>
public sealed class ChatConversation(ChatClient client, ITextActionExecutor? actionExecutor = null)
{
    private readonly List<ChatMessage> history = [];
    private int sending;

    /// <summary>Raised on the consuming context after each tool result; effects survive failed or cancelled turns.</summary>
    public event Action<string>? ActionCompleted;

    /// <summary>
    /// Yields text deltas on the consuming context. Cancellation aborts the HTTP stream.
    /// Images are sent as inline data, never uploaded to a separate service.
    /// </summary>
    public async IAsyncEnumerable<string> SendAsync(
        string text,
        IReadOnlyList<ChatImage> images,
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        IReadOnlyList<TextAction>? actions = null)
    {
        if (string.IsNullOrWhiteSpace(text) && images.Count == 0)
        {
            throw new ArgumentException("Écrivez un message ou ajoutez une image.", nameof(text));
        }

        if (Interlocked.CompareExchange(ref sending, 1, 0) != 0)
        {
            throw new InvalidOperationException("Une réponse est déjà en cours.");
        }

        try
        {
            var parts = new List<ChatMessageContentPart>();
            if (!string.IsNullOrWhiteSpace(text))
            {
                parts.Add(ChatMessageContentPart.CreateTextPart(text));
            }

            parts.AddRange(images.Select(image => ChatMessageContentPart.CreateImagePart(image.Data, image.MediaType)));
            var userMessage = new UserChatMessage(parts);
            var messages = new List<ChatMessage>(history) { userMessage };
            var availableActions = (actions ?? []).Where(action => action.Enabled).ToArray();
            var options = new ChatCompletionOptions();
            foreach (var action in availableActions)
            {
                action.Validate();
                options.Tools.Add(action.ToTool());
            }

            for (var round = 0; round < 5; round++)
            {
                var response = new StringBuilder();
                var batch = new ToolCallBatch();
                ChatFinishReason? finishReason = null;
                await foreach (var update in client.CompleteChatStreamingAsync(messages, options, cancellationToken))
                {
                    finishReason = update.FinishReason ?? finishReason;
                    foreach (var call in update.ToolCallUpdates)
                    {
                        batch.Append(call);
                    }

                    foreach (var part in update.ContentUpdate)
                    {
                        if (!string.IsNullOrEmpty(part.Text))
                        {
                            response.Append(part.Text);
                            yield return part.Text;
                        }
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                var calls = batch.Complete();
                if (finishReason == ChatFinishReason.ToolCalls && calls.Count > 0 && actionExecutor is not null)
                {
                    if (round == 4 || calls.Count > 8)
                    {
                        throw new InvalidOperationException("Limite d’appels d’actions atteinte pour ce message.");
                    }

                    var assistant = new AssistantChatMessage(calls);
                    if (response.Length > 0)
                    {
                        assistant.Content.Add(ChatMessageContentPart.CreateTextPart(response.ToString()));
                    }

                    messages.Add(assistant);
                    foreach (var call in calls)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var result = await ToolCallBatch.ExecuteAsync(call, availableActions, actionExecutor, cancellationToken);
                        messages.Add(new ToolChatMessage(call.Id, result));
                        ActionCompleted?.Invoke($"{call.FunctionName} · {call.FunctionArguments}\n{result}");
                    }

                    continue;
                }

                if (finishReason != ChatFinishReason.Stop || response.Length == 0 || calls.Count > 0)
                {
                    throw new InvalidOperationException("Le modèle n’a pas renvoyé de réponse textuelle complète.");
                }

                messages.Add(new AssistantChatMessage(response.ToString()));
                history.AddRange(messages.Skip(history.Count));
                yield break;
            }
        }
        finally
        {
            Volatile.Write(ref sending, 0);
        }
    }
}
