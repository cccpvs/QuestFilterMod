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
    int filter_skip = 0;


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

    /// <summary>
    /// Applies quest filtering, selection, generation, and modification logic to the in-memory quest database.
    /// The operation is **idempotent**: filters are applied only once per server session.
    /// All changes are made directly to the quest collection returned by <see cref="DatabaseService.GetQuests"/>.
    /// </summary>
    /// <param name="Config">Configuration object that defines filtering rules, selection constraints, and feature flags (e.g., random quest generation, linked quest chains, base quest modifications).</param>
    /// 
    /// <remarks>
    /// <para>
    /// This method executes the following high-level pipeline:
    /// </para>
    /// <list type="number">
    /// <item><description>Filters out quests not matching the configured <see cref="ModelConfig.QuestTypes"/> or excluded by <see cref="ModelConfig.SkipQuest"/> rules.</description></item>
    /// <item><description>Selects a subset of quests according to per-location quotas and global count limits (see <see cref="SelectQuests"/>).</description></item>
    /// <item><description>If <see cref="ModelConfig.RemoveStandartQuests"/> is enabled, removes *all* quests *not* in the selected pool from the database.</description></item>
    /// <item><description>Applies in-place modifications (e.g., rewards, location overrides) to selected quests, if configured via <see cref="ModelConfig.ModifyBaseQuest"/>.</description></item>
    /// <item><description>Generates new random quests (via <see cref="Generator.GenerateSingleQuest"/>) up to <see cref="ModelConfig.GenerateRandomQuests.Count"/>, merging them into both the database and the active quest pool.</description></item>
    /// <item><description>If <see cref="ModelConfig.LinkedQuest.Enable"/> is true, constructs a multi-stage branching quest chain and links its stages together.</description></item>
    /// <item><description>Ensures all selected/generated quests are registered in the quest database, even if they were previously removed or not present.</description></item>
    /// </list>
    /// 
    /// <para>
    /// <strong>Key behaviors & guarantees:</strong>
    /// </para>
    /// <list type="bullet">
    /// <item><description><strong>One-time only:</strong> If called multiple times within the same session, subsequent invocations are no-ops (unless the filter state is reset).</description></item>
    /// <item><description><strong>Non-destructive to skipped quests:</strong> Quests excluded by <see cref="ModelConfig.SkipQuest"/> are *not* removed from the DB — they are preserved and re-added at the end.</description></item>
    /// <item><description><strong>Random quest IDs are tracked:</strong> Generated quests' IDs are stored in <see cref="_randomQuestIds"/> for potential future validation or analytics.</description></item>
    /// <item><description><strong>Safe fallbacks:</strong> Missing or malformed location IDs are handled gracefully with fallbacks and debug logging (see <see cref="GetNormalizedLocationKey"/>).</description></item>
    /// <item><description><strong>Debug-first:</strong> When <see cref="Plugin.Config.Debug"/> is enabled, emits detailed logs per filtering step (e.g., "Picked 3 quests from group 'factory_day'").</description></item>
    /// </list>
    /// 
    /// <para>
    /// <strong>Side effects:</strong>
    /// </para>
    /// <list type="bullet">
    /// <item><description>Modifies the <em>in-memory</em> quest dictionary obtained from <see cref="DatabaseService.GetQuests"/>.</description></item>
    /// <item><description>Updates internal counters for filtering statistics (<see cref="filter_Deleted"/>, <see cref="filter_Filter"/>, <see cref="filter_Random"/>, etc.).</description></item>
    /// <item><description>Logs summary metrics (total quests, deleted, random, skipped, etc.) to the SPTARKOV logger.</description></item>
    /// </list>
    /// </remarks>
    /// 
    /// <example>
    /// <code>
    /// // Example workflow:
    /// // 1. Load all quests → 500 quests
    /// // 2. Filter by type & skip rules → 420 quests remain
    /// // 3. Select 30 quests (e.g., 10 for "factory_day", 5 for "any", + 15 random)
    /// // 4. Remove 400 unselected standard quests (if RemoveStandartQuests=true)
    /// // 5. Modify selected quests' rewards (e.g., increase $ prize)
    /// // 6. Generate 5 additional random quests → 35 total in pool
    /// // 7. Link quest 123 → 456 → 789 as a chain
    /// // 8. Final DB: all selected + generated quests, plus preserved skipped quests
    /// </code>
    /// </example>
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

        var skippedQuests = new List<Quest>();
        var filteredQuests = new List<Quest>();

        var shouldCheckSkip = Config.SkipQuest != null &&
                              (Config.SkipQuest.Traider?.Count > 0 || Config.SkipQuest.Types?.Count > 0);

        foreach (var q in quests.Values)
        {
            if (!allowedTypes.Contains(q.Type)) continue;
            if (ShouldExcludeByProgressSource(q, Config)) continue;

            if (shouldCheckSkip && ShouldSkipQuest(q, Config.SkipQuest))
                skippedQuests.Add(q);
            else
                filteredQuests.Add(q);
        }

        filter_skip = shouldCheckSkip ? skippedQuests.Count : 0;

        if (Plugin.Config.Debug && skippedQuests.Count > 0)
            _logger.Info($"[QuestFilterMod][FilterService] ✅ Excluded {skippedQuests.Count} quests from filtering (SkipQuest rules).");

        var OriginalQuestList = SelectQuests(filteredQuests, Config, random);

        if (Config.RemoveStandartQuests)
        {
            var selectedIds = OriginalQuestList.Select(q => q.Id).ToHashSet();
            var skippedIds = skippedQuests.Select(q => q.Id).ToHashSet();

            var toRemoveIds = quests.Keys
                    .Where(id => !selectedIds.Contains(id) && !skippedIds.Contains(id))
                    .ToHashSet();

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
            _logger.Warning($"-----------------------------------------------------------------------------");
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

        if (Config.LinkedQuest.Enable == true)
        {
            _logger.Warning($"Linked Quest [{OriginalQuestList.Count}]. Wait...");
            _logger.Warning($"-----------------------------------------------------------------------------");
            var (startQuest, finishMin, finishMax) = ResolveRandomLinkedQuest(Config.LinkedQuest);
            ApplyBranchingQuestChain(OriginalQuestList, quests, Config, startQuest, finishMin, finishMax);
            //_logger.Warning($"selectedQuests={selectedQuests.Count}, quests={quests.Count}");
        }

        filter_Filter = OriginalQuestList.Count;


        _logger.Warning($"{"AllBase",-7}|{"Deleted",-7}|{"Traider",-7}|{"Start",-7}|{"Finish",-7}|{"Modify",-7}|{"Linked",-7}|{"Random",-7}|{"Filter",-7}|{"Skip",-7}");
        _logger.Warning($"-----------------------------------------------------------------------------");
        _logger.Warning($"{quests.Count,-7}|{filter_Deleted,-7}|{filter_Trader,-7}|{filter_DelStart,-7}|{filter_DelFinish,-7}|{filter_Modify,-7}|{filter_Linked,-7}|{filter_Random,-7}|{filter_Filter,-7}|{filter_skip,-7}");


        foreach (var q in skippedQuests)
        {
            if (!quests.ContainsKey(q.Id))
                quests[q.Id] = q;
        }


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

    /// <summary>
    /// Returns the set of quest types that are allowed based on configuration, parsed and validated as QuestTypeEnum.
    /// </summary>
    /// <param name="config">Configuration containing list of allowed quest type names.</param>
    /// <returns>HashSet of valid QuestTypeEnum values.</returns>

    private HashSet<QuestTypeEnum> GetAllowedTypes(ModelConfig config)
    {
        return config.QuestTypes
            .Select(type => Enum.TryParse<QuestTypeEnum>(type, true, out var val) ? val : (QuestTypeEnum?)null)
            .Where(t => t.HasValue)
            .Select(t => t.GetValueOrDefault())
            .ToHashSet();
    }

    /// <summary>
    /// Selects a subset of quests according to location-based and global count constraints.
    /// Groups quests by normalized location (including special "any"), applies per-location quotas, and fills remaining slots randomly if needed.
    /// </summary>
    /// <param name="allQuests">List of all eligible quests (filtered by type and progress source).</param>
    /// <param name="Config">Configuration object specifying quest selection rules (count, location quotas).</param>
    /// <param name="random">Random instance used for shuffling.</param>
    /// <returns>List of selected quests, including base and optionally random quests.</returns>

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
    
    /// <summary>
    /// Normalizes a raw location ID (e.g., "factory_day", "55f2a33d4bdc2d8f068b4567") into its canonical PascalCase lowercase key (e.g., "factory_day").
    /// Handles aliases, mapped keys, and direct lookups against location database.
    /// </summary>
    /// <param name="locationId">Raw location identifier from quest.</param>
    /// <param name="tables">Database tables containing location metadata.</param>
    /// <returns>Normalized location key (lowercase Pascal name), or null if not found.</returns>

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

    /// <summary>
    /// Adds up to `count` quests from a specific location group (or "any") to the selected list, avoiding duplicates.
    /// </summary>
    /// <param name="groups">Dictionary mapping normalized location keys to lists of quests.</param>
    /// <param name="key">Location group key (e.g., "factory_day", "any").</param>
    /// <param name="count">Maximum number of quests to pick.</param>
    /// <param name="selected">Output list to append selected quests to.</param>
    /// <param name="random">Random instance used for selection shuffling.</param>
    /// <param name="debug">If true, emits debug logs.</param>

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