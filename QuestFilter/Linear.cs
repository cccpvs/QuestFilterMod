// LinearQuest.cs

using QuestFilterMod.QuestFilter.Models;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Utils.Json;

namespace QuestFilterMod.QuestFilter
{
    public partial class FilterService
    {
        private void ApplyBranchingQuestChain(
        List<Quest> selectedQuests,
        Dictionary<MongoId, Quest> quests,
        ModelConfig config,
        int startQuest,
        int finishQuestMin,
        int finishQuestMax)
        {
            var dependencyMap = new Dictionary<MongoId, List<MongoId>>(); 
            var currentQuestIndex = startQuest;
            var idFactory = new Func<MongoId>(() => new MongoId(Guid.NewGuid().ToString("N")[..24]));

            for (int i = 0; i < startQuest; i++)
            {
                var parentQuest = selectedQuests[i];
                var finishQuest = _random.Next(finishQuestMin, finishQuestMax + 1);

                if (config.Debug)
                    _logger.Info($"[QuestFilterMod] 🎲 Start quest #{i} ('{parentQuest.Name}') opens {finishQuest} quests");

                for (int j = 0; j < finishQuest && currentQuestIndex < selectedQuests.Count; j++)
                {
                    var childQuest = selectedQuests[currentQuestIndex++];
                    if (!dependencyMap.ContainsKey(childQuest.Id))
                        dependencyMap[childQuest.Id] = new List<MongoId>();
                    dependencyMap[childQuest.Id].Add(parentQuest.Id);

                    if (config.Debug)
                        _logger.Info($"[QuestFilterMod] 🔗 Dependency: '{parentQuest.Name}' → '{childQuest.Name}'");
                }
            }

            while (currentQuestIndex < selectedQuests.Count)
            {
                var parentQuest = selectedQuests[currentQuestIndex - 1];
                var finishQuest = _random.Next(finishQuestMin, finishQuestMax + 1); 

                if (config.Debug)
                    _logger.Info($"[QuestFilterMod] 🎲 Quest '{parentQuest.Name}' opens {finishQuest} quests");

                for (int j = 0; j < finishQuest && currentQuestIndex < selectedQuests.Count; j++)
                {
                    var childQuest = selectedQuests[currentQuestIndex++];
                    if (!dependencyMap.ContainsKey(childQuest.Id))
                        dependencyMap[childQuest.Id] = new List<MongoId>();
                    dependencyMap[childQuest.Id].Add(parentQuest.Id);

                    if (config.Debug)
                        _logger.Info($"[QuestFilterMod] 🔗 Dependency: '{parentQuest.Name}' → '{childQuest.Name}'");
                }
            }

            foreach (var quest in selectedQuests)
            {
                quest.Conditions ??= new QuestConditionTypes();
                quest.Conditions.AvailableForStart ??= new List<QuestCondition>();
                quest.Conditions.AvailableForStart.Clear(); 

                if (dependencyMap.TryGetValue(quest.Id, out var parentIds))
                {
                    foreach (var parentId in parentIds)
                    {
                        var parent = quests[parentId];
                        var condition = new QuestCondition
                        {
                            AvailableAfter = 0,
                            ConditionType = "Quest",
                            Dispersion = 0,
                            DynamicLocale = false,
                            GlobalQuestCounterId = "",
                            Id = idFactory(),
                            Index = 0,
                            ParentId = "",
                            Target = new ListOrT<string>(new List<string> { parentId }, (string)null),
                            Status = new HashSet<QuestStatusEnum> { QuestStatusEnum.Success },
                            VisibilityConditions = [],
                            ExtensionData = new Dictionary<string, object>
                            {
                                ["target"] = parentId
                            }
                        };
                        quest.Conditions.AvailableForStart.Add(condition);
                    }
                }
            }
        }

        private (int startQuest, int finishMin, int finishMax) ResolveRandomLinkedQuest(QuestFilterLinkedQuest config)
        {
            var startMin = config.StartQuest.Length > 0 ? config.StartQuest[0] : 1;
            var startMax = config.StartQuest.Length > 1 ? config.StartQuest[1] : startMin;
            var finishMin = config.QuestFinish.Length > 0 ? config.QuestFinish[0] : 1;
            var finishMax = config.QuestFinish.Length > 1 ? config.QuestFinish[1] : finishMin;

            if (startMin > startMax) (startMin, startMax) = (startMax, startMin);
            if (finishMin > finishMax) (finishMin, finishMax) = (finishMax, finishMin);

            var startQuest = _random.Next(startMin, startMax + 1);

            return (startQuest, finishMin, finishMax);
        }
    }
}