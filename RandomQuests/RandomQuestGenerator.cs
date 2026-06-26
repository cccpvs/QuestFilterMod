using QuestFilterMod.RandomQuests.Models;
using QuestFilterMod.RandomQuests.Utils;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Logging;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Services.Mod;
using System.Reflection;


namespace QuestFilterMod.RandomQuests
{
    public partial class RandomQuestGenerator
    {
        private readonly ISptLogger<Plugin> _logger;
        private readonly DatabaseService _databaseService;
        private readonly DatabaseServer databaseServer;
        private readonly Random _random = new();
        private readonly QuestConfig ConfigRandom;
        private readonly UniqueQuestTracker _tracker = new();
        private readonly CustomQuestService _customQuestService;
        private bool _hasExhaustedAllOptions = false;
        private readonly SaveServer _saveServer;

        public bool HasExhaustedAllOptions => _hasExhaustedAllOptions;
        public record QuestKey(string LocationId, string TargetPoint, string ItemTpl = "", string QuestType = "");

        public class UniqueQuestTracker
        {
            private readonly HashSet<QuestKey> _usedKeys = new();

            public bool IsUsed(QuestKey key) => _usedKeys.Contains(key);

            public bool TryUse(QuestKey key)
            {
                return _usedKeys.Add(key);
            }
            public void Clear() => _usedKeys.Clear();
        }

        public RandomQuestGenerator(
                ISptLogger<Plugin> logger,
                DatabaseService databaseService,
                 DatabaseServer databaseServer,
                CustomQuestService customQuestService,
                SaveServer saveServer)
        {
            _logger = logger;
            _databaseService = databaseService;
            this.databaseServer = databaseServer ?? throw new ArgumentNullException(nameof(databaseServer));
            _customQuestService = customQuestService;

            var assemblyLocation = Assembly.GetExecutingAssembly().Location;

            var configPath = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "user",
                "mods",
                "questFilterMod",
                "RandomQuestConfig.json"
            );

            if (Plugin.Config.Debug)
                _logger.LogWithColor($"[QuestFilterMod][RandomQuestGenerator] I'm looking for a config: {configPath}", LogTextColor.Magenta);

            if (!File.Exists(configPath))
            {
                if (Plugin.Config.Debug)
                    _logger.Error($"[QuestFilterMod][RandomQuestGenerator]❌ Config file not found: {configPath}");
                throw new FileNotFoundException("[QuestFilterMod][RandomQuestGenerator] Quest configuration not found", configPath);
            }

            ConfigRandom = JsonHelper.LoadFromJson<QuestConfig>(configPath)
                ?? throw new InvalidOperationException("[QuestFilterMod][RandomQuestGenerator] Failed to load quest configuration.");
            _saveServer = saveServer;
        }

        public Quest? GenerateSingleQuest(int maxAttempts = 10)
        {
            try
            {
                var locations = _databaseService.GetLocations()?.GetDictionary();
                if (locations != null && !LocationHelper.IdToPascalName.Any())
                {
                    LocationHelper.Initialize(locations);
                }

                var candidates = new List<(string Type, Func<Quest?> Generator)>();

                if (ConfigRandom.QuestGeneration.Types.Exploration)
                    candidates.Add(("Exploration", GenerateExplorationQuest));

                if (ConfigRandom.QuestGeneration.Types.Delivery)
                    candidates.Add(("Delivery", GenerateDeliveryQuest));

                if (ConfigRandom.QuestGeneration.Types.Beacon)
                    candidates.Add(("Beacon", GenerateBeaconQuest));

                if (ConfigRandom.QuestGeneration.Types.Kills)
                    candidates.Add(("Kill", GenerateKillQuest));

                if (ConfigRandom.QuestGeneration.Types.Transfer)
                {
#if DEBUG

                    _logger.Warning("[RandomQuestGenerator] ❗️ Adding Transfer generator...");
#endif
                    try
                    {
                        candidates.Add(("Transfer", GenerateTransferQuest));
#if DEBUG
                        _logger.Warning("[RandomQuestGenerator] ✅ Transfer generator added (SUCCESS)");
#endif
                    }
                    catch (Exception ex)
                    {
#if DEBUG
                        _logger.Error($"[RandomQuestGenerator] ❌ EXCEPTION when adding Transfer generator: {ex}");
                        _logger.Error($"[RandomQuestGenerator] StackTrace: {ex.StackTrace}");
#endif
                    }
                }
                if (ConfigRandom.QuestGeneration.Types.Combo)
                    candidates.Add(("ComboQuest", GenerateComboQuest));

                if (!candidates.Any())
                {
                    if (Plugin.Config.Debug)
                        _logger.Error("[QuestFilterMod][RandomQuestGenerator] ❌ There are no available quest types to generate.");
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
                                _logger.Info($"[QuestFilterMod][RandomQuestGenerator] ✅ Quest generated successfully on attempt #{attempt + 1}: {type}");

                            _hasExhaustedAllOptions = false;
                            return quest;
                        }
                    }
                }

                if (Plugin.Config.Debug)
                    _logger.Warning($"[QuestFilterMod][RandomQuestGenerator] Could not generate a quest in {maxAttempts} attempts.");

                _hasExhaustedAllOptions = true;
                return null;
            }
            catch (Exception e)
            {
                if (Plugin.Config.Debug)
                    _logger.Error($"[QuestFilterMod][RandomQuestGenerator] Error when generating quest: {e.Message}\n{e.StackTrace}");
                return null;
            }
        }
        public void ResetTracker()
        {
            _tracker.Clear();
        }

    }

    public static class ListExtensions
    {
        public static T? RandomItem<T>(this IReadOnlyList<T> list, Random random)
        {
            if (list == null || list.Count == 0)
                return default;
            return list[random.Next(list.Count)];
        }

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