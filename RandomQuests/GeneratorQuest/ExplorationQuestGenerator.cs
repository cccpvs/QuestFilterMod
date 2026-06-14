using QuestFilterMod.RandomQuests.Models;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

namespace QuestFilterMod.RandomQuests
{
    public partial class RandomQuestGenerator
    {
        private Quest? GenerateExplorationQuest()
        {
            var allowed = LocationHelper.GetAllowedLocations(СonfigRandom).ToList();

            var allPoints = new List<(LocationConfig Config, string Target)>();

            foreach (var (pascalName, locationId) in allowed)
            {
                if (СonfigRandom.ExplorationQuest.TryGetValue(pascalName, out var config))
                {
                    foreach (var target in config.Targets)
                    {
                        allPoints.Add((config, target));
                    }
                }
            }

            if (!allPoints.Any()) return null;

            foreach (var (loc, target) in allPoints.OrderBy(_ => _random.Next()))
            {
                var key = new QuestKey(loc.Id, target, "__EXPLORATION__", "Exploration");
                if (!_tracker.TryUse(key)) continue;

                var idFactory = new Func<MongoId>(() => new MongoId(Guid.NewGuid().ToString("N")[..24]));

                return GenerateBaseQuest("Exploration", (q, id) =>
                {
                    q.Location = loc.Id;
                    q.Type = QuestTypeEnum.Discover;

                    if (!LocationHelper.TryGetPascalName(loc.Id, out var pascalName))
                        return;

                    q.Conditions ??= new QuestConditionTypes();


                    q.Conditions.AvailableForFinish = new List<QuestCondition>
                    {
                        new()
                        {
                            Id = idFactory(),
                            DynamicLocale = false,
                            ConditionType = "CounterCreator",
                            CompleteInSeconds = 0,
                            GlobalQuestCounterId = "",
                            IsNecessary = false,
                            IsResetOnConditionFailed = false,
                            OneSessionOnly = false,
                            VisibilityConditions = [],
                            Index = 0,
                            Type = "Exploration",
                            Value = 1,
                            Counter = new QuestConditionCounter()
                                {
                                    Conditions = new List<QuestConditionCounterCondition>
                                    {
                                        new()
                                        {
                                            ConditionType = "VisitPlace",
                                            DynamicLocale = false,
                                            Id = idFactory(),
                                            Value = 1,
                                            ExtensionData = new Dictionary<string?, object?>
                                            {
                                                ["target"] = target
                                            }
                                        }
                                        
                                    },
                                    Id = idFactory(),
                                }
                        },
                        new() {
                            Id = idFactory(),
                            DynamicLocale = false,
                            ConditionType = "CounterCreator",
                            GlobalQuestCounterId = "",
                            IsNecessary = false,
                            IsResetOnConditionFailed = false,
                            OneSessionOnly = false,
                            VisibilityConditions = [],
                            Index = 1,
                            Type = "Completion",
                            Value = 1,
                            Counter = new QuestConditionCounter() {
                                Conditions = new List<QuestConditionCounterCondition> {
                                        new()
                                        {
                                            ConditionType = "ExitStatus",
                                            DynamicLocale = false,
                                            Id = idFactory(),
                                            Status = new List<string> {
                                                "Survived","Transit"
                                            }

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
                                Id = idFactory(),
                            }

                        }

                    };

                });
            }

            return null;
        }
    }
}
