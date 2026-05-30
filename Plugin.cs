using QuestFilterMod.QuestFilter;
using QuestFilterMod.RandomQuests;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Services.Mod;
using System.Reflection;
using System.Text.Json;

namespace QuestFilterMod;

[Injectable(TypePriority = OnLoadOrder.PreSptModLoader + 1)]
public class Plugin : IOnUpdate
{
    private readonly ISptLogger<Plugin> _logger;
    private readonly DatabaseService _databaseService;
    private readonly ServerLocalisationService _localisationService;
    private readonly string _configPath;
    private QuestFilterConfig _config = null!;
    private RandomQuestGenerator _randomQuestGenerator = null!;  
    private QuestFilterService _questFilterService = null!;   
    private bool _applied = false;
    private bool _localeEndpointRegistered = false;
    private readonly CustomQuestService _customQuestService;


    public Plugin(
    ISptLogger<Plugin> logger,
    DatabaseService databaseService,
    CustomQuestService customQuestService,
    ServerLocalisationService localisationService)
    {
        _logger = logger;
        _databaseService = databaseService;
        _customQuestService = customQuestService;
        _localisationService = localisationService;

        _configPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "Config.json");

        _logger.Info("[QuestFilterMod] Plugin инициализирован.");
    }

    private bool _loggedWaitingTables = false;
    private bool _loggedWaitingQuests = false;
    private bool _loggedWaitingLocations = false;

    public async Task<bool> OnUpdate(long secondsSinceLastRun)
    {
        try
        {
            if (_applied) return true;

            var tables = _databaseService.GetTables();
            if (tables == null)
            {
                if (!_loggedWaitingTables)
                {
                    _logger.Info("[QuestFilterMod] Ожидаю загрузки Tables...");
                    _loggedWaitingTables = true;
                }
                return true;
            }

            var quests = _databaseService.GetQuests();
            if (quests == null || quests.Count == 0)
            {
                if (!_loggedWaitingQuests)
                {
                    _logger.Info("[QuestFilterMod] Ожидаю загрузки квестов...");
                    _loggedWaitingQuests = true;
                }
                return true;
            }

            var locations = _databaseService.GetLocations();
            if (locations == null)
            {
                if (!_loggedWaitingLocations)
                {
                    _logger.Info("[QuestFilterMod] Ожидаю загрузки локаций...");
                    _loggedWaitingLocations = true;
                }
                return true;
            }

            // ✅ Создаём сервисы
            if (_randomQuestGenerator == null && _questFilterService == null)
            {
                _randomQuestGenerator = new RandomQuestGenerator(_logger, _databaseService, _customQuestService);
                _questFilterService = new QuestFilterService(
                    _logger,
                    _databaseService,
                    _randomQuestGenerator,
                    _customQuestService);
                }

            LoadConfig();


            // 🔥 Сохраняем снимок ДО применения фильтров (если нужен для бэкапа или сравнения)
            var allQuestsSnapshot = quests.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            // ✅ ОДИН РАЗ — применяем фильтры и генерируем квесты
            _questFilterService.ApplyFilters(_config);

            _applied = true;

            
            _logger.Info("[QuestFilterMod] ✅ Фильтрация квестов успешно применена.");
            _logger.Info("[QuestFilterMod] 🚀 Мод полностью инициализирован.");
            
        }
        catch (Exception ex)
        {
            _logger.Error($"[QuestFilterMod] Ошибка в OnUpdate: {ex.Message}\n{ex.StackTrace}");
        }


        return true;
    }


    private void LoadConfig()
    {
        if (!File.Exists(_configPath))
        {
            _logger.Error("[QuestFilterMod] ❌ Конфиг не найден! Создаю стандартный.");

            _config = new QuestFilterConfig();

            var exampleConfig = new QuestFilterConfig
            {
                Enabled = true,
                Debug = true,
                TargetTraderId = "",
                QuestTypes = new() { "PickUp" },
                RemoveOtherQuests = false,
                RemoveStartConditions = false,
                ExcludeArenaQuests = true,
                RemoveFinishConditionTypes = new(),
                GenerateRandomQuests = new()
                {
                    Enable = true,
                    Count = 3
                }
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(exampleConfig, options);
            File.WriteAllText(_configPath, json);

            _config = exampleConfig;
            _logger.Info("[QuestFilterMod] Пример Config.json создан.");
            return;
        }

        try
        {
            var json = File.ReadAllText(_configPath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            _config = JsonSerializer.Deserialize<QuestFilterConfig>(json, options) ?? new QuestFilterConfig();

            if (_config.Debug)
            {
                _logger.Info("[QuestFilterMod] ✅ Конфиг загружен.");
                _logger.Info($"[QuestFilterMod][CONFIG] Enabled={_config.Enabled}, GenerateRandom={_config.GenerateRandomQuests.Enable}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"[QuestFilterMod] Ошибка загрузки конфига: {ex.Message}");
            _config = new QuestFilterConfig();
        }
    }
}