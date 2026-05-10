using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NotionPad.Models;
using NotionPad.Services;

namespace NotionPad.Views;

public partial class MainWindow : Window
{
    private static readonly TimeSpan AutosaveDelay = TimeSpan.FromMilliseconds(1500);

    private readonly ObservableCollection<Note> _notes = new();
    private readonly DispatcherTimer _autosaveTimer;
    private Note? _activeNote;
    private bool _suppressEditorEvents;

    public MainWindow()
    {
        InitializeComponent();
        _autosaveTimer = new DispatcherTimer { Interval = AutosaveDelay };
        _autosaveTimer.Tick += OnAutosaveTick;

        NotesList.ItemsSource = _notes;
        LoadNotes();

        Loaded += OnLoaded;
    }

    private void LoadNotes()
    {
        _notes.Clear();
        foreach (var note in App.Store.Data.Notes.OrderByDescending(n => n.UpdatedAt))
        {
            HookNote(note);
            _notes.Add(note);
        }
        if (_notes.Count > 0) NotesList.SelectedIndex = 0;
    }

    private void HookNote(Note note)
    {
        note.PropertyChanged -= OnNotePropertyChanged;
        note.PropertyChanged += OnNotePropertyChanged;
    }

    private void OnNotePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not Note note) return;
        if (note != _activeNote) return;
        if (e.PropertyName is nameof(Note.SyncState) or nameof(Note.LastError) or nameof(Note.Title))
        {
            Dispatcher.BeginInvoke(new Action(() => RefreshHeader(note)));
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!App.Store.Data.Settings.IsConfigured)
        {
            OpenSettings(required: true);
        }
    }

    private void OnCreateNote(object sender, RoutedEventArgs e)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var note = new Note { CreatedAt = now, UpdatedAt = now };
        HookNote(note);
        App.Store.Data.Notes.Insert(0, note);
        App.Store.Persist();
        _notes.Insert(0, note);
        NotesList.SelectedItem = note;
        EditorBox.Focus();
    }

    private void OnNoteSelected(object sender, SelectionChangedEventArgs e)
    {
        FlushPendingAutosave();
        _activeNote = NotesList.SelectedItem as Note;
        _suppressEditorEvents = true;
        if (_activeNote is null)
        {
            EditorBox.Text = string.Empty;
            EditorBox.IsEnabled = false;
            EmptyHint.Visibility = Visibility.Visible;
            DeleteButton.IsEnabled = false;
            TitleLabel.Text = "No note selected";
            StateDot.Fill = (Brush)Application.Current.Resources["MutedBrush"];
            StateText.Text = string.Empty;
            RetryButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            EditorBox.Text = _activeNote.Body;
            EditorBox.IsEnabled = true;
            EmptyHint.Visibility = Visibility.Collapsed;
            DeleteButton.IsEnabled = true;
            RefreshHeader(_activeNote);
        }
        _suppressEditorEvents = false;
    }

    private void RefreshHeader(Note note)
    {
        TitleLabel.Text = string.IsNullOrWhiteSpace(note.Title) ? "Untitled" : note.Title;
        StateDot.Fill = (Brush)(Application.Current.Resources[StateBrushKey(note.SyncState)] ?? Brushes.Gray);
        StateText.Text = StateLabel(note.SyncState);
        var isError = note.SyncState == SyncState.Error;
        RetryButton.Visibility = isError ? Visibility.Visible : Visibility.Collapsed;
        CopyErrorButton.Visibility = isError ? Visibility.Visible : Visibility.Collapsed;
        ErrorBanner.Visibility = isError ? Visibility.Visible : Visibility.Collapsed;
        ErrorText.Text = note.LastError ?? string.Empty;
    }

    private void OnCopyError(object sender, RoutedEventArgs e)
    {
        if (_activeNote?.LastError is null) return;
        Clipboard.SetText(_activeNote.LastError);
    }

    private static string StateBrushKey(SyncState state) => state switch
    {
        SyncState.Idle => "OkBrush",
        SyncState.Pending => "WarnBrush",
        SyncState.Syncing => "WarnBrush",
        SyncState.Error => "ErrorBrush",
        _ => "MutedBrush"
    };

    private static string StateLabel(SyncState state) => state switch
    {
        SyncState.Idle => "Synced",
        SyncState.Pending => "Pending",
        SyncState.Syncing => "Syncing",
        SyncState.Error => "Error",
        _ => string.Empty
    };

    private void OnEditorTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEditorEvents || _activeNote is null) return;
        _autosaveTimer.Stop();
        _autosaveTimer.Start();
    }

    private void OnAutosaveTick(object? sender, EventArgs e)
    {
        _autosaveTimer.Stop();
        FlushPendingAutosave();
    }

    private void FlushPendingAutosave()
    {
        if (_activeNote is null) return;
        var body = EditorBox.Text;
        if (body == _activeNote.Body) return;
        _activeNote.Body = body;
        _activeNote.Title = NotionClient.DeriveTitle(body);
        _activeNote.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        App.Store.Persist();
        Resort(_activeNote);
        App.Sync.Enqueue(_activeNote.Id);
    }

    private void Resort(Note note)
    {
        var currentIndex = _notes.IndexOf(note);
        if (currentIndex <= 0) return;
        _notes.Move(currentIndex, 0);
        NotesList.SelectedItem = note;
    }

    private async void OnDeleteNote(object sender, RoutedEventArgs e)
    {
        if (_activeNote is null) return;
        var confirm = MessageBox.Show(
            this,
            "Delete this note? It will also be archived in Notion.",
            "Delete note",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK) return;

        var note = _activeNote;
        _autosaveTimer.Stop();
        _notes.Remove(note);
        App.Store.Data.Notes.Remove(note);
        App.Store.Persist();

        if (!string.IsNullOrEmpty(note.NotionPageId) && App.Store.Data.Settings.IsConfigured)
        {
            try
            {
                await App.Notion.ArchivePageAsync(note.NotionPageId);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Notion archive failed: {ex.Message}", "Warning",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void OnRetry(object sender, RoutedEventArgs e)
    {
        if (_activeNote is null) return;
        App.Sync.Enqueue(_activeNote.Id);
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        OpenSettings(required: false);
    }

    private void OpenSettings(bool required)
    {
        var dialog = new SettingsWindow(required) { Owner = this };
        dialog.ShowDialog();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        FlushPendingAutosave();
        App.Store.FlushNow();
        base.OnClosing(e);
    }
}
