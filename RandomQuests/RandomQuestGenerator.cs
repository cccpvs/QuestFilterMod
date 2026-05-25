using QuestFilterMod.Converters;
using QuestFilterMod.RandomQuests.Models;
using QuestFilterMod.RandomQuests.Utils;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Logging;
using SPTarkov.Server.Core.Models.Spt.Server;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO; // 🔥 Явно добавляем для Path
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

// 🔥 Чтобы избежать конфликта: явно указываем алиас
using EftPath = SPTarkov.Server.Core.Models.Eft.Common.Tables.Path;

namespace QuestFilterMod.RandomQuests
{
    
    
    public class RandomQuestGenerator
    {
        private readonly ISptLogger<Plugin> _logger;
        private readonly DatabaseService _databaseService;
        private readonly Random _random = new();
        private readonly QuestConfig _config;

        public RandomQuestGenerator(ISptLogger<Plugin> logger, DatabaseService databaseService)
        {
            _logger = logger;
            _databaseService = databaseService;

            // 🔧 Исправлено: используем System.IO.Path
            var configPath = global::System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "user",
                "mods",
                "questFilterMod",
                "Data",
                "QuestConfig.json"
            );

            _logger.Info($"[RandomQuestGenerator] Ищу конфиг: {configPath}");

            if (!File.Exists(configPath))
            {
                _logger.Error($"[RandomQuestGenerator] ❌ Файл конфигурации не найден: {configPath}");
                _logger.Error($"[RandomQuestGenerator] Убедитесь, что файл существует по пути: user/mods/questFilterMod/Data/QuestConfig.json");
                throw new FileNotFoundException("Конфигурация квестов не найдена", configPath);
            }

            _config = JsonHelper.LoadFromJson<QuestConfig>(configPath)
                ?? throw new InvalidOperationException("Не удалось загрузить конфигурацию квестов.");
        }

        public Quest? GenerateSingleQuest()
        {
            try
            {
                // 🔧 Исправлено: явно указываем тип T
                var locationKey = _config.Locations.Keys.ToList().RandomItem<string>(_random);
                var location = _config.Locations[locationKey];
                var targetPoint = location.Targets.RandomItem<string>(_random);
                var rewardItem = _config.RewardItems.RandomItem<RewardItemConfig>(_random);

                string NewId() => Guid.NewGuid().ToString("N")[..24];
                int index = 0;
                var quest = new Quest
                {
                    Id = new MongoId(NewId()),
                    Name = $"{_config.DefaultQuest.Name} ({targetPoint})",
                    Description = _config.DefaultQuest.Description,
                    TraderId = new MongoId(_config.TraderIds.RandomItem(_random)),
                    Side = null,
                    Location = location.Id,
                    Image = _config.DefaultQuest.Image,
                    Restartable = false,
                    CanShowNotificationsInGame = true,
                    SecretQuest = false,
                    Status = (int)QuestStatusEnum.Locked,
                    Type = QuestTypeEnum.Exploration,
                    ProgressSource = "eft",
                    AcceptanceAndFinishingSource = "eft",
                    GameModes = new List<string>(),
                    RankingModes = new List<string>(),
                    AcceptPlayerMessage = "AcceptPlayerMessage",
                    ChangeQuestMessageText = "ChangeQuestMessageText",
                    CompletePlayerMessage = "CompletePlayerMessage",
                    Note = "Note",
                    StartedMessageText = "quest_started_default",
                    SuccessMessageText = "quest_completed_default",
                    FailMessageText = "quest_failed_default",
                    Conditions = new QuestConditionTypes
                    {
                        AvailableForStart = new List<QuestCondition>(),
                        AvailableForFinish = new List<QuestCondition>(),
                        Fail = new List<QuestCondition>()
                    },
                    Rewards = new Dictionary<string, List<Reward>>
                    {
                        ["Started"] = new List<Reward>(),
                        ["Success"] = new List<Reward>(),
                        ["Fail"] = new List<Reward>()
                    }
                };
                

                /// Условие: посетить точку
                var visitCondition = new QuestCondition
                {
                    Id = new MongoId("686407ff1250f86c92d09ad7"),
                    ConditionType = "VisitPlace",
                    DynamicLocale = false,
                    Value = 1,
                    Index = index++,
                    ParentId = "",
                    VisibilityConditions = new List<VisibilityCondition>()
                };

                // Добавляем Target в ExtensionData
                if (visitCondition.ExtensionData == null)
                    visitCondition.ExtensionData = new Dictionary<string, object>(StringComparer.Ordinal);


                visitCondition.ExtensionData["target"] = targetPoint;

                quest.Conditions.AvailableForFinish.Add(visitCondition);

                // 🔁 Условие: CounterCreator с ExitStatus внутри
                var exitStatusCondition = new QuestCondition
                {
                    Id = new MongoId(NewId()),
                    ConditionType = "ExitStatus",
                    DynamicLocale = false,
                    Index = 0,
                    ParentId = "",
                    VisibilityConditions = new List<VisibilityCondition>()
                };

                // Устанавливаем status напрямую (не через ExtensionData!)
                if (exitStatusCondition.ExtensionData == null)
                    exitStatusCondition.ExtensionData = new Dictionary<string, object>(StringComparer.Ordinal);

                exitStatusCondition.ExtensionData["status"] = new[] { "Survived", "Runner", "Transit" };

                // Оборачиваем в CounterCreator
                var counterCreator = new QuestCondition
                {
                    Id = new MongoId("596762ec86f77426d3687a87"),
                    ConditionType = "CounterCreator",
                    DynamicLocale = false,
                    Value = 1,
                    Index = index++,
                    ParentId = "",
                    OneSessionOnly = true,
                    CompleteInSeconds = 30,
                    DoNotResetIfCounterCompleted = false,
                    VisibilityConditions = new List<VisibilityCondition>(),
                    ExtensionData = new Dictionary<string, object>
                    {
                        ["counter"] = new
                        {
                            id = new MongoId(NewId()),
                            conditions = new[] { exitStatusCondition }
                        }
                    }
                };

                quest.Conditions.AvailableForFinish.Add(counterCreator);

                // Опыт за выполнение
                quest.Rewards["Success"].Add(new Reward
                {
                    Id = new MongoId(NewId()),
                    Type = RewardType.Experience,
                    Value = _config.DefaultQuest.ExperienceReward,
                    FindInRaid = false,
                    IsEncoded = false,
                    IsHidden = false
                });

                // Предметная награда
                string itemId = new MongoId(NewId());
                var item = (Item)Activator.CreateInstance(typeof(Item), nonPublic: true)!;
                item.Id = itemId;

                var templateProp = typeof(Item).GetProperty("Template", BindingFlags.Public | BindingFlags.Instance);
                if (templateProp != null && templateProp.CanWrite)
                {
                    templateProp.SetValue(item, new MongoId(rewardItem.Tpl));
                }
                else
                {
                    _logger.Error("[RandomQuestGenerator] ❌ Не удалось установить 'Template' у Item. Возможно, изменилась структура класса Item.");
                    throw new InvalidOperationException("Не удалось установить свойство 'Template' у объекта Item. Проверьте совместимость с текущей версией SPT.");
                }

                var upd = new Upd();

                // 🔧 Исправлено: получаем FieldInfo или PropertyInfo как object, работаем отдельно
                FieldInfo stackField = typeof(Upd).GetField("StackObjectsCount", BindingFlags.Instance | BindingFlags.NonPublic);
                PropertyInfo stackProp = typeof(Upd).GetProperty("StackObjectsCount", BindingFlags.Instance | BindingFlags.Public);

                if (stackField != null)
                {
                    stackField.SetValue(upd, (double)rewardItem.Count); 
                }
                else if (stackProp != null && stackProp.CanWrite)
                {
                    stackProp.SetValue(upd, (double)rewardItem.Count); 
                }
                else
                {
                    _logger.Warning("[RandomQuestGenerator] ⚠️ Не найдено ни поля, ни свойства 'StackObjectsCount'");
                }

                item.Upd = upd;

                quest.Rewards["Success"].Add(new Reward
                {
                    Id = new MongoId(NewId()),
                    Type = RewardType.Item,
                    Target = itemId,
                    Value = rewardItem.Count,
                    FindInRaid = false,
                    IsEncoded = false,
                    IsHidden = false,
                    Unknown = false,
                    GameMode = new HashSet<string> { "regular", "pve" },
                    AvailableInGameEditions = new HashSet<string>(),
                    Items = new List<Item> { item }
                });

                // Установка времени провала
                var failDateTimeProp = typeof(Quest).GetProperty("FailDateTime");
                failDateTimeProp?.SetValue(quest, DateTime.UtcNow.AddDays(_config.DefaultQuest.FailAfterDays));

                AddQuestToLocale(_databaseService.GetTables(), quest);

                _logger.Info($"[RandomQuestGenerator] ✅ Квест '{quest.Id}' сгенерирован: {targetPoint} на {locationKey}");
                //_logger.Info($"[RandomQuestGenerator] 🔍 Сгенерированный квест (JSON):\n{JsonHelper.ToJson(quest)}");
                return quest;
            }
            catch (Exception e)
            {
                _logger.Error($"[RandomQuestGenerator] Ошибка при генерации: {e.Message}\n{e.StackTrace}");
                return null;
            }
        }

        public void AddQuestToServer(Quest quest)
        {
            if (quest == null) return;

            EnsureRewards(quest);
            EnsureFailConditions(quest);

            var quests = _databaseService.GetQuests();
            quests[quest.Id] = quest;

            _logger.Info($"[RandomQuestGenerator] ✅ Квест '{quest.Id}' добавлен в базу.");
        }

        private void EnsureRewards(Quest quest)
        {
            if (quest.Rewards == null)
                quest.Rewards = new Dictionary<string, List<Reward>>();

            foreach (var status in new[] { "Started", "Success", "Fail" })
            {
                if (!quest.Rewards.ContainsKey(status))
                    quest.Rewards[status] = new List<Reward>();
            }
        }

        private void EnsureFailConditions(Quest quest)
        {
            if (quest.Conditions?.Fail == null)
                quest.Conditions.Fail = new List<QuestCondition>();
        }

        public void AddQuestToLocale(DatabaseTables tables, Quest quest)
        {
            var globalLocales = tables.Locales.Global;

            foreach (var lang in new[] { "en", "ru" })
            {
                if (!globalLocales.TryGetValue(lang, out var lazyDict))
                {
                    lazyDict = new LazyLoad<Dictionary<string, string>>(() => new());
                    globalLocales[lang] = lazyDict;
                }

                var dict = lazyDict.Value;
                if (!dict.TryGetValue("quest", out var categoryJson) || string.IsNullOrEmpty(categoryJson))
                {
                    categoryJson = "{}";
                    dict["quest"] = categoryJson;
                }

                var categoryObj = JsonSerializer.Deserialize<Dictionary<string, object>>(categoryJson) ?? new();
                if (!categoryObj.ContainsKey(quest.Id))
                {
                    categoryObj[quest.Id] = new Dictionary<string, string>();
                }

                var questText = (Dictionary<string, string>)categoryObj[quest.Id];
                questText["name"] = quest.Name ?? "Unnamed";
                questText["description"] = quest.Description ?? "No description";
                questText["startedMessageText"] = quest.StartedMessageText ?? "Started";
                questText["successMessageText"] = quest.SuccessMessageText ?? "Completed";
                questText["failMessageText"] = quest.FailMessageText ?? "Failed";

                dict["quest"] = JsonSerializer.Serialize(categoryObj);
            }
        }
    }

    // ✅ Исправлено: расширения работают корректно
    public static class ListExtensions
    {
        public static T RandomItem<T>(this IReadOnlyList<T> list, Random random)
            => list[random.Next(list.Count)];
    }
}