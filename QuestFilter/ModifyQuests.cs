using QuestFilterMod.QuestFilter.Models;
using QuestFilterMod.RandomQuests;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Logging;


namespace QuestFilterMod.QuestFilter
{
    public partial class QuestFilterService
    {
        int q_deleted = 0;
        int q_moved = 0;
        int q_left = 0;
        int q_random = 0;

        private void ModifyQuests(Dictionary<MongoId, Quest> allQuests,List<Quest> selectedQuests,QuestFilterConfig config, Random random)
        {
            var selectedIds = selectedQuests.Select(q => q.Id).ToHashSet();


            if (config.RemoveStandartQuests)
            {
                var toRemove = allQuests.Values
                    .Where(q => !selectedIds.Contains(q.Id))
                    .Where(q => !_randomQuestIds.Contains(q.Id))
                    .ToList();

                foreach (var q in toRemove)
                {
                    allQuests.Remove(q.Id);
                    q_deleted++;
                }
            }
            
            //if (Plugin.Config.Debug)
                //_logger.Info($"[QuestFilterMod][ModifyQuests] Total deleted: {countRemoveQuest}");

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
                            _logger.Info($"[QuestFilterMod][ModifyQuests] ⚠️ Reward status restored: '{status}' for the quest '{q.Id}'");
                    }
                }

                if (config.TargetTraderIds?.Length > 0)
                {
                    if (config.TargetTraderIds?.Length > 0)
                    {
                        string selectedTraderId = config.TargetTraderIds[random.Next(config.TargetTraderIds.Length)];
                        q.TraderId = selectedTraderId;
                        q_moved++;
                        if (Plugin.Config.Debug)
                            _logger.Info($"[QuestFilterMod][ModifyQuests] Quest '{q.Name}' ({q.Id}) → trader {selectedTraderId}");
                    }
                    
                    
                }

                if (config.RemoveStartConditionsQuest && q.Conditions?.AvailableForStart != null)
                {
                    q.Conditions.AvailableForStart.Clear();
                    if (Plugin.Config.Debug)
                        _logger.Info($"[QuestFilterMod][ModifyQuests] Start conditions have been removed for the quest '{q.Name}'");
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
                            _logger.Info($"[QuestFilterMod][ModifyQuests] Condition removed '{checkType}' from the quest '{q.Id}'");
#endif
                        }
                    }

                    foreach (var cond in toRemove)
                    {
                        q.Conditions.AvailableForFinish.Remove(cond);
                    }
                }
            }
            
            q_left = allQuests.Count;
            if (Plugin.Config.Debug)
            {
                var locationStats = new Dictionary<string, int>();
                var locationDetails = new List<string>();
                
                //_logger.Info($"[QuestFilterMod][ModifyQuests] Trader quests moved: {countTraiderTransfer}");

                foreach (var kvp in locationStats.OrderBy(x => x.Key))
                {
                    _logger.Info($"[QuestFilterMod][ModifyQuests]  • {kvp.Key}: {kvp.Value} count.");
                }
                foreach (var quest in selectedQuests)
                {
                    string locKey = LocationHelper.TryGetPascalName(quest.Location, out var pascalName)
                        ? pascalName.ToLowerInvariant()
                        : "unknown";
                    locationStats[locKey] = locationStats.GetValueOrDefault(locKey, 0) + 1;
                    locationDetails.Add($"[QuestFilterMod][ModifyQuests] Quest '{quest.Name}' ({quest.Id}) → location '{locKey}'");
                }
                
                //_logger.Info($"[QuestFilterMod][ModifyQuests] Total quests left: {allQuests.Count}");

            }
            _logger.Warning($"|🗑️{"Deleted",-11} |➡️{"Moved",-11} |🎲{"Random",-11} |✅{"Left",-11} |");
            _logger.Warning($"-------------------------------------------------------------");
            _logger.Warning($"| {q_deleted,-12} | {q_moved,-12} | {q_random,-12} | {q_left,-12} |");
        }
    }
}
