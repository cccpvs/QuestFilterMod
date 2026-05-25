using EFT;
using QuestFilterMod.RandomQuests;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Logging;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using static RootMotion.FinalIK.RotationLimitPolygonal;
using IOPath = System.IO.Path;
using QuestFilterMod.QuestFilter;

namespace QuestFilterMod.QuestFilter;

public class QuestFilterService
{
    private readonly ISptLogger<Plugin> _logger;
    private readonly DatabaseService _databaseService;
    private readonly RandomQuestGenerator _randomQuestGenerator;

    public QuestFilterService(
        ISptLogger<Plugin> logger,
        DatabaseService databaseService,
        RandomQuestGenerator randomQuestGenerator)
    {
        _logger = logger;
        _databaseService = databaseService;
        _randomQuestGenerator = randomQuestGenerator;
    }

    private void LogAllAvailableFinishTypes(List<Quest> quests)
    {
        var finishTypes = new HashSet<string>();

        foreach (var q in quests)
        {
            if (q.Conditions?.AvailableForFinish == null) continue;

            foreach (var cond in q.Conditions.AvailableForFinish)
            {
                string type = cond.ConditionType.ToString() == "CounterCreator"
                    ? $"Counter:{cond.Type}"
                    : $"Direct:{cond.ConditionType}";

                finishTypes.Add(type);
            }
        }

        _logger.Info($"[QuestFilterMod][DEBUG] Найдено уникальных типов AvailableForFinish: {finishTypes.Count}");
        foreach (var t in finishTypes.OrderBy(x => x))
        {
            _logger.Info($"  → {t}");
        }
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

        if (config.Debug)
        {
            LogAllAvailableFinishTypes(quests.Values.ToList());
        }

        var allowedTypes = GetAllowedTypes(config);
        var allQuestList = quests.Values
            .Where(q => allowedTypes.Contains(q.Type))
            .Where(q => !ShouldExcludeByProgressSource(q, config))
            .ToList();

        if (allQuestList.Count == 0 && !config.GenerateRandomQuests.Enable)
        {
            _logger.Info("[QuestFilterMod] Нет подходящих квестов и генерация отключена.");
            return;
        }

        var selectedQuests = SelectQuests(allQuestList, config);

        // Генерация случайных квестов
        if (config.GenerateRandomQuests.Enable && config.GenerateRandomQuests.Count > 0)
        {
            var generatedCount = 0;
            var generatedQuests = new List<Quest>();

            for (int i = 0; i < config.GenerateRandomQuests.Count; i++)
            {
                var randomQuest = _randomQuestGenerator.GenerateSingleQuest();
                if (randomQuest != null)
                {
                    generatedQuests.Add(randomQuest);
                    generatedCount++;
                    _logger.Info($"[QuestFilterService] Сгенерирован квест: '{randomQuest.Name}' (ID: {randomQuest.Id}, локация: {LocationMapper.GetLocationName(randomQuest.Location)})");
                }
            }

            _logger.Info($"[QuestFilterService] Готово: сгенерировано {generatedCount} случайных квестов.");

            if (config.GenerateRandomQuests.OnlyRandom)
            {
                selectedQuests.Clear();
            }

            selectedQuests.AddRange(generatedQuests);
        }

        ModifyQuests(quests, selectedQuests, config);

        // Добавляем квесты в локали
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

            _randomQuestGenerator.AddQuestToLocale(tables, quest);

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

    private List<Quest> SelectQuests(List<Quest> allQuests, QuestFilterConfig config)
    {
        var random = new Random();
        var selected = new List<Quest>();

        // Группируем по локациям через LocationMapper
        var grouped = allQuests.GroupBy(q => LocationMapper.GetLocationName(q.Location))
                              .ToDictionary(g => g.Key, g => g.ToList());

        var locationProps = typeof(LocationQuestConfig).GetProperties();

        bool hasLocationFilters = locationProps
            .Select(p => p.GetValue(config.RandomQuests.Location))
            .Any(v => v is int count && count > 0);

        if (!hasLocationFilters && config.RandomQuests.Count == 0)
        {
            if (config.Debug)
                _logger.Info($"[QuestFilterMod][MODE] Режим 'все квесты': оставлено {allQuests.Count} квестов по типу");

            return new List<Quest>(allQuests);
        }

        // Выбор по локациям из конфига
        foreach (var prop in locationProps)
        {
            var value = prop.GetValue(config.RandomQuests.Location);
            if (value is not int count || count <= 0) continue;

            var jsonAttr = prop.GetCustomAttribute<JsonPropertyNameAttribute>();
            if (jsonAttr == null) continue;

            string locationKey = jsonAttr.Name;
            AddFromGroup(grouped, locationKey, count, selected, random, config.Debug);
        }

        // Дозаполнение до нужного количества
        if (config.RandomQuests.Count > 0 && selected.Count < config.RandomQuests.Count)
        {
            var remaining = config.RandomQuests.Count - selected.Count;
            var alreadySelectedIds = selected.Select(q => q.Id).ToHashSet();
            var available = allQuests.Where(q => !alreadySelectedIds.Contains(q.Id)).ToList();

            var extra = available.OrderBy(_ => random.Next()).Take(remaining).ToList();
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
        if (count <= 0 || !groups.TryGetValue(key, out var list)) return;

        var alreadySelectedIds = selected.Select(s => s.Id).ToHashSet();
        var available = list.Where(q => !alreadySelectedIds.Contains(q.Id)).ToList();

        var picked = available.OrderBy(_ => random.Next()).Take(count).ToList();
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

        // Удаление лишних квестов
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

        // Логирование статистики
        if (config.Debug)
        {
            var locationStats = new Dictionary<string, int>();
            var locationDetails = new List<string>();

            foreach (var quest in selectedQuests)
            {
                string locKey = LocationMapper.GetLocationName(quest.Location);
                locationStats[locKey] = locationStats.GetValueOrDefault(locKey, 0) + 1;
                locationDetails.Add($"Квест '{quest.Name}' ({quest.Id}) → локация '{locKey}'");
            }

            _logger.Info($"[QuestFilterMod][SUMMARY] Всего оставлено квестов: {selectedQuests.Count}");
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
                    _logger.Warning($"[QuestFilterService] ⚠️ Восстановлен статус награды: '{status}' для квеста '{q.Id}'");
                }
            }

            // Переназначение трейдера
            if (!string.IsNullOrEmpty(config.TargetTraderId))
            {
                q.TraderId = config.TargetTraderId;
                if (config.Debug)
                    _logger.Info($"[QuestFilterMod][ASSIGN] Квест '{q.Name}' ({q.Id}) → трейдер {config.TargetTraderId}");
            }

            // Очистка условий старта
            if (config.RemoveStartConditions && q.Conditions?.AvailableForStart != null)
            {
                q.Conditions.AvailableForStart.Clear();
                if (config.Debug)
                    _logger.Info($"[QuestFilterMod][CLEAR] Условия старта удалены для квеста '{q.Name}'");
            }

            // Удаление указанных типов условий завершения
            if (config.RemoveFinishConditionTypes?.Count > 0 && q.Conditions?.AvailableForFinish != null)
            {
                var toRemove = new List<QuestCondition>();
                foreach (var condition in q.Conditions.AvailableForFinish.ToList())
                {
                    string checkType = condition.ConditionType.ToString() == "CounterCreator"
                        ? condition.Type
                        : condition.ConditionType.ToString();

                    if (!string.IsNullOrEmpty(checkType) &&
                        config.RemoveFinishConditionTypes.Contains(checkType, StringComparer.OrdinalIgnoreCase))
                    {
                        toRemove.Add(condition);
                        if (config.Debug)
                            _logger.Info($"[QuestFilterMod][REMOVE] Удалено условие '{checkType}' из квеста '{q.Name}'");
                    }
                }

                foreach (var cond in toRemove)
                {
                    q.Conditions.AvailableForFinish.Remove(cond);
                }
            }

            // Защита: добавляем CompleteQuest, если нет условий завершения
            if (q.Conditions.AvailableForFinish == null || q.Conditions.AvailableForFinish.Count == 0)
            {
                q.Conditions.AvailableForFinish ??= new List<QuestCondition>();
                q.Conditions.AvailableForFinish.Add(new QuestCondition
                {
                    Id = Guid.NewGuid().ToString("N")[..24],
                    ConditionType = "CompleteQuest",
                    DynamicLocale = false,
                    Target = new ListOrT<string>(null, q.Id),
                    Value = 1,
                    Index = 0
                });
                _logger.Warning($"[QuestFilterService] ⚠️ Нет условий завершения для квеста '{q.Name}'. Добавлено условие CompleteQuest.");
            }
        }

        // ⚠️ Был объявлен invalidQuests, но нигде не использовался — удалил
        // Это был потенциальный баг: раньше, возможно, проверяли условия, а теперь — нет.

        _logger.Info($"[QuestFilterMod] Готово: оставлено {selectedQuests.Count} квестов.");
    }
}