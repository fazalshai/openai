using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using DeskAgent.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;

namespace DeskAgent;
public sealed class MainWindow : Window
{
    readonly WebView2 view = new();
    readonly ProviderApi api = new();
    readonly Dictionary<string, string> keys = new() { ["openai"] = KeyStore.Read("openai"), ["anthropic"] = KeyStore.Read("anthropic") };
    readonly Dictionary<string, TaskCompletionSource<bool>> approvals = [];
    AgentOptions options = new(); AgentLoop? agent; CancellationTokenSource? running; BrowserWindow? browser;
    bool modelsLoading;
    const string Origin = "https://desk-agent.local/";
    public MainWindow()
    {
        Title = "Desk Agent · Preview"; Width = 1200; Height = 820; MinWidth = 820; MinHeight = 600; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = view; Loaded += async (_, _) => await Initialize();
        Closing += (_, _) => { running?.Cancel(); foreach (var pending in approvals.Values) pending.TrySetCanceled(); browser?.Close(); api.Dispose(); };
    }
    async Task Initialize()
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(KeyStore.DataFolder, "interface"));
            await view.EnsureCoreWebView2Async(environment);
            var core = view.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping("desk-agent.local", Path.Combine(AppContext.BaseDirectory, "ui"), CoreWebView2HostResourceAccessKind.DenyCors);
            core.Settings.AreHostObjectsAllowed = false; core.Settings.AreDevToolsEnabled = false; core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDefaultScriptDialogsEnabled = false; core.Settings.IsStatusBarEnabled = false;
            core.NavigationStarting += (_, e) => { if (!e.Uri.StartsWith(Origin, StringComparison.Ordinal)) e.Cancel = true; };
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.WebMessageReceived += async (_, e) =>
            {
                if (e.Source != Origin + "index.html" || core.Source != Origin + "index.html") return;
                try { if (e.WebMessageAsJson.Length > 700_000) throw new InvalidOperationException("Message too large."); await Handle(JsonNode.Parse(e.WebMessageAsJson)!.AsObject()); }
                catch (Exception error) { Send(new { type = "error", text = error.Message }); }
            };
            core.Navigate(Origin + "index.html");
        }
        catch (Exception error)
        {
            MessageBox.Show("Desk Agent could not start its interface. It needs Microsoft Edge WebView2 Runtime.\n\n" + error.Message + "\n\nOfficial download: https://developer.microsoft.com/microsoft-edge/webview2/", "Desk Agent startup"); Close();
        }
    }
    void Send(object value)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => Send(value)); return; }
        if (view.CoreWebView2?.Source == Origin + "index.html") view.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(value));
    }
    static string Text(JsonObject message, string key, string fallback = "") => message[key]?.GetValue<string>() ?? fallback;
    static bool Flag(JsonObject message, string key) => message[key]?.GetValue<bool>() == true;
    async Task Handle(JsonObject message)
    {
        var type = Text(message, "type");
        switch (type)
        {
            case "ready": SendState(); break;
            case "chooseFolder":
                EnsureIdle(); var picker = new OpenFolderDialog { Title = "Choose the folder Desk Agent may work in", Multiselect = false };
                if (picker.ShowDialog(this) == true) { options.Workspace = WorkspaceTools.ValidateRoot(picker.FolderName); agent = null; SendState(); Send(new { type = "resetConversation" }); }
                break;
            case "saveSettings":
                EnsureIdle(); if (modelsLoading) throw new InvalidOperationException("Wait for the model list to finish loading.");
                var provider = Text(message, "provider"); if (!keys.ContainsKey(provider)) throw new InvalidOperationException("Unknown provider.");
                var key = Text(message, "apiKey").Trim(); if (key != "") keys[provider] = key;
                KeyStore.Save(provider, keys[provider], Flag(message, "remember"));
                var model = Text(message, "model").Trim(); if (model.Length > 180 || model.Any(char.IsWhiteSpace)) throw new InvalidOperationException("Enter an exact API model ID.");
                var effort = Text(message, "effort", "default"); if (!new[] { "default", "none", "minimal", "low", "medium", "high", "xhigh", "max" }.Contains(effort)) effort = "default";
                options = new AgentOptions { Provider = provider, ApiKey = keys[provider], Model = model, Effort = effort, Workspace = options.Workspace,
                    AutoRead = Flag(message, "autoRead"), EnableCommands = Flag(message, "commands"), EnableBrowser = Flag(message, "browser"), MaxSteps = 12 };
                agent = null; SendState(); Send(new { type = "settingsSaved" }); Send(new { type = "resetConversation" }); break;
            case "loadModels":
                EnsureIdle(); if (modelsLoading) return; modelsLoading = true;
                try
                {
                    var p = Text(message, "provider"); if (!keys.ContainsKey(p)) throw new InvalidOperationException("Unknown provider.");
                    var supplied = Text(message, "apiKey").Trim();
                    var models = await api.ModelsAsync(new AgentOptions { Provider = p, ApiKey = supplied != "" ? supplied : keys[p] }, CancellationToken.None);
                    Send(new { type = "models", provider = p, models });
                }
                finally { modelsLoading = false; Send(new { type = "modelsFinished" }); }
                break;
            case "forgetKey":
                EnsureIdle(); var target = Text(message, "provider"); if (!keys.ContainsKey(target)) throw new InvalidOperationException("Unknown provider.");
                keys[target] = ""; KeyStore.Save(target, "", false); if (target == options.Provider) options.ApiKey = ""; agent = null; SendState(); break;
            case "approval":
                var id = Text(message, "id"); if (approvals.Remove(id, out var pending)) pending.TrySetResult(Flag(message, "allow")); break;
            case "run":
                EnsureIdle(); _ = WorkspaceTools.ValidateRoot(options.Workspace);
                if (options.ApiKey == "" || options.Model == "") throw new InvalidOperationException("Add your API key and choose a model in Settings first.");
                var prompt = Text(message, "text"); if (prompt.Trim() == "") return;
                running = new CancellationTokenSource(); var cancellation = running.Token;
                try
                {
                    if (options.EnableBrowser && browser == null) browser = new BrowserWindow();
                    agent ??= new AgentLoop(api, options, new WorkspaceTools(options, AskApproval, browser), ev => Send(new { type = ev.Type, text = ev.Text, name = ev.Name }));
                    Send(new { type = "started", text = prompt }); await agent.RunAsync(prompt, cancellation);
                }
                catch (OperationCanceledException) { Send(new { type = "status", text = "Stopped. Completed actions remain in place." }); }
                catch (Exception error) { Send(new { type = "error", text = error.Message }); }
                finally { foreach (var p in approvals.Values) p.TrySetCanceled(); approvals.Clear(); running.Dispose(); running = null; Send(new { type = "finished" }); }
                break;
            case "stop": running?.Cancel(); break;
            case "newTask": EnsureIdle(); agent?.Clear(); Send(new { type = "resetConversation" }); break;
            case "showBrowser":
                if (!options.EnableBrowser) throw new InvalidOperationException("Enable browser tools in Settings first.");
                browser ??= new BrowserWindow(); await browser.ShowReadyAsync(); break;
        }
    }
    void EnsureIdle() { if (running != null) throw new InvalidOperationException("Stop the current task before changing settings."); }
    void SendState() => Send(new { type = "state", provider = options.Provider, model = options.Model, effort = options.Effort, workspace = options.Workspace,
        autoRead = options.AutoRead, commands = options.EnableCommands, browser = options.EnableBrowser, hasKeys = keys.ToDictionary(p => p.Key, p => p.Value != ""), savedKeys = keys.ToDictionary(p => p.Key, p => KeyStore.HasSaved(p.Key)) });
    async Task<bool> AskApproval(Approval request, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested(); var id = Guid.NewGuid().ToString("N");
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); approvals[id] = pending;
        using var registration = cancellation.Register(() => pending.TrySetCanceled(cancellation));
        Send(new { type = "approval", id, name = request.Name, summary = request.Summary, details = request.Details, warning = request.Warning });
        try { return await pending.Task; }
        finally { approvals.Remove(id); Send(new { type = "approvalResolved", id }); }
    }
}
