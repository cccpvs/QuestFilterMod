using QuestFilterMod.QuestFilter.Models;
using QuestFilterMod.RandomQuests;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;


namespace QuestFilterMod.QuestFilter
{
    public partial class FilterService
    {
        int q_deleted = 0;
        int q_moved = 0;
        int q_left = 0;
        int q_random = 0;

        private void ModifyQuests(Dictionary<MongoId, Quest> allQuests,List<Quest> selectedQuests,ModelConfig config, Random random)
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

                if (config.RemoveStartConditionsQuest && q.Conditions?.AvailableForStart != null && !config.LinkedQuest.Enable)
                {
                    q.Conditions.AvailableForStart.Clear();
                    if (Plugin.Config.Debug)
                        _logger.Info($"[QuestFilterMod][ModifyQuests] Start conditions have been removed for the quest '{q.Name}'");
                }

                if (config.RemoveFinishConditionTypes?.Count > 0 && q.Conditions?.AvailableForFinish != null)
                {
                    var toRemove = new HashSet<string>(config.RemoveFinishConditionTypes, StringComparer.OrdinalIgnoreCase);
#if DEBUG
                    _logger.Info($"[DEBUG] RemoveFinishConditionTypes count = {toRemove.Count}");
#endif
                    for (int i = q.Conditions.AvailableForFinish.Count - 1; i >= 0; i--)
                    {
                        var condition = q.Conditions.AvailableForFinish[i];
                        string conditionType = condition.ConditionType;
                        string typeValue = condition.Type;

                        bool shouldRemove =
                            (!string.IsNullOrEmpty(conditionType) && toRemove.Contains(conditionType)) ||
                            (!string.IsNullOrEmpty(typeValue) && toRemove.Contains(typeValue));

                        if (shouldRemove)
                        {
                            q.Conditions.AvailableForFinish.RemoveAt(i);
#if DEBUG
                            _logger.Info($"[DEBUG] Removed condition: conditionType='{conditionType ?? "null"}', type='{typeValue ?? "null"}'");
#endif
                            continue;
                        }

                        if (conditionType == "CounterCreator" && condition.Counter?.Conditions != null)
                        {
                            var beforeCount = condition.Counter.Conditions.Count;

                            condition.Counter.Conditions.RemoveAll(inner =>
                            {
                                string innerType = inner.ConditionType;
                                bool shouldRemoveInner = !string.IsNullOrEmpty(innerType) && toRemove.Contains(innerType);

                                if (shouldRemoveInner)
                                    _logger.Info($"[DEBUG] Removed nested condition: conditionType='{innerType}'");

                                return shouldRemoveInner;
                            });

                            var afterCount = condition.Counter.Conditions.Count;
                            if (beforeCount > afterCount)
                            {
                                _logger.Info($"[DEBUG] Removed {beforeCount - afterCount} nested conditions from CounterCreator in quest '{q.Id}', remaining = {afterCount}");
                            }

                            if (afterCount == 0)
                            {
                                q.Conditions.AvailableForFinish.RemoveAt(i);
                                _logger.Info($"[DEBUG] CounterCreator became empty → removed from quest '{q.Id}' at index {i}");
                            }
                        }
                    }

#if DEBUG
                    var json = System.Text.Json.JsonSerializer.Serialize(q, new System.Text.Json.JsonSerializerOptions
                    {
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                        WriteIndented = true
                    });
                    //_logger.Info($"[FINAL JSON for quest '{q.Id}']:\n{json}");
#endif
                }


                if (config.ModifyBaseQuest.Enabled)
                {
                    var countCond = config.ModifyBaseQuest?.CountCond;
                    int min = countCond?.Length > 0 ? countCond[0] : 1;
                    int max = countCond?.Length > 1 ? countCond[1] : min;
                    if (min <= 0) min = 1;
                    if (max < min) (min, max) = (max, min);
                    int count = random.Next(min, max + 1);

                    if (q.Conditions?.AvailableForFinish == null)
                        q.Conditions.AvailableForFinish = new List<QuestCondition>();

                    int attempts = 0;
                    int maxAttempts = count * 2;
                    while (q.Conditions.AvailableForFinish.Count < count && attempts < maxAttempts)
                    {

                        var condition2 = GenerateRandomFinishCondition(random, () => new MongoId());
                        if (condition2 != null)
                        {
                            q.Conditions.AvailableForFinish.Add(condition2);
                        }
                        attempts++;
                    }
                    var locales = new Dictionary<string, Dictionary<string, string>>();
                    _randomQuestGenerator.FillQuestLocales(q, locales);
                    AddConditionLocales(locales);

                    if (Plugin.Config.Debug)
                        _logger.Info($"[QuestFilterMod][ModifyQuests] ✅ Added {count} random finish conditions to '{q.Name}' ({q.Id})");
                }

            }
            
            q_left = allQuests.Count;
            if (Plugin.Config.Debug)
            {
                var locationStats = new Dictionary<string, int>();
                var locationDetails = new List<string>();

                foreach (var kvp in locationStats.OrderBy(x => x.Key))
                {
                    _logger.Info($"[QuestFilterMod][ModifyQuests]  • {kvp.Key}: {kvp.Value} count.");
                }
                foreach (var quest in selectedQuests)
                {
                    string locKey = Location.TryGetPascalName(quest.Location, out var pascalName)
                        ? pascalName.ToLowerInvariant()
                        : "unknown";
                    locationStats[locKey] = locationStats.GetValueOrDefault(locKey, 0) + 1;
                    locationDetails.Add($"[QuestFilterMod][ModifyQuests] Quest '{quest.Name}' ({quest.Id}) → location '{locKey}'");
                }

            }
            _logger.Warning($"|🗑️{"Deleted",-11} |➡️{"Moved",-11} |🎲{"Random",-11} |✅{"Left",-11} |");
            _logger.Warning($"-------------------------------------------------------------");
            _logger.Warning($"| {q_deleted,-12} | {q_moved,-12} | {q_random,-12} | {q_left,-12} |");
        }
    }
}
