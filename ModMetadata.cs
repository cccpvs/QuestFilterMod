using SPTarkov.Server.Core.Models.Spt.Mod;
using Version = SemanticVersioning.Version;
using Range = SemanticVersioning.Range;

namespace QuestFilterMod;

public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.cccpvs.QuestFilterMod"; 
    public override string Name { get; init; } = "QuestFilterMod";
    public override string Author { get; init; } = "cccpvs";
    public override List<string>? Contributors { get; init; }
    public override Version Version { get; init; } = new("1.0.1");
    public override Range SptVersion { get; init; } = new("~4.0.13");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, Range>? ModDependencies { get; init; } = new();
    public override string? Url { get; init; }
    public override bool? IsBundleMod { get; init; } = false;
    public override string License { get; init; } = "MIT";
    
}