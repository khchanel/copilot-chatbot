using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using CopilotChatbot.Models;
using CopilotChatbot.Services;
using Microsoft.Win32;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;

namespace CopilotChatbot;

public partial class SettingsWindow : Window
{
    private readonly SettingsStore _store;
    private readonly DebugLogger _debugLogger;
    public AppSettings Settings { get; }

    public SettingsWindow(SettingsStore store, AppSettings settings, DebugLogger debugLogger)
    {
        InitializeComponent();
        _store = store;
        _debugLogger = debugLogger;
        Settings = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings)) ?? new AppSettings();
        GitHubTokenBox.Password = Settings.GitHubToken ?? "";
        SecretsGrid.ItemsSource = Settings.UserSecrets;
        FoldersGrid.ItemsSource = Settings.Permissions.Folders;
        ToolsList.ItemsSource = Settings.Permissions.AllowedTools;
        HostsList.ItemsSource = Settings.Permissions.AllowedHosts;
        SavedRulesGrid.ItemsSource = Settings.Permissions.SavedRules;
        AllowMcpCheckBox.IsChecked = Settings.Permissions.AllowMcpByDefault;
        AllowCustomToolsCheckBox.IsChecked = Settings.Permissions.AllowCustomToolsByDefault;
        SystemPromptBox.Text = Settings.DefaultSystemPrompt ?? "";
        EnableDebugLoggingCheckBox.IsChecked = Settings.EnableDebugLogging;
        WorkingDirectoryBox.Text = Settings.WorkingDirectory ?? "";
        LogPathTextBlock.Text = _debugLogger.CurrentLogPath;
    }

    private void AddSecret_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SecretEnvBox.Text))
        {
            return;
        }

        Settings.UserSecrets.Add(new UserSecretSetting
        {
            Name = SecretNameBox.Text.Trim(),
            EnvironmentVariable = SecretEnvBox.Text.Trim(),
            EncryptedValue = _store.ProtectSecret(SecretValueBox.Password)
        });
        SecretNameBox.Clear();
        SecretEnvBox.Clear();
        SecretValueBox.Clear();
    }

    private void RemoveSecret_Click(object sender, RoutedEventArgs e)
    {
        if (SecretsGrid.SelectedItem is UserSecretSetting secret)
        {
            Settings.UserSecrets.Remove(secret);
        }
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(FolderPathBox.Text))
        {
            Settings.Permissions.Folders.Add(new FolderPermission { Path = FolderPathBox.Text.Trim(), CanWrite = FolderWriteBox.IsChecked == true });
            FolderPathBox.Clear();
            FolderWriteBox.IsChecked = false;
        }
    }

    private void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (FoldersGrid.SelectedItem is FolderPermission folder)
        {
            Settings.Permissions.Folders.Remove(folder);
        }
    }

    private void AddTool_Click(object sender, RoutedEventArgs e)
    {
        AddUnique(Settings.Permissions.AllowedTools, ToolBox.Text);
        ToolBox.Clear();
    }

    private void AddHost_Click(object sender, RoutedEventArgs e)
    {
        AddUnique(Settings.Permissions.AllowedHosts, HostBox.Text);
        HostBox.Clear();
    }

    private void RemoveSavedRule_Click(object sender, RoutedEventArgs e)
    {
        if (SavedRulesGrid.SelectedItem is PermissionRule rule)
        {
            Settings.Permissions.SavedRules.Remove(rule);
        }
    }

    private static void AddUnique(ICollection<string> list, string value)
    {
        value = value.Trim();
        if (!string.IsNullOrWhiteSpace(value) && !list.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(value);
        }
    }

    private void RevealSecretButton_Click(object sender, RoutedEventArgs e)
    {
        if (SecretValueTextBox.Visibility == Visibility.Collapsed)
        {
            SecretValueTextBox.Text = SecretValueBox.Password;
            SecretValueBox.Visibility = Visibility.Collapsed;
            SecretValueTextBox.Visibility = Visibility.Visible;
            RevealSecretIcon.Symbol = SymbolRegular.EyeOff20;
        }
        else
        {
            SecretValueBox.Password = SecretValueTextBox.Text;
            SecretValueTextBox.Visibility = Visibility.Collapsed;
            SecretValueBox.Visibility = Visibility.Visible;
            RevealSecretIcon.Symbol = SymbolRegular.Eye20;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Settings.GitHubToken = GitHubTokenBox.Password;
        Settings.WorkingDirectory = string.IsNullOrWhiteSpace(WorkingDirectoryBox.Text) ? null : WorkingDirectoryBox.Text.Trim();
        Settings.Permissions.AllowMcpByDefault = AllowMcpCheckBox.IsChecked == true;
        Settings.Permissions.AllowCustomToolsByDefault = AllowCustomToolsCheckBox.IsChecked == true;
        Settings.DefaultSystemPrompt = string.IsNullOrWhiteSpace(SystemPromptBox.Text) ? null : SystemPromptBox.Text;
        Settings.EnableDebugLogging = EnableDebugLoggingCheckBox.IsChecked == true;
        DialogResult = true;
    }

    private void BrowseWorkingDir_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Working Directory",
            InitialDirectory = string.IsNullOrWhiteSpace(WorkingDirectoryBox.Text)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : WorkingDirectoryBox.Text
        };
        if (dialog.ShowDialog() == true)
            WorkingDirectoryBox.Text = dialog.FolderName;
    }

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start("explorer.exe", _debugLogger.LogDirectory);
        }
        catch
        {
            // Best-effort; ignore if explorer can't open.
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
