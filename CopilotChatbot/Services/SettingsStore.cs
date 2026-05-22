using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CopilotChatbot.Models;

namespace CopilotChatbot.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _settingsPath;

    public SettingsStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CopilotChatbot");
        Directory.CreateDirectory(directory);
        _settingsPath = Path.Combine(directory, "settings.json");
    }

    public AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return new AppSettings();
        }

        var json = File.ReadAllText(_settingsPath);
        return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, Options));
    }

    public string ProtectSecret(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    public string UnprotectSecret(string encryptedValue)
    {
        if (string.IsNullOrWhiteSpace(encryptedValue))
        {
            return "";
        }

        var bytes = Convert.FromBase64String(encryptedValue);
        var clearBytes = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(clearBytes);
    }
}
