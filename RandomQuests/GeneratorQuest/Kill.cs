//Kill.cs

using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;


#if DEBUG
/*
 */

#endif


namespace QuestFilterMod.RandomQuests
{
    public partial class Generator
    {
        private Quest GenerateKillQuest()
        {
            var cfg = ConfigRandom.KillQuest;
            var allowed = Location.GetAllowedLocations(ConfigRandom).ToList();

#if DEBUG
            _logger.Warning($"[QuestFilterMod][Kill] Starting generation...");
            _logger.Warning($"[QuestFilterMod][Kill] Allowed locations: {allowed.Count}, Targets: {string.Join(", ", cfg.Target)}");
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
            _logger.Warning($"[QuestFilterMod][Kill] Total possible combinations: {allPoints.Count}");
#endif

            if (!allPoints.Any())
                return null;

            foreach (var (pascalName, locationId, target) in allPoints.OrderBy(_ => _random.Next()))
            {
                var key = new QuestKey(locationId, target, "__KILL__", "Kill");
                var keyStr = key.ToString();
                var wasUsed = _tracker.IsUsed(key);

#if DEBUG
                _logger.Warning($"[QuestFilterMod][Kill] Trying key: {keyStr} (used: {wasUsed})");
#endif

                if (!_tracker.TryUse(key))
                {
#if DEBUG
                    _logger.Info($"[QuestFilterMod][Kill] Key {keyStr} already used — skipping");
#endif
                    continue;
                }

#if DEBUG
                _logger.Info($"[QuestFilterMod][Kill] ✅ Using key: {keyStr} (tracker count now: {_tracker})");
#endif

                var randomKill = _random.Next(cfg.MinKills, cfg.MaxKills + 1);

                TimeOfDayRange timeOfDay = null;
                if (cfg.TimeDay.Enable)
                {
                    if (cfg.TimeDay.Minimal[0] == 0 && cfg.TimeDay.Minimal[1] == 0)
                    {
                        var startHour = _random.Next(0, 23);
                        var endHour = (startHour + cfg.TimeDay.Interval) % 24;
                        timeOfDay = new TimeOfDayRange(startHour, endHour);
                    }
                    else
                    {
                        var startHour = cfg.TimeDay.Minimal[0];
                        var endHour = cfg.TimeDay.Minimal[1];
                        timeOfDay = new TimeOfDayRange(startHour, endHour);
                    }
                }

                string weaponId = null;
                if (cfg.Weapons.Enable && cfg.Weapons.Ids.Count > 0)
                {
                    weaponId = cfg.Weapons.Ids[_random.Next(cfg.Weapons.Ids.Count)];
                }

                var quest = GenerateBaseQuest("Kill", (q, idFactory) =>
                {
                    q.Location = locationId;
                    q.Type = QuestTypeEnum.Elimination;


                    if (cfg.Weapons.Started && !string.IsNullOrEmpty(weaponId))
                    {
                        AddQuestStartedItemReward(q, weaponId, 1, idFactory);
                    }

                    q.Conditions.AvailableForFinish = new List<QuestCondition>
                    {
                        CounterCreator(
                        idFactory,
                        "Elimination",
                        randomKill,
                        false, false,
                        ConditionKillEnemy(target, pascalName, idFactory, cfg, weaponId),
                        ConditionLocation(pascalName, idFactory))
                    };
                });

                if (Plugin.Config.Debug)
                {
                    var timeStr = timeOfDay != null ? $" (time: {timeOfDay})" : "";
                    var weaponStr = weaponId != null ? $" (weapon: {weaponId})" : "";
#if DEBUG
                    _logger.Info($"[QuestFilterMod][Kill] ✅ Generated: {locationId} → {target} ({randomKill} kills){timeStr}{weaponStr}");
#endif
                }

                return quest;
            }

#if DEBUG
            _logger.Warning($"[QuestFilterMod][Kill] All {allPoints.Count} combinations are already used — returning null");
#endif
            return null;
        }
    }
}
