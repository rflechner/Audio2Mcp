using System.Runtime.CompilerServices;
using System.Text;
using OpenAI.Chat;

namespace Audio2Mcp.Chat;

/// <summary>
/// Owns an in-memory conversation. Only completed turns enter history; failed or cancelled
/// responses remain presentation-only. A conversation permits one active request at a time.
/// </summary>
public sealed class ChatConversation(ChatClient client)
{
    private readonly List<ChatMessage> history = [];
    private int sending;

    /// <summary>
    /// Yields text deltas on the consuming context. Cancellation aborts the HTTP stream.
    /// Images are sent as inline data, never uploaded to a separate service.
    /// </summary>
    public async IAsyncEnumerable<string> SendAsync(
        string text,
        IReadOnlyList<ChatImage> images,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
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
            var response = new StringBuilder();
            var completed = false;

            await foreach (var update in client.CompleteChatStreamingAsync(messages, cancellationToken: cancellationToken))
            {
                completed |= update.FinishReason is not null;
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
            if (!completed || response.Length == 0)
            {
                throw new InvalidOperationException("Le modèle n’a pas renvoyé de réponse textuelle complète.");
            }

            history.Add(userMessage);
            history.Add(new AssistantChatMessage(response.ToString()));
        }
        finally
        {
            Volatile.Write(ref sending, 0);
        }
    }
}
