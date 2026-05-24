using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Logging;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using System;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace QuestFilterMod;

[Injectable(TypePriority = OnLoadOrder.PreSptModLoader + 1)]
public class Plugin : IOnUpdate
{
    private readonly ISptLogger<Plugin> _logger;
    private readonly DatabaseService _databaseService;
    private readonly string _configPath;
    private QuestFilterConfig _config = null!;
    private readonly QuestFilterService _questFilterService;
    private bool _applied = false;

    public Plugin(ISptLogger<Plugin> logger, DatabaseService databaseService)
    {
        _logger = logger;
        _databaseService = databaseService;
        _configPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "config.json");
        _questFilterService = new QuestFilterService(logger, databaseService);
    }

    public async Task<bool> OnUpdate(long secondsSinceLastRun)
    {
        try
        {
            if (!_applied)
            {
                var tables = _databaseService.GetTables();
                if (tables != null)
                {
                    LoadConfig();
                    _questFilterService.ApplyFilters(_config);
                    _applied = true;
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"[QuestFilterMod] Ошибка в OnUpdate: {ex.Message}");
            return true;
        }
    }

    private void LoadConfig()
    {
        if (!File.Exists(_configPath))
        {
            _logger.Error("[QuestFilterMod] ❌ Конфиг не найден!");
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
                _logger.Info("[QuestFilterMod] ✅ Конфиг загружен.");
                _logger.Info($"[QuestFilterMod][CONFIG] Enabled={_config.Enabled}, Trader={_config.TargetTraderId}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"[QuestFilterMod] Ошибка загрузки конфига: {ex.Message}");
            _config = new QuestFilterConfig();
        }
    }
}