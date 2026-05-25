using System.Text.Json.Serialization;

namespace QuestFilterMod.QuestFilter;

public class LocationQuestConfig
{
    [JsonPropertyName("any")] public int Any { get; set; } = 0;
    [JsonPropertyName("bigmap")] public int Bigmap { get; set; } = 0;
    [JsonPropertyName("factory4_day")] public int Factory4Day { get; set; } = 0;
    [JsonPropertyName("factory4_night")] public int Factory4Night { get; set; } = 0;
    [JsonPropertyName("interchange")] public int Interchange { get; set; } = 0;
    [JsonPropertyName("laboratory")] public int Laboratory { get; set; } = 0;
    [JsonPropertyName("lighthouse")] public int Lighthouse { get; set; } = 0;
    [JsonPropertyName("rezervbase")] public int RezervBase { get; set; } = 0;
    [JsonPropertyName("sandbox")] public int Sandbox { get; set; } = 0;
    [JsonPropertyName("sandbox_high")] public int SandboxHigh { get; set; } = 0;
    [JsonPropertyName("shoreline")] public int Shoreline { get; set; } = 0;
    [JsonPropertyName("tarkovstreets")] public int TarkovStreets { get; set; } = 0;
    [JsonPropertyName("woods")] public int Woods { get; set; } = 0;
    [JsonPropertyName("labyrinth")] public int Labyrinth { get; set; } = 0;
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

    [JsonPropertyName("onlyRandom")]
    public bool OnlyRandom { get; set; } = false;
}

public class QuestFilterConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("targetTraderId")]
    public string TargetTraderId { get; set; } = "";

    [JsonPropertyName("debug")]
    public bool Debug { get; set; } = true;

    [JsonPropertyName("questTypes")]
    public List<string> QuestTypes { get; set; } = new() { "PickUp" };

    [JsonPropertyName("removeOtherQuests")]
    public bool RemoveOtherQuests { get; set; } = false;

    [JsonPropertyName("removeStartConditions")]
    public bool RemoveStartConditions { get; set; } = false;

    [JsonPropertyName("excludeArenaQuests")]
    public bool ExcludeArenaQuests { get; set; } = true;

    [JsonPropertyName("removeFinishConditionTypes")]
    public List<string> RemoveFinishConditionTypes { get; set; } = new();

    [JsonPropertyName("randomQuests")]
    public RandomQuestsConfig RandomQuests { get; set; } = new();

    [JsonPropertyName("generateRandomQuests")]
    public GenerateRandomQuestsConfig GenerateRandomQuests { get; set; } = new();
}