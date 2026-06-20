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

            _logger.Info($"[QuestFilterMod][KillQuest] Starting generation...");
            _logger.Info($"[QuestFilterMod][KillQuest] Allowed locations: {allowed.Count}, Targets: {string.Join(", ", cfg.Target)}");

            var allPoints = new List<(string PascalName, string LocationId, string Target)>();

            foreach (var (pascalName, locationId) in allowed)
            {
                foreach (var target in cfg.Target)
                {
                    allPoints.Add((pascalName, locationId, target));
                }
            }

            _logger.Info($"[QuestFilterMod][KillQuest] Total possible combinations: {allPoints.Count}");
            if (!allPoints.Any())
                return null;

            foreach (var (pascalName, locationId, target) in allPoints.OrderBy(_ => _random.Next()))
            {
                var key = new QuestKey(locationId, target, "__KILL__", "Kill");
                var keyStr = key.ToString();
                var wasUsed = _tracker.IsUsed(key);

                _logger.Info($"[QuestFilterMod][KillQuest] Trying key: {keyStr} (used: {wasUsed})");

                if (!_tracker.TryUse(key))
                {
                    _logger.Info($"[QuestFilterMod][KillQuest] Key {keyStr} already used — skipping");
                    continue;
                }

                _logger.Info($"[QuestFilterMod][KillQuest] ✅ Using key: {keyStr} (tracker count now: {_tracker})");

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

                _logger.Info($"[QuestFilterMod][KillQuest] ✅ Generated: {locationId} → {target} ({randomKill} kills)");
                return quest;
            }

            _logger.Warning($"[QuestFilterMod][KillQuest] All {allPoints.Count} combinations are already used — returning null");
            return null;
        }
    }
}
