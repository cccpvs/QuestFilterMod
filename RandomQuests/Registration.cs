//Registration.cs

using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Mod;

namespace QuestFilterMod.RandomQuests
{
    public partial class Generator
    {
        public void CreateAndRegisterQuest(Quest quest)
        {

            if (quest == null) return;

            EnsureRewards(quest);
            EnsureFailConditions(quest);

            try
            {
                var locales = new Dictionary<string, Dictionary<string, string>>();
                FillQuestLocales(quest, locales);

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
                    if (Plugin.Config.Debug)
                        _logger.Info($"[QuestFilterMod][Registration] ✅ Quest '{quest.Id}' has been successfully created and localized.");
#endif
                }
                else
                {
                    foreach (string error in result.Errors)
                    {
                        if (Plugin.Config.Debug)
                            _logger.Error($"[QuestFilterMod][Registration] ❌ Error creating quest: {error}");
                    }
                }
            }
            catch (Exception ex)
            {
                if (Plugin.Config.Debug)
                    _logger.Error($"[QuestFilterMod][Registration] 🔥 Exception when registering a quest: {ex}");
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
    }
}
