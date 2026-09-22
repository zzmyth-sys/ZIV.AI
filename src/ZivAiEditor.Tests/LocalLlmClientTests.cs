using System.Net;
using System.Text;
using ZivAiEditor.Backend;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// Unit tests for <see cref="LocalLlmClient"/> using a stubbed
/// <see cref="HttpMessageHandler"/>. No llama-server and no GPU are required
/// (Z29).
/// </summary>
public class LocalLlmClientTests
{
    private static (LocalLlmClient Client, StubHandler Handler) Create(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
        LlmClientOptions? options = null)
    {
        var stub = new StubHandler(handler);
        var http = new HttpClient(stub);
        var client = new LocalLlmClient(http, options ?? new LlmClientOptions());
        return (client, stub);
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private const string ValidResponse =
        "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"hello\"}}]}";

    [Fact]
    public async Task CompleteAsync_Sends_Correct_Payload()
    {
        var (client, handler) = Create((_, _) => Task.FromResult(Json(ValidResponse)));

        await client.CompleteAsync("SYS", "USER");

        Assert.NotNull(handler.LastBody);
        Assert.Contains("\"messages\"", handler.LastBody);
        Assert.Contains("\"role\":\"system\"", handler.LastBody);
        Assert.Contains("\"role\":\"user\"", handler.LastBody);
        Assert.Contains("\"stream\":false", handler.LastBody);
        Assert.Contains("\"temperature\":0.1", handler.LastBody);
        Assert.Contains("\"max_tokens\":2048", handler.LastBody);
        Assert.Contains("\"enable_thinking\":false", handler.LastBody);
    }

    [Fact]
    public async Task CompleteAsync_Parses_Valid_Response()
    {
        var (client, _) = Create((_, _) => Task.FromResult(Json(ValidResponse)));

        var content = await client.CompleteAsync("SYS", "USER");

        Assert.Equal("hello", content);
    }

    [Fact]
    public async Task CompleteAsync_Throws_On_HttpError()
    {
        var (client, _) = Create((_, _) => Task.FromResult(Json("boom", HttpStatusCode.InternalServerError)));

        var ex = await Assert.ThrowsAsync<LlmClientException>(() => client.CompleteAsync("SYS", "USER"));
        Assert.Contains("500", ex.Message);
    }

    [Fact]
    public async Task CompleteAsync_Throws_On_Timeout()
    {
        var options = new LlmClientOptions { Timeout = TimeSpan.FromMilliseconds(50) };
        var (client, _) = Create(
            async (_, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                return Json(ValidResponse);
            },
            options);

        await Assert.ThrowsAsync<LlmClientException>(() => client.CompleteAsync("SYS", "USER"));
    }

    [Fact]
    public async Task CompleteAsync_Throws_On_Invalid_Json()
    {
        var (client, _) = Create((_, _) => Task.FromResult(Json("not json at all")));

        await Assert.ThrowsAsync<LlmClientException>(() => client.CompleteAsync("SYS", "USER"));
    }

    [Fact]
    public async Task CompleteAsync_Uses_Options_Temperature()
    {
        var options = new LlmClientOptions { Temperature = 0.7, MaxTokens = 512 };
        var (client, handler) = Create((_, _) => Task.FromResult(Json(ValidResponse)), options);

        await client.CompleteAsync("SYS", "USER");

        Assert.NotNull(handler.LastBody);
        Assert.Contains("\"temperature\":0.7", handler.LastBody);
        Assert.Contains("\"max_tokens\":512", handler.LastBody);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return await _handler(request, cancellationToken);
        }
    }
}