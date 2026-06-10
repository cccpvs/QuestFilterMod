using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

namespace QuestFilterMod.RandomQuests
{
    public partial class RandomQuestGenerator
    {
        private List<Reward> GetOrCreateRewardList(Quest quest, string status)
        {
            if (quest.Rewards == null)
                quest.Rewards = new Dictionary<string, List<Reward>>();

            return quest.Rewards.TryGetValue(status, out var list)
                ? list
                : (quest.Rewards[status] = new List<Reward>());
        }
        private void AddExperienceReward(Quest quest)
        {
            var range = ConfigRandom.DefaultQuest.ExperienceRewardRange;
            int exp = _random.Next(range.Min / range.Step, (range.Max / range.Step) + 1) * range.Step;

            GetOrCreateRewardList(quest, "Success").Add(new Reward
            {
                Id = new MongoId(Guid.NewGuid().ToString("N")[..24]),
                Type = RewardType.Experience,
                Value = exp,
                FindInRaid = false,
                IsEncoded = false,
                IsHidden = false
            });
        }
        private void AddMoneyReward(Quest quest)
        {
            if (!ConfigRandom.RewardMoney.Enabled || string.IsNullOrEmpty(ConfigRandom.RewardMoney.Tpl)) return;

            int amount = GenerateRandomAmount(ConfigRandom.RewardMoney.Min, ConfigRandom.RewardMoney.Max, ConfigRandom.RewardMoney.Step);
            AddItemReward(quest, ConfigRandom.RewardMoney.Tpl, amount, "RUB");
        }
        private void AddRandomItemRewards(Quest quest)
        {
            if (!ConfigRandom.RewardItems.Enabled || !ConfigRandom.RewardItems.Parents.Any()) return;

            var prices = _databaseService.GetPrices();
            var itemsPool = _databaseService.GetTemplates().Items;

            if (prices == null || !prices.Any() || !itemsPool.Any())
            {
                if (Plugin.Config.Debug)
                    _logger.Error("[QuestFilterMod][RewardSystem] ❌ Не удалось загрузить данные из базы.");
                return;
            }

            int minPrice = ConfigRandom.RewardItems.PriceRange.Min;
            int maxPrice = ConfigRandom.RewardItems.PriceRange.Max;

            if (minPrice < 0) minPrice = 0;
            if (maxPrice < minPrice) maxPrice = minPrice;

            // === Шаг 1: Выбираем родительские ID с учётом весов ===
            var weightedParents = ConfigRandom.RewardItems.Parents
                .Where(p => p.Weight > 0 && !string.IsNullOrEmpty(p.Id))
                .ToList();

            if (!weightedParents.Any())
            {
                if (Plugin.Config.Debug)
                    _logger.Error("[QuestFilterMod][RewardSystem] ❌ Нет активных родителей с весом > 0.");
                return;
            }

            var parentIds = weightedParents.Select(p => new { Id = new MongoId(p.Id), p.Weight }).ToList();

            var validItemsByParent = new Dictionary<MongoId, List<MongoId>>();

            foreach (var kvp in prices)
            {
                var tplId = kvp.Key;
                if (!itemsPool.TryGetValue(tplId, out var template)) continue;

                double price = kvp.Value;
                if (price < minPrice || price > maxPrice) continue;

                var parentId = template.Parent;
                if (!parentIds.Any(p => p.Id == parentId)) continue;

                if (!validItemsByParent.ContainsKey(parentId))
                    validItemsByParent[parentId] = new List<MongoId>();

                validItemsByParent[parentId].Add(tplId);
            }

            var nonEmptyParents = parentIds
                .Where(p => validItemsByParent.ContainsKey(p.Id) && validItemsByParent[p.Id].Any())
                .ToList();

            if (!nonEmptyParents.Any())
            {
#if DEBUG
                if (Plugin.Config.Debug)
                    _logger.Error("[QuestFilterMod][RewardSystem] ❌ Нет подходящих предметов по цене и категориям.");
#endif
                return;
            }
#if DEBUG
            if (Plugin.Config.Debug)
                _logger.Info($"[QuestFilterMod][RewardSystem] Найдено {nonEmptyParents.Count} категорий с подходящими предметами.");
#endif

            int count = _random.Next(ConfigRandom.RewardItems.Count.Min, ConfigRandom.RewardItems.Count.Max + 1);
            var usedTpls = new HashSet<string>();

            for (int i = 0; i < count; i++)
            {
                var selectedParent = nonEmptyParents.WeightedRandomItem(_random, p => p.Weight);
                var itemsInCategory = validItemsByParent[selectedParent.Id];

                if (!itemsInCategory.Any()) continue;

                MongoId selectedId;
                string selectedTpl;
                int attempts = 0;
                do
                {
                    selectedId = itemsInCategory[_random.Next(itemsInCategory.Count)];
                    selectedTpl = selectedId.ToString();
                    attempts++;
                    if (attempts > 100) break;
                } while (usedTpls.Contains(selectedTpl));

                if (attempts > 100) break;
                usedTpls.Add(selectedTpl);

                string name = "Unknown Item";
                if (itemsPool.TryGetValue(selectedId, out var item) && !string.IsNullOrEmpty(item.Name))
                {
                    name = item.Name;
                }

                AddItemReward(quest, selectedTpl, 1, name);
            }
        }
        private MongoId? GetRandomSpecialItem()
        {
            var allowedTpls = ConfigRandom.DeliveryQuest.ItemPlant;
            if (!allowedTpls.Any())
            {
                _logger?.Info("[QuestFilterMod][RewardSystem] 📌 The ItemPlant list is empty!");
                return null;
            }

            var items = _databaseService.GetTemplates().Items;

            var candidates = items
                .Where(kvp => allowedTpls.Contains(kvp.Key))
                .Select(kvp => kvp.Key)
                .ToList();

            var missingTpls = allowedTpls
                .Where(tpl => !items.ContainsKey(tpl))
                .ToList();

            if (missingTpls.Any())
            {
                _logger?.Info($"[QuestFilterMod][RewardSystem] ⚠️ Not in the TPL database (from the config): {string.Join(", ", missingTpls)}");
            }

            if (!candidates.Any())
            {
                _logger?.Info($"[QuestFilterMod][RewardSystem] ❌ No items found from {allowedTpls.Count} TPL.");
                return null;
            }

            if (Plugin.Config.Debug)
                _logger?.Debug($"[QuestFilterMod][RewardSystem] ✔️ Found {candidates.Count} items from {allowedTpls.Count} TPL.");

            return new MongoId(candidates[_random.Next(candidates.Count)]);
        }
        private void AddTraderStandingReward(Quest quest)
        {
            if (!ConfigRandom.RewardTraderStanding.Enabled)
                return;

            float value = (float)_random.NextDouble() *
                          (ConfigRandom.RewardTraderStanding.Max - ConfigRandom.RewardTraderStanding.Min) +
                          ConfigRandom.RewardTraderStanding.Min;

            GetOrCreateRewardList(quest, "Success").Add(new Reward
            {
                Id = new MongoId(Guid.NewGuid().ToString("N")[..24]),
                Type = RewardType.TraderStanding,
                Target = quest.TraderId,
                Value = (float)Math.Round(value, 3),
                FindInRaid = false,
                IsEncoded = false,
                IsHidden = false,
                Unknown = false,
                GameMode = new HashSet<string> { "regular", "pve" },
                AvailableInGameEditions = new HashSet<string>()
            });
        }
        private int GenerateRandomAmount(int min, int max, int step)
        {
            int range = (max - min) / step;
            return min + _random.Next(range + 1) * step;
        }
        private void AddItemReward(Quest quest, string tpl, int count, string name = "Item", string rewardType = "Success")
        {
            string itemId = Guid.NewGuid().ToString("N")[..24];
            var gameItem = (Item)Activator.CreateInstance(typeof(Item), nonPublic: true)!;

            gameItem.Id = new MongoId(itemId);
            gameItem.Template = new MongoId(tpl);
            gameItem.Upd = new Upd()
            {
                StackObjectsCount = count,

            };

            var reward = new Reward
            {
                Id = new MongoId(Guid.NewGuid().ToString("N")[..24]),
                Type = RewardType.Item,
                Target = new MongoId(itemId),
                Value = count,
                FindInRaid = true,
                IsEncoded = false,
                IsHidden = false,
                Unknown = false,
                GameMode = new HashSet<string> { "regular", "pve" },
                AvailableInGameEditions = new HashSet<string>(),
                Items = new List<Item> { gameItem }
            };

            GetOrCreateRewardList(quest, "Success").Add(reward);
        }
    }
}
