using ReController.Core.Mapping;

namespace ReController.Core.Profiles;

/// <summary>A game (or app) with one or more mapping configs.</summary>
public sealed class GameProfile
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New game";

    /// <summary>Executable file names (e.g. "EliteDangerous64.exe") used for automatic profile switching.</summary>
    public List<string> Executables { get; set; } = [];

    public List<MappingConfig> Configs { get; set; } = [];

    public DateTimeOffset Modified { get; set; } = DateTimeOffset.UtcNow;

    public static GameProfile Create(string name)
    {
        var p = new GameProfile { Name = name };
        p.Normalize();
        return p;
    }

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(Name)) Name = "Untitled";
        Executables ??= [];
        Executables = Executables
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => Path.GetFileName(e.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        Configs ??= [];
        Configs.RemoveAll(c => c is null);
        if (Configs.Count == 0) Configs.Add(MappingConfig.CreateDefault());
        foreach (var c in Configs) c.Normalize();
        // Config ids must be unique inside a profile (duplicated imports, hand edits).
        var seen = new HashSet<Guid>();
        foreach (var c in Configs)
        {
            if (!seen.Add(c.Id)) { c.Id = Guid.NewGuid(); seen.Add(c.Id); }
        }
        SchemaVersion = CurrentSchemaVersion;
    }

    public MappingConfig? FindConfig(Guid id) => Configs.Find(c => c.Id == id);

    public bool MatchesExecutable(string exePathOrName)
    {
        string name = Path.GetFileName(exePathOrName);
        return Executables.Exists(e => string.Equals(e, name, StringComparison.OrdinalIgnoreCase));
    }
}
