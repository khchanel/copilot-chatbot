using System.Net;
using System.Text;
using CopilotChatbot.Models;
using Markdig;

namespace CopilotChatbot.Services;

public sealed class HtmlRenderer
{
    private readonly MarkdownPipeline _markdown = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public string RenderDocument(IEnumerable<ChatMessage> messages, bool darkTheme)
    {
        var messagesHtml = RenderBody(messages, darkTheme);

        var bg     = darkTheme ? "#111827" : "#FFFFFF";
        var card   = darkTheme ? "#161B22" : "#FFFFFF";
        var border = darkTheme ? "#30363D"  : "#D0D7DE";
        var text   = darkTheme ? "#E6EDF3"  : "#1F2328";
        var muted  = darkTheme ? "#8B949E"  : "#57606A";
        var btnBg  = darkTheme ? "#21262D"  : "#F6F8FA";

        return $$"""
<!doctype html>
<html>
<head>
<meta charset="utf-8">
<style>
*, *::before, *::after { box-sizing: border-box; }
html, body { margin:0; padding:0; background:{{bg}}; color:{{text}}; font-family:-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; }
main { padding:10px 14px; }
.msg { margin:0 0 7px 0; border:1px solid {{border}}; border-radius:8px; background:{{card}}; overflow:hidden; box-shadow:0 1px 2px rgba(0,0,0,.07); }
.head, details > summary.head { display:flex; align-items:center; gap:8px; padding:6px 11px; border-bottom:1px solid {{border}}; font-size:12px; color:{{muted}}; }
details > summary.head { cursor:pointer; user-select:none; list-style:none; border-bottom:0; }
details > summary.head::-webkit-details-marker { display:none; }
details[open] > summary.head { border-bottom:1px solid {{border}}; }
.xicon { font-size:9px; opacity:.5; transition:transform .14s; flex-shrink:0; }
details[open] .xicon { transform:rotate(90deg); }
.avatar { width:22px; height:22px; border-radius:50%; display:flex; align-items:center; justify-content:center; font-size:10px; flex-shrink:0; font-weight:800; }
.kind-label { font-weight:700; font-size:11px; letter-spacing:.05em; text-transform:uppercase; flex-shrink:0; }
.preview { font-size:11px; opacity:.7; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; flex:1; min-width:0; }
details[open] .preview { display:none; }
.ts { margin-left:auto; font-size:11px; opacity:.65; flex-shrink:0; }
details > summary.head .ts { margin-left:0; }
.open-btn { border:1px solid {{border}}; color:{{text}}; background:{{btnBg}}; border-radius:5px; padding:2px 8px; cursor:pointer; font:600 10px/1 inherit; margin-left:auto; }
details > summary.head .open-btn { margin-left:6px; }
.open-btn:hover { opacity:.75; }
.content { padding:0; }
.frame-body { margin:0; padding:8px 12px; color:{{text}}; background:{{card}}; font:14px/1.42 -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; overflow-wrap:anywhere; }
.frame-body h1,.frame-body h2,.frame-body h3,.frame-body h4,.frame-body h5,.frame-body h6 { margin:.7em 0 .35em; line-height:1.25; font-weight:700; }
.frame-body h1 { font-size:1.45em; } .frame-body h2 { font-size:1.22em; } .frame-body h3 { font-size:1.08em; } .frame-body h4 { font-size:1em; }
.frame-body p { margin:.25em 0 .55em; }
.frame-body pre { background:{{(darkTheme ? "#0D1117" : "#F6F8FA")}}; border:1px solid {{border}}; border-radius:8px; padding:10px 12px; overflow-x:auto; margin:.5em 0; }
.frame-body pre, .frame-body code { font-family:ui-monospace, 'Cascadia Code', Consolas, monospace; font-size:13px; color:{{text}}; }
.frame-body code:not(pre code) { background:{{(darkTheme ? "#0D1117" : "#F6F8FA")}}; border:1px solid {{border}}; border-radius:4px; padding:1px 5px; font-size:12.5px; }
.frame-body blockquote { border-left:3px solid {{border}}; margin:.5em 0; padding:3px 0 3px 12px; color:{{muted}}; }
.frame-body table { border-collapse:collapse; width:100%; margin:.5em 0; font-size:13px; }
.frame-body th { background:{{(darkTheme ? "#21262D" : "#F0F3F6")}}; font-weight:600; text-align:left; }
.frame-body td, .frame-body th { border:1px solid {{border}}; padding:6px 10px; }
.frame-body tr:nth-child(even) td { background:{{(darkTheme ? "#0D1117" : "#F8FAFC")}}; }
.frame-body a { color:{{(darkTheme ? "#58A6FF" : "#0969DA")}}; text-decoration:none; }
.frame-body a:hover { text-decoration:underline; }
.frame-body ul, .frame-body ol { padding-left:1.55em; margin:.25em 0 .55em; }
.frame-body li { margin:.18em 0; }
.frame-body hr { border:0; border-top:1px solid {{border}}; margin:.8em 0; }
.frame-body img { max-width:100%; border-radius:6px; }
.frame-body > :first-child { margin-top:0; }
.frame-body > :last-child { margin-bottom:0; }
.user .head, .user details > summary.head { background:{{(darkTheme ? "#0D2A4D" : "#EFF6FF")}}; }
.user .avatar { background:{{(darkTheme ? "#1B4F8A" : "#3B82F6")}}; color:#FFF; }
.user .kind-label { color:{{(darkTheme ? "#60A5FA" : "#1D4ED8")}}; }
.assistant .head, .assistant details > summary.head { background:{{(darkTheme ? "#0D2E1F" : "#F0FDF4")}}; }
.assistant .avatar { background:{{(darkTheme ? "#166534" : "#22C55E")}}; color:#FFF; }
.assistant .kind-label { color:{{(darkTheme ? "#4ADE80" : "#15803D")}}; }
.reasoning .head, .reasoning details > summary.head { background:{{(darkTheme ? "#2D1F00" : "#FFFBEB")}}; }
.reasoning .avatar { background:{{(darkTheme ? "#854D0E" : "#F59E0B")}}; color:#FFF; }
.reasoning .kind-label { color:{{(darkTheme ? "#FCD34D" : "#92400E")}}; }
.tool .head, .tool details > summary.head, .intent .head, .intent details > summary.head { background:{{(darkTheme ? "#1E1040" : "#F5F3FF")}}; }
.tool .avatar, .intent .avatar { background:{{(darkTheme ? "#6D28D9" : "#8B5CF6")}}; color:#FFF; }
.tool .kind-label, .intent .kind-label { color:{{(darkTheme ? "#A78BFA" : "#5B21B6")}}; }
.error .head, .error details > summary.head { background:{{(darkTheme ? "#2D0E0E" : "#FFF5F5")}}; }
.error .avatar { background:{{(darkTheme ? "#9B1C1C" : "#EF4444")}}; color:#FFF; }
.error .kind-label { color:{{(darkTheme ? "#FCA5A5" : "#991B1B")}}; }
.system .head, .system details > summary.head { background:{{(darkTheme ? "#1C2128" : "#F6F8FA")}}; }
.system .avatar { background:{{(darkTheme ? "#484F58" : "#6E7781")}}; color:#FFF; }
.system .kind-label { color:{{muted}}; }
</style>
</head>
<body><main>{{messagesHtml}}</main>
<script>
document.addEventListener('click', e => {
  const button = e.target.closest('[data-open-id]');
  if (!button) return;
  e.preventDefault();
  e.stopPropagation();
  chrome.webview.postMessage({ type: 'open', id: button.dataset.openId });
});
</script>
</body>
</html>
""";
    }

    public string RenderBody(IEnumerable<ChatMessage> messages, bool darkTheme)
    {
        var body = new StringBuilder();
        foreach (var message in messages)
            body.Append(RenderMessage(message, darkTheme));
        return body.ToString();
    }

    public string RenderStandalone(ChatMessage message) => RenderFrameSource(message, includeDocumentShell: true, darkTheme: false);

    private string RenderMessage(ChatMessage message, bool darkTheme)
    {
        var css = message.Kind.ToString().ToLowerInvariant();
        var time = WebUtility.HtmlEncode(message.CreatedAt.ToString("g"));
        var contentHtml = RenderInlineContent(message);
        var (avatar, kindLabel) = message.Kind switch
        {
            ChatMessageKind.User      => ("U",  "You"),
            ChatMessageKind.Assistant => ("C",  "Copilot"),
            ChatMessageKind.Reasoning => ("R",  "Reasoning"),
            ChatMessageKind.Tool      => ("⚙",  "Tool"),
            ChatMessageKind.Intent    => ("→",  "Intent"),
            ChatMessageKind.Error     => ("!",  "Error"),
            _                         => ("·",  message.Kind.ToString()),
        };
        var avatarHtml = WebUtility.HtmlEncode(avatar);
        var kindHtml   = WebUtility.HtmlEncode(kindLabel);
        var msgId      = message.Id;

        // Reasoning, Tool, Intent, System, Error are collapsed by default
        bool collapsible = message.Kind is not (ChatMessageKind.User or ChatMessageKind.Assistant);

        if (collapsible)
        {
            var preview = WebUtility.HtmlEncode(GetPreview(message.Content));
            return $$"""
<article class="msg {{css}}">
  <details>
    <summary class="head">
      <span class="xicon">▶</span>
      <div class="avatar">{{avatarHtml}}</div>
      <span class="kind-label">{{kindHtml}}</span>
      <span class="preview">{{preview}}</span>
      <span class="ts">{{time}}</span>
      <button class="open-btn" data-open-id="{{msgId}}">Open ↗</button>
    </summary>
    <div class="content"><div class="frame-body">{{contentHtml}}</div></div>
  </details>
</article>
""";
        }

        return $$"""
<article class="msg {{css}}">
  <div class="head">
    <div class="avatar">{{avatarHtml}}</div>
    <span class="kind-label">{{kindHtml}}</span>
    <span class="ts">{{time}}</span>
    <button class="open-btn" data-open-id="{{msgId}}">Open ↗</button>
  </div>
  <div class="content"><div class="frame-body">{{contentHtml}}</div></div>
</article>
""";
    }

    private string RenderInlineContent(ChatMessage message)
    {
        if (message.Kind is ChatMessageKind.User)
            return $"<p style=\"margin:0;white-space:pre-wrap;overflow-wrap:anywhere;word-break:break-word\">{WebUtility.HtmlEncode(message.Content)}</p>";
        if (message.Kind is ChatMessageKind.Intent or ChatMessageKind.Tool or ChatMessageKind.Error)
            return $"<pre style=\"white-space:pre-wrap;overflow-wrap:anywhere;word-break:break-word\">{WebUtility.HtmlEncode(message.Content)}</pre>";
        var html = Markdown.ToHtml(message.Content, _markdown);
        return InjectLiveHtmlBlocks(html);
    }

    // Replace fenced HTML code blocks with live iframes.
    private static string InjectLiveHtmlBlocks(string markdigOutput)
    {
        // Markdig renders ```html as: <pre><code class="language-html">...escaped html...</code></pre>
        return System.Text.RegularExpressions.Regex.Replace(
            markdigOutput,
            @"<pre><code class=""language-html"">([\s\S]*?)</code></pre>",
            m =>
            {
                var decoded = WebUtility.HtmlDecode(m.Groups[1].Value);
                var srcdoc = decoded
                    .Replace("&", "&amp;")
                    .Replace("\"", "&quot;");
                return $"""
<div style="margin:.5em 0">
  <iframe srcdoc="{srcdoc}" style="width:100%;border:1px solid #d0d7de;border-radius:6px;min-height:220px;resize:vertical;overflow:auto" sandbox="allow-scripts allow-same-origin"></iframe>
</div>
""";
            });
    }

    private static string GetPreview(string content, int maxLen = 120)
    {
        var clean = content.Replace('\r', ' ').Replace('\n', ' ');
        while (clean.Contains("  ")) clean = clean.Replace("  ", " ");
        clean = clean.Trim();
        return clean.Length <= maxLen ? clean : clean[..maxLen].TrimEnd() + "…";
    }

    private string RenderFrameSource(ChatMessage message, bool includeDocumentShell, bool darkTheme)
    {
        var inner = message.Kind is ChatMessageKind.User
            ? $"<p style=\"margin:0;white-space:pre-wrap;overflow-wrap:anywhere;word-break:break-word\">{WebUtility.HtmlEncode(message.Content)}</p>"
            : message.Kind is ChatMessageKind.Intent or ChatMessageKind.Tool or ChatMessageKind.Error
                ? $"<pre style=\"white-space:pre-wrap;overflow-wrap:anywhere;word-break:break-word\">{WebUtility.HtmlEncode(message.Content)}</pre>"
                : InjectLiveHtmlBlocks(Markdown.ToHtml(message.Content, _markdown));

        var background = darkTheme ? "#161B22" : "#FFFFFF";
        var foreground = darkTheme ? "#E6EDF3" : "#1F2328";
        var border     = darkTheme ? "#30363D"  : "#D0D7DE";
        var muted      = darkTheme ? "#8B949E"  : "#57606A";
        var link       = darkTheme ? "#58A6FF"  : "#0969DA";
        var codeBg     = darkTheme ? "#0D1117"  : "#F6F8FA";
        var codeText   = darkTheme ? "#E6EDF3"  : "#24292F";
        var tableHead  = darkTheme ? "#21262D"  : "#F0F3F6";
        var tableAlt   = darkTheme ? "#0D1117"  : "#F8FAFC";

        var css = $$"""
<style>
*, *::before, *::after { box-sizing: border-box; }
body { margin:0; padding:9px 12px; color:{{foreground}}; background:{{background}}; font:14px/1.45 -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; overflow-wrap:anywhere; }
h1,h2,h3,h4,h5,h6 { margin:.8em 0 .4em; line-height:1.3; font-weight:700; }
h1 { font-size:1.5em; } h2 { font-size:1.25em; } h3 { font-size:1.1em; } h4 { font-size:1em; }
p { margin:.35em 0 .75em; }
pre { background:{{codeBg}}; border:1px solid {{border}}; border-radius:8px; padding:13px 16px; overflow-x:auto; margin:.7em 0; }
pre, code { font-family:ui-monospace, 'Cascadia Code', Consolas, monospace; font-size:13px; color:{{codeText}}; }
code:not(pre code) { background:{{codeBg}}; border:1px solid {{border}}; border-radius:4px; padding:1px 5px; font-size:12.5px; }
blockquote { border-left:3px solid {{border}}; margin:.6em 0; padding:4px 0 4px 14px; color:{{muted}}; }
table { border-collapse:collapse; width:100%; margin:.7em 0; font-size:13px; }
th { background:{{tableHead}}; font-weight:600; text-align:left; }
td, th { border:1px solid {{border}}; padding:7px 12px; }
tr:nth-child(even) td { background:{{tableAlt}}; }
a { color:{{link}}; text-decoration:none; } a:hover { text-decoration:underline; }
ul, ol { padding-left:1.7em; margin:.35em 0 .75em; }
li { margin:.25em 0; }
hr { border:0; border-top:1px solid {{border}}; margin:1em 0; }
img { max-width:100%; border-radius:6px; }
</style>
""";

        return includeDocumentShell
            ? $"<!doctype html><html><head><meta charset=\"utf-8\">{css}</head><body>{inner}</body></html>"
            : $"{css}{inner}";
    }
}
