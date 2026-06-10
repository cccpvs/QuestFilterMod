using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

namespace QuestFilterMod.RandomQuests
{
    public partial class RandomQuestGenerator
    {
        private Quest? GenerateKillQuest()
        {
            var cfg = ConfigRandom.KillQuest;
            var allowed = LocationHelper.GetAllowedLocations(ConfigRandom).ToList();
#if DEBUG
            _logger.Warning($"[QuestFilterMod][KillQuest] Starting generation...");
            _logger.Warning($"[QuestFilterMod][KillQuest] Allowed locations: {allowed.Count}, Targets: {string.Join(", ", cfg.Target)}");
#endif

            var allPoints = new List<(string PascalName, string LocationId, string Target)>();

            foreach (var (pascalName, locationId) in allowed)
            {
                foreach (var target in cfg.Target)
                {
                    allPoints.Add((pascalName, locationId, target));
                }
            }
#if DEBUG
            _logger.Warning($"[QuestFilterMod][KillQuest] Total possible combinations: {allPoints.Count}");
#endif
            if (!allPoints.Any())
                return null;

            foreach (var (pascalName, locationId, target) in allPoints.OrderBy(_ => _random.Next()))
            {
                var key = new QuestKey(locationId, target, "__KILL__", "Kill");
                var keyStr = key.ToString();
                var wasUsed = _tracker.IsUsed(key);
#if DEBUG
                _logger.Warning($"[QuestFilterMod][KillQuest] Trying key: {keyStr} (used: {wasUsed})");
#endif
                if (!_tracker.TryUse(key))
                {
#if DEBUG
                    _logger.Info($"[QuestFilterMod][KillQuest] Key {keyStr} already used — skipping");
#endif
                    continue;
                }
#if DEBUG
                _logger.Info($"[QuestFilterMod][KillQuest] ✅ Using key: {keyStr} (tracker count now: {_tracker})");
#endif
                var randomKill = _random.Next(cfg.MinKills, cfg.MaxKills + 1);

                var quest = GenerateBaseQuest("Kill", (q, idFactory) =>
                {
                    q.Location = locationId;
                    q.Type = QuestTypeEnum.Elimination;

                    q.Conditions.AvailableForFinish = new List<QuestCondition>
                    {
                        new()
                        {
                            Id = idFactory(),
                            ConditionType = "CounterCreator",
                            DynamicLocale = false,
                            Value = randomKill,
                            ParentId = "",
                            Type = "Elimination",
                            VisibilityConditions = [],
                            Counter = new QuestConditionCounter()
                            {
                                Conditions = new List<QuestConditionCounterCondition>
                                {
                                    ConditionKillEnemy(target, idFactory),
                                    ConditionLocation(pascalName, idFactory)

                                },
                                Id = idFactory()
                            }
                        }
                    };
                });

                if(Plugin.Config.Debug)
                    _logger.Info($"[QuestFilterMod][KillQuest] ✅ Generated: {locationId} → {target} ({randomKill} kills)");

                return quest;
            }
#if DEBUG
            _logger.Warning($"[QuestFilterMod][KillQuest] All {allPoints.Count} combinations are already used — returning null");
#endif
            return null;
        }
    }
}
