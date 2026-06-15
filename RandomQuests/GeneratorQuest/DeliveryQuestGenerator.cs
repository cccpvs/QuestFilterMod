using QuestFilterMod.RandomQuests.Models;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

namespace QuestFilterMod.RandomQuests
{
    public partial class RandomQuestGenerator
    {
        private Quest? GenerateDeliveryQuest()
        {
            return GenerateDeployQuest(
                questType: "Delivery",
                config: ConfigRandom.DeliveryQuest,
                conditionType: "LeaveItemAtLocation"
            );
        }

        private Quest? GenerateBeaconQuest()
        {
            return GenerateDeployQuest(
                questType: "Delivery",
                config: ConfigRandom.BeaconQuest,
                conditionType: "PlaceBeacon"
            );
        }

        private Quest? GenerateDeployQuest(
    string questType,
    DeployQuestConfig config,
    string conditionType)
        {
            if (!config.Locations.Any()) return null;

            var allowed = LocationHelper.GetAllowedLocations(ConfigRandom).ToList();

            var allPoints = new List<(LocationConfig Config, string Target)>();

            foreach (var (pascalName, locationId) in allowed)
            {
                if (config.Locations.TryGetValue(pascalName, out var locConfig))
                {
                    foreach (var target in locConfig.Targets.Where(t => !string.IsNullOrEmpty(t)))
                    {
                        allPoints.Add((locConfig, target));
                    }
                }
            }

            if (!allPoints.Any()) return null;

            var itemTplList = config.ItemPlant.Any()
                ? config.ItemPlant  
                : new List<string> { GetRandomSpecialItem()?.ToString() ?? "" };

            var validItems = itemTplList.Where(t => !string.IsNullOrEmpty(t)).ToList();
            if (!validItems.Any())
            {
#if DEBUG
                _logger?.Info($"[QuestFilterMod][{questType}QuestGenerator] No valid items in ItemPlant or special items — skipping.");
#endif
                return null;
            }
            var randomItemTpl = validItems.OrderBy(_ => _random.Next()).First();

#if DEBUG
            if (string.IsNullOrEmpty(randomItemTpl))
            {
                _logger?.Info($"[QuestFilterMod][{questType}QuestGenerator] No valid items to deploy — skipping.");
                return null;
            }
#endif

            foreach (var (loc, targetPoint) in allPoints.OrderBy(_ => _random.Next()))
            {
                var itemTpl = randomItemTpl;

                var key = new QuestKey(loc.Id, targetPoint, "", questType);

#if DEBUG
                _logger?.Info($"[QuestFilterMod][{questType}QuestGenerator] Attempting to use QuestKey: {key}");
#endif

                if (!_tracker.TryUse(key)) continue;

#if DEBUG
                _logger?.Info($"[QuestFilterMod][{questType}QuestGenerator] QuestKey: {loc.Id}, {targetPoint}, {itemTpl}, \"{questType}\"");
#endif

                return GenerateBaseQuest(questType, (q, id) =>
                {
                    q.Location = loc.Id;
                    q.Type = QuestTypeEnum.Discover;

                    if (!LocationHelper.TryGetPascalName(loc.Id, out var pascalName))
                        return;

                    var idFactory = new Func<MongoId>(() => new MongoId(Guid.NewGuid().ToString("N")[..24]));
                    var itemId = id();
                    var idItems = itemId;

                    GetOrCreateRewardList(q, "Started").Add(new Reward
                    {
                        Id = id(),
                        Type = RewardType.Item,
                        Target = idItems,
                        Value = 1,
                        IsHidden = false,
                        IsEncoded = false,
                        Unknown = false,
                        Items = new List<Item>
                        {
                            new()
                            {
                                Id = idItems,
                                Template = itemTpl,
                                Upd = new Upd { StackObjectsCount = 1 }
                            }
                        }
                    });

                    q.Conditions.AvailableForFinish = new List<QuestCondition>
            {
                new()
                {
                    Id = idFactory(),
                    ConditionType = conditionType,
                    DogtagLevel = 0,
                    GlobalQuestCounterId = "",
                    IsEncoded = false,
                    OnlyFoundInRaid = false,
                    OneSessionOnly = false,
                    Value = 1,
                    Index = 1,
                    ZoneId = targetPoint,
                    DynamicLocale = false,
                    MaxDurability = 100,
                    MinDurability = 0,
                    ParentId = "",
                    PlantTime = config.PlantTime,
                    VisibilityConditions = [],
                    ExtensionData = new Dictionary<string?, object?>
                    {
                        ["target"] = new[] { itemTpl },
                        ["_item"] = itemTpl
                    }
                }
            };

#if DEBUG
                    q.Conditions.AvailableForStart = new List<QuestCondition>
                    {
                        new()
                        {
                            Id = idFactory(),
                            CompareMethod = ">=",
                            ConditionType = "Level",
                            DynamicLocale = false,
                            GlobalQuestCounterId = "",
                            Index = 0,
                            ParentId = "",
                            Value = 1,
                            VisibilityConditions = []
                        }
                    };
#endif
                });
            }

            return null;
        }
    }
}



#if DEBUG
/*
 * Точки которые не для квестов
 * Bigmap - "Wrong_wheels"
 * Lab - "Halloween_zone_for_antivirus(lab)"
 * 
 * Неизвестные точки.
 * "em_quest4_3","1","place_peacemaker_007_2_N3"
 *
 *
 * Квесты на //ConditionType = "PlaceBeacon",
 * в процессе работы.
 *
 *
 *
 * */
#endif