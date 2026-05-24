using System.IO;
using System.Text.Json;
using CopilotChatbot.Models;

namespace CopilotChatbot.Services;

public sealed class ChatSessionStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _statePath;

    public ChatSessionStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CopilotChatbot");
        Directory.CreateDirectory(directory);
        _statePath = Path.Combine(directory, "chat-sessions.json");
    }

    public bool Exists => File.Exists(_statePath);

    public PersistedChatState Load()
    {
        if (!File.Exists(_statePath))
        {
            return new PersistedChatState();
        }

        try
        {
            var json = File.ReadAllText(_statePath);
            return JsonSerializer.Deserialize<PersistedChatState>(json, Options) ?? new PersistedChatState();
        }
        catch
        {
            return new PersistedChatState();
        }
    }

    public void Save(PersistedChatState state)
    {
        File.WriteAllText(_statePath, JsonSerializer.Serialize(state, Options));
    }
}
