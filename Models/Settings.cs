namespace NotionPad.Models;

public class Settings
{
    public string? NotionToken { get; set; }
    public string? DatabaseId { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(NotionToken) && !string.IsNullOrWhiteSpace(DatabaseId);
}

public class StoreData
{
    public List<Note> Notes { get; set; } = new();
    public Settings Settings { get; set; } = new();
}
