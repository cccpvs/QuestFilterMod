using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Utils.Json;

namespace QuestFilterMod.RandomQuests
{
#if DEBUG
    /*
     * ConditionType = "FindItem",
     * при взятии квеста сразу выполняеться. решит почему
     * проверить данный квест на выполнение.

     * */
#endif
    public partial class RandomQuestGenerator
    {

        private Quest? GenerateTransferQuest()
        {
            var cfg = ConfigRandom.TransferQuest;

            if (cfg == null || cfg.ItemIds.Count == 0 || cfg.Condition == null || cfg.Condition.Length < 2)
            {
                _logger.Debug("[RandomQuestGenerator] cfg is null, empty or invalid Condition");
                return null;
            }

            // cfg.Condition — это число пар (каждая пара: FindItem + HandoverItem)
            var minPairs = Math.Max(1, cfg.Condition[0]);     // минимум 1 предмет
            var maxPairs = Math.Max(minPairs, cfg.Condition[1]); // максимум 3 предмета

            var itemIds = cfg.ItemIds.Where(i => !string.IsNullOrEmpty(i)).ToList();
            if (!itemIds.Any())
            {
                _logger.Debug("[RandomQuestGenerator] ItemIds is empty");
                return null;
            }

            foreach (var attempt in Enumerable.Range(0, 20))
            {
                // 1. Выбираем случайное кол-во пар (предметов)
                var pairCount = _random.Next(minPairs, maxPairs + 1);

                // 2. Выбираем `pairCount` уникальных предметов из списка
                var shuffledItemIds = itemIds.OrderBy(_ => _random.Next()).Take(pairCount).ToList();

                // 3. Генерируем условия
                var conditions = new List<QuestCondition>();
                var conditionIndex = 0;

                foreach (var itemId in shuffledItemIds)
                {
                    // ✅ ПЕРВОЕ УСЛОВИЕ: Найти предмет (Value = 1)
                    conditions.Add(new QuestCondition
                    {
                        Id = "",
                        ConditionType = "FindItem",
                        Value = 1,
                        CountInRaid = false,
                        DynamicLocale = true,
                        DogtagLevel = 0,
                        GlobalQuestCounterId = "",
                        IsEncoded = false,
                        MaxDurability = 100,
                        Index = conditionIndex++,
                        MinDurability = 0,
                        OneSessionOnly = true,
                        OnlyFoundInRaid = true,
                        VisibilityConditions = [],
                        Target = new ListOrT<string>(new List<string> { itemId }, null),
                        ExtensionData = new Dictionary<string?, object?> { ["_item"] = itemId }
                    });

                    // ✅ ВТОРОЕ УСЛОВИЕ: Сдать предмет (Value = случайное из ItemCount)
                    conditions.Add(new QuestCondition
                    {
                        Id = "",
                        ConditionType = "HandoverItem",
                        Value = _random.Next(cfg.ItemCount[0], cfg.ItemCount[1] + 1),
                        CountInRaid = false,
                        DynamicLocale = true,
                        DogtagLevel = 0,
                        GlobalQuestCounterId = "",
                        IsEncoded = false,
                        MaxDurability = 100,
                        Index = conditionIndex++,
                        MinDurability = 0,
                        OneSessionOnly = true,
                        OnlyFoundInRaid = true,
                        VisibilityConditions = [],
                        Target = new ListOrT<string>(new List<string> { itemId }, null),
                        ExtensionData = new Dictionary<string?, object?> { ["_item"] = itemId }
                    });
                }

                // 4. Резервируем уникальный ключ квеста (по всем itemId)
                var key = new QuestKey(string.Join("_", shuffledItemIds), "__TRANSFER__", "PickUp");
                if (!_tracker.TryUse(key)) continue;

                // 5. Генерируем квест
                return GenerateBaseQuest("PickUp", (q, idFactory) =>
                {
                    q.Location = "any";
                    q.Type = QuestTypeEnum.PickUp;
                    q.Conditions ??= new QuestConditionTypes();

                    // Присваиваем ID и индексы (всегда с 0)
                    for (int i = 0; i < conditions.Count; i++)
                    {
                        conditions[i].Id = idFactory();
                        conditions[i].Index = i;
                    }

                    q.Conditions.AvailableForFinish = conditions;
                });
            }

            _logger.Debug("[RandomQuestGenerator] Failed to generate TransferQuest after 20 attempts");
            return null;
        }
    }
}
