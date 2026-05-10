using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using NotionPad.Models;

namespace NotionPad.Services;

public class LocalStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _filePath;
    private readonly object _writeLock = new();
    private System.Threading.Timer? _debounceTimer;

    public StoreData Data { get; private set; } = new();

    public LocalStore()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NotionPad");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "notion-pad.json");
    }

    public void Load()
    {
        if (!File.Exists(_filePath))
        {
            Data = new StoreData();
            return;
        }
        var raw = File.ReadAllText(_filePath);
        Data = JsonSerializer.Deserialize<StoreData>(raw, JsonOptions) ?? new StoreData();
    }

    public void Persist()
    {
        _debounceTimer?.Dispose();
        _debounceTimer = new System.Threading.Timer(_ => FlushNow(), null, 200, System.Threading.Timeout.Infinite);
    }

    public void FlushNow()
    {
        lock (_writeLock)
        {
            var tmp = _filePath + ".tmp";
            var payload = JsonSerializer.Serialize(Data, JsonOptions);
            File.WriteAllText(tmp, payload);
            if (File.Exists(_filePath)) File.Replace(tmp, _filePath, null);
            else File.Move(tmp, _filePath);
        }
    }
}
