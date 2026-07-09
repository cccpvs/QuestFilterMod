//Registration.cs

using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Mod;

namespace QuestFilterMod.RandomQuests
{
    public partial class Generator
    {

        /// <summary>
        /// Registers a generated quest in the game's quest database, including its localized descriptions.
        /// Handles rewards initialization, fail conditions, and error logging.
        /// </summary>
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
        
        /// <summary>
        /// Ensures that the quest has reward entries for all required statuses: "Started", "Success", and "Fail".
        /// Initializes empty lists if missing.
        /// </summary>
        /// <param name="quest">Quest to inspect and augment.</param>

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

        /// <summary>
        /// Ensures that the quest has a non-null Fail conditions list.
        /// Initializes an empty list if Fail conditions are missing.
        /// </summary>
        /// <param name="quest">Quest to inspect and augment.</param>
        private void EnsureFailConditions(Quest quest)
        {
            if (quest.Conditions?.Fail == null)
                quest.Conditions.Fail = new List<QuestCondition>();
        }
    }
}
