using QuestFilterMod.QuestFilter.Models;
using QuestFilterMod.RandomQuests;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Services.Mod;
using System.Reflection;
using System.Text.Json.Serialization;

namespace QuestFilterMod.QuestFilter;

public class QuestFilterService
{
    private readonly ISptLogger<Plugin> _logger;
    private readonly DatabaseService _databaseService;
    private readonly RandomQuestGenerator _randomQuestGenerator;
    private readonly CustomQuestService _customQuestService;

    private bool _hasAppliedFilters = false;

    public QuestFilterService(
        ISptLogger<Plugin> logger,
        DatabaseService databaseService,
        RandomQuestGenerator randomQuestGenerator,
        CustomQuestService customQuestService)
    {
        _logger = logger;
        _databaseService = databaseService;
        _randomQuestGenerator = randomQuestGenerator;
        _customQuestService = customQuestService;
    }

    public void ApplyFilters(QuestFilterConfig Config)
    {
        // 🔒 Защита от повторного вызова
        if (_hasAppliedFilters)
        {
            if (Plugin.Config.Debug)
                _logger.Info("[QuestFilterMod][QuestFilterService] Filters have already been applied. We skip reprocessing.");
            return;
        }
        _hasAppliedFilters = true;

        if (!Config.Enabled) return;

        var quests = _databaseService.GetQuests();
        if (quests == null || quests.Count == 0)
        {
            if (Plugin.Config.Debug)
                _logger.Info("[QuestFilterMod][QuestFilterService] There are no quests in the database.");
            return;
        }


        var allowedTypes = GetAllowedTypes(Config);
        var allQuestList = quests.Values
            .Where(q => allowedTypes.Contains(q.Type))
            .Where(q => !ShouldExcludeByProgressSource(q, Config))
            .ToList();

        bool shouldGenerate = Config.GenerateRandomQuests.Enable && Config.GenerateRandomQuests.Count > 0;

        if (allQuestList.Count == 0 && !shouldGenerate)
        {
            if (Plugin.Config.Debug)
                _logger.Info("[QuestFilterMod][QuestFilterService] There are no suitable quests by type and generation is disabled.");

            var emptySelected = new List<Quest>();
            ModifyQuests(quests, emptySelected, Config);

            if (Plugin.Config.Debug)
                _logger.Info($"[QuestFilterMod][QuestFilterService] Done: {emptySelected.Count} quests left.");

            return;
        }
        var selectedQuests = SelectQuests(allQuestList, Config);

        // Генерация случайных квестов
        if (Config.GenerateRandomQuests.Enable && Config.GenerateRandomQuests.Count > 0)
        {
            // 🔁 Сбрасываем трекер перед серией генерации
            // Это позволяет использовать ВСЕ точки и предметы снова
            _randomQuestGenerator.ResetTracker();

            var generatedCount = 0;
            var generatedQuests = new List<Quest>();

            for (int i = 0; i < Config.GenerateRandomQuests.Count; i++)
            {
                var randomQuest = _randomQuestGenerator.GenerateSingleQuest();

                if (_randomQuestGenerator.HasExhaustedAllOptions)
                {
                    if (Plugin.Config.Debug)
                        _logger.Info("[QuestFilterMod][QuestFilterService] 🛑 Quest generator exhausted all options — stopping further attempts.");
                    break;
                }

                if (randomQuest != null)
                {
                    generatedQuests.Add(randomQuest);
                    generatedCount++;

                    // Добавляем в базу данных квестов
                    if (!quests.ContainsKey(randomQuest.Id))
                    {
                        quests[randomQuest.Id] = randomQuest;
                    }

                    if (Plugin.Config.Debug)
                    {
                        var locationName = LocationHelper.TryGetPascalName(randomQuest.Location, out var pascalName)
                        ? pascalName.ToLowerInvariant()
                        : "unknown";
                        _logger.Info($"[QuestFilterMod][QuestFilterService] Quest generated: '{randomQuest.Name}' (ID: {randomQuest.Id}, location: {locationName})");

                    }
                }
            }
            // Добавляем в selectedQuests, чтобы они не были удалены
            selectedQuests.AddRange(generatedQuests);

            if (Plugin.Config.Debug)
                _logger.Success($"[QuestFilterMod][QuestFilterService] Done: Generated {generatedCount} random quests.");
        }


        ModifyQuests(quests, selectedQuests, Config);

        var tables = _databaseService.GetTables();
        foreach (var quest in selectedQuests)
        {
            if (quest.Rewards == null)
                quest.Rewards = new Dictionary<string, List<Reward>>();

            foreach (var status in new[] { "Started", "Success", "Fail" })
            {
                if (!quest.Rewards.ContainsKey(status))
                    quest.Rewards[status] = new List<Reward>();
            }


            if (!quests.ContainsKey(quest.Id))
                quests[quest.Id] = quest;
        }
    }

    private bool ShouldExcludeByProgressSource(Quest quest, QuestFilterConfig config)
    {
        if (!config.ExcludeArenaQuests) return false;
        return string.Equals(quest.ProgressSource, "arena", StringComparison.OrdinalIgnoreCase);
    }

    private HashSet<QuestTypeEnum> GetAllowedTypes(QuestFilterConfig config)
    {
        return config.QuestTypes
            .Select(type => Enum.TryParse<QuestTypeEnum>(type, true, out var val) ? val : (QuestTypeEnum?)null)
            .Where(t => t.HasValue)
            .Select(t => t.GetValueOrDefault())
            .ToHashSet();
    }

    private List<Quest> SelectQuests(List<Quest> allQuests, QuestFilterConfig Config)
    {
        var random = new Random();
        var selected = new List<Quest>();

        var grouped = allQuests
                .GroupBy(q =>
                {
                    if (LocationHelper.TryGetPascalName(q.Location, out var pascalName))
                        return pascalName.ToLowerInvariant(); // → "woods", "rezervbase"
                    return "unknown";
                })
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var locationProps = typeof(LocationQuestConfig).GetProperties();

        bool hasLocationFilters = locationProps
            .Select(p => p.GetValue(Config.RandomQuests.Location))
            .Any(v => v is int count && count > 0);

        if (!hasLocationFilters && Config.RandomQuests.Count == 0)
        {
            if (Config.GenerateRandomQuests.Enable && Config.GenerateRandomQuests.Count > 0)
            {
                if (Plugin.Config.Debug)
                    _logger.Info("[QuestFilterMod][QuestFilterService] Random quests only mode: standard quests are NOT added");

                return new List<Quest>();
            }

            if (Plugin.Config.Debug)
                _logger.Info($"[QuestFilterMod][QuestFilterService] 'All quests' mode: left {allQuests.Count} quests by type");

            return new List<Quest>(allQuests);
        }

        foreach (var prop in locationProps)
        {
            var value = prop.GetValue(Config.RandomQuests.Location);
            if (value is not int count || count <= 0) continue;

            var jsonAttr = prop.GetCustomAttribute<JsonPropertyNameAttribute>();
            if (jsonAttr == null) continue;

            string locationKey = jsonAttr.Name;
            AddFromGroup(grouped, locationKey, count, selected, random, Config.Debug);
        }

        if (Config.RandomQuests.Count > 0 && selected.Count < Config.RandomQuests.Count)
        {
            var remaining = Config.RandomQuests.Count - selected.Count;
            var alreadySelectedIds = selected.Select(q => q.Id).ToHashSet();
            var available = allQuests.Where(q => !alreadySelectedIds.Contains(q.Id)).ToList();

            var extra = available.OrderBy(_ => random.Next()).Take(remaining).ToList();
            selected.AddRange(extra);

            if (Config.Debug)
                _logger.Info($"[QuestFilterMod][QuestFilterService] Added {extra.Count} quests to achieve total={Config.RandomQuests.Count}");
        }

        return selected;
    }

    private void AddFromGroup(
        Dictionary<string, List<Quest>> groups,
        string key,
        int count,
        List<Quest> selected,
        Random random,
        bool debug)
    {
        if (count <= 0 || !groups.TryGetValue(key, out var list)) return;

        var alreadySelectedIds = selected.Select(s => s.Id).ToHashSet();
        var available = list.Where(q => !alreadySelectedIds.Contains(q.Id)).ToList();

        var picked = available.OrderBy(_ => random.Next()).Take(count).ToList();
        selected.AddRange(picked);

        if (Plugin.Config.Debug)
            _logger.Info($"[QuestFilterMod][QuestFilterService] {picked.Count} quests for '{key}'");
    }

    private void ModifyQuests(
        Dictionary<MongoId, Quest> allQuests,
        List<Quest> selectedQuests,
        QuestFilterConfig config)
    {
        var selectedIds = selectedQuests.Select(q => q.Id).ToHashSet();

        var countRemoveQuest = 0;
        if (config.RemoveStandartQuests)
        {
            
            var toRemove = allQuests.Values.Where(q => !selectedIds.Contains(q.Id)).ToList();
            foreach (var q in toRemove)
            {
                allQuests.Remove(q.Id);
                if (config.Debug)
                    countRemoveQuest++;
            }
        }
        if (Plugin.Config.Debug)
            _logger.Info($"[QuestFilterMod][QuestFilterService] Total deleted: {countRemoveQuest}");

        var countTraiderTransfer = 0;
        // Обработка каждого выбранного квеста
        foreach (var q in selectedQuests)
        {
            if (q.Rewards == null)
                q.Rewards = new Dictionary<string, List<Reward>>();

            foreach (var status in new[] { "Started", "Success", "Fail" })
            {
                if (!q.Rewards.ContainsKey(status))
                {
                    q.Rewards[status] = new List<Reward>();
                    if (Plugin.Config.Debug)
                        _logger.Info($"[QuestFilterMod][QuestFilterService] ⚠️ Reward status restored: '{status}' for the quest '{q.Id}'");
                }
            }

            if (!string.IsNullOrEmpty(config.TargetTraderId))
            {
                q.TraderId = config.TargetTraderId;
                countTraiderTransfer++;
#if DEBUG
                if (Plugin.Config.Debug)
                    _logger.Info($"[QuestFilterMod][QuestFilterService] Quest '{q.Name}' ({q.Id}) → trader {config.TargetTraderId}");
#endif

            }

            if (config.RemoveStartConditionsQuest && q.Conditions?.AvailableForStart != null)
            {
                q.Conditions.AvailableForStart.Clear();
                if (Plugin.Config.Debug)
                    _logger.Info($"[QuestFilterMod][QuestFilterService] Start conditions have been removed for the quest '{q.Name}'");
            }

            if (config.RemoveFinishConditionTypes?.Count > 0 && q.Conditions?.AvailableForFinish != null)
            {
                var toRemove = new List<QuestCondition>();
                foreach (var condition in q.Conditions.AvailableForFinish.ToList())
                {
                    string? checkType = condition.ConditionType.ToString() == "CounterCreator"
                        ? condition.Type
                        : condition.ConditionType.ToString();

                    if (!string.IsNullOrEmpty(checkType) &&
                        config.RemoveFinishConditionTypes.Contains(checkType, StringComparer.OrdinalIgnoreCase))
                    {
                        toRemove.Add(condition);
#if DEBUG
                        if (Plugin.Config.Debug)
                            _logger.Info($"[QuestFilterMod][QuestFilterService] Condition removed'{checkType}' from the quest '{q.Name}'");
#endif

                    }
                }

                foreach (var cond in toRemove)
                {
                    q.Conditions.AvailableForFinish.Remove(cond);
                }
            }
        }

        if (Plugin.Config.Debug)
        {
            var locationStats = new Dictionary<string, int>();
            var locationDetails = new List<string>();

            _logger.Info($"[QuestFilterMod][QuestFilterService] Trader quests moved: {countTraiderTransfer}"); 

            foreach (var kvp in locationStats.OrderBy(x => x.Key))
            {
                _logger.Info($"[QuestFilterMod][QuestFilterService]  • {kvp.Key}: {kvp.Value} шт.");
            }
            foreach (var quest in selectedQuests)
            {
                string locKey = LocationHelper.TryGetPascalName(quest.Location, out var pascalName)
                    ? pascalName.ToLowerInvariant()
                    : "unknown";
                locationStats[locKey] = locationStats.GetValueOrDefault(locKey, 0) + 1;
                locationDetails.Add($"[QuestFilterMod][QuestFilterService] Quest '{quest.Name}' ({quest.Id}) → location '{locKey}'");
            }
            _logger.Info($"[QuestFilterMod][QuestFilterService] Total quests left: {selectedQuests.Count}");

        }
    }
}