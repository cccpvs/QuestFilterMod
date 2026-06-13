using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

namespace QuestFilterMod.RandomQuests
{
    public partial class RandomQuestGenerator
    {
        private Quest? GenerateKillQuest()
        {
            var cfg = СonfigRandom.KillQuest;

            var allowed = LocationHelper.GetAllowedLocations(СonfigRandom).ToList();

            if (!allowed.Any()) return null;

            foreach (var (pascalName, locationId) in allowed.OrderBy(_ => _random.Next()))
            {
                var key = new QuestKey(locationId, "KILL", "", "Kill");
                if (!_tracker.TryUse(key)) continue;

                var idFactory = new Func<MongoId>(() => new MongoId(Guid.NewGuid().ToString("N")[..24]));
                var randomKill = _random.Next(cfg.MinKills, cfg.MaxKills + 1);

                return GenerateBaseQuest("Kill", (q, id) =>
                {
                    q.Location = locationId;
                    q.Type = QuestTypeEnum.Elimination;

                    q.Conditions.AvailableForFinish = new List<QuestCondition> {
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
                                        new()
                                        {
                                            ConditionType = "Kills",
                                            CompareMethod = ">=",
                                            Daytime = new() {
                                                From = 0,
                                                To = 0
                                            },
                                            Distance = new()
                                            {
                                                CompareMethod = ">=",
                                                Value = 0
                                            },
                                            DynamicLocale = false,
                                            EnemyEquipmentExclusive = [],
                                            EnemyEquipmentInclusive = [],
                                            EnemyHealthEffects = [],
                                            Weapon = [],
                                            WeaponCaliber = [],
                                            WeaponModsExclusive = [],
                                            WeaponModsInclusive = [],
                                            Id = idFactory(),
                                            ResetOnSessionEnd = false,
                                            ExtensionData = new Dictionary<string?, object?>
                                            {
                                                ["target"] = cfg.Target
                                            },
                                            Value = 1
                                        },
                                        new() 
                                        { 
                                            ConditionType = "Location",
                                            DynamicLocale = false,
                                            Id = idFactory(),
                                            ExtensionData = new Dictionary<string?, object?>
                                            {
                                                ["target"] = new[] { pascalName }
                                            }

                                        }
                                    },
                                    Id = idFactory()
                                }
                        }

                    };
                });
            }

            return null;
        }
    }
}
