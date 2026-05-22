using System.IO;
using System.Windows;
using CopilotChatbot.Models;
using CopilotChatbot.Services;
using Microsoft.Win32;

namespace CopilotChatbot;

public partial class ResponseWindow : Window
{
    private readonly ChatMessage _message;
    private readonly string _html;

    public ResponseWindow(HtmlRenderer renderer, ChatMessage message)
    {
        InitializeComponent();
        _message = message;
        _html = renderer.RenderStandalone(message);
        Loaded += async (_, _) =>
        {
            await Browser.EnsureCoreWebView2Async();
            Browser.NavigateToString(_html);
        };
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(_message.Content);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "HTML (*.html)|*.html|Markdown/Text (*.md)|*.md|All files (*.*)|*.*",
            FileName = $"response-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.html"
        };

        if (dialog.ShowDialog(this) == true)
        {
            File.WriteAllText(dialog.FileName, Path.GetExtension(dialog.FileName).Equals(".html", StringComparison.OrdinalIgnoreCase) ? _html : _message.Content);
        }
    }
}
