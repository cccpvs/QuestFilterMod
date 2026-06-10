using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils.Json;


namespace QuestFilterMod.RandomQuests
{
    public partial class RandomQuestGenerator
    {
        private QuestCondition ConditionFindItem(string itemId, Func<string> idFactory, Random random)
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
                ExtensionData = new Dictionary<string?, object?> { ["_item"] = itemId }
            };
        }
        private QuestCondition ConditionHandoverItem(string itemId, int count, Func<string> idFactory, Random random)
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
                ExtensionData = new Dictionary<string?, object?> { ["_item"] = itemId }
            };
        }
        private QuestCondition ConditionDeployItem(string itemTpl, string zoneId, int plantTime, string conditionType, Func<MongoId> idFactory)
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
                ExtensionData = new Dictionary<string?, object?>
                {
                    ["target"] = new[] { itemTpl },
                    ["_item"] = itemTpl
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
        private QuestConditionCounterCondition ConditionKillEnemy(string target, Func<MongoId> idFactory)
        {
            return new QuestConditionCounterCondition
            {
                
                ConditionType = "Kills",
                CompareMethod = ">=",
                Daytime = new() { From = 0, To = 0 },
                Distance = new() { CompareMethod = ">=", Value = 0 },
                DynamicLocale = false,
                EnemyEquipmentExclusive = [],
                EnemyEquipmentInclusive = [],
                EnemyHealthEffects = [],
                Weapon = [],
                WeaponCaliber = [],
                WeaponModsExclusive = [],
                WeaponModsInclusive = [],
                Id = idFactory(),
                ResetOnSessionEnd = false,
                ExtensionData = new Dictionary<string?, object?>
                {
                    ["target"] = target
                },
                Value = 1
              
            };
        }
        private QuestConditionCounterCondition ConditionLocation(string pascalName, Func<MongoId> idFactory)
        {
            return new QuestConditionCounterCondition
            {

                Id = idFactory(),
                DynamicLocale = false,
                ConditionType = "Location",
                ExtensionData = new Dictionary<string?, object?>
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
                ExtensionData = new Dictionary<string?, object?>
                {
                    ["status"] = new HashSet<string> { "Survived", "Transit" }
                }
            };
        }
        private QuestConditionCounterCondition ConditionVisitPlace(string target, Func<MongoId> idFactory)
        {
            return new QuestConditionCounterCondition
            {
                ConditionType = "VisitPlace",
                DynamicLocale = false,
                Id = idFactory(),
                Value = 1,
                ExtensionData = new Dictionary<string?, object?>
                {
                    ["target"] = target
                }
 
            };
        }

    }
}
