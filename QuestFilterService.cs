using EFT;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Logging;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;

namespace QuestFilterMod;

public class QuestFilterService
{
    private readonly ISptLogger<Plugin> _logger;
    private readonly DatabaseService _databaseService;
    private readonly Dictionary<string, string> _locationIdToName = new()
    {
        { "56f40101d2720b2a4d8b45d6", "bigmap" },
        { "5704e3c2d2720bac5b8b4567", "woods" },
        { "5704e554d2720bac5b8b456e", "shoreline" },
        { "5714dbc024597771384a510d", "interchange" },
        { "5b0fc42d86f7744a585f9105", "laboratory" },
        { "5714dc692459777137212e12", "tarkovstreets" },
        { "5704e5fad2720bc05b8b4567", "rezervbase" },
        { "5704e4dad2720bb55b8b4567", "lighthouse" },
        { "55f2d3fd4bdc2d5f408b4567", "factory4_day" },
        { "6733700029c367a3d40b02af", "labyrinth" },
        { "653e6760052c01c1c805532f", "sandbox" },
        { "65b8d6f5cdde2479cb2a3125", "sandbox_high" },
        { "59fc81d786f774390775787e", "factory4_night" }
    };

    public QuestFilterService(ISptLogger<Plugin> logger, DatabaseService databaseService)
    {
        _logger = logger;
        _databaseService = databaseService;
    }

    public void ApplyFilters(QuestFilterConfig config)
    {
        if (!config.Enabled) return;

        var quests = _databaseService.GetQuests();
        if (quests == null || quests.Count == 0)
        {
            _logger.Info("[QuestFilterMod] Нет квестов в базе.");
            return;
        }

        var allowedTypes = GetAllowedTypes(config);
        var allQuestList = quests.Values
            .Where(q => allowedTypes.Contains(q.Type))
            .Where(q => !ShouldExcludeByProgressSource(q, config)) // ← Фильтр по arena
            .ToList();

        if (allQuestList.Count == 0)
        {
            _logger.Info("[QuestFilterMod] Нет квестов подходящих по типу.");
            return;
        }

        var selectedQuests = SelectQuests(allQuestList, config);
        ModifyQuests(quests, selectedQuests, config);
    }
    private bool ShouldExcludeByProgressSource(Quest quest, QuestFilterConfig config)
    {
        if (!config.ExcludeArenaQuests) return false; // если выключено в конфиге — не фильтруем
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

    private List<Quest> SelectQuests(List<Quest> allQuests, QuestFilterConfig config)
    {
        var random = new Random();
        var selected = new List<Quest>();

        // Группировка по локациям (для AddFromGroup)
        var grouped = allQuests.GroupBy(q =>
        {
            if (string.IsNullOrEmpty(q.Location)) return "any";
            _locationIdToName.TryGetValue(q.Location, out var name);
            return string.IsNullOrEmpty(name) ? "any" : name;
        }).ToDictionary(g => g.Key, g => g.ToList());

        var locationProps = typeof(LocationQuestConfig).GetProperties();

        // Проверяем, есть ли хотя бы одна локация с count > 0
        bool hasLocationFilters = locationProps
            .Select(p => p.GetValue(config.RandomQuests.Location))
            .Any(v => v is int count && count > 0);

        // 🔥 Режим: если НЕТ фильтров по локациям и count == 0 → вернуть ВСЕ
        if (!hasLocationFilters && config.RandomQuests.Count == 0)
        {
            if (config.Debug)
                _logger.Info($"[QuestFilterMod][MODE] Режим 'все квесты': оставлено {allQuests.Count} квестов по типу");

            return new List<Quest>(allQuests); // Копия всех подходящих
        }

        // Иначе: работаем как раньше — по локациям
        foreach (var prop in locationProps)
        {
            var value = prop.GetValue(config.RandomQuests.Location);
            if (value is int count && count > 0)
            {
                var jsonAttr = prop.GetCustomAttribute<JsonPropertyNameAttribute>();
                if (jsonAttr == null) continue;

                string locationKey = jsonAttr.Name;
                AddFromGroup(grouped, locationKey, count, selected, random, config.Debug);
            }
        }

        // Дополняем до count, если нужно
        if (config.RandomQuests.Count > 0 && config.RandomQuests.Count > selected.Count)
        {
            var remaining = config.RandomQuests.Count - selected.Count;
            var alreadySelectedIds = selected.Select(q => q.Id).ToHashSet();
            var available = allQuests.Where(q => !alreadySelectedIds.Contains(q.Id)).ToList();

            var extra = available.OrderBy(x => random.Next()).Take(remaining).ToList();
            selected.AddRange(extra);

            if (config.Debug)
                _logger.Info($"[QuestFilterMod][FILL] Добавлено {extra.Count} квестов для достижения total={config.RandomQuests.Count}");
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
        if (count <= 0) return;
        if (!groups.TryGetValue(key, out var list)) return;

        var alreadySelectedIds = selected.Select(s => s.Id).ToHashSet();
        var available = list.Where(q => !alreadySelectedIds.Contains(q.Id)).ToList();

        var picked = available.OrderBy(x => random.Next()).Take(count).ToList();
        selected.AddRange(picked);

        if (debug)
            _logger.Info($"[QuestFilterMod][LOCATION] {picked.Count} квестов для '{key}'");
    }

    private void ModifyQuests(
    Dictionary<MongoId, Quest> allQuests,
    List<Quest> selectedQuests,
    QuestFilterConfig config)
    {
        var selectedIds = selectedQuests.Select(q => q.Id).ToHashSet();

        if (config.RemoveOtherQuests)
        {
            var toRemove = allQuests.Values.Where(q => !selectedIds.Contains(q.Id)).ToList();
            foreach (var q in toRemove)
            {
                allQuests.Remove(q.Id);
                if (config.Debug)
                    _logger.Info($"[QuestFilterMod][REMOVE] Квест {q.Name} ({q.Id}) удалён");
            }
        }

        // --- НОВОЕ: Логирование оставшихся квестов по локациям ---
        if (config.Debug)
        {
            var locationStats = new Dictionary<string, int>();
            var locationDetails = new List<string>();

            foreach (var quest in selectedQuests)
            {
                string locKey = "any";
                if (!string.IsNullOrEmpty(quest.Location))
                {
                    _locationIdToName.TryGetValue(quest.Location, out var locName);
                    locKey = string.IsNullOrEmpty(locName) ? "any" : locName;
                }

                locationStats[locKey] = locationStats.GetValueOrDefault(locKey, 0) + 1;
                locationDetails.Add($"Квест '{quest.Name}' ({quest.Id}) → локация '{locKey}'");
            }

            _logger.Info($"[QuestFilterMod][SUMMARY] Всего оставлено квестов: {selectedQuests.Count}");
            _logger.Info($"[QuestFilterMod][SUMMARY] Распределение по локациям:");

            foreach (var kvp in locationStats.OrderBy(x => x.Key))
            {
                _logger.Info($"  • {kvp.Key}: {kvp.Value} шт.");
            }

            _logger.Info($"[QuestFilterMod][DETAILS] Список выбранных квестов:");
            foreach (var detail in locationDetails)
            {
                _logger.Info($"  → {detail}");
            }
        }
        // --- КОНЕЦ НОВОГО ---

        foreach (var q in selectedQuests)
        {
            q.TraderId = config.TargetTraderId;
            if (config.RemoveStartConditions && q.Conditions?.AvailableForStart != null)
                q.Conditions.AvailableForStart.Clear();

            if (config.Debug)
                _logger.Info($"[QuestFilterMod][ASSIGN] Квест '{q.Name}' → трейдер {config.TargetTraderId}");
        }

        _logger.Info($"[QuestFilterMod] Готово: оставлено {selectedQuests.Count} квестов.");
    }
}