using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuestFilterMod.RandomQuests
{
    public partial class RandomQuestGenerator
    {
        private Quest? GenerateKillQuest()
        {
            var cfg = _config.KillQuest;

            var allowed = LocationHelper.GetAllowedLocations(_config).ToList();

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
                        new QuestCondition()
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
                                        new QuestConditionCounterCondition
                                        {
                                            ConditionType = "Kills",
                                            CompareMethod = ">=",
                                            Daytime = new DaytimeCounter() {
                                                From = 0,
                                                To = 0
                                            },
                                            Distance = new CounterConditionDistance()
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
                                            //SavageRole  = [],
                                            ExtensionData = new Dictionary<string?, object?>
                                            {
                                                ["target"] = cfg.Target
                                            },
                                            Value = 1
                                        },
                                        new QuestConditionCounterCondition { 
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
