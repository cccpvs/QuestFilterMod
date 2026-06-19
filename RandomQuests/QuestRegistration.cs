using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
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
                        _logger.Info($"[QuestFilterMod][QuestRegistration] ✅ Quest '{quest.Id}' has been successfully created and localized.");
#endif

#if DEBUG
                    //Проба исрправить ошибку квеста предачи предмета из рейда
                    /***
                     * 
                     * 
                     * Временное исправление не помогло
                     * 
                     * 
                     * 
                     * */
                    /*var allProfiles = _saveServer.GetProfiles();
                    foreach (var kvp in allProfiles)
                    {
                        var profile = kvp.Value;
                        var pmcData = profile.CharacterData?.PmcData;

                        if (pmcData?.Quests != null)
                        {
                            if (!pmcData.Quests.Any(qs => qs.QId == quest.Id))
                            {
                                // Создаём QuestStatus с обязательным StatusTimers
                                var questStatus = new QuestStatus
                                {
                                    QId = quest.Id,
                                    Status = SPTarkov.Server.Core.Models.Enums.QuestStatusEnum.Started,
                                    StartTime = (long)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                                    CompletedConditions = new List<string>(),
                                    StatusTimers = new Dictionary<QuestStatusEnum, double>
                                    {
                                        [QuestStatusEnum.Started] = (double)DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                                    }
                                };

                                pmcData.Quests.Add(questStatus);
                                _logger.Info($"[QuestFilterMod][QuestRegistration] ✅ QuestStatus '{quest.Id}' added to profile '{kvp.Key}'.");
                            }
                        }
                    }*/
#endif


                }
                else
                {
                    foreach (string error in result.Errors)
                    {
                        if (Plugin.Config.Debug)
                            _logger.Error($"[QuestFilterMod][QuestRegistration] ❌ Error creating quest: {error}");
                    }
                }
            }
            catch (Exception ex)
            {
                if (Plugin.Config.Debug)
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
