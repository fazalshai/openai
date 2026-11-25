using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using DeskAgent.Core;

var testRoot = Path.Combine(Path.GetFullPath(args.Length > 0 ? args[0] : "."), "desk-agent-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
var checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); checks++; Console.WriteLine("PASS: " + name); }
async Task Reject(Func<Task> action, string name) { try { await action(); } catch (Exception e) when (e is not OperationCanceledException) { Check(true, name); return; } throw new Exception("FAIL: " + name); }
JsonObject PathArgs(string path) => new() { ["path"] = path };
var settings = new AgentOptions { Workspace = testRoot, AutoRead = false, Model = "test-model", ApiKey = "fake-test-key" };
var asked = 0;
var deny = new WorkspaceTools(settings, (_, _) => { asked++; return Task.FromResult(false); });
var result = await deny.ExecuteAsync("write_file", new() { ["path"] = "denied.txt", ["content"] = "not authorized" }, CancellationToken.None);
Check(result.StartsWith("Denied") && !File.Exists(Path.Combine(testRoot,"denied.txt")) && asked == 1, "denied writes cannot execute");
foreach (var path in new[] { "../outside.txt", "C:\\Windows\\test.txt", "file.txt:secret", ".git/config", ".desk-agent-backups/anything" })
    await Reject(() => Task.FromResult(deny.Resolve(path)), "blocked path: " + path);
await Reject(() => deny.ExecuteAsync("read_file", PathArgs(".env"), CancellationToken.None), "credential files excluded");
await Reject(() => deny.ExecuteAsync("read_file", new() { ["path"] = "file.txt", ["extra"] = "value" }, CancellationToken.None), "invalid arguments rejected");
var allow = new WorkspaceTools(settings, (_, _) => { asked++; return Task.FromResult(true); });
await allow.ExecuteAsync("write_file", new() { ["path"] = "nested/example.txt", ["content"] = "first" }, CancellationToken.None);
Check(File.ReadAllText(Path.Combine(testRoot,"nested/example.txt")) == "first", "approved file creation");
await allow.ExecuteAsync("write_file", new() { ["path"] = "nested/example.txt", ["content"] = "second" }, CancellationToken.None);
Check(Directory.GetFiles(Path.Combine(testRoot,".desk-agent-backups")).Any(f => File.ReadAllText(f) == "first"), "overwrite backs up previous content");
var changed = new WorkspaceTools(settings, (_, _) => { File.WriteAllText(Path.Combine(testRoot,"nested/example.txt"), "changed externally"); return Task.FromResult(true); });
await Reject(() => changed.ExecuteAsync("write_file", new() { ["path"] = "nested/example.txt", ["content"] = "must not replace" }, CancellationToken.None), "concurrent file change invalidates approval");
Check(File.ReadAllText(Path.Combine(testRoot,"nested/example.txt")) == "changed externally", "concurrent content preserved");
var cancellable = new WorkspaceTools(settings, async (_, cancellation) => { await Task.Delay(10000, cancellation); return true; });
using (var cancel = new CancellationTokenSource(100)) { try { await cancellable.ExecuteAsync("write_file", new() { ["path"] = "cancel.txt", ["content"] = "no" }, cancel.Token); } catch (OperationCanceledException) { } }
Check(!File.Exists(Path.Combine(testRoot,"cancel.txt")), "cancelled approval cannot execute");
await Reject(() => allow.ExecuteAsync("run_command", new() { ["command"] = "Write-Output no" }, CancellationToken.None), "terminal disabled by default");
settings.EnableCommands = true;
result = await deny.ExecuteAsync("run_command", new() { ["command"] = "Set-Content -LiteralPath 'should-not-exist.txt' -Value 'no'" }, CancellationToken.None);
Check(result.StartsWith("Denied") && !File.Exists(Path.Combine(testRoot,"should-not-exist.txt")), "denied terminal command cannot execute");
result = await allow.ExecuteAsync("run_command", new() { ["command"] = "Write-Output 'desk-agent-test-ok'" }, CancellationToken.None);
Check(result.Contains("desk-agent-test-ok") && result.Contains("Exit code: 0"), "approved terminal command runs");
using (var cancelCommand = new CancellationTokenSource(1500))
{
    result = await allow.ExecuteAsync("run_command", new() { ["command"] = "Start-Sleep -Seconds 20; Set-Content -LiteralPath 'late-command.txt' -Value 'no'" }, cancelCommand.Token);
    Check(result.Contains("Stopped by user") && !File.Exists(Path.Combine(testRoot, "late-command.txt")), "stop terminates an active command before later actions");
}
await Reject(() => allow.ExecuteAsync("web_open", new() { ["url"] = "https://example.com" }, CancellationToken.None), "browser disabled by default");
var browser = new StubBrowser(); settings.EnableBrowser = true;
var deniedBrowser = new WorkspaceTools(settings, (_, _) => Task.FromResult(false), browser);
result = await deniedBrowser.ExecuteAsync("web_open", new() { ["url"] = "https://example.com" }, CancellationToken.None);
Check(result.StartsWith("Denied") && browser.Executions == 0, "denied browser action cannot execute");
var approvedBrowser = new WorkspaceTools(settings, (_, _) => Task.FromResult(true), browser);
await approvedBrowser.ExecuteAsync("web_open", new() { ["url"] = "https://example.com" }, CancellationToken.None);
Check(browser.Executions == 1, "approved browser action reaches browser executor");
settings.EnableBrowser = false;
// Exercise real provider request construction and multi-step tools with deterministic HTTP responses.
foreach (var provider in new[] { "openai", "anthropic" })
{
    settings.Provider = provider; settings.EnableCommands = false; int requests = 0; var events = new List<AgentEvent>();
    using var api = new ProviderApi(new StubHandler(async request =>
    {
        var payload = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!.AsObject(); requests++;
        Check(request.RequestUri!.Host == (provider == "openai" ? "api.openai.com" : "api.anthropic.com"), provider + " uses correct endpoint");
        if (provider == "openai")
        {
            Check(request.Headers.Authorization?.Parameter == "fake-test-key" && !request.Headers.Contains("x-api-key"), "OpenAI key stays with OpenAI");
            if (requests == 1) return new JsonObject { ["status"] = "completed", ["output"] = new JsonArray(new JsonObject { ["type"] = "function_call", ["name"] = "write_file", ["call_id"] = "c1", ["arguments"] = new JsonObject { ["path"] = "openai.txt", ["content"] = "OpenAI tool result" }.ToJsonString() }) };
            Check(payload["input"]!.AsArray().Any(n => n?["type"]?.GetValue<string>() == "function_call_output"), "OpenAI receives executed tool result");
            return new JsonObject { ["status"] = "completed", ["output"] = new JsonArray(new JsonObject { ["type"] = "message", ["role"] = "assistant", ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = "Completed test." }) }) };
        }
        Check(request.Headers.GetValues("x-api-key").Single() == "fake-test-key" && request.Headers.Authorization == null, "Claude key stays with Claude");
        if (requests == 1) return new JsonObject { ["stop_reason"] = "tool_use", ["content"] = new JsonArray(new JsonObject { ["type"] = "tool_use", ["name"] = "write_file", ["id"] = "t1", ["input"] = new JsonObject { ["path"] = "claude.txt", ["content"] = "Claude tool result" } }) };
        Check(payload["messages"]!.AsArray().Last()?["content"]?[0]?["type"]?.GetValue<string>() == "tool_result", "Claude receives executed tool result");
        return new JsonObject { ["stop_reason"] = "end_turn", ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "Completed test." }) };
    }));
    var runner = new AgentLoop(api, settings, allow, events.Add); await runner.RunAsync("Create a test file.", CancellationToken.None);
    Check(requests == 2 && events.Any(e => e.Type == "assistant" && e.Text == "Completed test."), provider + " completes an agent tool loop");
}
Check(File.Exists(Path.Combine(testRoot,"openai.txt")) && File.Exists(Path.Combine(testRoot,"claude.txt")), "both provider tools produced real files");
Console.WriteLine($"ALL {checks} CHECKS PASSED. Test artifacts: {testRoot}");

sealed class StubHandler(Func<HttpRequestMessage, Task<JsonObject>> respond) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation) => new(HttpStatusCode.OK) { Content = new StringContent((await respond(request)).ToJsonString(), Encoding.UTF8, "application/json") };
}
sealed class StubBrowser : IBrowserTools
{
    public int Executions;
    public Task<string> DescribeAsync(string name, JsonObject args, CancellationToken cancellation) => Task.FromResult("Open example.com");
    public Task<string> ExecuteAsync(string name, JsonObject args, CancellationToken cancellation) { Executions++; return Task.FromResult("Page snapshot"); }
}
