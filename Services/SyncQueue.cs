using System.Collections.Concurrent;
using System.Windows;
using NotionPad.Models;

namespace NotionPad.Services;

public class SyncQueue
{
    private readonly LocalStore _store;
    private readonly NotionClient _notion;
    private readonly ConcurrentDictionary<string, Task> _tails = new();

    public SyncQueue(LocalStore store, NotionClient notion)
    {
        _store = store;
        _notion = notion;
    }

    public void Enqueue(string noteId)
    {
        var note = FindNote(noteId);
        if (note is null) return;
        if (note.SyncState != SyncState.Syncing) UpdateOnUi(note, SyncState.Pending, null);

        _tails.AddOrUpdate(
            noteId,
            _ => Run(noteId),
            (_, prior) => prior.ContinueWith(_ => Run(noteId)).Unwrap()
        );
    }

    private async Task Run(string noteId)
    {
        var note = FindNote(noteId);
        if (note is null) return;
        var settings = _store.Data.Settings;
        if (!settings.IsConfigured)
        {
            UpdateOnUi(note, SyncState.Error, "Notion not configured");
            return;
        }
        UpdateOnUi(note, SyncState.Syncing, null);
        try
        {
            if (string.IsNullOrEmpty(note.NotionPageId))
            {
                var pageId = await _notion.CreatePageAsync(note.Body);
                UpdateOnUi(note, SyncState.Idle, null, pageId);
            }
            else
            {
                await _notion.UpdatePageAsync(note.NotionPageId, note.Body);
                UpdateOnUi(note, SyncState.Idle, null);
            }
        }
        catch (Exception ex)
        {
            UpdateOnUi(note, SyncState.Error, ex.Message);
        }
    }

    private Note? FindNote(string id) => _store.Data.Notes.FirstOrDefault(n => n.Id == id);

    private void UpdateOnUi(Note note, SyncState state, string? error, string? pageId = null)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Apply(note, state, error, pageId);
            return;
        }
        dispatcher.Invoke(() => Apply(note, state, error, pageId));
    }

    private void Apply(Note note, SyncState state, string? error, string? pageId)
    {
        note.SyncState = state;
        note.LastError = error;
        if (pageId is not null) note.NotionPageId = pageId;
        if (state == SyncState.Idle) note.SyncedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _store.Persist();
    }
}
