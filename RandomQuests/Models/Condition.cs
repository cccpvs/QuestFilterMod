// Condition.cs

using QuestFilterMod.RandomQuests.Models;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils.Json;


namespace QuestFilterMod.RandomQuests
{
    public partial class Generator
    {




        public static QuestCondition CounterCreator(
                Func<MongoId> idFactory,
                string Type,
                double Value,
                bool OneSessionOnly,
                bool OnlyFoundInRaid,
                params QuestConditionCounterCondition[] subConditions)
        {
            var conditionsList = subConditions?.ToList()
                              ?? new List<QuestConditionCounterCondition>();

            return new QuestCondition
            {
                Id = idFactory(),
                DynamicLocale = false,
                Type = Type,
                ConditionType = "CounterCreator",
                Value = Value,
                Counter = new QuestConditionCounter
                {
                    Id = idFactory(),
                    Conditions = conditionsList
                },
                IsNecessary = false,
                OneSessionOnly = OneSessionOnly,
                CountInRaid = true,
                OnlyFoundInRaid = OnlyFoundInRaid,
                Index = 0,
                VisibilityConditions = [],
                CompleteInSeconds = 0,
                GlobalQuestCounterId = ""
            };
        }


        public static QuestCondition ConditionFindItem(string itemId, Func<string> idFactory, Random random)
        {
            return new QuestCondition
            {
                Id = idFactory(), 
                ConditionType = "FindItem",
                Value = 1,
                CountInRaid = false,
                DynamicLocale = true,
                DogtagLevel = 0,
                GlobalQuestCounterId = "",
                IsEncoded = false,
                MaxDurability = 100,
                Index = 0, 
                MinDurability = 0,
                OneSessionOnly = true,
                OnlyFoundInRaid = true,
                VisibilityConditions = [],
                Target = new ListOrT<string>(new List<string> { itemId }, null),
                ExtensionData = new Dictionary<string, object> { ["_item"] = itemId }
            };
        }
        public static QuestCondition ConditionHandoverItem(string itemId, int count, Func<string> idFactory, Random random)
        {
            return new QuestCondition
            {
                Id = idFactory(),
                ConditionType = "HandoverItem",
                Value = count,
                CountInRaid = false,
                DynamicLocale = true,
                DogtagLevel = 0,
                GlobalQuestCounterId = "",
                IsEncoded = false,
                MaxDurability = 100,
                Index = 0,
                MinDurability = 0,
                OneSessionOnly = true,
                OnlyFoundInRaid = true,
                VisibilityConditions = [],
                Target = new ListOrT<string>(new List<string> { itemId }, null),
                ExtensionData = new Dictionary<string, object> { ["_item"] = itemId }
            };
        }
        public static QuestCondition ConditionDeployItem(string itemTpl, string zoneId, int plantTime, string conditionType, string pascalName, Func<MongoId> idFactory)
        {
            return new QuestCondition
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
                ZoneId = zoneId,
                DynamicLocale = false,
                MaxDurability = 100,
                MinDurability = 0,
                ParentId = "",
                PlantTime = plantTime,
                VisibilityConditions = [],
                ExtensionData = new Dictionary<string, object>
                {
                    ["target"] = new[] { itemTpl },
                    ["_item"] = itemTpl,
                    ["_pascalName"] = pascalName
                }
            };
        }
        private QuestCondition ConditionRequiredLevel(int minLevel, Func<MongoId> idFactory)
        {
            return new QuestCondition
            {
                Id = idFactory(),
                CompareMethod = ">=",
                ConditionType = "Level",
                DynamicLocale = false,
                GlobalQuestCounterId = "",
                Index = 0,
                ParentId = "",
                Value = minLevel,
                VisibilityConditions = []
            };
        }
        public static QuestConditionCounterCondition ConditionKillEnemy(
            string target,
            string pascalName,
            Func<MongoId> idFactory,
            KillQuestConfig config,
            string weaponId = null)
        {
            DaytimeCounter daytime = null;

            if (config?.TimeDay?.Enable == true)
            {
                int startHour, endHour;

                if (config.TimeDay.Minimal[0] == 0 && config.TimeDay.Minimal[1] == 0)
                {

                    var start = new Random().Next(0, 24);
                    var end = (start + config.TimeDay.Interval) % 24;
                    startHour = start;
                    endHour = end;
                }
                else
                {
                    startHour = config.TimeDay.Minimal[0];
                    endHour = config.TimeDay.Minimal[1];
                }

                daytime = new DaytimeCounter { From = startHour, To = endHour };
            }

            return new QuestConditionCounterCondition
            {
                ConditionType = "Kills",
                CompareMethod = ">=",
                Daytime = daytime,
                Distance = new() { CompareMethod = ">=", Value = 0 },
                DynamicLocale = false,
                EnemyEquipmentExclusive = [],
                EnemyEquipmentInclusive = [],
                EnemyHealthEffects = [],
                Weapon = string.IsNullOrEmpty(weaponId) ? [] : new() { weaponId },
                WeaponCaliber = [],
                WeaponModsExclusive = [],
                WeaponModsInclusive = [],
                Id = idFactory(),
                ResetOnSessionEnd = false,
                ExtensionData = new Dictionary<string, object>
                {
                    ["target"] = target,
                    ["_pascalName"] = pascalName,
                    ["_time"] = daytime,
                    ["_weapons"] = weaponId

                },
                Value = 1
            };
        }
        public class TimeOfDayRange
        {
            public int FromHour { get; set; }
            public int ToHour { get; set; }

            public TimeOfDayRange(int from, int to)
            {
                FromHour = from;
                ToHour = to;
            }
            public override string ToString() => $"{FromHour:00}:00–{ToHour:00}:00";
        }
        public static QuestConditionCounterCondition ConditionLocation(string pascalName, Func<MongoId> idFactory)
        {
            return new QuestConditionCounterCondition
            {

                Id = idFactory(),
                DynamicLocale = false,
                ConditionType = "Location",
                ExtensionData = new Dictionary<string, object>
                {
                    ["target"] = new[] { pascalName }
                }
                
            };
        }
        private QuestConditionCounterCondition ConditionSurvivedExit(Func<MongoId> idFactory)
        {
            return new QuestConditionCounterCondition
            {
                Id = idFactory(),
                DynamicLocale = false,
                ConditionType = "ExitStatus",
                ExtensionData = new Dictionary<string, object>
                {
                    ["status"] = new HashSet<string> { "Survived", "Transit" },

                }
            };
        }
        public static QuestConditionCounterCondition ConditionVisitPlace(string target, string pascalName, Func<MongoId> idFactory)
        {
            return new QuestConditionCounterCondition
            {
                ConditionType = "VisitPlace",
                DynamicLocale = false,
                Id = idFactory(),
                Value = 1,
                ExtensionData = new Dictionary<string, object>
                {
                    ["target"] = target,
                    ["_pascalName"] = pascalName
                }
 
            };
        }

    }
}
