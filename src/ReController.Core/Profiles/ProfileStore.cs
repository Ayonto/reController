using System.Text.Json;

namespace ReController.Core.Profiles;

public sealed record ProfileLoadIssue(string File, string Message);

/// <summary>
/// Persists one JSON file per game profile. Corrupt files are quarantined (renamed to *.corrupt)
/// rather than crashing the app or being silently overwritten.
/// </summary>
public sealed class ProfileStore
{
    public const string FileExtension = ".rcprofile";
    private readonly object _gate = new();

    public ProfileStore(string directory)
    {
        Directory = directory;
        System.IO.Directory.CreateDirectory(directory);
    }

    public string Directory { get; }

    public (List<GameProfile> Profiles, List<ProfileLoadIssue> Issues) LoadAll()
    {
        var profiles = new List<GameProfile>();
        var issues = new List<ProfileLoadIssue>();
        lock (_gate)
        {
            foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*" + FileExtension))
            {
                try
                {
                    var p = Deserialize(File.ReadAllText(file));
                    if (profiles.Exists(x => x.Id == p.Id)) p.Id = Guid.NewGuid();
                    profiles.Add(p);
                }
                catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException or NotSupportedException)
                {
                    issues.Add(new ProfileLoadIssue(file, ex.Message));
                    Quarantine(file);
                }
            }
        }
        profiles.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return (profiles, issues);
    }

    public void Save(GameProfile profile)
    {
        profile.Normalize();
        profile.Modified = DateTimeOffset.UtcNow;
        string json = Serialize(profile);
        lock (_gate) AtomicFile.WriteAllText(PathFor(profile.Id), json);
    }

    public void Delete(Guid id)
    {
        lock (_gate)
        {
            string path = PathFor(id);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    public string PathFor(Guid id) => Path.Combine(Directory, id.ToString("N") + FileExtension);

    public static string Serialize(GameProfile profile) => JsonSerializer.Serialize(profile, JsonDefaults.Options);

    public static GameProfile Deserialize(string json)
    {
        var p = JsonSerializer.Deserialize<GameProfile>(json, JsonDefaults.Options)
                ?? throw new InvalidDataException("Profile file is empty.");
        if (p.SchemaVersion > GameProfile.CurrentSchemaVersion)
            throw new NotSupportedException($"Profile was created by a newer version of reController (schema {p.SchemaVersion}).");
        Migrate(p);
        p.Normalize();
        return p;
    }

    /// <summary>Exports a profile for sharing. Ids are regenerated on import so shares never collide.</summary>
    public static void Export(GameProfile profile, string path) => AtomicFile.WriteAllText(path, Serialize(profile));

    public static GameProfile Import(string path)
    {
        var p = Deserialize(File.ReadAllText(path));
        p.Id = Guid.NewGuid();
        foreach (var c in p.Configs) c.Id = Guid.NewGuid();
        return p;
    }

    private static void Migrate(GameProfile p)
    {
        // Schema 1 is the first public schema. Future migrations go here, keyed on p.SchemaVersion.
        _ = p;
    }

    private static void Quarantine(string file)
    {
        try
        {
            string target = file + $".{DateTime.UtcNow:yyyyMMddHHmmss}.corrupt";
            File.Move(file, target);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
