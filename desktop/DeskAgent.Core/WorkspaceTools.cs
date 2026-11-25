using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace DeskAgent.Core;

public sealed class WorkspaceTools(AgentOptions options, Approver approve, IBrowserTools? browser = null)
{
    public string Root { get; } = ValidateRoot(options.Workspace);
    public static string ValidateRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) throw new InvalidOperationException("Choose an existing workspace folder.");
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        if (string.Equals(full, Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose a project folder, not an entire drive.");
        for (var current = new DirectoryInfo(full); current != null; current = current.Parent)
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Choose a folder without symbolic links or junctions in its path.");
        return full;
    }
    public string Resolve(string path)
    {
        ValidateRoot(Root);
        if (string.IsNullOrWhiteSpace(path)) path = ".";
        if (Path.IsPathRooted(path) || path.Contains(':') || path.Contains('\0')) throw new InvalidOperationException("Only relative workspace paths are allowed.");
        var full = Path.GetFullPath(Path.Combine(Root, path));
        if (!full.Equals(Root, StringComparison.OrdinalIgnoreCase) && !full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("That path is outside the selected workspace.");
        var relative = Path.GetRelativePath(Root, full);
        if (relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part.Equals(".desk-agent-backups", StringComparison.OrdinalIgnoreCase) || part.Equals(".git", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Agent backups and Git internals are protected.");
        var current = Root;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Symbolic links and junctions are not allowed in file tools.");
        }
        return full;
    }
    static bool Sensitive(string path)
    {
        var name = Path.GetFileName(path).ToLowerInvariant();
        return name.StartsWith(".env") || name is "id_rsa" or "id_ed25519" or "credentials" or "auth.json" || name.EndsWith(".pem") || name.EndsWith(".pfx") || name.EndsWith(".key");
    }
    static string Fingerprint(string full) => File.Exists(full) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(full))) : "new-file";
    public async Task<string> ExecuteAsync(string name, JsonObject args, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested(); ToolDefinitions.Validate(name, args);
        if (name.StartsWith("web_"))
        {
            if (!options.EnableBrowser || browser == null) throw new InvalidOperationException("Browser tools are disabled.");
            var description = await browser.DescribeAsync(name, args, cancellation);
            if (!await approve(new Approval(name, "Browser action", description, "This can send information to the website. Approve only the action you intend."), cancellation)) return "Denied by user. Do not retry this action.";
            cancellation.ThrowIfCancellationRequested();
            return await browser.ExecuteAsync(name, args, cancellation);
        }
        if (name == "run_command")
        {
            if (!options.EnableCommands) throw new InvalidOperationException("Terminal commands are disabled.");
            var command = args["command"]!.GetValue<string>();
            if (command.Length is 0 or > 16000) throw new InvalidOperationException("Command length is invalid.");
            if (!await approve(new Approval(name, "Run a PowerShell command", "Working folder: " + Root + "\n\n" + command,
                "Terminal commands run with your Windows account permissions and can affect files outside the selected folder. This is not an OS sandbox."), cancellation)) return "Denied by user. Do not retry this command.";
            cancellation.ThrowIfCancellationRequested();
            return await RunCommand(command, cancellation);
        }
        var relative = args["path"]!.GetValue<string>(); var full = Resolve(relative);
        if (Sensitive(full)) throw new InvalidOperationException("Credential and secret files are excluded from agent tools. Do not read or overwrite them.");
        if (name == "read_file" && new FileInfo(full).Length > 300_000) throw new InvalidOperationException("Text files must be smaller than 300 KB.");
        string? content = null, before = null;
        if (name == "write_file")
        {
            content = args["content"]!.GetValue<string>();
            if (Encoding.UTF8.GetByteCount(content) > 500_000) throw new InvalidOperationException("A write is limited to 500 KB.");
            if (File.Exists(full) && new FileInfo(full).Length > 2_000_000) throw new InvalidOperationException("Replacing files larger than 2 MB is not supported.");
            before = Fingerprint(full);
        }
        if (name == "write_file" || !options.AutoRead)
        {
            var details = full + (content != null ? "\n\nNew file content:\n" + content : "");
            if (!await approve(new Approval(name, name == "write_file" ? "Write a workspace file" : "Read workspace information", details,
                name == "write_file" ? "Existing content will be backed up before replacement." : "The result will be sent to your selected AI provider to complete this task."), cancellation)) return "Denied by user. Do not retry this action.";
        }
        cancellation.ThrowIfCancellationRequested(); full = Resolve(relative);
        switch (name)
        {
            case "list_files":
                return string.Join('\n', new DirectoryInfo(full).EnumerateFileSystemInfos()
                    .Where(f => !Sensitive(f.Name) && f.Name is not ".git" and not ".desk-agent-backups" && (f.Attributes & FileAttributes.ReparsePoint) == 0)
                    .Take(200).Select(f => ((f.Attributes & FileAttributes.Directory) != 0 ? "[folder] " : "[file] ") + f.Name)) + "\n(Up to 200 entries.)";
            case "read_file":
                if (new FileInfo(full).Length > 300_000) throw new InvalidOperationException("File grew beyond the read limit.");
                return await File.ReadAllTextAsync(full, new UTF8Encoding(false, true), cancellation);
            case "write_file":
                if (Fingerprint(full) != before) throw new InvalidOperationException("The file changed while waiting for approval. Read it again before proposing a replacement.");
                string? backup = null;
                if (File.Exists(full))
                {
                    var folder = Path.Combine(Root, ".desk-agent-backups");
                    if (Directory.Exists(folder) && (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Backup folder cannot be a link.");
                    Directory.CreateDirectory(folder); backup = Path.Combine(folder, Guid.NewGuid().ToString("N") + "-" + Path.GetFileName(full)); File.Copy(full, backup);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                await File.WriteAllTextAsync(full, content!, new UTF8Encoding(false), cancellation);
                return "Wrote " + relative + (backup != null ? ". Previous content backed up at " + backup : "");
            default: throw new InvalidOperationException("Unknown file action.");
        }
    }
    async Task<string> RunCommand(string command, CancellationToken cancellation)
    {
        var info = new ProcessStartInfo("powershell.exe") { WorkingDirectory = Root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        info.ArgumentList.Add("-NoProfile"); info.ArgumentList.Add("-NonInteractive"); info.ArgumentList.Add("-EncodedCommand"); info.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(command)));
        // API keys are never placed in process environments; remove inherited provider secrets too.
        foreach (var key in info.Environment.Keys.Where(k => k.Contains("API_KEY", StringComparison.OrdinalIgnoreCase) || k.Contains("TOKEN", StringComparison.OrdinalIgnoreCase) || k.Contains("SECRET", StringComparison.OrdinalIgnoreCase)).ToArray()) info.Environment.Remove(key);
        using var process = new Process { StartInfo = info }; process.Start(); process.StandardInput.Close();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(45));
        var output = ReadBounded(process.StandardOutput, timeout.Token); var error = ReadBounded(process.StandardError, timeout.Token);
        string result;
        try { await process.WaitForExitAsync(timeout.Token); result = "Exit code: " + process.ExitCode; }
        catch (OperationCanceledException) { try { process.Kill(true); } catch (InvalidOperationException) { } await process.WaitForExitAsync(); result = cancellation.IsCancellationRequested ? "Stopped by user." : "Stopped after the 45-second limit."; }
        return result + "\n" + await output + "\n" + await error;
    }
    static async Task<string> ReadBounded(StreamReader reader, CancellationToken cancellation)
    {
        var text = new StringBuilder(); var buffer = new char[4096]; int count; bool clipped = false;
        try { while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellation)) > 0) { var keep = Math.Min(count, Math.Max(0, 16000 - text.Length)); text.Append(buffer, 0, keep); clipped |= keep < count; } }
        catch (OperationCanceledException) { text.Append("\n[Output collection stopped]"); }
        return text + (clipped ? "\n[Output truncated]" : "");
    }
}
