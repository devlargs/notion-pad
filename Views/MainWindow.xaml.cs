using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NotionPad.Models;
using NotionPad.Services;

namespace NotionPad.Views;

public partial class MainWindow : Window
{
    private const double DefaultFontSize = 14d;
    private const double MinFontSize = 9d;
    private const double MaxFontSize = 36d;
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

        TabsList.ItemsSource = _notes;
        LoadNotes();

        InputBindings.Add(new KeyBinding(new ActionCommand(() => OnCreateNote(this, new RoutedEventArgs())),
            new KeyGesture(Key.T, ModifierKeys.Control)));
        InputBindings.Add(new KeyBinding(new ActionCommand(() => OnCloseActiveTab(this, new RoutedEventArgs())),
            new KeyGesture(Key.W, ModifierKeys.Control)));
        InputBindings.Add(new KeyBinding(new ActionCommand(OnZoomInGesture),
            new KeyGesture(Key.OemPlus, ModifierKeys.Control)));
        InputBindings.Add(new KeyBinding(new ActionCommand(OnZoomInGesture),
            new KeyGesture(Key.Add, ModifierKeys.Control)));
        InputBindings.Add(new KeyBinding(new ActionCommand(OnZoomOutGesture),
            new KeyGesture(Key.OemMinus, ModifierKeys.Control)));
        InputBindings.Add(new KeyBinding(new ActionCommand(OnZoomOutGesture),
            new KeyGesture(Key.Subtract, ModifierKeys.Control)));
        InputBindings.Add(new KeyBinding(new ActionCommand(OnZoomResetGesture),
            new KeyGesture(Key.D0, ModifierKeys.Control)));

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
        if (_notes.Count > 0) TabsList.SelectedIndex = 0;
        else SyncEditorWithSelection();
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
            Dispatcher.BeginInvoke(new Action(() => RefreshStatus(note)));
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await App.Updater.CheckAndApplyAsync();
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
        TabsList.SelectedItem = note;
        EditorBox.Focus();
    }

    private void OnNoteSelected(object sender, SelectionChangedEventArgs e)
    {
        FlushPendingAutosave();
        SyncEditorWithSelection();
    }

    private void SyncEditorWithSelection()
    {
        _activeNote = TabsList.SelectedItem as Note;
        _suppressEditorEvents = true;
        if (_activeNote is null)
        {
            EditorBox.Text = string.Empty;
            EditorBox.IsEnabled = false;
            EmptyHint.Visibility = Visibility.Visible;
            ResetStatus();
        }
        else
        {
            EditorBox.Text = _activeNote.Body;
            EditorBox.IsEnabled = true;
            EmptyHint.Visibility = Visibility.Collapsed;
            EditorBox.CaretIndex = 0;
            RefreshStatus(_activeNote);
            UpdateCaretStatus();
            UpdateCharCount();
        }
        _suppressEditorEvents = false;
    }

    private void ResetStatus()
    {
        LnColText.Text = "Ln 1, Col 1";
        CharCountText.Text = "0 characters";
        StateDot.Fill = (Brush)Application.Current.Resources["MutedBrush"];
        StateText.Text = "Idle";
        StateText.ToolTip = null;
    }

    private void RefreshStatus(Note note)
    {
        StateDot.Fill = (Brush)(Application.Current.Resources[StateBrushKey(note.SyncState)] ?? Brushes.Gray);
        StateText.Text = StateLabel(note.SyncState);
        StateText.ToolTip = note.SyncState == SyncState.Error ? note.LastError : null;
        Title = string.IsNullOrWhiteSpace(note.Title) ? "Notion Pad" : $"{note.Title} — Notion Pad";
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
        UpdateCharCount();
        UpdateCaretStatus();
        if (_suppressEditorEvents || _activeNote is null) return;
        _autosaveTimer.Stop();
        _autosaveTimer.Start();
    }

    private void OnEditorSelectionChanged(object sender, RoutedEventArgs e)
    {
        UpdateCaretStatus();
    }

    private void UpdateCharCount()
    {
        CharCountText.Text = $"{EditorBox.Text.Length} character{(EditorBox.Text.Length == 1 ? string.Empty : "s")}";
    }

    private void UpdateCaretStatus()
    {
        var text = EditorBox.Text;
        var caret = EditorBox.CaretIndex;
        if (caret > text.Length) caret = text.Length;
        var line = 1;
        var lastBreak = -1;
        for (var i = 0; i < caret; i++)
        {
            if (text[i] != '\n') continue;
            line++;
            lastBreak = i;
        }
        var col = caret - lastBreak;
        LnColText.Text = $"Ln {line}, Col {col}";
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
        TabsList.SelectedItem = note;
    }

    private void OnCloseTab(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not string id) return;
        var note = _notes.FirstOrDefault(n => n.Id == id);
        if (note is null) return;
        CloseNote(note);
    }

    private void OnCloseActiveTab(object sender, RoutedEventArgs e)
    {
        if (_activeNote is null) return;
        CloseNote(_activeNote);
    }

    private async void CloseNote(Note note)
    {
        var confirm = MessageBox.Show(
            this,
            "Close this note? It will be deleted locally and archived in Notion.",
            "Close note",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK) return;

        _autosaveTimer.Stop();
        var wasActive = note == _activeNote;
        _notes.Remove(note);
        App.Store.Data.Notes.Remove(note);
        App.Store.Persist();
        if (wasActive) SyncEditorWithSelection();

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

    private void OnExit(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnToggleWordWrap(object sender, RoutedEventArgs e)
    {
        EditorBox.TextWrapping = WordWrapMenuItem.IsChecked ? TextWrapping.Wrap : TextWrapping.NoWrap;
        EditorBox.HorizontalScrollBarVisibility = WordWrapMenuItem.IsChecked
            ? ScrollBarVisibility.Disabled
            : ScrollBarVisibility.Auto;
    }

    private void OnZoomIn(object sender, RoutedEventArgs e) => OnZoomInGesture();
    private void OnZoomOut(object sender, RoutedEventArgs e) => OnZoomOutGesture();
    private void OnZoomReset(object sender, RoutedEventArgs e) => OnZoomResetGesture();

    private void OnZoomInGesture()
    {
        EditorBox.FontSize = Math.Min(MaxFontSize, EditorBox.FontSize + 1);
        UpdateZoomText();
    }

    private void OnZoomOutGesture()
    {
        EditorBox.FontSize = Math.Max(MinFontSize, EditorBox.FontSize - 1);
        UpdateZoomText();
    }

    private void OnZoomResetGesture()
    {
        EditorBox.FontSize = DefaultFontSize;
        UpdateZoomText();
    }

    private void UpdateZoomText()
    {
        var percent = (int)Math.Round(EditorBox.FontSize / DefaultFontSize * 100d);
        ZoomText.Text = $"{percent}%";
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        FlushPendingAutosave();
        App.Store.FlushNow();
        base.OnClosing(e);
    }
}

internal sealed class ActionCommand : ICommand
{
    private readonly Action _action;
    public ActionCommand(Action action) => _action = action;
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _action();
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }
}
