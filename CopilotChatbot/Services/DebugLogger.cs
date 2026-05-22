using System.IO;
using System.Text;

namespace CopilotChatbot.Services;

public sealed class DebugLogger
{
    private readonly object _lock = new();
    private readonly string _directory;

    public bool IsEnabled { get; set; }

    public string LogDirectory => _directory;

    public string CurrentLogPath => Path.Combine(_directory, $"debug-{DateTime.Now:yyyy-MM-dd}.log");

    public DebugLogger()
    {
        _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CopilotChatbot");
        Directory.CreateDirectory(_directory);
    }

    /// <summary>Logs a single-line entry.</summary>
    public void Log(string category, string? content)
    {
        if (!IsEnabled || string.IsNullOrEmpty(content))
            return;

        var indent = new string(' ', category.Length + 14); // align continuation lines
        var indented = content.Replace("\n", "\n" + indent);
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{category.ToUpperInvariant()}] {indented}";
        Write(line);
    }

    /// <summary>Logs a multi-line block with a header/footer separator.</summary>
    public void LogBlock(string category, string? content)
    {
        if (!IsEnabled || string.IsNullOrEmpty(content))
            return;

        var sb = new StringBuilder();
        sb.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] [{category.ToUpperInvariant()}] ---- begin ----");
        sb.AppendLine(content);
        sb.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] [{category.ToUpperInvariant()}] ---- end ----");
        Write(sb.ToString());
    }

    private void Write(string text)
    {
        lock (_lock)
        {
            try
            {
                File.AppendAllText(CurrentLogPath, text + Environment.NewLine);
            }
            catch
            {
                // Logging must never crash the app.
            }
        }
    }
}
