using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZivAiEditor.Contracts.Inference;

namespace ZivAiEditor.Backend;

/// <summary>
/// HTTP implementation of <see cref="ILlmClient"/> that talks to a local
/// llama-server's OpenAI-compatible <c>/v1/chat/completions</c> endpoint. The
/// request shape mirrors klein's proven <c>llm_client.chat</c>: messages,
/// <c>stream=false</c>, optional <c>max_tokens</c>, and
/// <c>chat_template_kwargs.enable_thinking=false</c> for Qwen3.5-family models.
///
/// Only the standard HTTP client is used here; C# never loads an LLM runtime
/// (Z17). The <see cref="HttpClient"/> is injected so callers control lifetime,
/// and this type is intentionally generic so later scenarios reuse it with
/// different <see cref="LlmClientOptions"/>.
/// </summary>
public sealed class LocalLlmClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly LlmClientOptions _options;
    private readonly bool _ownsHttpClient;
    private bool _disposed;

    public LocalLlmClient(HttpClient httpClient, LlmClientOptions options, bool ownsHttpClient = false)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _ownsHttpClient = ownsHttpClient;
    }

    public async Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_options.Timeout);
        var token = timeoutCts.Token;

        try
        {
            try
            {
                return await SendAsync(BuildRequest(systemPrompt, userPrompt, disableThinking: !_options.EnableThinking), token)
                    .ConfigureAwait(false);
            }
            catch (LlmClientException) when (!_options.EnableThinking)
            {
                // klein experience: some servers reject chat_template_kwargs;
                // retry once without it before giving up.
                return await SendAsync(BuildRequest(systemPrompt, userPrompt, disableThinking: false), token)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new LlmClientException(
                $"LLM call timed out after {_options.Timeout.TotalSeconds:0.#}s.");
        }
    }

    private ChatRequest BuildRequest(string systemPrompt, string userPrompt, bool disableThinking)
    {
        var request = new ChatRequest
        {
            Model = string.IsNullOrWhiteSpace(_options.Model) ? null : _options.Model,
            Messages = new List<ChatMessage>
            {
                new() { Role = "system", Content = systemPrompt ?? "" },
                new() { Role = "user", Content = userPrompt ?? "" },
            },
            Stream = false,
            Temperature = _options.Temperature,
            MaxTokens = _options.MaxTokens,
        };

        if (disableThinking)
        {
            request.ChatTemplateKwargs = new ChatTemplateKwargs { EnableThinking = false };
        }

        return request;
    }

    private async Task<string> SendAsync(ChatRequest request, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(request, LocalLlmJsonContext.Default.ChatRequest);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync(_options.Endpoint, content, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new LlmClientException($"LLM request to {_options.Endpoint} failed.", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var snippet = body.Length > 200 ? body[..200] : body;
                throw new LlmClientException(
                    $"LLM endpoint returned {(int)response.StatusCode} {response.ReasonPhrase}: {snippet}");
            }

            ChatResponse? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize(body, LocalLlmJsonContext.Default.ChatResponse);
            }
            catch (JsonException ex)
            {
                throw new LlmClientException("LLM response was not valid JSON.", ex);
            }

            var message = parsed?.Choices is { Count: > 0 } choices ? choices[0].Message?.Content : null;
            if (message is null)
            {
                throw new LlmClientException("LLM response contained no assistant message.");
            }

            return message;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }
}

/// <summary>Raised when the local LLM call fails (HTTP error, timeout, or bad JSON).</summary>
public sealed class LlmClientException : Exception
{
    public LlmClientException(string message)
        : base(message)
    {
    }

    public LlmClientException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal sealed class ChatRequest
{
    public string? Model { get; set; }

    public List<ChatMessage> Messages { get; set; } = new();

    public bool Stream { get; set; }

    public double Temperature { get; set; }

    public int MaxTokens { get; set; }

    public ChatTemplateKwargs? ChatTemplateKwargs { get; set; }
}

internal sealed class ChatMessage
{
    public string Role { get; set; } = "";

    public string? Content { get; set; }
}

internal sealed class ChatTemplateKwargs
{
    public bool EnableThinking { get; set; }
}

internal sealed class ChatResponse
{
    public List<ChatChoice>? Choices { get; set; }
}

internal sealed class ChatChoice
{
    public ChatMessage? Message { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ChatRequest))]
[JsonSerializable(typeof(ChatResponse))]
internal partial class LocalLlmJsonContext : JsonSerializerContext
{
}