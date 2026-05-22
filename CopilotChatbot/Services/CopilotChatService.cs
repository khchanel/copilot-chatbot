using System.Collections.Concurrent;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using CopilotChatbot.Models;
using GitHub.Copilot.SDK;

namespace CopilotChatbot.Services;

public sealed class CopilotChatService : IAsyncDisposable
{
    private readonly SettingsStore _settingsStore;
    private readonly Func<PermissionPrompt, Task<PermissionPromptDecision>> _permissionPrompt;
    private readonly Func<UserInputPrompt, Task<UserInputPromptResult>> _userInputPrompt;
    private readonly DebugLogger _logger;
    private CopilotClient? _client;
    private readonly ConcurrentDictionary<ChatSessionView, CopilotSession> _sessions = [];
    private readonly ConcurrentDictionary<string, byte> _sessionPermissionApprovals = [];
    public event Action<CopilotUsageStatus>? UsageUpdated;
    public event Action<ChatSessionView, bool>? SessionPendingChanged;

    public CopilotChatService(
        SettingsStore settingsStore,
        Func<PermissionPrompt, Task<PermissionPromptDecision>> permissionPrompt,
        Func<UserInputPrompt, Task<UserInputPromptResult>> userInputPrompt,
        DebugLogger logger)
    {
        _settingsStore = settingsStore;
        _permissionPrompt = permissionPrompt;
        _userInputPrompt = userInputPrompt;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ModelChoice>> ListModelsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var client = await EnsureClientAsync(settings, cancellationToken);
        var models = await client.ListModelsAsync(cancellationToken);
        return models
            .OrderBy(model => model.Name ?? model.Id)
            .Select(model => new ModelChoice
            {
                Id = model.Id ?? "",
                Name = model.Name ?? "",
                SupportsReasoningEffort = model.Capabilities?.Supports?.ReasoningEffort == true,
                ReasoningEfforts = model.SupportedReasoningEfforts?.ToArray() ?? [],
                DefaultReasoningEffort = model.DefaultReasoningEffort,
                BillingMultiplier = model.Billing?.Multiplier
            })
            .Where(model => !string.IsNullOrWhiteSpace(model.Id))
            .ToArray();
    }

    public async Task<CopilotRuntimeStatus> GetRuntimeStatusAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var client = await EnsureClientAsync(settings, cancellationToken);
        var status = await client.GetStatusAsync(cancellationToken);
        var auth = await client.GetAuthStatusAsync(cancellationToken);

        return new CopilotRuntimeStatus(
            client.State == ConnectionState.Connected,
            status.Version ?? "unknown",
            status.ProtocolVersion,
            auth.IsAuthenticated,
            auth.Login ?? "",
            auth.AuthType ?? "",
            auth.StatusMessage ?? "");
    }

    public async Task SendAsync(ChatSessionView chat, string prompt, AppSettings settings, ModelChoice? model, string? reasoningEffort)
    {
        var session = await EnsureSessionAsync(chat, settings, model, reasoningEffort);
        _logger.LogBlock("USER-SEND", prompt);
        SetPending(chat, true);
        try
        {
            await session.SendAsync(new MessageOptions { Prompt = prompt });
        }
        catch
        {
            SetPending(chat, false);
            throw;
        }
    }

    public async Task AbortAsync(ChatSessionView chat)
    {
        if (_sessions.TryGetValue(chat, out var session))
        {
            _logger.Log("ABORT", $"User aborted session {chat.CopilotSessionId}");
            await session.AbortAsync();
        }

        SetPending(chat, false);
    }

    public async Task CloseSessionAsync(ChatSessionView chat)
    {
        if (_sessions.TryRemove(chat, out var session))
        {
            try
            {
                await session.DisposeAsync();
            }
            catch
            {
                // Closing a tab should not fail if the CLI already dropped the session.
            }
        }
    }

    private async Task<CopilotClient> EnsureClientAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        if (_client is not null)
        {
            return _client;
        }

        var token = await ResolveGitHubTokenAsync(settings.GitHubToken);
        var env = CreateProcessEnvironment();
        foreach (var secret in settings.UserSecrets.Where(s => !string.IsNullOrWhiteSpace(s.EnvironmentVariable)))
        {
            env[secret.EnvironmentVariable] = _settingsStore.UnprotectSecret(secret.EncryptedValue);
        }

        // Inject the GitHub token into the child process environment so the builtin
        // github-mcp-server (and any gh-based tools) can authenticate to the GitHub API.
        // The Copilot CLI's GitHubToken option only authenticates the chat API; MCP servers
        // spawned as subprocesses rely on GH_TOKEN / GITHUB_TOKEN environment variables.
        if (!string.IsNullOrWhiteSpace(token))
        {
            env["GH_TOKEN"] = token;
            env["GITHUB_TOKEN"] = token;
        }

        var bundledCliPath = ResolveBundledCliPath();
        var cwd = !string.IsNullOrWhiteSpace(settings.WorkingDirectory) && Directory.Exists(settings.WorkingDirectory)
            ? settings.WorkingDirectory
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var client = new CopilotClient(new CopilotClientOptions
        {
            CliPath = bundledCliPath,
            GitHubToken = token,
            Environment = env,
            Cwd = cwd
        });

        try
        {
            await client.StartAsync(cancellationToken);
            _client = client;
            await LoadUserMcpConfigAsync(client, cancellationToken);
            return _client;
        }
        catch
        {
            await DisposeClientQuietlyAsync(client);
            throw;
        }
    }

    // Reads ~/.copilot/mcp-config.json and registers each server with the SDK.
    // Format: { "mcpServers": { "name": { "command": "...", "args": [...], "env": {...} } } }
    // HTTP servers: { "name": { "url": "https://...", "headers": {...} } }
    private async Task LoadUserMcpConfigAsync(CopilotClient client, CancellationToken cancellationToken)
    {
        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".copilot", "mcp-config.json");

        if (!File.Exists(configPath))
            return;

        try
        {
            var json = await File.ReadAllTextAsync(configPath, cancellationToken);
            var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("mcpServers", out var servers))
                return;

            foreach (var server in servers.EnumerateObject())
            {
                var name = server.Name;
                var cfg = server.Value;
                try
                {
                    object config;
                    if (cfg.TryGetProperty("url", out var urlProp))
                    {
                        var httpCfg = new McpHttpServerConfig { Url = urlProp.GetString() ?? "" };
                        if (cfg.TryGetProperty("headers", out var headersProp))
                            httpCfg.Headers = headersProp.EnumerateObject()
                                .ToDictionary(h => h.Name, h => h.Value.GetString() ?? "");
                        config = httpCfg;
                    }
                    else
                    {
                        var stdio = new McpStdioServerConfig
                        {
                            Command = cfg.TryGetProperty("command", out var cmd) ? cmd.GetString() ?? "" : "",
                        };
                        if (cfg.TryGetProperty("args", out var argsProp))
                            stdio.Args = argsProp.EnumerateArray().Select(a => a.GetString() ?? "").ToList();
                        if (cfg.TryGetProperty("env", out var envProp))
                            stdio.Env = envProp.EnumerateObject()
                                .ToDictionary(e => e.Name, e => e.Value.GetString() ?? "");
                        if (cfg.TryGetProperty("cwd", out var cwdProp))
                            stdio.Cwd = cwdProp.GetString();
                        config = stdio;
                    }

                    await UpsertMcpServerAsync(client, name, config, cancellationToken);
                    _logger.Log("MCP-CONFIG", $"Registered MCP server '{name}' from {configPath}");
                }
                catch (Exception ex)
                {
                    _logger.Log("MCP-CONFIG-ERROR", $"Failed to register MCP server '{name}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Log("MCP-CONFIG-ERROR", $"Failed to load {configPath}: {ex.Message}");
        }
    }

    private async Task UpsertMcpServerAsync(CopilotClient client, string name, object config, CancellationToken cancellationToken)
    {
        try
        {
            await client.Rpc.Mcp.Config.AddAsync(name, config, cancellationToken);
        }
        catch (Exception ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            await client.Rpc.Mcp.Config.UpdateAsync(name, config, cancellationToken);
        }
    }

    private async Task<CopilotSession> EnsureSessionAsync(ChatSessionView chat, AppSettings settings, ModelChoice? model, string? reasoningEffort)
    {
        if (_sessions.TryGetValue(chat, out var existing))
        {
            return existing;
        }

        var client = await EnsureClientAsync(settings);
        var token = await ResolveGitHubTokenAsync(settings.GitHubToken);
        var session = await client.CreateSessionAsync(new SessionConfig
        {
            Model = model?.Id ?? settings.SelectedModelId,
            ReasoningEffort = string.IsNullOrWhiteSpace(reasoningEffort) ? null : reasoningEffort,
            Streaming = true,
            GitHubToken = token,
            SystemMessage = string.IsNullOrWhiteSpace(chat.SystemPrompt) ? null : new SystemMessageConfig { Content = chat.SystemPrompt },
            AvailableTools = settings.Permissions.AllowedTools.Count == 0 ? null : settings.Permissions.AllowedTools.ToArray(),
            OnPermissionRequest = async (request, _) => await EvaluatePermissionAsync(request, settings),
            OnUserInputRequest = async (request, _) =>
            {
                try { return await EvaluateUserInputAsync(chat, request); }
                catch (Exception ex)
                {
                    _logger.Log("ASK-USER-FATAL", ex.ToString());
                    return new UserInputResponse { Answer = "", WasFreeform = true };
                }
            }
        });

        chat.CopilotSessionId = session.SessionId;
        _logger.Log("SESSION", $"Created session {session.SessionId} | model={model?.Id ?? settings.SelectedModelId} | reasoning={reasoningEffort ?? "default"}");
        session.On(evt => HandleEvent(chat, evt));
        _sessions[chat] = session;
        return session;
    }

    private async Task<UserInputResponse> EvaluateUserInputAsync(ChatSessionView chat, UserInputRequest request)
    {
        var prompt = new UserInputPrompt(
            request.Question ?? "",
            request.Choices?.ToArray() ?? [],
            request.AllowFreeform != false);

        _logger.Log("ASK-USER-REQUEST", $"Thread={System.Threading.Thread.CurrentThread.IsThreadPoolThread} IsBackground={System.Threading.Thread.CurrentThread.IsBackground} ManagedId={System.Threading.Thread.CurrentThread.ManagedThreadId} | Question: {prompt.Question} | Choices: [{string.Join(", ", prompt.Choices)}] | AllowFreeform: {prompt.AllowFreeform}");

        AddOrUpdate(
            chat,
            ChatMessageKind.System,
            BuildUserInputPromptMessage(prompt),
            $"ask-user-{Guid.NewGuid():N}");

        try
        {
            var response = await _userInputPrompt(prompt);
            _logger.Log("ASK-USER-RESPONSE", $"Answer: {(string.IsNullOrWhiteSpace(response.Answer) ? "(empty/cancelled)" : response.Answer)} | WasFreeform: {response.WasFreeform}");

            if (!string.IsNullOrWhiteSpace(response.Answer))
            {
                AddOrUpdate(
                    chat,
                    ChatMessageKind.User,
                    response.Answer,
                    $"ask-user-answer-{Guid.NewGuid():N}");
            }

            return new UserInputResponse
            {
                Answer = response.Answer ?? "",
                WasFreeform = response.WasFreeform
            };
        }
        catch (Exception ex)
        {
            _logger.Log("ASK-USER-ERROR", ex.ToString());
            return new UserInputResponse { Answer = "", WasFreeform = true };
        }
    }

    private async Task<PermissionRequestResult> EvaluatePermissionAsync(PermissionRequest request, AppSettings settings)
    {
        var prompt = ToPermissionPrompt(request);
        var key = BuildPermissionKey(prompt);

        if (IsAllowedBySettings(request, settings) || _sessionPermissionApprovals.ContainsKey(key))
        {
            _logger.Log("PERMISSION-AUTO", $"Kind={prompt.Kind} Tool={prompt.ToolName} File={prompt.FileName} Host={prompt.Host} | auto-approved");
            return new PermissionRequestResult { Kind = PermissionRequestResultKind.Approved };
        }

        _logger.Log("PERMISSION-REQUEST", $"Kind={prompt.Kind} Tool={prompt.ToolName} File={prompt.FileName} Host={prompt.Host} Command={prompt.Command}");
        var decision = await _permissionPrompt(prompt);
        _logger.Log("PERMISSION-DECISION", $"Kind={prompt.Kind} Tool={prompt.ToolName} | Decision={decision}");

        if (decision == PermissionPromptDecision.AllowForSession)
        {
            _sessionPermissionApprovals.TryAdd(key, 0);
        }
        else if (decision == PermissionPromptDecision.SaveToSettings)
        {
            SavePermissionApproval(prompt, settings);
            _settingsStore.Save(settings);
        }

        return new PermissionRequestResult
        {
            Kind = decision != PermissionPromptDecision.Deny
                ? PermissionRequestResultKind.Approved
                : PermissionRequestResultKind.Rejected
        };
    }

    private static readonly HashSet<string> _implicitlyApprovedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        // GitHub API hosts used by the builtin github-mcp-server.
        // These are implicitly trusted when the user is already authenticated via GitHub token.
        "api.github.com",
        "github.com",
        "uploads.github.com",
        "objects.githubusercontent.com",
    };

    private static bool IsAllowedBySettings(PermissionRequest request, AppSettings settings)
    {
        var kind = GetString(request, "Kind") ?? "";
        var tool = GetString(request, "ToolName") ?? "";
        var file = GetString(request, "FileName") ?? GetString(request, "Path") ?? "";
        var host = TryGetHost(GetString(request, "Host")) ?? TryGetHost(GetString(request, "Url")) ?? "";
        var command = GetString(request, "FullCommandText") ?? "";

        if (settings.Permissions.SavedRules.Any(rule => RuleMatches(rule, kind, tool, file, command, host)))
            return true;

        // Read-only file access is allowed by default
        if (kind.Equals("read", StringComparison.OrdinalIgnoreCase))
            return true;

        // MCP and custom tools may be allowed globally via settings
        if (kind.Equals("mcp", StringComparison.OrdinalIgnoreCase) && settings.Permissions.AllowMcpByDefault)
            return true;
        if (kind.Equals("custom_tool", StringComparison.OrdinalIgnoreCase) && settings.Permissions.AllowCustomToolsByDefault)
            return true;

        if ((kind.Equals("custom_tool", StringComparison.OrdinalIgnoreCase) ||
             kind.Equals("mcp", StringComparison.OrdinalIgnoreCase)) &&
            settings.Permissions.AllowedTools.Contains(tool, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (kind.Equals("url", StringComparison.OrdinalIgnoreCase))
        {
            // Well-known GitHub API hosts are implicitly approved — the builtin github-mcp-server
            // always calls these, and the user is already authenticated via GitHub token.
            if (_implicitlyApprovedHosts.Contains(host))
                return true;

            if (settings.Permissions.AllowedHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
                return true;
        }

        if (kind.Equals("write", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(file))
        {
            var full = Path.GetFullPath(file);
            return settings.Permissions.Folders.Any(rule =>
            {
                if (string.IsNullOrWhiteSpace(rule.Path))
                    return false;
                var root = Path.GetFullPath(rule.Path);
                var underRoot = full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                                full.Equals(root, StringComparison.OrdinalIgnoreCase);
                return underRoot && rule.CanWrite;
            });
        }

        return false;
    }

    private static PermissionPrompt ToPermissionPrompt(PermissionRequest request)
    {
        var url = GetString(request, "Url");
        return new PermissionPrompt(
            GetString(request, "Kind") ?? "unknown",
            GetString(request, "ToolName"),
            GetString(request, "FileName") ?? GetString(request, "Path"),
            GetString(request, "FullCommandText"),
            TryGetHost(GetString(request, "Host")) ?? TryGetHost(url));
    }

    private static string BuildUserInputPromptMessage(UserInputPrompt prompt)
    {
        var choices = prompt.Choices.Count == 0
            ? ""
            : "\n\nSuggested choices:\n" + string.Join("\n", prompt.Choices.Select(choice => "- " + choice));
        var freeform = prompt.AllowFreeform ? "\n\nFreeform answer is allowed." : "";
        return "Copilot asked for input:\n\n" + prompt.Question + choices + freeform;
    }

    private static void SavePermissionApproval(PermissionPrompt prompt, AppSettings settings)
    {
        if (prompt.Kind.Equals("url", StringComparison.OrdinalIgnoreCase) &&
            AddUnique(settings.Permissions.AllowedHosts, prompt.Host))
        {
            return;
        }

        if ((prompt.Kind.Equals("custom_tool", StringComparison.OrdinalIgnoreCase) ||
             prompt.Kind.Equals("mcp", StringComparison.OrdinalIgnoreCase)) &&
            AddUnique(settings.Permissions.AllowedTools, prompt.ToolName))
        {
            return;
        }

        if (prompt.Kind.Equals("write", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(prompt.FileName))
        {
            var folder = GetPermissionFolder(prompt.FileName);
            if (!settings.Permissions.Folders.Any(rule =>
                    rule.CanWrite &&
                    NormalizePath(rule.Path).Equals(NormalizePath(folder), StringComparison.OrdinalIgnoreCase)))
            {
                settings.Permissions.Folders.Add(new FolderPermission { Path = folder, CanWrite = true });
            }
            return;
        }

        var rule = new PermissionRule
        {
            Kind = prompt.Kind,
            ToolName = prompt.ToolName ?? "",
            FileName = prompt.FileName ?? "",
            Command = prompt.Command ?? "",
            Host = prompt.Host ?? ""
        };

        if (!settings.Permissions.SavedRules.Any(existing =>
                RuleMatches(existing, rule.Kind, rule.ToolName, rule.FileName, rule.Command, rule.Host)))
        {
            settings.Permissions.SavedRules.Add(rule);
        }
    }

    private static bool RuleMatches(PermissionRule rule, string kind, string tool, string file, string command, string host)
    {
        if (!rule.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase))
            return false;

        return ValueMatches(rule.ToolName, tool) &&
               PathMatches(rule.FileName, file) &&
               ValueMatches(rule.Command, command) &&
               ValueMatches(rule.Host, host);
    }

    private static bool ValueMatches(string ruleValue, string requestedValue)
        => string.IsNullOrWhiteSpace(ruleValue) ||
           ruleValue.Equals(requestedValue, StringComparison.OrdinalIgnoreCase);

    private static bool PathMatches(string rulePath, string requestedPath)
    {
        if (string.IsNullOrWhiteSpace(rulePath))
            return true;
        if (string.IsNullOrWhiteSpace(requestedPath))
            return false;

        return NormalizePath(rulePath).Equals(NormalizePath(requestedPath), StringComparison.OrdinalIgnoreCase);
    }

    private static bool AddUnique(ICollection<string> collection, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || collection.Contains(value, StringComparer.OrdinalIgnoreCase))
            return false;

        collection.Add(value.Trim());
        return true;
    }

    private static string GetPermissionFolder(string fileName)
    {
        var full = NormalizePath(fileName);
        if (Directory.Exists(full))
            return full;

        return Path.GetDirectoryName(full) ?? full;
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim();
        }
    }

    private static string BuildPermissionKey(PermissionPrompt prompt)
        => string.Join("|", new[]
        {
            NormalizeKey(prompt.Kind),
            NormalizeKey(prompt.ToolName),
            NormalizeKey(prompt.FileName),
            NormalizeKey(prompt.Command),
            NormalizeKey(prompt.Host)
        });

    private static string NormalizeKey(string? value)
        => string.IsNullOrWhiteSpace(value) ? "" : value.Trim().ToUpperInvariant();

    private void HandleEvent(ChatSessionView chat, SessionEvent evt)
    {
        switch (evt)
        {
            case AssistantIntentEvent intent:
                _logger.Log("INTENT", intent.Data.Intent);
                AddOrUpdate(chat, ChatMessageKind.Intent, intent.Data.Intent, "intent");
                break;
            case AssistantReasoningDeltaEvent delta:
                // Deltas are too noisy to log — only log the complete reasoning block below.
                AddOrUpdate(chat, ChatMessageKind.Reasoning, delta.Data.DeltaContent, $"reason-{delta.Data.ReasoningId}", append: true);
                break;
            case AssistantReasoningEvent reasoning:
                _logger.LogBlock("REASONING", reasoning.Data.Content);
                AddOrUpdate(chat, ChatMessageKind.Reasoning, reasoning.Data.Content, $"reason-{reasoning.Data.ReasoningId}");
                break;
            case AssistantMessageDeltaEvent delta:
                AddOrUpdate(chat, ChatMessageKind.Assistant, delta.Data.DeltaContent, $"msg-{delta.Data.MessageId}", append: true);
                break;
            case AssistantMessageEvent message:
                _logger.LogBlock("ASSISTANT", message.Data.Content);
                if (!string.IsNullOrWhiteSpace(message.Data.ReasoningText))
                    _logger.LogBlock("REASONING-INLINE", message.Data.ReasoningText);
                AddOrUpdate(chat, ChatMessageKind.Assistant, message.Data.Content, $"msg-{message.Data.MessageId}");
                if (!string.IsNullOrWhiteSpace(message.Data.ReasoningText))
                {
                    AddOrUpdate(chat, ChatMessageKind.Reasoning, message.Data.ReasoningText, $"reason-msg-{message.Data.MessageId}");
                }
                break;
            case ToolExecutionStartEvent tool:
            {
                var extra = string.Join("\n", GetAllStringProperties(tool.Data)
                    .Where(kv => !kv.Key.Equals("ToolCallId", StringComparison.OrdinalIgnoreCase)
                              && !kv.Key.Equals("ToolName",   StringComparison.OrdinalIgnoreCase))
                    .Select(kv => $"{kv.Key}: {kv.Value}"));
                var description = string.IsNullOrEmpty(extra)
                    ? $"Running: {tool.Data.ToolName}"
                    : $"Running: {tool.Data.ToolName}\n\n{extra}";
                _logger.LogBlock("TOOL-START", description);
                AddOrUpdate(chat, ChatMessageKind.Tool, description, $"tool-{tool.Data.ToolCallId}");
                break;
            }
            case ToolExecutionCompleteEvent tool:
            {
                if (tool.Data.Success)
                {
                    _logger.Log("TOOL-DONE", $"✓ {tool.Data.ToolCallId}: completed");
                    AddOrUpdate(chat, ChatMessageKind.Tool,
                        $"\u2713 {tool.Data.ToolCallId}: completed",
                        $"tool-{tool.Data.ToolCallId}");
                }
                else
                {
                    var details = string.Join("\n", GetAllStringProperties(tool.Data)
                        .Where(kv => !kv.Key.Equals("Success",    StringComparison.OrdinalIgnoreCase)
                                  && !kv.Key.Equals("ToolCallId", StringComparison.OrdinalIgnoreCase))
                        .Select(kv => $"{kv.Key}: {kv.Value}"));
                    var message = string.IsNullOrEmpty(details)
                        ? $"\u2717 {tool.Data.ToolCallId}: failed"
                        : $"\u2717 {tool.Data.ToolCallId}: failed\n\n{details}";
                    _logger.LogBlock("TOOL-FAILED", message);
                    AddOrUpdate(chat, ChatMessageKind.Tool, message, $"tool-{tool.Data.ToolCallId}");
                }
                break;
            }
            case SessionErrorEvent error:
            {
                var errExtra = string.Join("\n", GetAllStringProperties(error.Data)
                    .Where(kv => !kv.Key.Equals("Message", StringComparison.OrdinalIgnoreCase))
                    .Select(kv => $"{kv.Key}: {kv.Value}"));
                var errMessage = string.IsNullOrEmpty(errExtra)
                    ? error.Data.Message
                    : $"{error.Data.Message}\n\n{errExtra}";
                _logger.LogBlock("ERROR", errMessage);
                AddOrUpdate(chat, ChatMessageKind.Error, errMessage, $"err-{evt.Id}");
                SetPending(chat, false);
                break;
            }
            case SessionIdleEvent:
                _logger.Log("IDLE", "Session became idle");
                SetPending(chat, false);
                break;
            case AssistantUsageEvent usage:
                _logger.Log("USAGE", ToUsageStatus(usage.Data).ToStatusText());
                UsageUpdated?.Invoke(ToUsageStatus(usage.Data));
                break;
        }
    }

    private void SetPending(ChatSessionView chat, bool isPending)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            chat.IsPending = isPending;
            SessionPendingChanged?.Invoke(chat, isPending);
        });
    }

    private static CopilotUsageStatus ToUsageStatus(AssistantUsageData data)
    {
        var quota = data.QuotaSnapshots?.Values.FirstOrDefault();
        return new CopilotUsageStatus(
            data.Model ?? "unknown model",
            data.InputTokens,
            data.OutputTokens,
            data.ReasoningTokens,
            data.Cost,
            quota?.UsedRequests,
            quota?.EntitlementRequests,
            quota?.RemainingPercentage,
            quota?.ResetDate);
    }

    private static void AddOrUpdate(ChatSessionView chat, ChatMessageKind kind, string? content, string key, bool append = false)
    {
        if (string.IsNullOrEmpty(content))
        {
            return;
        }

        App.Current.Dispatcher.Invoke(() =>
        {
            var existing = chat.Messages.FirstOrDefault(m => m.Id == key);
            if (existing is null)
            {
                chat.Messages.Add(new ChatMessage { Id = key, Kind = kind, Content = content });
            }
            else
            {
                var index = chat.Messages.IndexOf(existing);
                existing.Content = append ? existing.Content + content : content;
                chat.Messages.RemoveAt(index);
                chat.Messages.Insert(index, existing);
            }
        });
    }

    private static string? TryGetHost(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri.Host : value;
    }

    private static string? GetString(object source, string propertyName)
    {
        return source.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)
            ?.GetValue(source)?.ToString();
    }

    private static IEnumerable<KeyValuePair<string, string>> GetAllStringProperties(object source)
        => source.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(p =>
            {
                try { return new KeyValuePair<string, string?>(p.Name, p.GetValue(source)?.ToString()); }
                catch { return new KeyValuePair<string, string?>(p.Name, null); }
            })
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value!));

    private static async Task<string?> ResolveGitHubTokenAsync(string? settingsToken)
    {
        if (!string.IsNullOrWhiteSpace(settingsToken))
            return settingsToken;

        // Fall back to the token stored by the GitHub CLI (gh auth login).
        // This is the same credential source that `gh` commands use.
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("gh", "auth token")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null) return null;
            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            var token = output.Trim();
            return string.IsNullOrWhiteSpace(token) ? null : token;
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, string> CreateProcessEnvironment()
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                env[key] = value;
            }
        }

        EnsureEnvironmentValue(env, "SystemRoot", Environment.GetEnvironmentVariable("SystemRoot"));
        EnsureEnvironmentValue(env, "WINDIR", Environment.GetEnvironmentVariable("WINDIR"));
        EnsureEnvironmentValue(env, "PATH", Environment.GetEnvironmentVariable("PATH"));
        EnsureEnvironmentValue(env, "TEMP", Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar));
        EnsureEnvironmentValue(env, "TMP", Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar));
        return env;
    }

    private static void EnsureEnvironmentValue(IDictionary<string, string> env, string key, string? value)
    {
        if (!env.ContainsKey(key) && !string.IsNullOrWhiteSpace(value))
        {
            env[key] = value;
        }
    }

    private static string? ResolveBundledCliPath()
    {
        var executable = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "copilot.exe" : "copilot";
        var runtimeId = RuntimeInformation.RuntimeIdentifier;
        var preferredPath = Path.Combine(AppContext.BaseDirectory, "runtimes", runtimeId, "native", executable);
        if (File.Exists(preferredPath))
        {
            return preferredPath;
        }

        var runtimesDirectory = Path.Combine(AppContext.BaseDirectory, "runtimes");
        if (!Directory.Exists(runtimesDirectory))
        {
            return null;
        }

        return Directory.EnumerateFiles(runtimesDirectory, executable, SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in _sessions.Values)
        {
            try
            {
                await session.DisposeAsync();
            }
            catch
            {
                // Shutdown must not surface SDK transport failures to WPF.
            }
        }

        if (_client is not null)
        {
            await DisposeClientQuietlyAsync(_client);
            _client = null;
        }
    }

    private static async Task DisposeClientQuietlyAsync(CopilotClient client)
    {
        try
        {
            await client.DisposeAsync();
        }
        catch
        {
            // The Copilot CLI may already be gone, especially after a native startup failure.
        }
    }
}

public enum PermissionPromptDecision
{
    Deny,
    AllowOnce,
    AllowForSession,
    SaveToSettings
}

public sealed record PermissionPrompt(string Kind, string? ToolName, string? FileName, string? Command, string? Host);

public sealed record UserInputPrompt(string Question, IReadOnlyList<string> Choices, bool AllowFreeform);

public sealed record UserInputPromptResult(string Answer, bool WasFreeform);
