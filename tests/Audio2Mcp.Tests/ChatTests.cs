using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json;
using Audio2Mcp.Chat;
using OpenAI;
using OpenAI.Chat;
using Xunit;

namespace Audio2Mcp.Tests;

public sealed class ChatTests
{
    [Theory]
    [InlineData("search_google", "{\"query\":\"été & café\"}", true)]
    [InlineData("unknown", "{\"query\":\"test\"}", false)]
    [InlineData("search_google", "{\"url\":\"https://example.com\"}", false)]
    public async Task ToolCallsAreDispatchedAndResultsReturnedToModel(string toolName, string arguments, bool expectedExecution)
    {
        using var handler = new FakeChatHandler { ToolName = toolName, ToolArguments = arguments };
        using var http = new HttpClient(handler);
        var executor = new FakeActionExecutor();
        var conversation = CreateConversation(http, executor);
        var events = new List<string>();
        conversation.ActionCompleted += events.Add;
        var action = new TextAction("search_google", "Search the web", "https://example.com/?q={query}", TextActionKind.OpenBrowser);

        await CollectAsync(conversation.SendAsync("cherche", [], actions: [action]));

        Assert.Equal(expectedExecution ? 1 : 0, executor.Count);
        Assert.Single(events);
        using var first = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Equal("search_google", first.RootElement.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString());
        using var second = JsonDocument.Parse(handler.Bodies[1]);
        var messages = second.RootElement.GetProperty("messages");
        Assert.Equal("tool", messages[2].GetProperty("role").GetString());
        Assert.Equal("call_1", messages[2].GetProperty("tool_call_id").GetString());
        Assert.Contains(expectedExecution ? "Executed" : "Erreur", messages[2].GetProperty("content").GetString());
    }

    private sealed class FakeActionExecutor : ITextActionExecutor
    {
        public int Count { get; private set; }

        public Task<string> ExecuteAsync(TextAction action, string query, CancellationToken cancellationToken)
        {
            Count++;
            Assert.Equal("été & café", query);
            return Task.FromResult("Executed");
        }
    }

    [Fact]
    public async Task SendsInlineImageAndStreamsTextWithConversationHistory()
    {
        using var handler = new FakeChatHandler();
        using var http = new HttpClient(handler);
        var conversation = CreateConversation(http);
        using var imageStream = new MemoryStream(Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII="));
        var image = await ChatImage.ReadAsync("pixel.png", imageStream);

        var deltas = await CollectAsync(conversation.SendAsync("Décris ceci", [image]));
        Assert.Equal(new[] { "Bon", "jour" }, deltas);
        await CollectAsync(conversation.SendAsync("Et ensuite ?", []));

        Assert.Equal("http://localhost:8888/v1/chat/completions", handler.RequestUri);
        using var first = JsonDocument.Parse(handler.Bodies[0]);
        Assert.True(first.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal("local-model", first.RootElement.GetProperty("model").GetString());
        var content = first.RootElement.GetProperty("messages")[0].GetProperty("content");
        Assert.Equal("Décris ceci", content[0].GetProperty("text").GetString());
        Assert.StartsWith("data:image/png;base64,", content[1].GetProperty("image_url").GetProperty("url").GetString());

        using var second = JsonDocument.Parse(handler.Bodies[1]);
        var messages = second.RootElement.GetProperty("messages");
        Assert.Equal(3, messages.GetArrayLength());
        Assert.Equal("assistant", messages[1].GetProperty("role").GetString());
        Assert.Contains("Bonjour", messages[1].GetProperty("content").ToString());
    }

    [Fact]
    public async Task CancelledPartialTurnDoesNotPolluteHistoryAndAllowsRetry()
    {
        using var handler = new FakeChatHandler();
        using var http = new HttpClient(handler);
        var conversation = CreateConversation(http);
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var delta in conversation.SendAsync("interrompu", [], cancellation.Token))
            {
                cancellation.Cancel();
            }
        });

        await CollectAsync(conversation.SendAsync("réessayer", []));
        using var request = JsonDocument.Parse(handler.Bodies[^1]);
        Assert.Equal(1, request.RootElement.GetProperty("messages").GetArrayLength());
    }

    [Fact]
    public async Task RejectsConcurrentRequestAndReleasesGuardWhenEnumeratorIsDisposed()
    {
        using var handler = new FakeChatHandler();
        using var http = new HttpClient(handler);
        var conversation = CreateConversation(http);
        await using (var response = conversation.SendAsync("premier", []).GetAsyncEnumerator())
        {
            Assert.True(await response.MoveNextAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => CollectAsync(conversation.SendAsync("second", [])));
        }

        await CollectAsync(conversation.SendAsync("nouvel essai", []));
        using var request = JsonDocument.Parse(handler.Bodies[^1]);
        Assert.Equal(1, request.RootElement.GetProperty("messages").GetArrayLength());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedOrTruncatedStreamDoesNotEnterHistory(bool httpFailure)
    {
        using var handler = new FakeChatHandler { Fail = httpFailure, Truncate = !httpFailure };
        using var http = new HttpClient(handler);
        var conversation = CreateConversation(http);
        await Assert.ThrowsAnyAsync<Exception>(() => CollectAsync(conversation.SendAsync("échec", [])));

        handler.Fail = false;
        handler.Truncate = false;
        await CollectAsync(conversation.SendAsync("nouvel essai", []));
        using var request = JsonDocument.Parse(handler.Bodies[^1]);
        Assert.Equal(1, request.RootElement.GetProperty("messages").GetArrayLength());
    }

    [Fact]
    public async Task RejectsInvalidAndOversizedImages()
    {
        using var invalid = new MemoryStream("not an image"u8.ToArray());
        await Assert.ThrowsAsync<InvalidOperationException>(() => ChatImage.ReadAsync("fake.png", invalid));

        using var oversized = new MemoryStream(new byte[ChatImage.MaximumBytes + 1]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ChatImage.ReadAsync("huge.png", oversized));
    }

    [Fact]
    public void ConfigurationRequiresValidEndpointAndNamedSecret()
    {
        var invalid = new LlmSettings { Endpoint = "file:///tmp", Model = "local", ApiKeyEnvironmentVariable = "KEY" };
        Assert.Throws<InvalidOperationException>(invalid.Validate);

        var missingKey = new LlmSettings
        {
            Endpoint = "http://localhost:8888/v1",
            Model = "local",
            ApiKeyEnvironmentVariable = "AUDIO2MCP_TEST_MISSING_" + Guid.NewGuid().ToString("N")
        };
        Assert.Throws<InvalidOperationException>(() => missingKey.CreateClient());
    }

    private static ChatConversation CreateConversation(HttpClient http, ITextActionExecutor? executor = null)
    {
        var client = new ChatClient("local-model", new ApiKeyCredential("test-key"), new OpenAIClientOptions
        {
            Endpoint = new Uri("http://localhost:8888/v1"),
            Transport = new HttpClientPipelineTransport(http),
            RetryPolicy = new ClientRetryPolicy(0)
        });
        return new ChatConversation(client, executor);
    }

    private static async Task<List<string>> CollectAsync(IAsyncEnumerable<string> stream)
    {
        var result = new List<string>();
        await foreach (var delta in stream)
        {
            result.Add(delta);
        }

        return result;
    }

    private sealed class FakeChatHandler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        public string? RequestUri { get; private set; }

        public bool Fail { get; set; }

        public bool Truncate { get; set; }

        public string? ToolName { get; set; }

        public string ToolArguments { get; set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.ToString();
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            if (Fail)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("{\"error\":{\"message\":\"unsupported image\"}}", Encoding.UTF8, "application/json")
                };
            }

            var events = Event("Bon", null) + Event("jour", null);
            if (ToolName is not null && Bodies.Count == 1)
            {
                var midpoint = ToolArguments.Length / 2;
                events = ToolEvent(ToolName, ToolArguments[..midpoint], null)
                    + ToolEvent(null, ToolArguments[midpoint..], "tool_calls");
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(events + "data: [DONE]\n\n", Encoding.UTF8, "text/event-stream")
                };
            }

            if (!Truncate)
            {
                events += Event("", "stop") + "data: [DONE]\n\n";
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(events, Encoding.UTF8, "text/event-stream")
            };
        }

        private static string Event(string text, string? finishReason)
        {
            var payload = new
            {
                id = "test",
                @object = "chat.completion.chunk",
                created = 1,
                model = "local-model",
                choices = new[] { new { index = 0, delta = new { content = text }, finish_reason = finishReason } }
            };
            return $"data: {JsonSerializer.Serialize(payload)}\n\n";
        }

        private static string ToolEvent(string? name, string arguments, string? finishReason)
        {
            var payload = new
            {
                id = "test",
                @object = "chat.completion.chunk",
                created = 1,
                model = "local-model",
                choices = new[]
                {
                    new
                    {
                        index = 0,
                        delta = new
                        {
                            tool_calls = new[]
                            {
                                new { index = 0, id = name is null ? null : "call_1", type = "function", function = new { name, arguments } }
                            }
                        },
                        finish_reason = finishReason
                    }
                }
            };
            return $"data: {JsonSerializer.Serialize(payload)}\n\n";
        }
    }
}
