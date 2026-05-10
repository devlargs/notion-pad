using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NotionPad.Models;

public enum SyncState
{
    Idle,
    Pending,
    Syncing,
    Error
}

public class Note : INotifyPropertyChanged
{
    private string _title = "Untitled";
    private string _body = string.Empty;
    private string? _notionPageId;
    private long _updatedAt;
    private long? _syncedAt;
    private SyncState _syncState = SyncState.Idle;
    private string? _lastError;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    public string Body
    {
        get => _body;
        set => SetField(ref _body, value);
    }

    public string? NotionPageId
    {
        get => _notionPageId;
        set => SetField(ref _notionPageId, value);
    }

    public long CreatedAt { get; set; }

    public long UpdatedAt
    {
        get => _updatedAt;
        set => SetField(ref _updatedAt, value);
    }

    public long? SyncedAt
    {
        get => _syncedAt;
        set => SetField(ref _syncedAt, value);
    }

    public SyncState SyncState
    {
        get => _syncState;
        set => SetField(ref _syncState, value);
    }

    public string? LastError
    {
        get => _lastError;
        set => SetField(ref _lastError, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
