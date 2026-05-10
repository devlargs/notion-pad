using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace NotionPad.Views;

public partial class SettingsWindow : Window
{
    private readonly bool _required;

    public SettingsWindow(bool required)
    {
        InitializeComponent();
        _required = required;
        TokenBox.Password = App.Store.Data.Settings.NotionToken ?? string.Empty;
        DatabaseBox.Text = App.Store.Data.Settings.DatabaseId ?? string.Empty;
        CancelButton.IsEnabled = !required;
        CancelButton.Visibility = required ? Visibility.Collapsed : Visibility.Visible;
        UpdateButtons();
    }

    private void OnFieldChanged(object sender, RoutedEventArgs e)
    {
        TestResult.Text = string.Empty;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var ready = TokenBox.Password.Trim().Length > 0 && DatabaseBox.Text.Trim().Length > 0;
        TestButton.IsEnabled = ready;
        SaveButton.IsEnabled = ready;
    }

    private async void OnTest(object sender, RoutedEventArgs e)
    {
        PersistFields();
        TestResult.Text = "Testing…";
        TestResult.Foreground = (Brush)Application.Current.Resources["MutedBrush"];
        TestButton.IsEnabled = false;
        var (ok, error) = await App.Notion.TestConnectionAsync();
        TestButton.IsEnabled = true;
        if (ok)
        {
            TestResult.Text = "✓ Connected";
            TestResult.Foreground = (Brush)Application.Current.Resources["OkBrush"];
            return;
        }
        TestResult.Text = error ?? "Failed";
        TestResult.Foreground = (Brush)Application.Current.Resources["ErrorBrush"];
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        PersistFields();
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void PersistFields()
    {
        App.Store.Data.Settings.NotionToken = TokenBox.Password.Trim();
        App.Store.Data.Settings.DatabaseId = DatabaseBox.Text.Trim();
        App.Store.Persist();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_required && DialogResult != true && !App.Store.Data.Settings.IsConfigured)
        {
            e.Cancel = true;
            return;
        }
        base.OnClosing(e);
    }
}
