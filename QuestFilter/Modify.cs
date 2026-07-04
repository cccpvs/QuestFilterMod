//Modify.cs

using QuestFilterMod.QuestFilter.Models;
using QuestFilterMod.RandomQuests;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;


namespace QuestFilterMod.QuestFilter
{
    public partial class FilterService
    {

        private void ModifyQuests(List<Quest> OriginalQuestList, ModelConfig config, Random random)
        {
            var selectedIds = OriginalQuestList.Select(q => q.Id).ToHashSet();
            if (OriginalQuestList.Any() && OriginalQuestList.All(q => _randomQuestIds.Contains(q.Id)))
            {
                if (Plugin.Config.Debug)
                    _logger.Info("[QuestFilterMod][Modify] Skip filter application: all quests are random-generated.");
                return;
            }

            foreach (var q in OriginalQuestList)
            {
                if (!_randomQuestIds.Contains(q.Id))
                {
                    EnsureRewardStatuses(q);
                    ModifyQuestTrader(q, config, random);
                    RemoveStartConditions(q, config);
                    RemoveFinishConditions(q, config.RemoveFinishConditionTypes);
                    AddRandomFinishConditions(q, config, random);
                }
                else if (Plugin.Config.Debug)
                {
                    _logger.Info($"[QuestFilterMod][Modify] Skip modification: quest '{q.Name}' is random-generated.");
                }
            }

            if (Plugin.Config.Debug)
            {
                var locationStats = new Dictionary<string, int>();
                foreach (var quest in OriginalQuestList)
                {
                    string locKey = Location.TryGetPascalName(quest.Location, out var pascalName)
                        ? pascalName.ToLowerInvariant()
                        : "unknown";
                    locationStats[locKey] = locationStats.GetValueOrDefault(locKey, 0) + 1;
                }

                foreach (var kvp in locationStats)
#if DEBUG
                    if (Plugin.Config.Debug)
                        _logger.Info($"[QuestFilterMod][Modify]  • {kvp.Key}: {kvp.Value} quests");
#endif
            }
        }
    }
}