//Modify.cs

using QuestFilterMod.QuestFilter.Models;
using QuestFilterMod.RandomQuests;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;


#if DEBUG
/*
 * = Модификация квестов
 * 1. Пропуск квестов с указанными торговцами
 * 2. Пропуск квестов по уникальному Ид
 * 3. Пропуск квестов по типу.
 * 
 * Не создавать или не менять условия квеста по текущему списку.
 * Подход реализация план.
 *
 *
 *= Конфигурация пропуска фильтра
 *"SkipQuest": {
    "Traider": [],
    "Types": []
  },
 *
 *  1. SkipQuest - Раздел настроек пропуска квеста и фильтра
 *  2. Traider - Список торговцев которые будут пропущены из фильтра
 *  3. Types - Тип квеста пропущеный из фильтра.
 *  
 *  Задача. Пропуск работает когда фильтр проходит все квесты пропускает квесты, не трагая из базовые настройки.
 *
 *
 * */
#endif


namespace QuestFilterMod.QuestFilter
{
    public partial class FilterService
    {
        /// <summary>
        /// Builds and applies a branching quest chain by linking start → finish quests via intermediate conditions.
        /// </summary>
        /// <param name="originalQuestList">Current list of selected quests (modified in-place).</param>
        /// <param name="questDatabase">Full quest dictionary.</param>
        /// <param name="Config">Filter configuration.</param>
        /// <param name="startQuest">Starting quest of the chain.</param>
        /// <param name="finishMin">Minimum number of finish quests to generate.</param>
        /// <param name="finishMax">Maximum number of finish quests to generate.</param>
        private void ModifyQuests(List<Quest> OriginalQuestList, ModelConfig config, Random random)
        {
            var selectedIds = OriginalQuestList.Select(q => q.Id).ToHashSet();
            if (OriginalQuestList.Any() && OriginalQuestList.All(q => _randomQuestIds.Contains(q.Id)))
            {
                if (Plugin.Config.Debug)
                    _logger.Info("[QuestFilterMod][Modify] Skip filter application: all quests are random-generated.");
                return;
            }

            foreach (var q in OriginalQuestList)
            {

                if (!_randomQuestIds.Contains(q.Id))
                {
                    EnsureRewardStatuses(q);
                    ModifyQuestTrader(q, config, random);
                    RemoveStartConditions(q, config);
                    RemoveFinishConditions(q, config.RemoveFinishConditionTypes);
                    AddRandomFinishConditions(q, config, random);
                }
                else if (Plugin.Config.Debug)
                {
                    _logger.Info($"[QuestFilterMod][Modify] Skip modification: quest '{q.Name}' is random-generated.");
                }
            }
#if DEBUG
            if (Plugin.Config.Debug)
            {
                var locationStats = new Dictionary<string, int>();
                foreach (var quest in OriginalQuestList)
                {
                    string locKey = Location.TryGetPascalName(quest.Location, out var pascalName)
                        ? pascalName.ToLowerInvariant()
                        : "unknown";
                    locationStats[locKey] = locationStats.GetValueOrDefault(locKey, 0) + 1;
                }

                foreach (var kvp in locationStats) {

                    if (Plugin.Config.Debug)
                        _logger.Info($"[QuestFilterMod][Modify]  • {kvp.Key}: {kvp.Value} quests");

                }

            }
#endif
        }
    }
}