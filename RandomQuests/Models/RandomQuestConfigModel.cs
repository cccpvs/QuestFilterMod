using System.Text.Json.Serialization;

namespace QuestFilterMod.RandomQuests.Models
{
    public class LocationConfig
    {
        public string Id { get; set; } = "";
        public List<string> Targets { get; set; } = new();
    }

    public class DefaultQuestConfig
    {
        public string Image { get; set; } = "";
        public ExperienceRange ExperienceRewardRange { get; set; } = new();
    }

    public class ExperienceRange
    {
        public int Min { get; set; } = 1000;
        public int Max { get; set; } = 5000;
        public int Step { get; set; } = 100;
    }

    public class MoneyRewardConfig
    {
        public bool Enabled { get; set; } = true;
        public string Tpl { get; set; } = "5449016a4bdc2d6f028b456f";
        public int Min { get; set; } = 50000;
        public int Max { get; set; } = 100000;
        public int Step { get; set; } = 10000;
    }

    public class RewardItemsConfig
    {
        public bool Enabled { get; set; } = true;
        public CountRange Count { get; set; } = new();
        public PriceRange PriceRange { get; set; } = new();
        [JsonPropertyName("Parents")]
        public ParentWeight[] Parents { get; set; } = Array.Empty<ParentWeight>();
    }
    public class ParentWeight
    {
        [JsonPropertyName("Id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("Weight")]
        public int Weight { get; set; } = 1; 
    }
    public class PriceRange
    {
        [JsonPropertyName("Min")]
        public int Min { get; set; } = 10000;

        [JsonPropertyName("Max")]
        public int Max { get; set; } = 100000;
    }
    public class TraderStandingRewardConfig
    {
        public bool Enabled { get; set; } = false;
        public float Min { get; set; } = 0.01f;
        public float Max { get; set; } = 0.03f;
    }

    public class CountRange
    {
        public int Min { get; set; } = 1;
        public int Max { get; set; } = 3;
    }

 
    public class QuestGenerationConfig
    {
        public QuestTypeFlags Types { get; set; } = new();
        public Dictionary<string, bool> AllowedLocations { get; set; } = new();
    }

    public class QuestTypeFlags
    {
        public bool Exploration { get; set; } = true;
        public bool Delivery { get; set; } = true;
        public bool Beacon { get; set; } = true;
        public bool Kills { get; set; } = true;
    }

    public class DeployQuestBaseConfig
    {
        public int PlantTime { get; set; } = 30000;
        public Dictionary<string, LocationConfig> Locations { get; set; } = new();
    }

    public class DeployQuestConfig : DeployQuestBaseConfig
    {
        public List<string> ItemPlant { get; set; } = new();
    }

    public class KillQuestConfig
    {
        public int MinKills { get; set; } = 5;
        public int MaxKills { get; set; } = 15;
        public string Target { get; set; } = "Any";
    }
    public class ZoneConfig
    {
        public string Location { get; set; } = "";
        public string Target { get; set; } = "";
        public int Weight { get; set; } = 1;
    }

    public class QuestConfig
    {
        public Dictionary<string, LocationConfig> ExplorationQuest { get; set; } = new();

        public List<string> TraderIds { get; set; } = new();
        public DefaultQuestConfig DefaultQuest { get; set; } = new();
        public MoneyRewardConfig RewardMoney { get; set; } = new();
        public RewardItemsConfig RewardItems { get; set; } = new();
        public DeployQuestConfig DeliveryQuest { get; set; } = new();
        public DeployQuestConfig BeaconQuest { get; set; } = new();
        public QuestGenerationConfig QuestGeneration { get; set; } = new();
        public KillQuestConfig KillQuest { get; set; } = new();
        public TraderStandingRewardConfig RewardTraderStanding { get; set; } = new();
    }
}