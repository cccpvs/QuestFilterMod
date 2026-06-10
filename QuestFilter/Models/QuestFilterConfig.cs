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

public class QuestFilterConfig
{
    public bool Enabled { get; set; } = true;
    public string[]? TargetTraderIds { get; set; } = null;
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
}