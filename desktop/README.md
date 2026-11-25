# Desk Agent — Windows preview 0.1.0

A downloadable personal agent with OpenAI and Claude support. Each person supplies their own API key, selects an accessible model and chooses which tools to enable. This is an independent app, not the official Codex or Claude desktop client.

[Download and setup](https://fazalshai.github.io/openai/desktop.html) · [Release files](https://github.com/fazalshai/openai/releases/tag/desk-agent-v0.1.0)

## Start using it

1. Download `Desk-Agent-0.1.0-win-x64.zip` and extract the entire ZIP to a folder you control. Keep `DeskAgent.exe`, the `ui` folder and the accompanying files together. Do not launch from inside the ZIP.
2. Open `DeskAgent.exe`. This preview is unsigned. If Windows or your organization blocks it, follow your organization's software policy; a signed installer is not available in this release.
3. Choose a project folder. Start with a disposable project while getting familiar with the preview.
4. Open **Model & permissions**, choose **OpenAI** or **Anthropic · Claude**, and enter that provider's API key. An OpenAI key cannot call Claude. API billing is separate from a ChatGPT or Claude chat subscription.
5. Click **Load models** and select an available model, or enter an exact API model ID. Use a model that supports tool calling through Responses (OpenAI) or Messages (Claude). The model list is account-specific and does not guarantee tool compatibility. **Model default** is the most compatible reasoning setting; supported models expose other effort levels.
6. Choose whether to enable browser tools or terminal commands. Both start disabled. Save settings, describe a task, and review each proposed action before choosing **Allow once** or **Deny**.

Requires Windows 10/11 x64 and [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/). The ZIP includes the .NET runtime; Node.js, Python and the .NET SDK are not required. It does not need administrator privileges.

## What this version does

| Tool | Behavior |
| --- | --- |
| List and read text files | Works in the chosen folder. Asks by default; you can allow workspace reads for the session. |
| Create and replace text files | Always asks with the proposed full content. Backs up replaced content in `.desk-agent-backups`. Rejects changes if the file changed while awaiting approval. |
| Run PowerShell | Optional. Shows the exact command and working folder before every execution. Cancels the process tree when stopped or after 45 seconds. Output is bounded. |
| Use the task browser | Optional. Opens public web pages, reads visible text, clicks identified controls and types ordinary text. Each tool action requires approval. |
| Choose a provider and model | Calls your provider directly. OpenAI and Claude use separate keys. Loads models from your account; reasoning choices depend on model support. |

Example tasks: “Explain this project's files,” “Draft a README using this folder,” or “Open Wikipedia and read the homepage.” With terminal tools enabled, the agent can propose build/test commands for software already installed on your PC.

## Data and permissions

- Task prompts, approved file contents, command output and browser snapshots are sent to the selected AI provider. Browser actions can also submit information to the visited website. Provider charges and policies apply.
- API keys stay in memory unless **Remember key with Windows encryption** is selected. Remembered keys use Windows DPAPI for the current Windows user, under `%LOCALAPPDATA%\DeskAgent`. They are not put in the agent prompt, JavaScript storage, or command environment. Anyone or any process with access to your Windows account may still be able to access that account's data.
- **Remove saved key** removes that provider's saved key and clears its in-memory copy. Settings other than remembered keys reset on app restart. Conversations are memory-only; new tasks and settings changes clear conversation context.
- Browser profiles (including site cookies) persist in `%LOCALAPPDATA%\DeskAgent\task-browser`. The interface profile is separate. Sign out of websites yourself after use if desired. Close the app before manually removing its local data.
- File tools reject paths outside the chosen folder, junctions/symlinks, Git internals, backup files and common secret filenames. These are guardrails, not comprehensive secret detection.
- **Terminal commands are not sandboxed.** They run with your Windows account's access, can use the network, and can affect files outside the workspace. File-tool restrictions do not constrain a command. Review the full command; do not grant approval to an action you do not understand.
- The task browser rejects direct private/local URL navigation, disables downloads and denies camera/microphone/geolocation permissions. It is not a network sandbox. Page scripts and content remain untrusted. Passwords, payment details and sign-in codes are manual steps. Do not ask the agent to handle secrets.
- Stop cancels pending approvals, active API requests and commands. It does not undo completed writes or website actions. Restore a replaced file manually from `.desk-agent-backups` if needed. Commands and website actions do not have automatic rollback.

## Preview limits and validation

This release handles text files, commands and basic workflows in its own browser. It does not provide general native desktop app control, built-in Gmail/Slack connectors, background scheduled work, image/PDF understanding, cross-device synchronization, automatic updates, or a signed installer. Browser workflows that depend on downloads, complex frames or embedded-browser sign-in restrictions may not work. Some sites require you to sign in manually.

Each task has a 12-step model limit. OpenAI output is capped at 16,000 tokens per step and Claude at 8,192. File reads are limited to 300 KB, new writes to 500 KB, replacement targets to 2 MB, and file lists to 200 entries. Large tasks may need follow-ups. API requests have a four-minute timeout.

Validation: 33 automated checks passed, including real file writes/backups, a real approved command, command cancellation, denied-action enforcement, path restrictions, provider credential routing and complete tool loops with simulated API responses. The Windows interface was inspected in WebView2. No paid live-provider task or end-to-end website workflow was tested with a real API key; those remain preview validation limits.

## Build from source

Install the .NET 10 SDK on Windows, then run from the repository root:

```powershell
dotnet run --project desktop/DeskAgent.Tests/DeskAgent.Tests.csproj -c Release -- "$env:TEMP"
dotnet publish desktop/DeskAgent/DeskAgent.csproj -c Release -r win-x64 --self-contained true -o desktop/publish
```

The tests create a uniquely named test folder; they do not call paid APIs. The desktop build pins Microsoft.Web.WebView2 1.0.4370.26. Package the executable and `ui` folder together, with this README and the redistributed components' licenses/notices. Do not include keys, local browser profiles, build caches or test artifacts in a release.

The release includes license/notice texts for Microsoft .NET, Windows Desktop runtime and WebView2 in `licenses/`. These apply to their respective third-party components.
