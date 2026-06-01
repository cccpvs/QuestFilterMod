using QuestFilterMod.RandomQuests;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
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

    public void ApplyFilters(QuestFilterConfig config)
    {
        // 🔒 Защита от повторного вызова
        if (_hasAppliedFilters)
        {
            if (Plugin._config.Debug)
                _logger.Info("[QuestFilterMod][Service] Фильтры уже применены. Пропускаем повторную обработку.");
            return;
        }
        _hasAppliedFilters = true;

        if (!config.Enabled) return;

        var quests = _databaseService.GetQuests();
        if (quests == null || quests.Count == 0)
        {
            if (Plugin._config.Debug)
                _logger.Info("[QuestFilterMod][Service] Нет квестов в базе.");
            return;
        }


        var allowedTypes = GetAllowedTypes(config);
        var allQuestList = quests.Values
            .Where(q => allowedTypes.Contains(q.Type))
            .Where(q => !ShouldExcludeByProgressSource(q, config))
            .ToList();

        bool shouldGenerate = config.GenerateRandomQuests.Enable && config.GenerateRandomQuests.Count > 0;

        // Если нет подходящих квестов и не будет генерации — удаляем ВСЕ квесты, если нужно
        if (allQuestList.Count == 0 && !shouldGenerate)
        {
            if (Plugin._config.Debug)
                _logger.Info("[QuestFilterMod][Service] Нет подходящих квестов по типам и генерация отключена.");

            // 🟢 Используем другое имя, чтобы не конфликтовать с будущей переменной
            var emptySelected = new List<Quest>();
            ModifyQuests(quests, emptySelected, config);

            if (Plugin._config.Debug)
                _logger.Info($"[QuestFilterMod][Service] Готово: оставлено {emptySelected.Count} квестов.");

            return;
        }

        // ✅ Теперь безопасно объявляем основную переменную
        var selectedQuests = SelectQuests(allQuestList, config);

        // Генерация случайных квестов
        if (config.GenerateRandomQuests.Enable && config.GenerateRandomQuests.Count > 0)
        {
            // 🔁 Сбрасываем трекер перед серией генерации
            // Это позволяет использовать ВСЕ точки и предметы снова
            _randomQuestGenerator.ResetTracker();

            var generatedCount = 0;
            var generatedQuests = new List<Quest>();

            for (int i = 0; i < config.GenerateRandomQuests.Count; i++)
            {
                var randomQuest = _randomQuestGenerator.GenerateSingleQuest();
                if (randomQuest != null)
                {
                    generatedQuests.Add(randomQuest);
                    generatedCount++;

                    // Добавляем в базу данных квестов
                    if (!quests.ContainsKey(randomQuest.Id))
                    {
                        quests[randomQuest.Id] = randomQuest;
                    }

                    if (Plugin._config.Debug)
                    {
                        var locationName = LocationHelper.TryGetPascalName(randomQuest.Location, out var pascalName)
                        ? pascalName.ToLowerInvariant()
                        : "unknown";


                        _logger.Info($"[QuestFilterMod][Service] Сгенерирован квест: '{randomQuest.Name}' (ID: {randomQuest.Id}, локация: {locationName})");

                    }
                        

                    
                }
            }
            // Добавляем в selectedQuests, чтобы они не были удалены
            selectedQuests.AddRange(generatedQuests);

            if (Plugin._config.Debug)
                _logger.Info($"[QuestFilterMod][Service] Готово: сгенерировано {generatedCount} случайных квестов.");
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

        // Группируем по локациям
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
            .Select(p => p.GetValue(config.RandomQuests.Location))
            .Any(v => v is int count && count > 0);

        // 🔥 Ключевое изменение: если включена генерация случайных квестов и нет фильтров — НЕ возвращаем все квесты!
        if (!hasLocationFilters && config.RandomQuests.Count == 0)
        {
            // Но если включена генерация случайных квестов — не возвращаем стандартные автоматически
            if (config.GenerateRandomQuests.Enable && config.GenerateRandomQuests.Count > 0)
            {
                if (Plugin._config.Debug)
                    _logger.Info("[QuestFilterMod][Service] Режим только случайных квестов: стандартные квесты НЕ добавляются");

                return new List<Quest>(); // Пусто — только сгенерированные будут добавлены позже
            }

            // Иначе — оставляем поведение по умолчанию (все квесты)
            if (Plugin._config.Debug)
                _logger.Info($"[QuestFilterMod][Service] Режим 'все квесты': оставлено {allQuests.Count} квестов по типу");

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
                _logger.Info($"[QuestFilterMod][Service] Добавлено {extra.Count} квестов для достижения total={config.RandomQuests.Count}");
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

        if (Plugin._config.Debug)
            _logger.Info($"[QuestFilterMod][Service] {picked.Count} квестов для '{key}'");
    }

    private void ModifyQuests(
        Dictionary<MongoId, Quest> allQuests,
        List<Quest> selectedQuests,
        QuestFilterConfig config)
    {
        var selectedIds = selectedQuests.Select(q => q.Id).ToHashSet();

        var countRemoveQuest = 0;
        // Удаление лишних квестов
        if (config.RemoveQuests)
        {
            
            var toRemove = allQuests.Values.Where(q => !selectedIds.Contains(q.Id)).ToList();
            foreach (var q in toRemove)
            {
                allQuests.Remove(q.Id);
                if (config.Debug)
                    countRemoveQuest++;
            }
        }
        if (Plugin._config.Debug)
            _logger.Info($"[QuestFilterMod][Service] Всего удалено: {countRemoveQuest}");

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
                    if (Plugin._config.Debug)
                        _logger.Info($"[QuestFilterMod][Service] ⚠️ Восстановлен статус награды: '{status}' для квеста '{q.Id}'");
                }
            }

            // Переназначение трейдера
            if (!string.IsNullOrEmpty(config.TargetTraderId))
            {
                q.TraderId = config.TargetTraderId;
                countTraiderTransfer++;
                /*if (Plugin._config.Debug)
                    _logger.Info($"[QuestFilterMod][Service] Квест '{q.Name}' ({q.Id}) → трейдер {config.TargetTraderId}");*/
            }

            // Очистка условий старта
            if (config.RemoveStartConditionsQuest && q.Conditions?.AvailableForStart != null)
            {
                q.Conditions.AvailableForStart.Clear();
                if (Plugin._config.Debug)
                    _logger.Info($"[QuestFilterMod][Service] Условия старта удалены для квеста '{q.Name}'");
            }

            // Удаление указанных типов условий завершения
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
                        if (config.Debug)
                            _logger.Info($"[QuestFilterMod][Service] Удалено условие '{checkType}' из квеста '{q.Name}'");
                    }
                }

                foreach (var cond in toRemove)
                {
                    q.Conditions.AvailableForFinish.Remove(cond);
                }
            }
        }

        // Логирование статистики
        if (Plugin._config.Debug)
        {
            var locationStats = new Dictionary<string, int>();
            var locationDetails = new List<string>();

            _logger.Info($"[QuestFilterMod][Service] Перенесено квестов трейдеру: {countTraiderTransfer}"); 

            foreach (var kvp in locationStats.OrderBy(x => x.Key))
            {
                _logger.Info($"[QuestFilterMod][Service]  • {kvp.Key}: {kvp.Value} шт.");
            }
            foreach (var quest in selectedQuests)
            {
                string locKey = LocationHelper.TryGetPascalName(quest.Location, out var pascalName)
                    ? pascalName.ToLowerInvariant()
                    : "unknown";
                locationStats[locKey] = locationStats.GetValueOrDefault(locKey, 0) + 1;
                locationDetails.Add($"[QuestFilterMod][Service] Квест '{quest.Name}' ({quest.Id}) → локация '{locKey}'");
            }
            _logger.Info($"[QuestFilterMod][Service] Всего оставлено квестов: {selectedQuests.Count}");

        }
    }
}