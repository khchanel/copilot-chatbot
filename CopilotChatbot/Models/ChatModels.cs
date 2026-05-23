using System.Collections.ObjectModel;
using Microsoft.Web.WebView2.Wpf;

namespace CopilotChatbot.Models;

public enum ChatMessageKind
{
    User,
    Assistant,
    Reasoning,
    Intent,
    Tool,
    Error,
    System
}

public sealed class ChatMessage
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public ChatMessageKind Kind { get; init; }
    public string Content { get; set; } = "";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
}

public sealed record McpServerInfo(string Name, string Status, IReadOnlyList<string> Tools);
public sealed record AgentInfo(string Name, string Status, string Source);
public sealed record SkillInfo(string Name, string? Description);
public sealed record SessionCapabilitiesSnapshot(
    IReadOnlyList<McpServerInfo> McpServers,
    IReadOnlyList<AgentInfo> Agents,
    IReadOnlyList<SkillInfo> Skills);

public sealed class ChatSessionView
{
    public string Title { get; set; }
    public string? CopilotSessionId { get; set; }
    public bool IsPageInitialized { get; set; }
    public bool IsPending { get; set; }
    public string? SystemPrompt { get; set; }
    public string? LastStatus { get; set; }
    public ObservableCollection<ChatMessage> Messages { get; } = [];
    public WebView2 Browser { get; } = new();

    public ChatSessionView(string title)
    {
        Title = title;
    }
}
