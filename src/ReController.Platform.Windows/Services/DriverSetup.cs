using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;
using Serilog;

namespace ReController.Platform.Windows.Services;

public enum DriverHealth
{
    /// <summary>Installed and usable.</summary>
    Ready,
    /// <summary>Not installed.</summary>
    Missing,
    /// <summary>Partially installed, outdated or not started — needs a (re)install.</summary>
    Broken,
}

public sealed record DriverPackage(string Id, string DisplayName, string Version, string ResourceName, string Sha256);

public sealed record DriverInstallStep(string Id, int ExitCode, bool Ok, bool RebootRequired, string Message);

public sealed record DriverInstallResult(IReadOnlyList<DriverInstallStep> Steps)
{
    public bool Ok => Steps.All(s => s.Ok);
    public bool RebootRequired => Steps.Any(s => s.RebootRequired);
}

/// <summary>
/// One-click installation of the drivers reController needs. The official, signed installers are embedded in the
/// application (see build/fetch-redist.ps1) and run silently from an elevated helper process.
/// </summary>
/// <remarks>
/// Security: the elevated side takes only driver ids from the command line, extracts the installers from its own
/// executable into a directory only Administrators/SYSTEM can write, and verifies each file's pinned SHA-256
/// before running it.
/// </remarks>
public static class DriverSetup
{
    public const string Verb = "--elevated-drivers";

    public static readonly DriverPackage ViGEmBus = new(
        "vigembus", "ViGEmBus", "1.22.0", "redist/ViGEmBus.exe",
        "89220a7865076b342892f98865f3499fb7c4cfd673159e89d352c360fd014c6a");

    public static readonly DriverPackage HidHide = new(
        "hidhide", "HidHide", "1.5.230", "redist/HidHide.exe",
        "f4bbbcb82e6258641b887c74bc81c4c5f66e4aa811808dfc304347687b7605f6");

    private static readonly DriverPackage[] All = [ViGEmBus, HidHide];
    private static readonly TimeSpan InstallerTimeout = TimeSpan.FromMinutes(10);

    private static Assembly ResourceAssembly => Assembly.GetEntryAssembly() ?? typeof(DriverSetup).Assembly;

    /// <summary>True when the installer is embedded in this build of the app.</summary>
    public static bool IsBundled(DriverPackage p) => ResourceAssembly.GetManifestResourceInfo(p.ResourceName) is not null;

    public static DriverHealth ViGEmHealth()
    {
        try
        {
            using var client = new ViGEmClient();
            return DriverHealth.Ready;
        }
        catch (VigemBusNotFoundException)
        {
            return DriverInfo.ViGEmBus().Installed ? DriverHealth.Broken : DriverHealth.Missing;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "ViGEmBus present but unusable");
            return DriverHealth.Broken;
        }
    }

    public static DriverHealth HidHideHealth()
    {
        bool service = DriverInfo.HidHide().Installed;
        if (!service) return DriverHealth.Missing;
        return HidHideClient.FindCli() is null ? DriverHealth.Broken : DriverHealth.Ready;
    }

    /// <summary>Installs the requested drivers. Shows one UAC prompt when not already elevated.</summary>
    /// <exception cref="OperationCanceledException">The user declined the administrator prompt.</exception>
    public static async Task<DriverInstallResult> InstallAsync(bool vigem, bool hidhide, CancellationToken ct = default)
    {
        var ids = Select(vigem, hidhide);
        if (ids.Count == 0) return new DriverInstallResult([]);
        if (ElevatedBatch.IsElevated) return await Task.Run(() => RunInstallers(ids), ct).ConfigureAwait(false);

        string dir = Path.Combine(Path.GetTempPath(), "reController-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string response = Path.Combine(dir, "drivers.json");
        try
        {
            var psi = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            psi.ArgumentList.Add(Verb);
            psi.ArgumentList.Add(response);
            foreach (var id in ids) psi.ArgumentList.Add(id);

            Process? p;
            try { p = Process.Start(psi); }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                throw new OperationCanceledException("Administrator permission was declined.", ex);
            }
            if (p is null) throw new InvalidOperationException("Could not start the setup helper.");
            using (p) await p.WaitForExitAsync(ct).ConfigureAwait(false);

            if (!File.Exists(response)) throw new InvalidOperationException("The setup helper did not report a result. See the log for details.");
            return JsonSerializer.Deserialize<DriverInstallResult>(await File.ReadAllTextAsync(response, ct).ConfigureAwait(false))
                   ?? throw new InvalidOperationException("The setup helper returned an empty result.");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Entry point of the elevated helper: <c>--elevated-drivers &lt;response.json&gt; vigembus hidhide</c>.</summary>
    public static int ExecuteElevated(string responsePath, IEnumerable<string> ids)
    {
        DriverInstallResult result;
        try
        {
            var valid = ids.Where(id => All.Any(p => p.Id == id)).Distinct().ToList();
            result = RunInstallers(valid);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Driver installation failed");
            result = new DriverInstallResult([new DriverInstallStep("setup", -1, false, false, ex.Message)]);
        }
        try
        {
            File.WriteAllText(responsePath, JsonSerializer.Serialize(result));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not write driver setup result");
        }
        return result.Ok ? 0 : 1;
    }

    private static List<string> Select(bool vigem, bool hidhide)
    {
        var ids = new List<string>();
        if (vigem) ids.Add(ViGEmBus.Id);
        if (hidhide) ids.Add(HidHide.Id);
        return ids;
    }

    private static DriverInstallResult RunInstallers(IReadOnlyList<string> ids)
    {
        var steps = new List<DriverInstallStep>();
        string dir = CreateSecureDirectory();
        try
        {
            foreach (var pkg in All.Where(p => ids.Contains(p.Id)))
            {
                steps.Add(InstallOne(pkg, dir));
                if (pkg == HidHide && steps[^1].Ok) RegisterWithHidHide();
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return new DriverInstallResult(steps);
    }

    private static DriverInstallStep InstallOne(DriverPackage pkg, string dir)
    {
        string file = Path.Combine(dir, pkg.Id + "-setup.exe");
        using (var res = ResourceAssembly.GetManifestResourceStream(pkg.ResourceName))
        {
            if (res is null) return new(pkg.Id, -1, false, false, $"{pkg.DisplayName} is not included in this build.");
            using var fs = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            res.CopyTo(fs);
        }

        // Verify what we are about to run as administrator; read with sharing denied so it can't change underneath us.
        using (var verify = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            string hash = Convert.ToHexString(SHA256.HashData(verify)).ToLowerInvariant();
            if (hash != pkg.Sha256) return new(pkg.Id, -1, false, false, $"{pkg.DisplayName} installer failed its integrity check.");
        }

        Log.Information("Installing {Driver} {Version}", pkg.DisplayName, pkg.Version);
        var psi = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = dir,
        };
        // Advanced Installer bootstrapper: no bootstrapper UI, quiet MSI, never reboot by itself.
        foreach (var a in new[] { "/exenoui", "/qn", "/norestart" }) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        if (!p.WaitForExit(InstallerTimeout))
        {
            try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return new(pkg.Id, -1, false, false, $"{pkg.DisplayName} setup timed out.");
        }
        int code = p.ExitCode;
        Log.Information("{Driver} installer exited with {Code}", pkg.DisplayName, code);
        return Interpret(pkg, code);
    }

    internal static DriverInstallStep Interpret(DriverPackage pkg, int code) => code switch
    {
        0 => new(pkg.Id, code, true, false, $"{pkg.DisplayName} installed."),
        3010 or 1641 => new(pkg.Id, code, true, true, $"{pkg.DisplayName} installed — a restart is needed to finish."),
        1638 => new(pkg.Id, code, true, false, $"{pkg.DisplayName} is already installed."),
        1602 => new(pkg.Id, code, false, false, $"{pkg.DisplayName} setup was cancelled."),
        1618 => new(pkg.Id, code, false, false, "Another installation is in progress. Wait for it to finish and try again."),
        _ => new(pkg.Id, code, false, false, $"{pkg.DisplayName} setup failed (code {code}). Restart Windows and try again."),
    };

    private static void RegisterWithHidHide()
    {
        // Let reController itself keep seeing hidden controllers. The driver may only be fully active after a reboot;
        // failure here is harmless because the HidHide panel registers the app again when applying.
        try
        {
            string? cli = HidHideClient.FindCli();
            if (cli is null) return;
            var psi = new ProcessStartInfo(cli) { UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("--app-reg");
            psi.ArgumentList.Add(Environment.ProcessPath!);
            using var p = Process.Start(psi);
            p?.WaitForExit(15_000);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not register reController with HidHide yet");
        }
    }

    /// <summary>A fresh directory writable only by Administrators and SYSTEM (prevents swapping the installer).</summary>
    private static string CreateSecureDirectory()
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "reController", "setup");
        Directory.CreateDirectory(root);
        string dir = Path.Combine(root, Guid.NewGuid().ToString("N"));
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.SetOwner(admins);
        new DirectoryInfo(dir).Create(security);
        return dir;
    }
}
