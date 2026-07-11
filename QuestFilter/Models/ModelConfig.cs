//ModelConfig.cs

using System.Text.Json.Serialization;

namespace QuestFilterMod.QuestFilter.Models;

public class LocationQuestConfig
{
    public int Any { get; set; } = 0;
    public int Bigmap { get; set; } = 0;
    public int Factory4Day { get; set; } = 0;
    public int Factory4Night { get; set; } = 0;
    public int Interchange { get; set; } = 0;
    public int Laboratory { get; set; } = 0;
    public int Lighthouse { get; set; } = 0;
    public int RezervBase { get; set; } = 0;
    public int Sandbox { get; set; } = 0;
    public int SandboxHigh { get; set; } = 0;
    public int Shoreline { get; set; } = 0;
    public int TarkovStreets { get; set; } = 0;
    public int Woods { get; set; } = 0;
    public int Labyrinth { get; set; } = 0;
}


public class RandomQuestsConfig
{
    [JsonPropertyName("count")]
    public int Count { get; set; } = 5;
    [JsonPropertyName("location")]
    public LocationQuestConfig Location { get; set; } = new();
}

public class GenerateRandomQuestsConfig
{
    [JsonPropertyName("enable")]
    public bool Enable { get; set; } = false;
    [JsonPropertyName("count")]
    public int Count { get; set; } = 3;
}

public class QuestFilterLinkedQuest
{
    public bool Enable { get; set; } = false;
    public int[] StartQuest { get; set; } = [1, 3];
    public int[] QuestFinish { get; set; } = [2, 4];
}


public class QuestFilterModifyBaseQuest
{
    public bool Enabled { get; set; } = false;
    public string[] Type { get; set; } = null;
    public int[] CountCond { get; set; } = [1, 3];
}

public class SkipQuestConfig
{
    /// <summary>
    /// Список ID торговцев, квесты от которых будут пропущены из модификации.
    /// </summary>
    public List<string> Traider { get; set; } = new();

    /// <summary>
    /// Список типов квестов, которые будут пропущены из модификации.
    /// </summary>
    public List<string> Types { get; set; } = new();
}


public class ModelConfig
{
    public bool Enabled { get; set; } = true;
    public string[] TargetTraderIds { get; set; } = null;
    public bool Debug { get; set; } = true;
    public bool CleanDroppedItems { get; set; } = true;
    public List<string> QuestTypes { get; set; } = new() { "PickUp" };
    public bool RemoveStandartQuests { get; set; } = false;
    public bool RemoveRepeatableQuests { get; set; } = false;
    public bool RemoveStartConditionsQuest { get; set; } = false;
    public bool ExcludeArenaQuests { get; set; } = true;
    public List<string> RemoveFinishConditionTypes { get; set; } = new();
    public RandomQuestsConfig RandomQuests { get; set; } = new();
    public GenerateRandomQuestsConfig GenerateRandomQuests { get; set; } = new();
    public QuestFilterLinkedQuest LinkedQuest { get; set; } = null;
    public QuestFilterModifyBaseQuest ModifyBaseQuest { get; set; } = null;
    public SkipQuestConfig SkipQuest { get; set; } = new();
}