# Copilot Chatbot (WPF)

Desktop chat client for GitHub Copilot built with WPF and .NET 9 on Windows.

It provides a tabbed chat UI, model selection, reasoning effort controls, permission prompts, and rich markdown/HTML rendering through WebView2.

## Features

- Multi-tab chat sessions with per-tab system prompts
- Live model discovery from GitHub Copilot SDK
- Reasoning effort selection for models that support it
- Tool and permission workflow:
  - Folder access rules (read/read-write)
  - Allowed tools and hosts lists
  - Saved permission rules
- User secrets mapped to environment variables (stored encrypted with Windows DPAPI)
- Configurable working directory for Copilot CLI execution
- Optional debug logging
- Markdown rendering (with syntax-friendly code blocks) and embedded HTML previews

## Tech Stack

- .NET 9 (`net9.0-windows`)
- WPF
- GitHub.Copilot.SDK
- Microsoft.Web.WebView2
- Markdig
- WPF-UI

## Prerequisites

- Windows 10/11
- .NET 9 SDK
- GitHub account with Copilot access
- A GitHub token configured in-app (the app UI references a token with `copilot` scope)

## Getting Started

1. Clone the repository.
2. Restore and build:

```powershell
dotnet restore .\CopilotChatbot.sln
dotnet build .\CopilotChatbot.sln -c Debug
```

3. Run the app:

```powershell
dotnet run --project .\CopilotChatbot\CopilotChatbot.csproj
```

## First-Time Setup

Open **Settings** in the app and configure:

- GitHub token
- Optional working directory (defaults to your user profile folder)
- Optional user secrets (`Name`, `Environment Variable`, `Value`)
- Optional permission defaults and allow-lists
- Optional default system prompt
- Optional debug logging

Then use **Refresh Models** to fetch available models from the Copilot runtime.

## Configuration and Data Files

The app stores local configuration under:

- `%APPDATA%\CopilotChatbot\settings.json`

If debug logging is enabled, logs are written to:

- `%APPDATA%\CopilotChatbot\debug-YYYY-MM-DD.log`

## MCP Notes

- The app loads user MCP server config from:
  - `~/.copilot/mcp-config.json`
- The configured working directory is also where Copilot CLI runs; project-level Copilot/MCP config is typically resolved relative to that folder.

## Project Structure

- `CopilotChatbot/` - WPF application
- `CopilotChatbot/Services/` - runtime services (Copilot client, rendering, settings, logging)
- `CopilotChatbot/Models/` - settings and chat data models
- `CopilotChatbot/MainWindow.*` - main chat UI and interaction logic
- `CopilotChatbot/SettingsWindow.*` - settings UI and persistence wiring

## Troubleshooting

- No models appear:
  - Verify token and Copilot entitlement.
  - Open Settings and confirm Working Directory is valid.
  - Click **Refresh Models** again.
- Authentication or connection failures:
  - Re-check token value in Settings.
  - Confirm network/proxy restrictions do not block Copilot CLI.
- Web content not rendering:
  - Ensure WebView2 runtime is available and updated on the machine.

## Build Configuration

Current project defaults (from `CopilotChatbot.csproj`):

- `TargetFramework`: `net9.0-windows`
- `RuntimeIdentifier`: `win-x64`
- `UseWPF`: `true`
- `SelfContained`: `false`
