using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ReController.Platform.Windows.Services;

public sealed record HidHideDevice(string InstancePath, string Name, bool Present);

public sealed record HidHideSnapshot(
    bool CloakEnabled,
    IReadOnlyList<HidHideDevice> GamingDevices,
    IReadOnlyList<string> HiddenInstancePaths,
    IReadOnlyList<string> AllowedApplications);

/// <summary>
/// HidHide integration: hides the physical controller from games so they only see reController's
/// virtual one (prevents "double input"). reController itself is whitelisted so it can still read it.
/// </summary>
public static partial class HidHideClient
{
    public static string? FindCli()
    {
        var candidates = new List<string>();
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Nefarius Software Solutions e.U.\HidHide");
            if (key?.GetValue("Path") is string p && p.Length > 0)
            {
                candidates.Add(Path.Combine(p, "x64", "HidHideCLI.exe"));
                candidates.Add(Path.Combine(p, "HidHideCLI.exe"));
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException) { }

        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        candidates.Add(Path.Combine(pf, "Nefarius Software Solutions", "HidHide", "x64", "HidHideCLI.exe"));
        candidates.Add(Path.Combine(pf, "Nefarius Software Solutions", "HidHide", "HidHideCLI.exe"));
        return candidates.FirstOrDefault(File.Exists);
    }

    public static bool IsInstalled => DriverInfo.HidHide().Installed && FindCli() is not null;

    public static async Task<HidHideSnapshot> QueryAsync(CancellationToken ct = default)
    {
        var r = await ElevatedBatch.RunHidHideAsync(
        [
            ["--cloak-state"],
            ["--dev-gaming"],
            ["--dev-list"],
            ["--app-list"],
        ], ct).ConfigureAwait(false);
        Ensure(r, 4);
        return new HidHideSnapshot(
            CloakEnabled: r[0].StdOut.Contains("--cloak-on", StringComparison.OrdinalIgnoreCase),
            GamingDevices: ParseGamingDevices(r[1].StdOut),
            HiddenInstancePaths: ParseQuoted(r[2].StdOut),
            AllowedApplications: ParseQuoted(r[3].StdOut));
    }

    /// <summary>Hides/unhides devices, whitelists this app and turns cloaking on in a single UAC prompt.</summary>
    public static async Task ApplyAsync(IEnumerable<string> hide, IEnumerable<string> unhide, bool enableCloak, CancellationToken ct = default)
    {
        var cmds = new List<string[]>();
        string self = Environment.ProcessPath!;
        cmds.Add(["--app-reg", self]);
        foreach (var u in unhide) cmds.Add(["--dev-unhide", u]);
        foreach (var h in hide) cmds.Add(["--dev-hide", h]);
        cmds.Add([enableCloak ? "--cloak-on" : "--cloak-off"]);
        var r = await ElevatedBatch.RunHidHideAsync(cmds, ct).ConfigureAwait(false);
        Ensure(r, cmds.Count);
    }

    private static void Ensure(IReadOnlyList<CliResult> r, int expected)
    {
        if (r.Count == 1 && r[0].ExitCode == -1) throw new InvalidOperationException(r[0].StdErr);
        if (r.Count != expected) throw new InvalidOperationException("Unexpected response from HidHide.");
        var failed = r.FirstOrDefault(x => x.ExitCode != 0);
        if (failed is not null) throw new InvalidOperationException($"HidHide reported an error: {failed.StdErr.Trim()} {failed.StdOut.Trim()}".Trim());
    }

    internal static List<HidHideDevice> ParseGamingDevices(string json)
    {
        var list = new List<HidHideDevice>();
        if (string.IsNullOrWhiteSpace(json)) return list;
        try
        {
            using var doc = JsonDocument.Parse(json);
            Walk(doc.RootElement, null, list);
        }
        catch (JsonException) { }
        return list.GroupBy(d => d.InstancePath, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
    }

    private static void Walk(JsonElement e, string? groupName, List<HidHideDevice> into)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var child in e.EnumerateArray()) Walk(child, groupName, into);
                break;
            case JsonValueKind.Object:
                string? name = groupName;
                if (TryGetString(e, "friendlyName", out var fn)) name = fn;
                if (TryGetString(e, "deviceInstancePath", out var path))
                {
                    bool present = !e.TryGetProperty("present", out var pr) || pr.ValueKind != JsonValueKind.False;
                    string display = name ?? (TryGetString(e, "description", out var d) ? d : path);
                    into.Add(new HidHideDevice(path, display, present));
                }
                foreach (var prop in e.EnumerateObject())
                    if (prop.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object) Walk(prop.Value, name, into);
                break;
        }
    }

    private static bool TryGetString(JsonElement e, string name, out string value)
    {
        foreach (var p in e.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
            {
                value = p.Value.GetString() ?? "";
                return value.Length > 0;
            }
        }
        value = "";
        return false;
    }

    internal static List<string> ParseQuoted(string text) =>
        QuotedRegex().Matches(text ?? "").Select(m => m.Groups[1].Value).Where(s => s.Length > 0).ToList();

    [GeneratedRegex("\"([^\"]+)\"")]
    private static partial Regex QuotedRegex();
}
