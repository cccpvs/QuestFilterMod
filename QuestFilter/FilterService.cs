// FilterService.cs

using QuestFilterMod.QuestFilter.Models;
using QuestFilterMod.RandomQuests;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Services.Mod;
using QuestFilterMod.RandomQuests.Models;


namespace QuestFilterMod.QuestFilter;

#if DEBUG
/***/
#endif

public partial class FilterService
{
    private readonly ISptLogger<Plugin> _logger;
    private readonly DatabaseService _databaseService;
    private readonly Generator _randomQuestGenerator;
    private readonly CustomQuestService _customQuestService;
    private readonly HashSet<MongoId> _randomQuestIds = new();

    private QuestConfig ConfigRandom => _randomQuestGenerator?.ConfigRandom;


    private bool _hasAppliedFilters = false;
    private readonly System.Random _random = new();

    int filter_Deleted = 0;
    int filter_Filter = 0;
    int filter_Random = 0;
    int filter_Reward = 0;
    int filter_Trader = 0;
    int filter_DelStart = 0;
    int filter_DelFinish = 0;
    int filter_Modify = 0;
    int filter_Linked = 0;


    public FilterService(
        ISptLogger<Plugin> logger,
        DatabaseService databaseService,
        Generator randomQuestGenerator,
        CustomQuestService customQuestService)
    {
        _logger = logger;
        _databaseService = databaseService;
        _randomQuestGenerator = randomQuestGenerator;
        _customQuestService = customQuestService;
    }

    public void ApplyFilters(ModelConfig Config)
    {

        if (_hasAppliedFilters)
        {
            if (Plugin.Config.Debug)
                _logger.Info("[QuestFilterMod][FilterService] Filters have already been applied. We skip reprocessing.");
            return;
        }
        _hasAppliedFilters = true;

        if (!Config.Enabled) return;

        _randomQuestIds.Clear();

        var quests = _databaseService.GetQuests();
        if (quests == null || quests.Count == 0)
        {
            if (Plugin.Config.Debug)
                _logger.Info("[QuestFilterMod][FilterService] There are no quests in the database.");
            return;
        }
        var random = new Random();

        var allowedTypes = GetAllowedTypes(Config);
        var allQuestList = quests.Values
            .Where(q => allowedTypes.Contains(q.Type))
            .Where(q => !ShouldExcludeByProgressSource(q, Config))
            .ToList();


        var OriginalQuestList = SelectQuests(allQuestList, Config, random);

        if (Config.RemoveStandartQuests)
        {
            var selectedIds = OriginalQuestList.Select(q => q.Id).ToHashSet();
            var toRemoveIds = quests.Keys.Where(id => !selectedIds.Contains(id)).ToHashSet();

            if (toRemoveIds.Count > 0)
            {
                foreach (var id in toRemoveIds)
                    quests.Remove(id);

                filter_Deleted = toRemoveIds.Count;
                if (Plugin.Config.Debug)
                    _logger.Info($"[QuestFilterMod][FilterService] Removed {filter_Deleted} quests from DB (not in selected list)");
            }
        }
        else
        {

            if (Plugin.Config.Debug)
                _logger.Info($"[QuestFilterMod][FilterService] RemoveStandartQuests = false → all standard quests preserved");
        }


        if (Config.ModifyBaseQuest.Enabled == true && OriginalQuestList.Count>0)
        {
            _logger.Warning($"Modification of basic quests [{OriginalQuestList.Count}]. Wait...");
            _logger.Warning($"-------------------------------------------------------------------------");
        }


        if (OriginalQuestList.Any())
            ModifyQuests(OriginalQuestList, Config, random);

        if (Config.GenerateRandomQuests.Enable && Config.GenerateRandomQuests.Count > 0)
        {

            _logger.Warning($"Generating Random Quest [{Config.GenerateRandomQuests.Count}]. Wait...");
            _logger.Warning($"-------------------------------------------------------------------------");
            _randomQuestGenerator.ResetTracker();

            var generatedCount = 0;
            var generatedQuests = new List<Quest>();

            for (int i = 0; i < Config.GenerateRandomQuests.Count; i++)
            {
                var randomQuest = _randomQuestGenerator.GenerateSingleQuest();

                if (_randomQuestGenerator.HasExhaustedAllOptions)
                {
                    if (Plugin.Config.Debug)
                        _logger.Info("[QuestFilterMod][FilterService] 🛑 Quest generator exhausted all options — stopping further attempts.");
                    break;
                }

                if (randomQuest != null)
                {
                    generatedQuests.Add(randomQuest);
                    generatedCount++;

                    if (!quests.ContainsKey(randomQuest.Id))
                    {
                        quests[randomQuest.Id] = randomQuest;
                    }

                    _randomQuestIds.Add(randomQuest.Id);

                    if (Plugin.Config.Debug)
                    {
                        var locationName = Location.TryGetPascalName(randomQuest.Location, out var pascalName)
                        ? pascalName.ToLowerInvariant()
                        : "unknown";
                        _logger.Success($"[QuestFilterMod][FilterService] Quest generated: '{randomQuest.Name}' (ID: {randomQuest.Id}, location: {locationName})");

                    }
                }

            }
            OriginalQuestList.AddRange(generatedQuests);
            filter_Random = generatedCount;
#if DEBUG
            /*if (Plugin.Config.Debug)
                _logger.Success($"[QuestFilterMod][FilterService] Done: Generated {generatedCount} random quests.");*/
#endif
        }
#if DEBUG
        /*
         * Проблемы с линейкой квестов.
         * фильтры явно не работают
         * квесты стандартые не удаляються
         * случайных квестов нет в списке
         * проверить рализацию линейки квеста.
         * 
         * 
         * */
#endif




        if (Config.LinkedQuest.Enable == true)
        {
            _logger.Warning($"Linked Quest [{OriginalQuestList.Count}]. Wait...");
            _logger.Warning($"-------------------------------------------------------------------------");
            var (startQuest, finishMin, finishMax) = ResolveRandomLinkedQuest(Config.LinkedQuest);
            ApplyBranchingQuestChain(OriginalQuestList, quests, Config, startQuest, finishMin, finishMax);
            //_logger.Warning($"selectedQuests={selectedQuests.Count}, quests={quests.Count}");
        }

        filter_Filter = OriginalQuestList.Count;
#if DEBUG
        //_logger.Success($"Reward={filter_Reward}, Traider={filter_Trader}, AvailableForStart={filter_DelStart}, RemoveFinish={filter_DelFinish}, Modify={filter_Modify}");
#endif

        _logger.Warning($"{"AllBase",-7}|{"Deleted",-7}|{"Traider",-7}|{"delStar",-7}|{"delFins",-7}|{"Modify",-7}|{"Linked",-7}|{"Random",-7}|{"Filter",-7}");
        _logger.Warning($"-------------------------------------------------------------------------");
        _logger.Warning($"{quests.Count,-7}|{filter_Deleted,-7}|{filter_Trader,-7}|{filter_DelStart,-7}|{filter_DelFinish,-7}|{filter_Modify,-7}|{filter_Linked,-7}|{filter_Random,-7}|{filter_Filter,-7}");

        var tables = _databaseService.GetTables();
        foreach (var quest in OriginalQuestList)
        {
            if (quest.Rewards == null)
                quest.Rewards = new Dictionary<string, List<Reward>>();

            foreach (var status in new[] { "Started", "Success", "Fail" })
            {
                if (!quest.Rewards.ContainsKey(status))
                    quest.Rewards[status] = new List<Reward>();
            }

            //if (!quests.ContainsKey(quest.Id))
                quests[quest.Id] = quest;
        }
    }

    

    private HashSet<QuestTypeEnum> GetAllowedTypes(ModelConfig config)
    {
        return config.QuestTypes
            .Select(type => Enum.TryParse<QuestTypeEnum>(type, true, out var val) ? val : (QuestTypeEnum?)null)
            .Where(t => t.HasValue)
            .Select(t => t.GetValueOrDefault())
            .ToHashSet();
    }

    private List<Quest> SelectQuests(List<Quest> allQuests, ModelConfig Config, Random random)
    {
        var selected = new List<Quest>();

        var tables = _databaseService.GetTables();
        var locationDict = tables?.Locations?.GetDictionary()
            ?? throw new InvalidOperationException("Failed to load location data. Check your database service.");


        var normalizedLocationKeyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in locationDict)
        {
            var pascalName = kvp.Key;
            var normalized = pascalName.ToLowerInvariant();

            normalizedLocationKeyMap[pascalName] = normalized;
            normalizedLocationKeyMap[normalized] = normalized;

            var mappedKey = tables.Locations.GetMappedKey(pascalName);
            if (!string.IsNullOrEmpty(mappedKey) && !normalizedLocationKeyMap.ContainsKey(mappedKey))
            {
                normalizedLocationKeyMap[mappedKey.ToLowerInvariant()] = normalized;
            }
        }

        normalizedLocationKeyMap["any"] = "any";


        var grouped = allQuests
            .GroupBy(q =>
            {
                if (string.IsNullOrWhiteSpace(q.Location))
                    return "unknown";

                var normalized = GetNormalizedLocationKey(q.Location, tables);
                if (!string.IsNullOrEmpty(normalized))
                    return normalized;


                if (string.Equals(q.Location, "any", StringComparison.OrdinalIgnoreCase))
                    return "any";

                return "unknown";
            })
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var locationProps = Config.RandomQuests.Location.GetType().GetProperties();
        bool hasLocationFilters = locationProps
            .Select(p => p.GetValue(Config.RandomQuests.Location))
            .Any(v => v is int count && count > 0);

        if (!hasLocationFilters && Config.RandomQuests.Count == 0)
        {
            if (Config.GenerateRandomQuests.Enable && Config.GenerateRandomQuests.Count > 0)
            {
                if (Config.Debug)
                    _logger.Info("[QuestFilterMod][FilterService] Random quests + standard quests mode: both types will be included");

                return new List<Quest>(allQuests);
            }

            if (Config.Debug)
                _logger.Info($"[QuestFilterMod][FilterService] 'All quests' mode: left {allQuests.Count} quests by type");

            return new List<Quest>(allQuests);
        }

        foreach (var prop in locationProps)
        {
            var value = prop.GetValue(Config.RandomQuests.Location);
            if (value is not int count || count <= 0) continue;

            string locationKey = prop.Name.ToLowerInvariant();
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
                _logger.Info($"[QuestFilterMod][FilterService] Added {extra.Count} quests to achieve total={Config.RandomQuests.Count}");
        }

        if (Config.Debug)
        {
            _logger.Info("[QuestFilterMod][FilterService] 🔍 Sample location checks:");
            foreach (var q in allQuests.Take(5))
            {
                var normalized = GetNormalizedLocationKey(q.Location, tables);
                _logger.Info($"  - Quest '{q.Name}' → loc='{q.Location}' → normalized='{normalized}'");
            }
        }

        return selected;
    }

    private string GetNormalizedLocationKey(string locationId, SPTarkov.Server.Core.Models.Spt.Server.DatabaseTables tables)
    {
        if (string.Equals(locationId, "any", StringComparison.OrdinalIgnoreCase))
        {
#if Debug
            _logger.Info($"[QuestFilterMod] ✅ Special keyword 'any' → 'any'");
#endif
            return "any";
        }

        var locationDict = tables.Locations.GetDictionary();

        if (locationDict == null || locationDict.Count == 0)
        {
            _logger.Error($"[QuestFilterMod][FilterService] ❌ locationDict is null or empty!");
            return null;
        }

        if (Location.TryGetPascalName(locationId, out var pascalName))
        {
            _logger.Info($"[QuestFilterMod][FilterService] ✅ Matched by TryGetPascalName: '{pascalName}'");
            return pascalName.ToLowerInvariant();
        }

        var mappedKey = tables.Locations.GetMappedKey(locationId);
#if Debug
        _logger.Info($"[QuestFilterMod][FilterService] 🧪 GetMappedKey('{locationId}') = '{mappedKey}'");
#endif
        if (!string.IsNullOrEmpty(mappedKey))
        {
            if (Location.TryGetPascalName(mappedKey, out var pascal2))
            {
                _logger.Info($"[QuestFilterMod][FilterService] ✅ Matched by GetMappedKey → Pascal: '{pascal2}'");
                return pascal2.ToLowerInvariant();
            }

#if Debug
            _logger.Info($"[QuestFilterMod][FilterService] 🧪 mappedKey is NOT PascalCase, trying to find via dictionary...");
#endif
            foreach (var loc in locationDict)
            {
                var locObj = loc.Value;
                if (locObj?.Base == null) continue;

                if (locObj.Base.IdField.ToString() == locationId)
                {
#if Debug
                    _logger.Info($"[QuestFilterMod][FilterService] ✅ Found by IdField: '{loc.Key}'");
#endif
                    return loc.Key.ToLowerInvariant();
                }
                if (locObj.Base.Id == locationId)
                {
                    _logger.Info($"[QuestFilterMod][FilterService] ✅ Found by Id: '{loc.Key}'");
                    return loc.Key.ToLowerInvariant();
                }
            }

#if Debug
            _logger.Error($"[QuestFilterMod] ⚠️ GetMappedKey returned unmapped key '{mappedKey}', returning lowercase");
#endif
            return mappedKey.ToLowerInvariant();
        }

        foreach (var loc in locationDict)
        {
            var locObj = loc.Value;
            if (locObj?.Base == null) continue;

            if (locObj.Base.IdField.ToString() == locationId)
            {
                _logger.Info($"[QuestFilterMod][FilterService] ✅ Found by direct IdField lookup: '{loc.Key}'");
                return loc.Key.ToLowerInvariant();
            }

            if (locObj.Base.Id == locationId)
            {
                _logger.Info($"[QuestFilterMod][FilterService] ✅ Found by direct Id lookup: '{loc.Key}'");
                return loc.Key.ToLowerInvariant();
            }
        }

        _logger.Error($"[QuestFilterMod][FilterService] ❌ Could not find location for '{locationId}'");
        return null;
    }

    private void AddFromGroup(
            Dictionary<string, List<Quest>> groups,
            string key,
            int count,
            List<Quest> selected,
            Random random,
            bool debug)
    {
        if (count <= 0) return;

        var alreadySelectedIds = selected.Select(s => s.Id).ToHashSet();

        if (string.Equals(key, "any", StringComparison.OrdinalIgnoreCase))
        {
            if (!groups.TryGetValue("any", out var anyList) || anyList.Count == 0)
            {
                if (debug)
                    _logger.Info("[QuestFilterMod][FilterService] ❌ No quests with Location='any' in DB");
                return;
            }

            var availableQuests = anyList.Where(q => !alreadySelectedIds.Contains(q.Id)).ToList();

            if (availableQuests.Count == 0)
            {
                if (debug)
                    _logger.Info("[QuestFilterMod][FilterService] ⚠️ All 'any' quests already selected");
                return;
            }

            var picked = availableQuests.OrderBy(_ => random.Next()).Take(count).ToList();
            selected.AddRange(picked);

            if (debug)
                _logger.Info($"[QuestFilterMod][FilterService] 🌍 Picked {picked.Count} quests from group 'any' (Location='any')");
            return;
        }

        if (!groups.TryGetValue(key, out var list))
        {
            if (debug)
                _logger.Info($"[QuestFilterMod][FilterService] ❌ No group found for '{key}' (key not in groups)");
            return;
        }

        var availableQuestsForLoc = list.Where(q => !alreadySelectedIds.Contains(q.Id)).ToList();

        if (availableQuestsForLoc.Count == 0)
        {
            if (debug)
                _logger.Info($"[QuestFilterMod][FilterService] ⚠️ No available quests in group '{key}' (all already selected)");
            return;
        }

        var pickedQuests = availableQuestsForLoc.OrderBy(_ => random.Next()).Take(count).ToList();
        selected.AddRange(pickedQuests);

        if (debug)
            _logger.Info($"[QuestFilterMod][FilterService] 📦 {pickedQuests.Count} quests for '{key}'");
    }
}