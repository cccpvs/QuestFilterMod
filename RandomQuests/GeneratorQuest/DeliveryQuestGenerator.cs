using Microsoft.VisualBasic;
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
            var delivery = _config.DeliveryQuest;
            if (!delivery.Locations.Any()) return null;

            var allowed = LocationHelper.GetAllowedLocations(_config).ToList();

            var allPoints = new List<(LocationConfig Config, string Target)>();

            foreach (var (pascalName, locationId) in allowed)
            {
                if (delivery.Locations.TryGetValue(pascalName, out var config))
                {
                    foreach (var target in config.Targets.Where(t => !string.IsNullOrEmpty(t)))
                    {
                        allPoints.Add((config, target));
                    }
                }
            }

            if (!allPoints.Any()) return null;

            foreach (var (loc, targetPoint) in allPoints.OrderBy(_ => _random.Next()))
            {
                var itemTpl = GetRandomSpecialItem();
#if DEBUG
                if (itemTpl == null)
                {
                    _logger?.Info($"[QuestFilterMod][DeliveryQuestGenerator] GetRandomSpecialItem() returned null — skipping delivery quest generation.");
                    continue;
                }
#endif
                
                if (itemTpl == null) continue;

                var key = new QuestKey(loc.Id, targetPoint, itemTpl.ToString(), "Delivery");
#if DEBUG
                _logger?.Info($"[QuestFilterMod][DeliveryQuestGenerator] Attempting to use QuestKey: {key}");

#endif


                if (!_tracker.TryUse(key)) continue;
#if DEBUG
                _logger?.Info($"[QuestFilterMod][DeliveryQuestGenerator] QuestKey: {loc.Id}, {targetPoint}, {itemTpl}, \"Delivery\"");
#endif


                return GenerateBaseQuest("Delivery", (q, id) =>
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
                            new Item
                            {
                                Id = idItems,
                                Template = itemTpl.Value,
                                Upd = new Upd { StackObjectsCount = 1 }
                            }
                        }
                    });

                    q.Conditions.AvailableForFinish = new List<QuestCondition>
                    {
                        new QuestCondition
                        {
                            Id = idFactory(),
                            //ConditionType = "LeaveItemAtLocation",
                            ConditionType = "PlaceBeacon",

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
                            PlantTime = delivery.PlantTime,
                            VisibilityConditions = [],
                            ExtensionData = new Dictionary<string?, object?>
                            {
                                ["target"] = new[] { itemTpl },
                            }
                        }
                    };
#if DEBUG
                    q.Conditions.AvailableForStart = new List<QuestCondition>
                    {
                        new QuestCondition 
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

 * */
#endif