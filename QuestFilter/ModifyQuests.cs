using QuestFilterMod.QuestFilter.Models;
using QuestFilterMod.RandomQuests;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace QuestFilterMod.QuestFilter
{
    public partial class QuestFilterService
    {
        private void ModifyQuests(Dictionary<MongoId, Quest> allQuests,List<Quest> selectedQuests,QuestFilterConfig config,Random random)
        {
            var selectedIds = selectedQuests.Select(q => q.Id).ToHashSet();

            var countRemoveQuest = 0;
            if (config.RemoveStandartQuests)
            {
                var toRemove = allQuests.Values
                    .Where(q => !selectedIds.Contains(q.Id))
                    .Where(q => !_randomQuestIds.Contains(q.Id))
                    .ToList();

                foreach (var q in toRemove)
                {
                    allQuests.Remove(q.Id);
                    if (config.Debug)
                        countRemoveQuest++;
                }
            }
            if (Plugin.Config.Debug)
                _logger.Info($"[QuestFilterMod][QuestFilterService] Total deleted: {countRemoveQuest}");

            var countTraiderTransfer = 0;
            foreach (var q in selectedQuests)
            {
                if (q.Rewards == null)
                    q.Rewards = new Dictionary<string, List<Reward>>();

                foreach (var status in new[] { "Started", "Success", "Fail" })
                {
                    if (!q.Rewards.ContainsKey(status))
                    {
                        q.Rewards[status] = new List<Reward>();
                        if (Plugin.Config.Debug)
                            _logger.Info($"[QuestFilterMod][QuestFilterService] ⚠️ Reward status restored: '{status}' for the quest '{q.Id}'");
                    }
                }

                if (config.TargetTraderIds?.Length > 0)
                {
                    if (config.TargetTraderIds?.Length > 0)
                    {
                        string selectedTraderId = config.TargetTraderIds[random.Next(config.TargetTraderIds.Length)];
                        q.TraderId = selectedTraderId;

                        if (Plugin.Config.Debug)
                            _logger.Info($"[QuestFilterMod][QuestFilterService] Quest '{q.Name}' ({q.Id}) → trader {selectedTraderId}");
                    }
                    countTraiderTransfer = selectedQuests.Count;
                }

                if (config.RemoveStartConditionsQuest && q.Conditions?.AvailableForStart != null)
                {
                    q.Conditions.AvailableForStart.Clear();
                    if (Plugin.Config.Debug)
                        _logger.Info($"[QuestFilterMod][QuestFilterService] Start conditions have been removed for the quest '{q.Name}'");
                }

                if (config.RemoveFinishConditionTypes?.Count > 0 && q.Conditions?.AvailableForFinish != null)
                {
                    var toRemove = new List<QuestCondition>();
                    foreach (var condition in q.Conditions.AvailableForFinish.ToList())
                    {
                        string? checkType = condition.ConditionType.ToString() == "CounterCreator"
                            ? condition.Type
                            : condition.ConditionType.ToString();

                        if (!string.IsNullOrEmpty(checkType) &&
                            config.RemoveFinishConditionTypes.Contains(checkType, StringComparer.OrdinalIgnoreCase))
                        {
                            toRemove.Add(condition);
#if DEBUG
                        if (Plugin.Config.Debug)
                            _logger.Info($"[QuestFilterMod][QuestFilterService] Condition removed '{checkType}' from the quest '{q.Id}'");
#endif
                        }
                    }

                    foreach (var cond in toRemove)
                    {
                        q.Conditions.AvailableForFinish.Remove(cond);
                    }
                }
            }

            if (Plugin.Config.Debug)
            {
                var locationStats = new Dictionary<string, int>();
                var locationDetails = new List<string>();

                _logger.Info($"[QuestFilterMod][QuestFilterService] Trader quests moved: {countTraiderTransfer}");

                foreach (var kvp in locationStats.OrderBy(x => x.Key))
                {
                    _logger.Info($"[QuestFilterMod][QuestFilterService]  • {kvp.Key}: {kvp.Value} шт.");
                }
                foreach (var quest in selectedQuests)
                {
                    string locKey = LocationHelper.TryGetPascalName(quest.Location, out var pascalName)
                        ? pascalName.ToLowerInvariant()
                        : "unknown";
                    locationStats[locKey] = locationStats.GetValueOrDefault(locKey, 0) + 1;
                    locationDetails.Add($"[QuestFilterMod][QuestFilterService] Quest '{quest.Name}' ({quest.Id}) → location '{locKey}'");
                }
                _logger.Info($"[QuestFilterMod][QuestFilterService] Total quests left: {allQuests.Count}");

            }
        }
    }
}
