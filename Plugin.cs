using HarmonyLib;
using QuestFilterMod.QuestFilter;
using QuestFilterMod.QuestFilter.Models;
using QuestFilterMod.RandomQuests;
using QuestFilterMod.RepeatableQuest;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Services.Mod;
using System.Reflection;
using System.Text.Json;



[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]



namespace QuestFilterMod;


#if DEBUG
/*
 *      
 * 1. Замена локализации на других языках на основе EN
 * 2. Проверить еще моменты мода перед публикацией.
 * 3. Временные квесты при удалении дают ошибку.
 * 
 */
#endif

[Injectable(TypePriority = OnLoadOrder.PreSptModLoader + 1)]
public class Plugin : IOnUpdate
{

    private readonly ISptLogger<Plugin> _logger;
    private readonly DatabaseService _databaseService;
    private readonly ServerLocalisationService _localisationService;
    private readonly string _configPath;
    public static QuestFilterConfig _config { get; private set; } = null!;
    private RandomQuestGenerator _randomQuestGenerator = null!;  
    private QuestFilterService _questFilterService = null!;   
    private bool _applied = false;
    private bool _localeEndpointRegistered = false;
    private readonly CustomQuestService _customQuestService;
    private ClearRepetableQuest _temporaryQuestCleaner = null!;
    private readonly SaveServer _saveServer;


    public Plugin(
    ISptLogger<Plugin> logger,
    DatabaseService databaseService,
    CustomQuestService customQuestService,
    ServerLocalisationService localisationService,
    SaveServer saveServer)
    {
        _logger = logger;
        _databaseService = databaseService;
        _customQuestService = customQuestService;
        _localisationService = localisationService;
        _saveServer = saveServer;


        _configPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "Config.json");
        _logger.Info("[QuestFilterMod] QuestFilterMod Loaded...");
    }


    private bool _loggedWaitingTables = false;
    private bool _loggedWaitingQuests = false;
    private bool _loggedWaitingLocations = false;

    public async Task<bool> OnUpdate(long secondsSinceLastRun)
    {
        try
        {
            if (_applied) return true;

            LoadConfig();


            var tables = _databaseService.GetTables();
            if (tables == null)
            {
                if (!_loggedWaitingTables)
                {
                    if (_config.Debug)
                        _logger.Info("[QuestFilterMod] Wait load Tables...");
                    _loggedWaitingTables = true;
                }
                return true;
            }

            var quests = _databaseService.GetQuests();
            if (quests == null || quests.Count == 0)
            {
                if (!_loggedWaitingQuests)
                {
                    if (_config.Debug)
                        _logger.Info("[QuestFilterMod] Wait load Quests...");
                    _loggedWaitingQuests = true;
                }
                return true;
            }

            var locations = _databaseService.GetLocations();
            if (locations == null)
            {
                if (!_loggedWaitingLocations)
                {
                    if (_config.Debug)
                        _logger.Info("[QuestFilterMod] Wait load Locations...");
                    _loggedWaitingLocations = true;
                }
                return true;
            }

#if DEBUG
            //Локации для обзора
            if (_config.Debug)
            {
                var locationDict = locations.GetDictionary(); 
                _logger.Info("[QuestFilterMod][DEBUG] 📋 Location Open - (PascalName → ID):");
                foreach (var kvp in locationDict)
                {
                    var pascalName = kvp.Key;
                    var locationId = kvp.Value.Base.IdField;
                    var locationName = kvp.Value.Base.Name;

                    _logger.Info($"  [LOC] {pascalName} → {locationId}");
                }
            }
#endif


            if (_config.CleanDroppedItems)
            {
                CleanDroppedItems();
            }

            if (_randomQuestGenerator == null && _questFilterService == null)
            {
                _randomQuestGenerator = new RandomQuestGenerator(_logger, _databaseService, _customQuestService);
                _questFilterService = new QuestFilterService(
                    _logger,
                    _databaseService,
                    _randomQuestGenerator,
                    _customQuestService);
                _temporaryQuestCleaner = new ClearRepetableQuest(_logger, _databaseService);
            }

            
            if (_config.RemoveRepeatableQuests)
            {
                var repeatableDb = tables?.Templates?.RepeatableQuests;
                if (repeatableDb == null)
                {
                    if (_config.Debug)
                        _logger.Info("[QuestFilterMod] ❌ Failed to get RepeatableQuests for cleaning -skip.");
                    return true;
                }

                _temporaryQuestCleaner.SetQuestDatabase(repeatableDb);
                _temporaryQuestCleaner.ClearAllTemplates();

                if (_config.Debug)
                    _logger.Info("[QuestFilterMod] ✅ Temporary quests successfully cleared.");
            }
            else
            {
                if (_config.Debug)
                    _logger.Info("[QuestFilterMod] ⚙️ Removal of temporary quests is disabled (RemoveRepeatableQuests = false)");
            }

            
            var allQuestsSnapshot = quests.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            
            _questFilterService.ApplyFilters(_config);

            _applied = true;

            _logger.Info("[QuestFilterMod] ✅ Quest filtering successfully applied.");
            _logger.Info("[QuestFilterMod] 🚀 The mod is fully initialized.");




        }
        catch (Exception ex)
        {
            if (_config.Debug)
                _logger.Info($"[QuestFilterMod] Error inOnUpdate: {ex.Message}\n{ex.StackTrace}");
        }


        return true;
    }


    private void CleanDroppedItems()
    {
        try
        {
            // ✅ Получаем SaveServer через DI — добавь его в конструктор Plugin
            if (_saveServer == null)
            {
                _logger.Error("[QuestFilterMod] ❌ _saveServer is null — cannot clean DroppedItems.");
                return;
            }

            var profiles = _saveServer.GetProfiles();
            if (profiles == null || profiles.Count == 0)
            {
                _logger.Warning("[QuestFilterMod] ⚠️ No profiles loaded yet — skipping DroppedItems cleanup.");
                return;
            }

            _logger.Info($"[QuestFilterMod] 🔍 Cleaning DroppedItems from {profiles.Count} profiles...");

            int cleanedCount = 0;
            foreach (var kvp in profiles)
            {
                var profile = kvp.Value;

                // ✅ Очищаем DroppedItems у Pmc и Scav
                if (profile.CharacterData?.PmcData?.Stats?.Eft?.DroppedItems != null)
                    profile.CharacterData.PmcData.Stats.Eft.DroppedItems = null;

                if (profile.CharacterData?.ScavData?.Stats?.Eft?.DroppedItems != null)
                    profile.CharacterData.ScavData.Stats.Eft.DroppedItems = null;

                cleanedCount++;
            }

            _logger.Info($"[QuestFilterMod] ✅ Cleared DroppedItems from {cleanedCount} profiles (in-memory).");
            _logger.Info("[QuestFilterMod] 📌 Changes will be saved on next profile save (exit or auto-save).");
        }
        catch (Exception ex)
        {
            _logger.Error($"[QuestFilterMod] ❌ Error in CleanDroppedItems(): {ex}");
        }
    }

    private async Task CleanAllDroppedItemsAsync()
    {
        try
        {
            _logger.Info("[QuestFilterMod] 🔍 Starting CleanAllDroppedItemsAsync()...");

            var profiles = _saveServer.GetProfiles();
            _logger.Info($"[QuestFilterMod] 📊 Found {profiles.Count} profiles.");

            int cleanedCount = 0;
            foreach (var kvp in profiles)
            {
                var profile = kvp.Value;

                // ✅ Очистка в памяти
                if (profile.CharacterData?.PmcData?.Stats?.Eft?.DroppedItems != null)
                    profile.CharacterData.PmcData.Stats.Eft.DroppedItems = null;

                if (profile.CharacterData?.ScavData?.Stats?.Eft?.DroppedItems != null)
                    profile.CharacterData.ScavData.Stats.Eft.DroppedItems = null;

                cleanedCount++;
            }

            _logger.Info($"[QuestFilterMod] 🧹 Cleared DroppedItems in {cleanedCount} profiles — saving...");

            // ✅ Сохраняем по одному с проверкой — без SaveAsync()
            foreach (var kvp in profiles)
            {
                try
                {
                    // Сначала пробуем асинхронно
                    await _saveServer.SaveProfileAsync(kvp.Key);
                }
                catch (Exception ex)
                {
                    _logger.Error($"[QuestFilterMod] ⚠️ Failed to save profile {kvp.Key}: {ex.Message}");
                }
            }

            _logger.Info($"[QuestFilterMod] ✅ Cleaned DroppedItems from {cleanedCount} profiles and saved (async, per-profile).");
        }
        catch (Exception ex)
        {
            _logger.Error($"[QuestFilterMod] ❌ Error cleaning DroppedItems (async): {ex}");
            _logger.Error(ex.StackTrace);
        }
    }

    private void LoadConfig()
    {
        if (!File.Exists(_configPath))
        {
            _logger.Info("[QuestFilterMod] ❌ Config not found:" + _configPath);
            _config = new QuestFilterConfig();
            return;
        }

        try
        {
            var json = File.ReadAllText(_configPath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            _config = JsonSerializer.Deserialize<QuestFilterConfig>(json, options) ?? new QuestFilterConfig();

            if (_config.Debug)
            {
                if (_config.Debug)
                    _logger.Info("[QuestFilterMod] ✅ The config is loaded.");
                    _logger.Info($"[QuestFilterMod][CONFIG] Enabled={_config.Enabled}, GenerateRandom={_config.GenerateRandomQuests?.Enable}");
            }
        }
        catch (Exception ex)
        {
            if (_config.Debug)
                _logger.Info($"[QuestFilterMod] Error loading config: {ex.Message}");

            _config = new QuestFilterConfig();
        }
    }

}