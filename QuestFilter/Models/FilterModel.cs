using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using QuestFilterMod.QuestFilter.Models;

namespace QuestFilterMod.QuestFilter
{
    public partial class FilterService
    {
        
        private void EnsureRewardStatuses(Quest quest)
        {
            if (quest.Rewards == null)
                quest.Rewards = new Dictionary<string, List<Reward>>();

            foreach (var status in new[] { "Started", "Success", "Fail" })
            {
                if (!quest.Rewards.ContainsKey(status))
                {
                    quest.Rewards[status] = new List<Reward>();
                    filter_Reward++;
                    if (Plugin.Config.Debug)
                        _logger.Info($"[QuestFilterMod][Modify] ⚠️ Reward status restored: '{status}' for quest '{quest.Id}'");
                }
            }
        }
        private void ModifyQuestTrader(Quest quest, ModelConfig config, Random random)
        {
            if (config.TargetTraderIds?.Length > 0)
            {
                string selectedTraderId = config.TargetTraderIds[random.Next(config.TargetTraderIds.Length)];
                quest.TraderId = selectedTraderId;
                filter_Trader++;
                if (Plugin.Config.Debug)
                    _logger.Info($"[QuestFilterMod][Modify] Quest '{quest.Name}' ({quest.Id}) → trader {selectedTraderId}");
            }
        }
        private void RemoveStartConditions(Quest quest, ModelConfig config)
        {
            if (config.RemoveStartConditionsQuest && quest.Conditions?.AvailableForStart != null && !config.LinkedQuest.Enable)
            {
                quest.Conditions.AvailableForStart.Clear();
                filter_DelStart++;
                if (Plugin.Config.Debug)
                    _logger.Info($"[QuestFilterMod][Modify] Start conditions removed for quest '{quest.Name}'");
            }
        }
        private void RemoveFinishConditions(Quest quest, List<string> typesToRemove)
        {
            if (quest.Conditions?.AvailableForFinish == null || !typesToRemove.Any()) return;
            filter_DelFinish++;
            var toRemove = new HashSet<string>(typesToRemove, StringComparer.OrdinalIgnoreCase);
            for (int i = quest.Conditions.AvailableForFinish.Count - 1; i >= 0; i--)
            {
                var condition = quest.Conditions.AvailableForFinish[i];
                string conditionType = condition.ConditionType;
                string typeValue = condition.Type;

                bool shouldRemove =
                    (!string.IsNullOrEmpty(conditionType) && toRemove.Contains(conditionType)) ||
                    (!string.IsNullOrEmpty(typeValue) && toRemove.Contains(typeValue));

                if (shouldRemove)
                {
                    quest.Conditions.AvailableForFinish.RemoveAt(i);
                    if (Plugin.Config.Debug)
                        _logger.Info($"[QuestFilterMod][Modify] Removed condition: conditionType='{conditionType ?? "null"}', type='{typeValue ?? "null"}'");
                    continue;
                }

                if (conditionType == "CounterCreator" && condition.Counter?.Conditions != null)
                {
                    var beforeCount = condition.Counter.Conditions.Count;
                    condition.Counter.Conditions.RemoveAll(inner =>
                    {
                        string innerType = inner.ConditionType;
                        bool shouldRemoveInner = !string.IsNullOrEmpty(innerType) && toRemove.Contains(innerType);
                        if (shouldRemoveInner && Plugin.Config.Debug)
                            _logger.Info($"[QuestFilterMod][Modify] Removed nested condition: conditionType='{innerType}'");
                        return shouldRemoveInner;
                    });

                    var afterCount = condition.Counter.Conditions.Count;
                    if (beforeCount > afterCount && Plugin.Config.Debug)
                        _logger.Info($"[QuestFilterMod][Modify] Removed {beforeCount - afterCount} nested conditions from CounterCreator in quest '{quest.Id}', remaining = {afterCount}");

                    if (afterCount == 0)
                    {
                        quest.Conditions.AvailableForFinish.RemoveAt(i);
                        if (Plugin.Config.Debug)
                            _logger.Info($"[QuestFilterMod][Modify] CounterCreator became empty → removed from quest '{quest.Id}'");
                    }
                }
            }
        }
        private void AddRandomFinishConditions(Quest quest, ModelConfig config, Random random)
        {
            if (!config.ModifyBaseQuest?.Enabled ?? true) return;


            var countCond = config.ModifyBaseQuest?.CountCond;
            int min = countCond?.Length > 0 ? countCond[0] : 1;
            int max = countCond?.Length > 1 ? countCond[1] : min;
            if (min <= 0) min = 1;
            if (max < min) (min, max) = (max, min);
            int count = random.Next(min, max + 1);

            if (quest.Conditions?.AvailableForFinish == null)
                quest.Conditions.AvailableForFinish = new List<QuestCondition>();

            int addedCount = 0;
            for (int i = 0; i < count; i++)
            {
                var condition2 = GenerateRandomFinishCondition(random, () => new MongoId());
                if (condition2 != null)
                {
                    quest.Conditions.AvailableForFinish.Add(condition2);
                    addedCount++;
                }
                else if (Plugin.Config.Debug)
                {
                    _logger.Warning($"[QuestFilterMod][Modify] Failed to generate condition #{i + 1} for quest '{quest.Name}' ({quest.Id})");
                }
            }

            var locales = new Dictionary<string, Dictionary<string, string>>();
            _randomQuestGenerator.FillQuestLocales(quest, locales);
            AddConditionLocales(locales);

            filter_Modify++;
            if (Plugin.Config.Debug)
                _logger.Info($"[QuestFilterMod][Modify] ✅ Added {count} random finish conditions to '{quest.Name}' ({quest.Id})");
        }
        private bool ShouldExcludeByProgressSource(Quest quest, ModelConfig config)
        {
            if (!config.ExcludeArenaQuests) return false;
            return string.Equals(quest.ProgressSource, "arena", StringComparison.OrdinalIgnoreCase);
        }
    }

}
