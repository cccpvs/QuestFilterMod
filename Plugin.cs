using QuestFilterMod.QuestFilter;
using QuestFilterMod.QuestFilter.Models;
using QuestFilterMod.RandomQuests;
using QuestFilterMod.RepeatableQuestCleaner;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Logging;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Services.Mod;
using System.Reflection;
using System.Text.Json;

[assembly: AssemblyVersion("1.0.2.0")]
[assembly: AssemblyFileVersion("1.0.2.0")]
[assembly: AssemblyInformationalVersion("1.0.2")]
[assembly: AssemblyTitle("QuestFilterMod Mod SPT ~4.0.13")]
[assembly: AssemblyProduct("QuestFilterMod")]

namespace QuestFilterMod;

[Injectable(TypePriority = OnLoadOrder.PreSptModLoader + 1)]
public class Plugin : IOnUpdate
{

    private readonly ISptLogger<Plugin> _logger;
    private readonly DatabaseService _databaseService;
    private readonly DatabaseServer databaseServer;
    private readonly ServerLocalisationService _localisationService;
    private readonly string ConfigPath;
    public static QuestFilterConfig Config { get; private set; } = null!;
    private RandomQuestGenerator _randomQuestGenerator = null!;
    private QuestFilterService _questFilterService = null!;
    private bool _applied = false;
    private readonly CustomQuestService _customQuestService;
    private ClearRepetableQuest _temporaryQuestCleaner = null!;
    private readonly SaveServer _saveServer;

    public Plugin(
    ISptLogger<Plugin> logger,
    DatabaseService databaseService,
    CustomQuestService customQuestService,
    ServerLocalisationService localisationService,
    SaveServer saveServer,
    DatabaseServer databaseServer)
    {
        _logger = logger;
        _databaseService = databaseService;
        _customQuestService = customQuestService;
        _localisationService = localisationService;
        _saveServer = saveServer;
        this.databaseServer = databaseServer;


        ConfigPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "Config.json");
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
                    if (Config.Debug)
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
                    if (Config.Debug)
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
                    if (Config.Debug)
                        _logger.Info("[QuestFilterMod] Wait load Locations...");
                    _loggedWaitingLocations = true;
                }
                return true;
            }

#if DEBUG
            if (Config.Debug)
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


            if (Config.CleanDroppedItems)
            {
                CleanDroppedItems();
            }

            if (_randomQuestGenerator == null && _questFilterService == null)
            {
                _randomQuestGenerator = new RandomQuestGenerator(
                   _logger,
                   _databaseService,
                   databaseServer,
                   _customQuestService,
                   _saveServer);

                _questFilterService = new QuestFilterService(
                    _logger,
                    _databaseService,
                    _randomQuestGenerator,
                    _customQuestService);
                _temporaryQuestCleaner = new ClearRepetableQuest(_logger, _databaseService);
            }


            if (Config.RemoveRepeatableQuests)
            {
                var repeatableDb = tables.Templates.RepeatableQuests;

                _temporaryQuestCleaner.SetQuestDatabase(repeatableDb);

                if (Config.Debug)
                    _logger.Success("[QuestFilterMod] ✅ All repeatable quest templates cleared.");
            }
            var allQuestsSnapshot = quests.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            _questFilterService.ApplyFilters(Config);

            _applied = true;
         
            _logger.Warning($"-------------------------------------------------------------");
            _logger.Warning($"|{"",-15}{"QuestFilterMod Loaded 🚀",-44}|");
            _logger.Warning($"-------------------------------------------------------------");
        }
        catch (Exception ex)
        {
            if (Config.Debug)
                _logger.Error($"[QuestFilterMod] Error inOnUpdate: {ex.Message}\n{ex.StackTrace}");
        }


        return true;
    }


    private void CleanDroppedItems()
    {
        try
        {
            if (_saveServer == null)
            {
                _logger.Error("[QuestFilterMod] ❌ _saveServer is null — cannot clean DroppedItems.");
                return;
            }

            var profiles = _saveServer.GetProfiles();
            if (profiles == null || profiles.Count == 0)
            {
                if (Config.Debug)
                    _logger.Warning("[QuestFilterMod] ⚠️ No profiles loaded yet — skipping DroppedItems cleanup.");
                return;
            }
            if (Config.Debug)
                _logger.LogWithColor($"[QuestFilterMod][Plugin] 🔍 Cleaning DroppedItems from {profiles.Count} profiles...", LogTextColor.Magenta);

            int cleanedCount = 0;
            foreach (var kvp in profiles)
            {
                var profile = kvp.Value;

                if (profile.CharacterData?.PmcData?.Stats?.Eft?.DroppedItems != null)
                    profile.CharacterData.PmcData.Stats.Eft.DroppedItems = null;

                if (profile.CharacterData?.ScavData?.Stats?.Eft?.DroppedItems != null)
                    profile.CharacterData.ScavData.Stats.Eft.DroppedItems = null;

                cleanedCount++;
            }

            if (Config.Debug)
            {
                _logger.Success($"[QuestFilterMod] ✅ Cleared DroppedItems from {cleanedCount} profiles (in-memory).");
                _logger.Success("[QuestFilterMod] 📌 Changes will be saved on next profile save (exit or auto-save).");
            }
                
        }
        catch (Exception ex)
        {
            if (Config.Debug)
                _logger.Error($"[QuestFilterMod] ❌ Error in CleanDroppedItems(): {ex}");
        }
    }

    private void LoadConfig()
    {
        if (!File.Exists(ConfigPath))
        {
            _logger.Info("[QuestFilterMod] ❌ Config not found:" + ConfigPath);
            Config = new QuestFilterConfig();
            return;
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            Config = JsonSerializer.Deserialize<QuestFilterConfig>(json, options) ?? new QuestFilterConfig();
            //_logger.Warning($"------------------QuestFilterMod Starting 🚀-----------------");
            _logger.Warning($"-------------------------------------------------------------");
            _logger.Warning($"|{"",-15}{"QuestFilterMod Starting 🚀",-44}|");
            _logger.Warning($"-------------------------------------------------------------");

            if (Config.Debug)
            {
               
                _logger.Info("[QuestFilterMod] ✅ The Config is loaded.");
#if DEBUG
                _logger.Warning($"[QuestFilterMod][Plugin] Enabled={Config.Enabled}, GenerateRandom={Config.GenerateRandomQuests?.Enable}");
#endif
            }
        }
        catch (Exception ex)
        {
            if (Config.Debug)
                _logger.Info($"[QuestFilterMod] Error loading Config: {ex.Message}");

            Config = new QuestFilterConfig();
        }
    }

}


#if DEBUG
/* 
 * 
 * Вызов через консоль игры получение точек квестов на каждой локации.
 * sinai-dev-UnityExplorer
 * 
 * 
System.Console.Clear();

var triggers = UnityEngine.Object.FindObjectsOfType<EFT.Interactive.ExperienceTrigger>();
var uniqueIds1 = new HashSet<string>();

for (int i = 0; i < triggers.Length; i++)
{
    uniqueIds1.Add(triggers[i].Id);
}

System.Console.WriteLine($"----------ExperienceTrigger----------");
foreach (var id in uniqueIds1)
{
    System.Console.WriteLine($"\"{id}\",");
}

var triggers2 = UnityEngine.Object.FindObjectsOfType<EFT.Interactive.PlaceItemTrigger>();
var uniqueIds2 = new HashSet<string>();

for (int i = 0; i < triggers2.Length; i++)
{
    uniqueIds2.Add(triggers2[i].Id);
}

System.Console.WriteLine($"----------PlaceItemTrigger----------");
foreach (var id in uniqueIds2)
{
    System.Console.WriteLine($"\"{id}\",");
}

 * 
 * 
 * 
 */
#endif