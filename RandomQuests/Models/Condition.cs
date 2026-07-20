// Condition.cs

using QuestFilterMod.RandomQuests.Models;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils.Json;


namespace QuestFilterMod.RandomQuests
{
    public partial class Generator
    {
        /// <summary>
        /// Constructs a CounterCreator condition wrapper with a list of sub-conditions.
        /// Used to group multiple conditions (e.g., location + kills) under one CounterCreator type.
        /// </summary>
        /// <param name="idFactory">Function generating unique IDs for main condition and nested counter.</param>
        /// <param name="Type">Underlying condition type (e.g., "Exploration", "Elimination").</param>
        /// <param name="Value">Target count (e.g., number of kills or visits).</param>
        /// <param name="Index">Order index of the condition.</param>
        /// <param name="OneSessionOnly">If true, condition resets between raids.</param>
        /// <param name="OnlyFoundInRaid">If true, condition can only be completed in raid.</param>
        /// <param name="subConditions">Inner conditions to be grouped.</param>
        /// <returns>Configured QuestCondition with CounterCreator wrapper.</returns>

        public static QuestCondition CounterCreator(
                Func<MongoId> idFactory,
                string Type,
                double Value,
                int Index,
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
                CountInRaid = false,
                OnlyFoundInRaid = OnlyFoundInRaid,
                Index = Index,
                VisibilityConditions = [],
                CompleteInSeconds = 0,
                GlobalQuestCounterId = "",
                DoNotResetIfCounterCompleted = false,
                IsResetOnConditionFailed = false,
                ParentId = ""
            };
        }

        /// <summary>
        /// Creates a FindItem condition to locate a specific item in raid.
        /// Marks condition as dynamic for in-raid locale updates.
        /// </summary>
        /// <param name="itemId">Item template ID to find.</param>
        /// <param name="Index">Condition index.</param>
        /// <param name="idFactory">Function generating unique ID.</param>
        /// <param name="random">Unused (kept for API consistency).</param>
        /// <returns>QuestCondition with FindItem type and extension metadata.</returns>

        public static QuestCondition ConditionFindItem(string itemId, int Index, Func<string> idFactory, Random random)
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
                Index = Index, 
                MinDurability = 0,
                OneSessionOnly = false,
                OnlyFoundInRaid = true,
                VisibilityConditions = [],
                Target = new ListOrT<string>(new List<string> { itemId }, null),
                ExtensionData = new Dictionary<string, object> { ["_item"] = itemId }
            };
        }

        /// <summary>
        /// Creates a HandoverItem condition to deliver a specific item (counted).
        /// Includes item metadata in ExtensionData for localization.
        /// </summary>
        /// <param name="itemId">Item template ID to hand over.</param>
        /// <param name="count">Required number of items.</param>
        /// <param name="Index">Condition index.</param>
        /// <param name="idFactory">Function generating unique ID.</param>
        /// <param name="random">Unused (kept for API consistency).</param>
        /// <returns>QuestCondition with HandoverItem type and item metadata.</returns>

        public static QuestCondition ConditionHandoverItem(string itemId, int count, int Index, Func<string> idFactory, Random random)
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
                Index = Index,
                MinDurability = 0,
                OneSessionOnly = false,
                OnlyFoundInRaid = true,
                VisibilityConditions = [],
                Target = new ListOrT<string>(new List<string> { itemId }, null),
                ExtensionData = new Dictionary<string, object> { ["_item"] = itemId }
            };
        }
        
        /// <summary>
        /// Creates a condition to deploy/plant an item at a specific zone (Delivery or Beacon).
        /// Sets extension data including target item and zone Pascal name.
        /// </summary>
        /// <param name="itemTpl">Item template ID to deploy.</param>
        /// <param name="zoneId">Zone/area identifier where item must be placed.</param>
        /// <param name="plantTime">Time limit for planting (seconds).</param>
        /// <param name="Index">Condition index.</param>
        /// <param name="conditionType">Either "LeaveItemAtLocation" or "PlaceBeacon".</param>
        /// <param name="pascalName">PascalCase location name (for localization).</param>
        /// <param name="idFactory">Function generating unique ID.</param>
        /// <returns>QuestCondition with deployment-specific config.</returns>

        public static QuestCondition ConditionDeployItem(string itemTpl, string zoneId, int plantTime, int Index, string conditionType, string pascalName, Func<MongoId> idFactory)
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
                Index = Index,
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

        /// <summary>
        /// (Private) Creates a Level-based requirement condition (e.g., "minLevel >= X").
        /// Used for start conditions or internal checks.
        /// </summary>
        /// <param name="minLevel">Minimum required level.</param>
        /// <param name="Index">Condition index.</param>
        /// <param name="idFactory">Function generating unique ID.</param>
        /// <returns>QuestCondition with Level type.</returns>

        private QuestCondition ConditionRequiredLevel(int minLevel, int Index,Func<MongoId> idFactory)
        {
            return new QuestCondition
            {
                Id = idFactory(),
                CompareMethod = ">=",
                ConditionType = "Level",
                DynamicLocale = false,
                GlobalQuestCounterId = "",
                Index = Index,
                ParentId = "",
                Value = minLevel,
                VisibilityConditions = []
            };
        }

        /// <summary>
        /// Creates a KillEnemy condition for elimination quests.
        /// Optionally includes time window and weapon restrictions.
        /// </summary>
        /// <param name="target">Target type (e.g., "Usec", "Bear", "Savage").</param>
        /// <param name="pascalName">Location name (for locale display).</param>
        /// <param name="idFactory">Function generating unique ID.</param>
        /// <param name="config">Kill quest config with time/weapon settings.</param>
        /// <param name="weaponId">Optional weapon ID to restrict kills.</param>
        /// <returns>QuestConditionCounterCondition with kill criteria and metadata.</returns>
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

        /// <summary>
        /// Represents a time-of-day range (e.g., 06:00–12:00) for daytime conditions.
        /// Used internally to serialize time constraints in quest conditions.
        /// </summary>
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

        /// <summary>
        /// Creates a Location condition requiring presence in a specific location.
        /// Maps Pascal name to internal JSON key.
        /// </summary>
        /// <param name="pascalName">Location’s PascalCase name.</param>
        /// <param name="idFactory">Function generating unique ID.</param>
        /// <returns>QuestConditionCounterCondition with location requirement.</returns>
        public static QuestConditionCounterCondition ConditionLocation(string pascalName, Func<MongoId> idFactory)
        {
            var pascalNameQuest = Location.GetJsonKey(pascalName);
            return new QuestConditionCounterCondition
            {

                Id = idFactory(),
                DynamicLocale = false,
                ConditionType = "Location",
                ExtensionData = new Dictionary<string, object>
                {
                    ["target"] = new[] { pascalNameQuest }
                } 
                
            };
        }

        /// <summary>
        /// (Private) Creates an ExitStatus condition requiring player to survive or transit.
        /// Used for completion tracking.
        /// </summary>
        /// <param name="idFactory">Function generating unique ID.</param>
        /// <returns>QuestConditionCounterCondition with status filter.</returns>

        private QuestConditionCounterCondition ConditionSurvivedExit(Func<MongoId> idFactory)
        {
            return new QuestConditionCounterCondition
            {
                Id = idFactory(),
                DynamicLocale = false,
                ConditionType = "ExitStatus",
                Status = new List<string> { "Survived", "Transit"},
                ExtensionData = new Dictionary<string, object>
                {
                    ["_status"] = new HashSet<string> { "Survived", "Transit" }
                }
            };
        }

        /// <summary>
        /// Creates a VisitPlace condition requiring entering a specific zone/point in raid.
        /// Stores target zone ID and Pascal name for locale and tracking.
        /// </summary>
        /// <param name="target">Zone/point ID to visit.</param>
        /// <param name="pascalName">Location’s PascalCase name.</param>
        /// <param name="idFactory">Function generating unique ID.</param>
        /// <returns>QuestConditionCounterCondition with visit requirement.</returns>
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
