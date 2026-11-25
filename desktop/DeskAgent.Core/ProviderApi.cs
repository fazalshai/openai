using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace DeskAgent.Core;

public sealed class ProviderApi : IDisposable
{
    readonly HttpClient client;
    public ProviderApi(HttpMessageHandler? handler = null)
    {
        client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false });
        client.Timeout = TimeSpan.FromMinutes(4); client.MaxResponseContentBufferSize = 10_000_000;
    }
    public async Task<JsonObject> CallAsync(AgentOptions options, string path, JsonObject? body, CancellationToken cancellation)
    {
        if (options.Provider is not "openai" and not "anthropic") throw new InvalidOperationException("Choose OpenAI or Claude.");
        if (string.IsNullOrWhiteSpace(options.ApiKey)) throw new InvalidOperationException("Add your own provider API key in Settings.");
        var baseUrl = options.Provider == "openai" ? "https://api.openai.com/v1/" : "https://api.anthropic.com/v1/";
        using var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, baseUrl + path);
        if (options.Provider == "openai") request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey.Trim());
        else { request.Headers.Add("x-api-key", options.ApiKey.Trim()); request.Headers.Add("anthropic-version", "2023-06-01"); }
        if (body != null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, cancellation);
        var raw = await response.Content.ReadAsStringAsync(cancellation);
        JsonObject? parsed = null; try { parsed = JsonNode.Parse(raw) as JsonObject; } catch { }
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Provider error " + (int)response.StatusCode + ": " + (parsed?["error"]?["message"]?.GetValue<string>() ?? response.ReasonPhrase));
        return parsed ?? throw new InvalidOperationException("The provider returned an unexpected response.");
    }
    public async Task<JsonArray> ModelsAsync(AgentOptions options, CancellationToken cancellation)
    {
        var all = new JsonArray(); var after = ""; var cursors = new HashSet<string>();
        do
        {
            var path = options.Provider == "anthropic" ? "models?limit=100" + (after != "" ? "&after_id=" + Uri.EscapeDataString(after) : "") : "models";
            var result = await CallAsync(options, path, null, cancellation);
            foreach (var model in result["data"]?.AsArray() ?? [])
            {
                var id = model?["id"]?.GetValue<string>() ?? "";
                if (options.Provider == "openai" && (!System.Text.RegularExpressions.Regex.IsMatch(id, @"^(gpt-\d|o[134](?:-|$)|codex-)") || System.Text.RegularExpressions.Regex.IsMatch(id, "audio|image|realtime|search|transcri|embedding|tts|instruct|moderation|computer-use"))) continue;
                if (model?["lifecycle"]?.GetValue<string>() == "retired") continue;
                all.Add(model?.DeepClone());
            }
            after = options.Provider == "anthropic" && result["has_more"]?.GetValue<bool>() == true ? result["last_id"]?.GetValue<string>() ?? "" : "";
            if (after != "" && !cursors.Add(after)) throw new InvalidOperationException("Model pagination did not complete. Try again.");
        } while (after != "");
        return all;
    }
    public void Dispose() => client.Dispose();
}
