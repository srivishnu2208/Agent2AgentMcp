using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowLocalUI", policy =>
    {
        policy.WithOrigins("http://localhost:5173")   // Vite UI origin
              .AllowAnyMethod()
              .AllowAnyHeader();
        // .AllowCredentials(); // uncomment only if you use cookies/credentials
    });
});
var app = builder.Build();
app.UseCors("AllowLocalUI");

// agent-card metadata
var agentCard = JsonSerializer.Serialize(new
{
    a2a_version = "2025-06-18",
    id = "agent://localhost:5092/responder",
    name = "LocalResponderAgent",
    description = "Generates answer from corrected text using configured LLM and records history.",
    capabilities = new[] {
        new {
            name = "answer",
            intents = new[] { "text.answer" },
            input_schema = new { type = "object", properties = new { text = new { type = "string" } }, required = new[] { "text" } }
        }
    },
    invoke = new { url = "http://localhost:5092/invoke", method = "POST", auth = new { type = "bearer", scopes = new[] { "a2a.invoke" } } }
});

app.MapGet("/agent-card", () => Results.Content(agentCard, "application/json"));

// configuration from env (fallbacks)
var provider = Environment.GetEnvironmentVariable("LLM_PROVIDER") ?? "ollama"; // "ollama" | "openai" | "azure"
var endpoint = Environment.GetEnvironmentVariable("LLM_ENDPOINT") ?? "http://localhost:11434";
var model = Environment.GetEnvironmentVariable("LLM_MODEL") ?? "gemma3:1b";

// history storage path (next to running binary)
var historyPath = Path.Combine(AppContext.BaseDirectory, "history.jsonl");

// helper: append a history entry (best-effort)
async Task AppendHistoryAsync(string userOriginal, string corrected, string answer)
{
    try
    {
        var entry = new
        {
            timestamp = DateTime.UtcNow.ToString("o"),
            original = userOriginal,
            corrected = corrected,
            answer = answer
        };
        var line = JsonSerializer.Serialize(entry);
        await File.AppendAllTextAsync(historyPath, line + Environment.NewLine, Encoding.UTF8);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Failed to write history: {ex.Message}");
    }
}

// GET /history -> return array of history entries (most recent last)
app.MapGet("/history", () =>
{
    if (!File.Exists(historyPath)) return Results.Json(Array.Empty<object>());

    var lines = File.ReadAllLines(historyPath);
    var list = new List<JsonElement>();
    foreach (var l in lines)
    {
        if (string.IsNullOrWhiteSpace(l)) continue;
        try
        {
            using var doc = JsonDocument.Parse(l);
            list.Add(doc.RootElement.Clone());
        }
        catch
        {
            // ignore malformed lines
        }
    }
    return Results.Json(list);
});

// POST /invoke -> call LLM (ollama/openai/azure) and write history
app.MapPost("/invoke", async (HttpRequest req) =>
{
    // simple bearer check for demo
    if (!req.Headers.TryGetValue("Authorization", out var auth) || !auth.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { error = "Unauthorized" }, statusCode: 401);

    using var doc = await JsonDocument.ParseAsync(req.Body);
    var root = doc.RootElement;

    // input text
    var corrected = root.GetProperty("input").GetProperty("text").GetString() ?? "";
    // optional original in context
    var original = corrected;
    if (root.TryGetProperty("context", out var ctx) && ctx.ValueKind == JsonValueKind.Object && ctx.TryGetProperty("original", out var orig))
        original = orig.GetString() ?? corrected;

    string assistant = "";

    try
    {
        if (provider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
        {
            var payload = new { model = model, messages = new[] { new { role = "user", content = $"Answer the following: {corrected}" } } };
            var chatUrl = endpoint.TrimEnd('/') + "/v1/chat/completions";
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            var resp = await http.PostAsJsonAsync(chatUrl, payload);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                return Results.Json(new { error = "LLM call failed", details = body }, statusCode: 502);
            }
            assistant = ParseAssistantFromChatCompletions(body);
        }
        else if (provider.Equals("openai", StringComparison.OrdinalIgnoreCase))
        {
            var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey)) return Results.Json(new { error = "OPENAI_API_KEY not set" }, statusCode: 500);

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            var payload = new { model = model, messages = new[] { new { role = "user", content = $"Answer the following: {corrected}" } } };
            var chatUrl = "https://api.openai.com/v1/chat/completions";
            var resp = await http.PostAsJsonAsync(chatUrl, payload);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                return Results.Json(new { error = "OpenAI call failed", details = body }, statusCode: 502);
            }
            assistant = ParseAssistantFromChatCompletions(body);
        }
        else if (provider.Equals("azure", StringComparison.OrdinalIgnoreCase))
        {
            var ep = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
            var key = Environment.GetEnvironmentVariable("AZURE_OPENAI_APIKEY");
            var deploy = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT");
            if (string.IsNullOrWhiteSpace(ep) || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(deploy))
                return Results.Json(new { error = "Azure OpenAI env vars missing" }, statusCode: 500);

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            http.DefaultRequestHeaders.Add("api-key", key);

            var chatUrl = $"{ep.TrimEnd('/')}/openai/deployments/{deploy}/chat/completions?api-version=2024-06-01-preview";
            var payload = new { messages = new[] { new { role = "user", content = $"Answer the following: {corrected}" } } };
            var resp = await http.PostAsJsonAsync(chatUrl, payload);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                return Results.Json(new { error = "Azure call failed", details = body }, statusCode: 502);
            }
            assistant = ParseAssistantFromChatCompletions(body);
        }
        else
        {
            return Results.Json(new { error = "Unsupported provider" }, statusCode: 500);
        }
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = "Exception calling LLM", details = ex.Message }, statusCode: 500);
    }

    // record history (best-effort)
    _ = AppendHistoryAsync(original, corrected, assistant ?? "");

    return Results.Json(new { capability = "answer", output = new { answer = (assistant ?? "").Trim() } }, statusCode: 200);
});

// small parser: robustly tries to extract assistant content from typical chat/completions responses
string ParseAssistantFromChatCompletions(string responseBody)
{
    try
    {
        using var jdoc = JsonDocument.Parse(responseBody);
        var root = jdoc.RootElement;

        // common pattern: choices[0].message.content
        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
        {
            var first = choices[0];
            if (first.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object && message.TryGetProperty("content", out var content))
                return content.GetString() ?? "";

            // fallback: choices[0].text
            if (first.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                return text.GetString() ?? "";
        }

        // some providers return "result" or "output"
        if (root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.String) return result.GetString() ?? "";
        if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.String) return output.GetString() ?? "";

        // fallback: return entire JSON as string (safe fallback)
        return responseBody;
    }
    catch
    {
        return responseBody;
    }
}

app.Run("http://localhost:5092");
