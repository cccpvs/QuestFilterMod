//Transfer.cs

using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

namespace QuestFilterMod.RandomQuests
{

    public partial class Generator
    {
        private Quest GenerateTransferQuest()
        {
            var cfg = ConfigRandom.TransferQuest;

            if (cfg == null || cfg.ItemIds.Count == 0 || cfg.Condition == null || cfg.Condition.Length < 2)
            {
                _logger.Debug("[RandomQuestGenerator][Transfer] cfg is null, empty or invalid Condition");
                return null;
            }

            var minPairs = Math.Max(1, cfg.Condition[0]);
            var maxPairs = Math.Max(minPairs, cfg.Condition[1]);

            var itemIds = cfg.ItemIds.Where(i => !string.IsNullOrEmpty(i)).ToList();
            if (!itemIds.Any())
            {
                if (Plugin.Config.Debug)
                {
                    _logger.Debug("[RandomQuestGenerator][Transfer] ItemIds is empty");
                }
                    
                return null;
            }

            foreach (var attempt in Enumerable.Range(0, 20))
            {
                var pairCount = _random.Next(minPairs, maxPairs + 1);
                var shuffledItemIds = itemIds.OrderBy(_ => _random.Next()).Take(pairCount).ToList();

                var conditions = new List<QuestCondition>();
                foreach (var itemId in shuffledItemIds)
                {
                    conditions.Add(ConditionFindItem(itemId, 0,() => "", _random));
                    conditions.Add(ConditionHandoverItem(itemId, _random.Next(cfg.ItemCount[0], cfg.ItemCount[1] + 1), 0, () => "", _random));
                }

                var key = new QuestKey(string.Join("_", shuffledItemIds), "__TRANSFER__", "PickUp");
                if (!_tracker.TryUse(key)) continue;

                return GenerateBaseQuest("PickUp", (q, idFactory) =>
                {
                    q.Location = "any";
                    q.Type = QuestTypeEnum.PickUp;
                    q.Conditions ??= new QuestConditionTypes();


                    for (int i = 0; i < conditions.Count; i++)
                    {
                        conditions[i].Id = idFactory();
                        conditions[i].Index = i;
                    }

                    q.Conditions.AvailableForFinish = conditions;
                });
            }

            if (Plugin.Config.Debug)
                _logger.Debug("[RandomQuestGenerator][Transfer] Failed to generate TransferQuest after 20 attempts");

            return null;
        }
    }
}
