//Exploration.cs

using QuestFilterMod.RandomQuests.Models;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

namespace QuestFilterMod.RandomQuests
{
    public partial class Generator
    {
        private Quest GenerateExplorationQuest()
        {
            var allowed = Location.GetAllowedLocations(ConfigRandom).ToList();

            var allPoints = new List<(LocationConfig Config, string Target)>();

            foreach (var (pascalName, locationId) in allowed)
            {
                if (ConfigRandom.ExplorationQuest.Locations.TryGetValue(pascalName, out var config))
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

                return GenerateBaseQuest("Exploration", (q, idFactory) =>
                {
                    q.Location = loc.Id;
                    q.Type = QuestTypeEnum.Discover;

                    if (!Location.TryGetPascalName(loc.Id, out var pascalName))
                        return;
                    q.Conditions ??= new QuestConditionTypes();

                    if (ConfigRandom.ExplorationQuest.Survive)
                    {
                        q.Conditions.AvailableForFinish = new List<QuestCondition>
                        {
                            CounterCreator(
                                idFactory,
                                "Exploration",
                                1,0,
                                true, false,
                                ConditionVisitPlace(target, pascalName, idFactory)),
                            CounterCreator(
                                idFactory,
                                "Completion",
                                1,1,
                                true, false,
                                ConditionSurvivedExit(idFactory),
                                ConditionLocation(pascalName, idFactory))
                        };
                    }
                    else {
                        q.Conditions.AvailableForFinish = new List<QuestCondition>
                        {
                            CounterCreator(
                                idFactory,
                                "Exploration",
                                1,0,
                                false, false,
                                ConditionVisitPlace(target, pascalName, idFactory))
                        };

                    }

                });
            }

            return null;
        }
    }
}
