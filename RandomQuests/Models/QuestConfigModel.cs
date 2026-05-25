namespace QuestFilterMod.RandomQuests.Models
{
    public class LocationConfig
    {
        public string Id { get; set; } = "";
        public List<string> Targets { get; set; } = new();
    }

    public class RewardItemConfig
    {
        public string Name { get; set; } = "";
        public string Tpl { get; set; } = "";
        public int Count { get; set; }
    }

    public class DefaultQuestConfig
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Image { get; set; } = "";
        public int ExperienceReward { get; set; }
        public int FailAfterDays { get; set; }
    }

    public class QuestConfig
    {
        public Dictionary<string, LocationConfig> Locations { get; set; } = new();
        public List<RewardItemConfig> RewardItems { get; set; } = new();
        public DefaultQuestConfig DefaultQuest { get; set; } = new();
        public List<string> TraderIds { get; set; } = new();
    }
}