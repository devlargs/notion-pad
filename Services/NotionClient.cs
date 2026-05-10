using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using NotionPad.Models;

namespace NotionPad.Services;

public class NotionClient
{
    private const string BaseUrl = "https://api.notion.com/v1/";
    private const string NotionVersion = "2022-06-28";
    private const int BlockCharLimit = 2000;
    private const int TitleCharLimit = 200;
    private const int AppendChunk = 100;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        DictionaryKeyPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly Func<Settings> _settingsAccessor;

    public NotionClient(HttpClient http, Func<Settings> settingsAccessor)
    {
        _http = http;
        _settingsAccessor = settingsAccessor;
    }

    public static string DeriveTitle(string body)
    {
        if (string.IsNullOrEmpty(body)) return "Untitled";
        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.Trim('\r', ' ', '\t');
            if (trimmed.Length == 0) continue;
            return trimmed.Length > TitleCharLimit ? trimmed[..TitleCharLimit] : trimmed;
        }
        return "Untitled";
    }

    public async Task<(bool ok, string? error)> TestConnectionAsync()
    {
        var settings = _settingsAccessor();
        if (string.IsNullOrWhiteSpace(settings.NotionToken)) return (false, "Token is empty");
        if (string.IsNullOrWhiteSpace(settings.DatabaseId)) return (false, "Database ID is empty");
        try
        {
            using var req = BuildRequest(HttpMethod.Get, $"databases/{settings.DatabaseId}");
            using var resp = await _http.SendAsync(req);
            if (resp.IsSuccessStatusCode) return (true, null);
            var body = await resp.Content.ReadAsStringAsync();
            return (false, $"{(int)resp.StatusCode}: {ExtractMessage(body)}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<string> CreatePageAsync(string body)
    {
        var settings = RequireSettings();
        var payload = new
        {
            parent = new { database_id = settings.DatabaseId },
            properties = TitleProperties(body),
            children = BodyToBlocks(body)
        };
        using var req = BuildRequest(HttpMethod.Post, "pages", payload);
        using var resp = await _http.SendAsync(req);
        await EnsureSuccess(resp);
        using var stream = await resp.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Notion response missing id");
    }

    public async Task UpdatePageAsync(string pageId, string body)
    {
        var titlePayload = new { properties = TitleProperties(body) };
        using (var req = BuildRequest(HttpMethod.Patch, $"pages/{pageId}", titlePayload))
        using (var resp = await _http.SendAsync(req))
            await EnsureSuccess(resp);

        await ClearChildrenAsync(pageId);

        var blocks = BodyToBlocks(body);
        for (var i = 0; i < blocks.Count; i += AppendChunk)
        {
            var slice = blocks.GetRange(i, Math.Min(AppendChunk, blocks.Count - i));
            using var req = BuildRequest(HttpMethod.Patch, $"blocks/{pageId}/children", new { children = slice });
            using var resp = await _http.SendAsync(req);
            await EnsureSuccess(resp);
        }
    }

    public async Task ArchivePageAsync(string pageId)
    {
        using var req = BuildRequest(HttpMethod.Patch, $"pages/{pageId}", new { archived = true });
        using var resp = await _http.SendAsync(req);
        await EnsureSuccess(resp);
    }

    private async Task ClearChildrenAsync(string pageId)
    {
        string? cursor = null;
        do
        {
            var path = $"blocks/{pageId}/children?page_size=100";
            if (cursor is not null) path += $"&start_cursor={Uri.EscapeDataString(cursor)}";
            using var listReq = BuildRequest(HttpMethod.Get, path);
            using var listResp = await _http.SendAsync(listReq);
            await EnsureSuccess(listResp);
            using var stream = await listResp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            foreach (var child in doc.RootElement.GetProperty("results").EnumerateArray())
            {
                var id = child.GetProperty("id").GetString();
                if (id is null) continue;
                using var delReq = BuildRequest(HttpMethod.Delete, $"blocks/{id}");
                using var delResp = await _http.SendAsync(delReq);
                await EnsureSuccess(delResp);
            }
            cursor = doc.RootElement.GetProperty("has_more").GetBoolean()
                ? doc.RootElement.TryGetProperty("next_cursor", out var nc) && nc.ValueKind == JsonValueKind.String
                    ? nc.GetString()
                    : null
                : null;
        } while (cursor is not null);
    }

    private static Dictionary<string, object> TitleProperties(string body) => new()
    {
        ["Name"] = new
        {
            title = new[] { new { text = new { content = DeriveTitle(body) } } }
        }
    };

    private static List<object> BodyToBlocks(string body)
    {
        var result = new List<object>();
        if (string.IsNullOrEmpty(body)) return result;
        var paragraphs = System.Text.RegularExpressions.Regex.Split(body, @"\r?\n\r?\n+");
        foreach (var raw in paragraphs)
        {
            var paragraph = raw.Replace("\r\n", " ").Replace('\n', ' ').Trim();
            if (paragraph.Length == 0) continue;
            for (var i = 0; i < paragraph.Length; i += BlockCharLimit)
            {
                var piece = paragraph.Substring(i, Math.Min(BlockCharLimit, paragraph.Length - i));
                result.Add(new
                {
                    @object = "block",
                    type = "paragraph",
                    paragraph = new { rich_text = new[] { new { type = "text", text = new { content = piece } } } }
                });
            }
        }
        return result;
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string path, object? body = null)
    {
        var settings = RequireSettings();
        var req = new HttpRequestMessage(method, BaseUrl + path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.NotionToken);
        req.Headers.Add("Notion-Version", NotionVersion);
        if (body is not null)
        {
            req.Content = JsonContent.Create(body, options: JsonOptions);
        }
        return req;
    }

    private Settings RequireSettings()
    {
        var s = _settingsAccessor();
        if (!s.IsConfigured) throw new InvalidOperationException("Notion settings are not configured");
        return s;
    }

    private static async Task EnsureSuccess(HttpResponseMessage resp)
    {
        if (resp.IsSuccessStatusCode) return;
        var body = await resp.Content.ReadAsStringAsync();
        throw new HttpRequestException($"Notion API {(int)resp.StatusCode}: {ExtractMessage(body)}");
    }

    private static string ExtractMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "(empty body)";
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
                return msg.GetString() ?? body;
        }
        catch (JsonException)
        {
        }
        return body;
    }
}
