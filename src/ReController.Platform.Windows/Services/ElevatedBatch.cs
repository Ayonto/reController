using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using Serilog;

namespace ReController.Platform.Windows.Services;

public sealed record CliResult(int ExitCode, string StdOut, string StdErr);

/// <summary>
/// Runs HidHide CLI commands with administrator rights. When we are not elevated, the app re-launches
/// itself elevated with <c>--elevated-batch</c>. The elevated side trusts nothing from the request file
/// except an allow-listed set of HidHide verbs, and resolves the CLI path itself.
/// </summary>
public static class ElevatedBatch
{
    public const string Verb = "--elevated-batch";

    private static readonly HashSet<string> AllowedVerbs = new(StringComparer.Ordinal)
    {
        "--cloak-on", "--cloak-off", "--cloak-state",
        "--dev-gaming", "--dev-list", "--dev-hide", "--dev-unhide",
        "--app-list", "--app-reg", "--app-unreg",
    };

    public static bool IsElevated
    {
        get
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public static async Task<IReadOnlyList<CliResult>> RunHidHideAsync(IReadOnlyList<string[]> commands, CancellationToken ct = default)
    {
        Validate(commands);
        if (IsElevated) return RunDirect(commands);

        string dir = Path.Combine(Path.GetTempPath(), "reController-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string request = Path.Combine(dir, "request.json");
        string response = Path.Combine(dir, "response.json");
        try
        {
            await File.WriteAllTextAsync(request, JsonSerializer.Serialize(commands), ct).ConfigureAwait(false);
            var psi = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            psi.ArgumentList.Add(Verb);
            psi.ArgumentList.Add(request);
            psi.ArgumentList.Add(response);

            Process? p;
            try { p = Process.Start(psi); }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                throw new OperationCanceledException("Administrator permission was declined.", ex);
            }
            if (p is null) throw new InvalidOperationException("Could not start elevated helper.");
            using (p) await p.WaitForExitAsync(ct).ConfigureAwait(false);

            if (!File.Exists(response)) throw new InvalidOperationException("Elevated helper produced no result.");
            return JsonSerializer.Deserialize<List<CliResult>>(await File.ReadAllTextAsync(response, ct).ConfigureAwait(false)) ?? [];
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Entry point for the elevated helper process. Returns the process exit code.</summary>
    public static int Execute(string requestPath, string responsePath)
    {
        try
        {
            var commands = JsonSerializer.Deserialize<List<string[]>>(File.ReadAllText(requestPath)) ?? [];
            Validate(commands);
            var results = RunDirect(commands);
            File.WriteAllText(responsePath, JsonSerializer.Serialize(results));
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Elevated batch failed");
            try { File.WriteAllText(responsePath, JsonSerializer.Serialize(new[] { new CliResult(-1, "", ex.Message) })); } catch (IOException) { }
            return 1;
        }
    }

    private static void Validate(IReadOnlyList<string[]> commands)
    {
        if (commands.Count > 64) throw new InvalidOperationException("Too many commands.");
        foreach (var cmd in commands)
        {
            if (cmd.Length is < 1 or > 2 || !AllowedVerbs.Contains(cmd[0]))
                throw new InvalidOperationException($"Command not allowed: {string.Join(' ', cmd)}");
            if (cmd.Length == 2 && (cmd[1].Length > 1024 || cmd[1].Contains('"', StringComparison.Ordinal) || cmd[1].Contains('\0', StringComparison.Ordinal)))
                throw new InvalidOperationException("Invalid argument.");
        }
    }

    private static List<CliResult> RunDirect(IReadOnlyList<string[]> commands)
    {
        string cli = HidHideClient.FindCli() ?? throw new FileNotFoundException("HidHideCLI.exe was not found. Is HidHide installed?");
        var results = new List<CliResult>();
        foreach (var cmd in commands)
        {
            var psi = new ProcessStartInfo(cli)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var a in cmd) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(15_000)) { try { p.Kill(); } catch (InvalidOperationException) { } }
            results.Add(new CliResult(p.HasExited ? p.ExitCode : -1, stdout.Result, stderr.Result));
        }
        return results;
    }
}
