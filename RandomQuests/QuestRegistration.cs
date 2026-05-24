using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Mod;

namespace QuestFilterMod.RandomQuests
{
    public partial class RandomQuestGenerator
    {
        public void CreateAndRegisterQuest(Quest quest)
        {

            if (quest == null) return;

            EnsureRewards(quest);
            EnsureFailConditions(quest);

            try
            {
                var englishLocales = new Dictionary<string, string>();
                var russianLocales = new Dictionary<string, string>();

                FillQuestLocales(quest, englishLocales, russianLocales);

                var locales = new Dictionary<string, Dictionary<string, string>>
                {
                    ["en"] = englishLocales,
                    ["ru"] = russianLocales
                };

                var newQuestDetails = new NewQuestDetails
                {
                    NewQuest = quest,
                    Locales = locales,
                    LockedToSide = null
                };


                CreateQuestResult result = _customQuestService.CreateQuest(newQuestDetails);

                if (result.Success)
                {
#if DEBUG
                    if (Plugin._config.Debug)
                        _logger.Info($"[QuestFilterMod][QuestRegistration] ✅ Quest '{quest.Id}' has been successfully created and localized.");
#endif

                }
                else
                {
                    foreach (string error in result.Errors)
                    {
                        if (Plugin._config.Debug)
                            _logger.Error($"[QuestFilterMod][QuestRegistration] ❌ Error creating quest: {error}");
                    }
                }
            }
            catch (Exception ex)
            {
                if (Plugin._config.Debug)
                    _logger.Error($"[QuestFilterMod][QuestRegistration] 🔥 Exception when registering a quest: {ex}");
            }
        }
        private void EnsureRewards(Quest quest)
        {
            if (quest.Rewards == null)
                quest.Rewards = new Dictionary<string, List<Reward>>();

            foreach (var status in new[] { "Started", "Success", "Fail" })
            {
                if (!quest.Rewards.ContainsKey(status))
                    quest.Rewards[status] = new List<Reward>();
            }
        }
        private void EnsureFailConditions(Quest quest)
        {
            if (quest.Conditions?.Fail == null)
                quest.Conditions.Fail = new List<QuestCondition>();
        }
        private static string GetExtValue(QuestCondition cond, string key)
        {
            return cond.ExtensionData?.TryGetValue(key, out var v) == true ? v?.ToString() : null;
        }
        private List<QuestCondition> GetConditions(Quest quest)
        {
            var list = new List<QuestCondition>();
            if (quest.Conditions?.AvailableForStart != null) list.AddRange(quest.Conditions.AvailableForStart);
            if (quest.Conditions?.AvailableForFinish != null) list.AddRange(quest.Conditions.AvailableForFinish);
            if (quest.Conditions?.Fail != null) list.AddRange(quest.Conditions.Fail);
            return list;
        }
    }
}
