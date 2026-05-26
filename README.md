# Copilot Chatbot (WPF)

Desktop chat client for GitHub Copilot built with WPF and .NET 9 on Windows.

It provides a tabbed chat UI, session restore, model selection, reasoning effort controls, permission prompts, MCP/agent/skill visibility, and rich Markdown/HTML rendering through WebView2.

## Features

- Multi-tab chat sessions with per-tab system prompts
- Saved chat sessions restored on startup
- Live model discovery from GitHub Copilot SDK
- Reasoning effort selection for models that support it
- Chat navigation controls:
  - Scroll to top and bottom
  - Jump to previous and next user question
- Tool and permission workflow:
  - Folder access rules (read/read-write)
  - Allowed tools and hosts lists
  - Saved permission rules
- User secrets mapped to environment variables (stored encrypted with Windows DPAPI)
- Configurable working directory for Copilot CLI execution
- MCP server, agent, and skill capability view
- Extra agent and skill folder configuration
- Light, dark, and follow-the-sun theme options
- Optional debug logging
- Markdown rendering, collapsible message cards, response pop-out windows, and embedded HTML previews
- Custom application icon and Windows `.ico` packaging

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
- Optional extra agent and skill folders
- Preferred appearance/theme
- Optional debug logging

Then use **Refresh Models** to fetch available models from the Copilot runtime.

## Configuration and Data Files

The app stores local configuration under:

- `%APPDATA%\CopilotChatbot\settings.json`
- `%APPDATA%\CopilotChatbot\chat-sessions.json`

If debug logging is enabled, logs are written to:

- `%APPDATA%\CopilotChatbot\debug-YYYY-MM-DD.log`

## MCP Notes

- The app loads user MCP server config from:
  - `~/.copilot/mcp-config.json`
- The configured working directory is where Copilot CLI runs.
- Project-level Copilot/MCP config is typically resolved relative to the working directory.
- Agents and skills are loaded from the default Copilot locations plus any extra folders configured in Settings.

Default agent and skill locations shown by the app:

- `~/.copilot/agents`
- `<working-dir>/.github/agents`
- `~/.copilot/skills`
- `<working-dir>/.github/skills`

## Project Structure

- `CopilotChatbot/` - WPF application
- `CopilotChatbot/Services/` - runtime services (Copilot client, rendering, settings, logging)
- `CopilotChatbot/Models/` - settings and chat data models
- `CopilotChatbot/Assets/` - application icon and bundled MCP server metadata
- `CopilotChatbot/MainWindow.*` - main chat UI and interaction logic
- `CopilotChatbot/ChatTabContent.*` - per-tab chat input, status, and navigation controls
- `CopilotChatbot/SettingsWindow.*` - settings UI and persistence wiring
- `.github/workflows/dotnet-build.yaml` - GitHub Actions build workflow

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
- Manual GitHub Actions run does not start:
  - Confirm Actions are enabled for the repository.
  - Confirm the workflow exists on the default branch at `.github/workflows/dotnet-build.yaml`.
  - Check GitHub Status if the UI reports that the workflow could not be queued.

## Build Configuration

Current project defaults (from `CopilotChatbot.csproj`):

- `TargetFramework`: `net9.0-windows`
- `RuntimeIdentifier`: `win-x64`
- `UseWPF`: `true`
- `SelfContained`: `false`
- `ApplicationIcon`: `Assets\AppIcon.ico`

## GitHub Actions

The repository includes a Windows build workflow:

- Runs on pushes to `main` or `master`
- Runs on pull requests targeting `main` or `master`
- Can be started manually from the GitHub Actions UI
- Builds with .NET 9 on `windows-latest`

Build versioning uses:

- `VERSION_PREFIX` repository variable, defaulting to `1.0`
- `github.run_number` as the final build number

For example, with `VERSION_PREFIX=1.0`, workflow runs produce versions like:

- `1.0.1`
- `1.0.2`
- `1.0.3`

Set the prefix in:

```text
Repository Settings > Secrets and variables > Actions > Variables
Name: VERSION_PREFIX
Value: 1.0
```

## License

This project uses an MIT-style non-commercial license. See `LICENSE`.

Note: this is not the standard OSI MIT License because commercial use is restricted.
