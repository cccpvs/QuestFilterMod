using SPTarkov.Server.Core.Models.Spt.Mod;
using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;

namespace QuestFilterMod;

public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.cccpvs.QuestFilterMod";
    public override string Name { get; init; } = "QuestFilterMod";
    public override string Author { get; init; } = "cccpvs";
    public override List<string>? Contributors { get; init; }
    public override Version Version { get; init; } = new("1.0.2");
    public override Range SptVersion { get; init; } = new("~4.0.13");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, Range>? ModDependencies { get; init; } = new();
    public override string? Url { get; init; }
    public override bool? IsBundleMod { get; init; } = false;
    public override string License { get; init; } = "MIT";

}

#if DEBUG
/*

Процесс разработки и идей.
--------------------------------------------------------------------------------
			
? Новые способы и механики
? Стандартные квесты. Изменение условий выполнения.? Идея подписчика.
? Добавление к стандартным квестам, дополнительные задания, поиск, посещение, закладку.
? Стандартные квесты, увелечение выполненой задачи, или частичное изменение.

весты для каждого игрока разные и не повторяющие.
огика сохранения квестов в базе, при первом запуске. Без повторяющих квестов повторный.


*/
#endif