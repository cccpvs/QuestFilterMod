//Generator.cs

using QuestFilterMod.RandomQuests.Models;
using QuestFilterMod.RandomQuests.Utils;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Modding.Custom;
using System.Reflection;


namespace QuestFilterMod.RandomQuests
{

    /// <summary>
    /// Core class for generating randomized quests based on configuration and available database content.
    /// Handles quest type selection, uniqueness tracking, retry logic, and fallback handling.
    /// </summary>
    public partial class Generator
    {
        private readonly ISptLogger<Plugin> _logger;
        private readonly TemplateTable _templateTable;
        private readonly GlobalTable _globalTable;
        private readonly Random _random = new();
        public readonly QuestConfig ConfigRandom;
        /// <summary>
        /// Service for accessing location data (maps, triggers, etc.).
        /// </summary>
        private readonly LocationTable _locationTable;
        /// <summary>
        /// Service for accessing localized strings for items and other game data.
        /// </summary>
        private readonly LocaleTable _localeTable;
        /// <summary>
        /// Tracks used quest configurations to prevent duplicates (e.g., same location + target + item).
        /// Used during a single generation session to ensure variety.
        /// </summary>
        private readonly UniqueQuestTracker _tracker = new();
        private readonly CustomQuestService _customQuestService;
        private bool _hasExhaustedAllOptions = false;
        private readonly SaveServer _saveServer;

        /// <summary>
        /// Indicates whether all possible quest variants have been exhausted and no new quests can be generated.
        /// Set to true after repeated failed attempts.
        /// </summary>
        public bool HasExhaustedAllOptions => _hasExhaustedAllOptions;

        /// <summary>
        /// Represents a unique quest signature used for deduplication: (location, target, item, type).
        /// </summary>
        /// <param name="LocationId">Location ID string.</param>
        /// <param name="TargetPoint">Target zone or point in location.</param>
        /// <param name="ItemTpl">Item template ID (optional; used for delivery/beacon quests).</param>
        /// <param name="QuestType">Quest type (e.g., "Exploration", "Delivery").</param>
        public record QuestKey(string LocationId, string TargetPoint, string ItemTpl = "", string QuestType = "");

        /// <summary>
        /// Tracks used quest keys and ensures uniqueness via set semantics.
        /// </summary>
        public class UniqueQuestTracker
        {
            private readonly HashSet<QuestKey> _usedKeys = new();

            /// <summary>Checks if a given quest key has been used already.</summary>
            /// <param name="key">Quest key to check.</param>
            public bool IsUsed(QuestKey key) => _usedKeys.Contains(key);

            /// <summary>Attempts to mark a quest key as used. Returns false if already present.</summary>
            /// <param name="key">Quest key to record.</param>
            public bool TryUse(QuestKey key)
            {
                return _usedKeys.Add(key);
            }

            /// <summary>Clears all tracked keys (used before new generation session).</summary>
            public void Clear() => _usedKeys.Clear();
        }

        /// <summary>
        /// Initializes the quest generator by loading configuration and dependencies.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="templateTable">Database table containing quests, items, and location services.</param>
        /// <param name="databaseServer">Global database server instance.</param>
        /// <param name="customQuestService">Service for handling custom quests.</param>
        /// <param name="saveServer">Server responsible for saving player profiles.</param>
        /// <param name="locationTable">Table containing detailed location data (Bigmap, Woods, etc.).</param>
        /// <param name="localeTable">Table containing localized strings for the game.</param>
        public Generator(
                ISptLogger<Plugin> logger,
                TemplateTable _templateTable,
                GlobalTable _globalTable,
                CustomQuestService customQuestService,
                SaveServer saveServer,
                LocationTable locationTable,
                LocaleTable localeTable)
        {
            _logger = logger;
            this._templateTable = _templateTable ?? throw new ArgumentNullException(nameof(_globalTable));
            this._globalTable = _globalTable ?? throw new ArgumentNullException(nameof(_globalTable));
            _customQuestService = customQuestService;

            this._locationTable = locationTable;
            this._localeTable = localeTable;

            var assemblyLocation = Assembly.GetExecutingAssembly().Location;

            var configPath = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "user",
                "mods",
                "questFilterMod",
                "RandomQuestConfig.json"
            );

            if (Plugin.Config.Debug)
                _logger.LogWithColor($"[QuestFilterMod][Generator] I'm looking for a config: {configPath}");

            if (!File.Exists(configPath))
            {
                if (Plugin.Config.Debug)
                    _logger.Error($"[QuestFilterMod][Generator]❌ Config file not found: {configPath}");
                throw new FileNotFoundException("[QuestFilterMod][Generator] Quest configuration not found", configPath);
            }

            ConfigRandom = JsonHelper.LoadFromJson<QuestConfig>(configPath)
                ?? throw new InvalidOperationException("[QuestFilterMod][Generator] Failed to load quest configuration.");
            _saveServer = saveServer;
        }

        /// <summary>
        /// Generates a single quest by randomly selecting from configured quest types and retrying with fallbacks.
        /// Stops after maxAttempts or when exhausted options are detected.
        /// </summary>
        /// <param name="maxAttempts">Maximum number of attempts across all quest types before giving up.</param>
        /// <returns>A generated Quest, or null if all attempts failed or options exhausted.</returns>
        public Quest GenerateSingleQuest(int maxAttempts = 10)
        {
            try
            {
                var locations = _locationTable?.GetDictionary();

                if (locations != null && !Location.IdToPascalName.Any())
                {
                    Location.Initialize(locations);
                }

                var candidates = new List<(string Type, Func<Quest> Generator)>();

                if (ConfigRandom.QuestGeneration.Types.Exploration)
                    candidates.Add(("Exploration", GenerateExplorationQuest));

                if (ConfigRandom.QuestGeneration.Types.Delivery)
                    candidates.Add(("Delivery", GenerateDeliveryQuest));

                if (ConfigRandom.QuestGeneration.Types.Beacon)
                    candidates.Add(("Beacon", GenerateBeaconQuest));

                if (ConfigRandom.QuestGeneration.Types.Kills)
                    candidates.Add(("Kill", GenerateKillQuest));

                if (ConfigRandom.QuestGeneration.Types.Transfer)
                    candidates.Add(("Transfer", GenerateTransferQuest));
                
                if (ConfigRandom.QuestGeneration.Types.Combo)
                    candidates.Add(("ComboQuest", GenerateComboQuest));
                
                if (!candidates.Any())
                {
                    if (Plugin.Config.Debug)
                        _logger.Error("[QuestFilterMod][Generator] ❌ There are no available quest types to generate.");
                    return null;
                }

                candidates = candidates.OrderBy(_ => _random.Next()).ToList();

                for (int attempt = 0; attempt < maxAttempts; attempt++)
                {
                    foreach (var (type, generator) in candidates)
                    {
                        var quest = generator();
                        if (quest != null)
                        {
                            if (Plugin.Config.Debug)
                                _logger.Info($"[QuestFilterMod][Generator] ✅ Quest generated successfully on attempt #{attempt + 1}: {type}");

                            _hasExhaustedAllOptions = false;
                            return quest;
                        }
                    }
                }

                if (Plugin.Config.Debug)
                    _logger.Warning($"[QuestFilterMod][Generator] Could not generate a quest in {maxAttempts} attempts.");

                _hasExhaustedAllOptions = true;
                return null;
            }
            catch (Exception e)
            {
                if (Plugin.Config.Debug)
                    _logger.Error($"[QuestFilterMod][Generator] Error when generating quest: {e.Message}\n{e.StackTrace}");
                return null;
            }
        }
        /// <summary>
        /// Resets the unique quest tracker, allowing reuse of quest patterns in subsequent generation cycles.
        /// </summary>
        public void ResetTracker()
        {
            _tracker.Clear();
        }
    }

    /// <summary>
    /// RandomExtensions for list operations used throughout quest generation.
    /// </summary>
    public static class ListExtensions
    {
        /// <summary>
        /// Returns a random item from a list using uniform distribution.
        /// Returns default(T) if list is null or empty.
        /// </summary>
        /// <param name="list">Source list.</param>
        /// <param name="random">Random instance for shuffling.</param>
        /// <typeparam name="T">Item type.</typeparam>
        public static T RandomItem<T>(this IReadOnlyList<T> list, Random random)
        {
            if (list == null || list.Count == 0)
                return default;
            return list[random.Next(list.Count)];
        }

        /// <summary>
        /// Returns a weighted-random item from a list based on custom weights.
        /// Useful for prioritizing rarer quest variants.
        /// </summary>
        /// <param name="list">Source list.</param>
        /// <param name="random">Random instance.</param>
        /// <param name="weightSelector">Function mapping item to its non-negative weight.</param>
        /// <typeparam name="T">Item type.</typeparam>
        public static T WeightedRandomItem<T>(this IList<T> list, Random random, Func<T, int> weightSelector)
        {
            int totalWeight = list.Sum(weightSelector);
            int pick = random.Next(totalWeight);
            int current = 0;

            foreach (var item in list)
            {
                current += weightSelector(item);
                if (pick < current)
                    return item;
            }
            return list[^1];
        }
    }
}