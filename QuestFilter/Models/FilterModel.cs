using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using QuestFilterMod.QuestFilter.Models;

namespace QuestFilterMod.QuestFilter
{
    public partial class FilterService
    {
        // <summary>
        /// Ensures that a quest has reward entries for "Started", "Success", and "Fail" statuses.
        /// Initializes empty reward lists if missing.
        /// </summary>
        /// <param name="quest">Quest to inspect and augment.</param>

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

        /// <summary>
        /// Overrides the quest’s assigned trader with a random one from the configured list.
        /// Does nothing if no trader IDs are specified in config.
        /// </summary>
        /// <param name="quest">Quest to modify.</param>
        /// <param name="config">Configuration containing allowed trader IDs.</param>
        /// <param name="random">Random instance for selection.</param>

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

        /// <summary>
        /// Removes all start conditions (AvailableForStart) from the quest, if enabled in config and not part of a linked chain.
        /// </summary>
        /// <param name="quest">Quest to modify.</param>
        /// <param name="config">Configuration flag for removal.</param>
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

        /// <summary>
        /// Removes finish conditions (AvailableForFinish) matching specified types (e.g., "FindItem", "LeaveItemAtLocation").
        /// Also handles nested conditions inside CounterCreator (removes inner conditions, then deletes empty CounterCreator).
        /// </summary>
        /// <param name="quest">Quest to modify.</param>
        /// <param name="typesToRemove">List of condition types or nested condition types to remove (case-insensitive).</param>

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

        /// <summary>
        /// Adds a random number of new finish conditions to the quest (min–max count from config).
        /// Each condition is randomly generated (Exploration, Elimination, Delivery, Transfer, or Beacon).
        /// After modification, re-loads and registers localized descriptions for the quest.
        /// </summary>
        /// <param name="quest">Quest to modify.</param>
        /// <param name="config">Configuration specifying number of conditions to add.</param>
        /// <param name="random">Random instance for condition selection and generation.</param>

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
        /// <summary>
        /// Checks whether a quest should be excluded based on its ProgressSource (e.g., "arena").
        /// Used to filter out arena-specific quests when config.ExcludeArenaQuests is enabled.
        /// </summary>
        /// <param name="quest">Quest to evaluate.</param>
        /// <param name="config">Configuration flag for exclusion.</param>
        /// <returns>True if the quest should be excluded; otherwise false.</returns>

        private bool ShouldExcludeByProgressSource(Quest quest, ModelConfig config)
        {
            if (!config.ExcludeArenaQuests) return false;
            return string.Equals(quest.ProgressSource, "arena", StringComparison.OrdinalIgnoreCase);
        }


        /// <summary>
        /// Determines whether a quest should be skipped during filtering and modification (i.e., left untouched).
        /// A quest is skipped if it matches any of the following criteria:
        /// - Its trader ID is listed in <see cref="SkipQuestConfig.Traider"/>.
        /// - Its quest type (enum) is listed in <see cref="SkipQuestConfig.Types"/>.
        /// 
        /// Note: This check is independent of random quest handling — random-generated quests are skipped separately via <see cref="_randomQuestIds"/>.
        /// </summary>
        /// <param name="quest">The quest to evaluate for skipping.</param>
        /// <param name="config">The current filter configuration.</param>
        /// <returns>
        /// <c>true</c> if the quest should be skipped (i.e., no modifications applied);
        /// <c>false</c> otherwise (i.e., normal modification proceeds unless it's a random quest).
        /// </returns>
        /// <example>
        /// Example configuration in config JSON:
        /// <code>
        /// "SkipQuest": {
        ///   "Traider": ["579dc531d53a0658a154be4f", "579dc531d53a0658a154be50"],
        ///   "Types": ["PickUp", "MainQuest"]
        /// }
        /// </code>
        /// In this example, all quests from the listed traders and of the listed types will be skipped.
        /// </example>
        private bool ShouldSkipQuest(Quest quest, SkipQuestConfig skipConfig)
        {
            if (skipConfig == null)
                return false;

            if (skipConfig.Traider?.Count > 0)
            {
                if (skipConfig.Traider.Contains(quest.TraderId))
                {
                    if (Plugin.Config.Debug)
                        _logger.Info($"[QuestFilterMod][ShouldSkipQuest] ✓ Skipping: quest '{quest.Name}' ({quest.Id}) — trader '{quest.TraderId}' is in exclusion list.");

                    return true;
                }
            }
            if (skipConfig.Types?.Count > 0)
            {
                string questTypeStr = quest.Type.ToString(); 
                if (skipConfig.Types.Any(t => t == questTypeStr))
                {
                    if (Plugin.Config.Debug)
                        _logger.Info($"[QuestFilterMod][ShouldSkipQuest] ✓ Skipping: quest '{quest.Name}' ({quest.Id}) — type '{questTypeStr}' is in exclusion list.");
                    return true;
                }
            }

            return false;
        }


    }

}
