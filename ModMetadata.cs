using SPTarkov.Server.Core.Models.Spt.Mod;
using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;

namespace QuestFilterMod;

public class MyModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.cccpvs.QuestFilterMod";
    public string Name { get; init; } = "QuestFilterMod";
    public string Author { get; init; } = "cccpvs";
    public List<string> Contributors { get; init; } = null;
    public Version Version { get; init; } = new Version("1.0.5");
    public Range SptVersion { get; init; } = new Range("~4.1.2");
    public bool HasPrepatcher { get; init; } = false;
    public List<string> Incompatibilities { get; init; } = null;
    public Dictionary<string, Range> ModDependencies { get; init; } = null;
    public string Url { get; init; } = null;
    public string License { get; init; } = "MIT";
}