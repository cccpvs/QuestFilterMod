using EFT;
using EFT.Quests;
using QuestFilterMod.RandomQuests.Models;
using QuestFilterMod.RandomQuests.Utils;
using SPTarkov.Server.Core.Constants;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Logging;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Server;
using SPTarkov.Server.Core.Models.Spt.Templates;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Services.Mod;
using SPTarkov.Server.Core.Utils.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design.Serialization;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using static RawQuestClass;
using ServerLocationConfig = SPTarkov.Server.Core.Models.Spt.Config.LocationConfig;
using ServerQuestConfig = SPTarkov.Server.Core.Models.Spt.Config.QuestConfig;
using SPTServerConfig = SPTarkov.Server.Core.Models.Spt.Config;

namespace QuestFilterMod.RandomQuests
{


    public class RandomQuestGenerator
    {
        private readonly ISptLogger<Plugin> _logger;
        private readonly DatabaseService _databaseService;
        private readonly Random _random = new();
        private readonly QuestConfig _config;
        private readonly UniqueQuestTracker _tracker = new();
        private readonly CustomQuestService _customQuestService;

        private void LogQuest(Quest quest)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            string json = JsonSerializer.Serialize(quest, options);
            _logger.Info($"[DEBUG QUEST] Квест '{quest.Name}' (ID: {quest.Id}):\n{json}");
        }

        public record QuestKey(string LocationId, string TargetPoint, string ItemTpl = "", string QuestType = "");

        public class UniqueQuestTracker
        {
            private readonly HashSet<QuestKey> _usedKeys = new();

            public bool IsUsed(QuestKey key) => _usedKeys.Contains(key);

            public bool TryUse(QuestKey key)
            {
                return _usedKeys.Add(key); // Add возвращает true, если элемента не было
            }

            public void Clear() => _usedKeys.Clear();
        }

        public RandomQuestGenerator(
                ISptLogger<Plugin> logger,
                DatabaseService databaseService,
                CustomQuestService customQuestService)
        {
            _logger = logger;
            _databaseService = databaseService;
            _customQuestService = customQuestService;


            var assemblyLocation = Assembly.GetExecutingAssembly().Location;

            var configPath = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "user",
                "mods",
                "questFilterMod",
                "RandomQuestConfig.json"
            );

            _logger.Info($"[RandomQuestGenerator] Ищу конфиг: {configPath}");

            if (!File.Exists(configPath))
            {
                _logger.Error($"[RandomQuestGenerator] ❌ Файл конфигурации не найден: {configPath}");
                throw new FileNotFoundException("Конфигурация квестов не найдена", configPath);
            }

            _config = JsonHelper.LoadFromJson<QuestConfig>(configPath)
                ?? throw new InvalidOperationException("Не удалось загрузить конфигурацию квестов.");
        }

        public Quest? GenerateSingleQuest()
        {

            try
            {
                var types = new List<Func<Quest?>>();

                if (_config.QuestGeneration.Types.Exploration)
                    types.Add(GenerateExplorationQuest);


                if (_config.QuestGeneration.Types.Planting && _config.DeliveryQuest.Enabled)
                    types.Add(GenerateDeliveryQuest);

                if (!types.Any())
                {
                    _logger.Error("[RandomQuestGenerator] ❌ Нет доступных типов квестов для генерации.");
                    return null;
                }

                return types.RandomItem(_random)();
            }
            catch (Exception e)
            {
                _logger.Error($"[RandomQuestGenerator] Ошибка при генерации: {e.Message}\n{e.StackTrace}");
                return null;
            }
        }

        //Квесты Exploration
        private Quest? GenerateExplorationQuest()
        {
            var allowedLocations = _config.ExplorationQuest
                .Where(x => _config.QuestGeneration.AllowedLocations.TryGetValue(x.Key, out var allowed) ? allowed : false)
                .ToList();

            if (!allowedLocations.Any())
            {
                _logger.Warning("[RandomQuestGenerator] ❌ Нет разрешённых локаций для Exploration.");
                return null;
            }

            // Собираем все возможные уникальные комбинации (локация + точка)
            var candidates = new List<(string LocKey, LocationConfig Loc, string Target)>();
            foreach (var kvp in allowedLocations)
            {
                foreach (var target in kvp.Value.Targets)
                {
                    var key = new QuestKey(kvp.Value.Id, target, "", "Exploration");
                    if (!_tracker.IsUsed(key))
                        candidates.Add((kvp.Key, kvp.Value, target));
                }
            }

            if (!candidates.Any())
                return null; // Все комбинации уже использованы

            var (locationKey, location, targetPoint) = candidates.RandomItem(_random);
            _tracker.TryUse(new QuestKey(location.Id, targetPoint, "", "Exploration"));

            string NewId() => Guid.NewGuid().ToString("N")[..24];
            int index = 0;

            string questId = NewId();

            var quest = new Quest
            {
                Id = new MongoId(questId),
                QuestName = $"{questId} questName",
                Name = $"{questId} name",
                Description = $"{questId} description",
                Note = $"{questId} note",
                TraderId = new MongoId(_config.TraderIds.RandomItem(_random)),
                Side = "Pmc",
                Location = location.Id,
                Image = _config.DefaultQuest.Image,
                InstantComplete = false,
                IsKey = false,
                Restartable = false,
                CanShowNotificationsInGame = true,
                SecretQuest = false,
                Status = 0,
                Type = QuestTypeEnum.PickUp,
                ProgressSource = "eft",
                AcceptanceAndFinishingSource = "eft",
                GameModes = new List<string>(),
                RankingModes = new List<string>(),
                AcceptPlayerMessage = $"{questId} accept",
                ChangeQuestMessageText = $"{questId} change",
                CompletePlayerMessage = $"{questId} complete",
                StartedMessageText = "quest_started_default",
                SuccessMessageText = "quest_completed_default",
                FailMessageText = "quest_failed_default",
                Conditions = new QuestConditionTypes
                {
                    AvailableForStart = new(),
                    AvailableForFinish = new(),
                    Fail = new()
                },
                Rewards = new Dictionary<string, List<Reward>>
                {
                    ["Started"] = new(),
                    ["Success"] = new(),
                    ["Fail"] = new()
                }
            };

            // Посещение точки
            var visitCondition = new QuestCondition
            {
                Id = new MongoId(NewId()),
                ConditionType = "VisitPlace",
                DynamicLocale = false,
                Value = 1,
                Index = index++,
                ParentId = "",
                VisibilityConditions = new()
            };


            visitCondition.ExtensionData ??= new(StringComparer.Ordinal);
            visitCondition.ExtensionData["target"] = targetPoint;
            quest.Conditions.AvailableForFinish.Add(visitCondition);

            // Условие выхода
            var exitStatus = new QuestCondition
            {
                Id = new MongoId(NewId()),
                ConditionType = "ExitStatus",
                DynamicLocale = false,
                ExtensionData = new()
                {
                    ["status"] = new[] { "Survived", "Runner", "Transit" }
                }
            };

            var counterCreator = new QuestCondition
            {
                Id = new MongoId(NewId()),
                ConditionType = "CounterCreator",
                DynamicLocale = false,
                Value = 1,
                Index = index++,
                OneSessionOnly = true,
                CompleteInSeconds = 30,
                Type = "Completion",
                DoNotResetIfCounterCompleted = false,
                ExtensionData = new()
                {
                    ["counter"] = new
                    {
                        id = new MongoId(NewId()),
                        conditions = new[] { exitStatus },
                        DynamicLocale = false
                    }
                }
            };

            quest.Conditions.AvailableForFinish.Add(counterCreator);

            AddExperienceReward(quest);
            AddMoneyReward(quest);
            AddRandomItemRewards(quest);
            AddTraderStandingReward(quest);

            // Сохраняем квест
            CreateAndRegisterQuest(quest);

            _logger.Info($"[GenerateDeliveryQuest] ✅ Квест '{quest.Id}' создан и локализован");
            _logger.Info($"[GenerateDeliveryQuest] ✅ Квест '{quest.Id}' создан");
            return quest;
        }


        //Квесты Delivery
        private Quest? GenerateDeliveryQuest()
        {
            _logger.Info("[GenerateDeliveryQuest] Начало генерации квеста на доставку предмета");

            var delivery = _config.DeliveryQuest;

            if (!delivery.Enabled)
            {
                _logger.Warning("[GenerateDeliveryQuest] ❌ DeliveryQuest отключён");
                return null;
            }

            if (!delivery.ItemPool.Any())
            {
                _logger.Warning("[GenerateDeliveryQuest] ❌ Пул предметов пуст");
                return null;
            }

            if (!delivery.Locations.Any())
            {
                _logger.Warning("[GenerateDeliveryQuest] ❌ Нет локаций для закладки");
                return null;
            }

            var allowedLocations = delivery.Locations
                .Where(x => _config.QuestGeneration.AllowedLocations.TryGetValue(x.Key, out var allowed) && allowed)
                .ToList();

            if (!allowedLocations.Any())
            {
                _logger.Warning("[GenerateDeliveryQuest] ❌ Нет разрешённых локаций");
                return null;
            }

            _logger.Info($"[GenerateDeliveryQuest] Разрешено {allowedLocations.Count} локаций");

            var candidates = new List<(string LocKey, LocationConfig Loc, string Target, ItemPoolConfig Item)>();

            foreach (var locEntry in allowedLocations)
            {
                if (locEntry.Value?.Targets == null) continue;

                foreach (var target in locEntry.Value.Targets.Where(t => !string.IsNullOrEmpty(t)))
                {
                    foreach (var poolItem in delivery.ItemPool.Where(i => !string.IsNullOrEmpty(i.Tpl)))
                    {
                        var key = new QuestKey(locEntry.Value.Id, target, poolItem.Tpl, "Delivery");
                        if (!_tracker.IsUsed(key))
                        {
                            candidates.Add((locEntry.Key, locEntry.Value, target, poolItem));
                        }
                    }
                }
            }

            if (!candidates.Any())
            {
                _logger.Info("[GenerateDeliveryQuest] ⚠️ Нет доступных комбинаций");
                return null;
            }

            var (locationKey, location, targetPoint, item) = candidates.RandomItem(_random);
            var questKey = new QuestKey(location.Id, targetPoint, item.Tpl, "Delivery");

            if (!_tracker.TryUse(questKey))
            {
                _logger.Warning("[GenerateDeliveryQuest] ⚠️ Ключ уже использован");
                return null;
            }

            _logger.Info($"[GenerateDeliveryQuest] Генерация: доставить {item.Name} → {targetPoint} на {locationKey}");


            var traderId = new MongoId(_config.TraderIds.RandomItem(_random));
            if (!_databaseService.GetTraders().ContainsKey(traderId))
            {
                _logger.Error($"[GenerateDeliveryQuest] ❌ Трейдер {traderId} не найден в базе");
                return null;
            }

            try
            {
                string NewId() => Guid.NewGuid().ToString("N")[..24];
                int index = 0;
                string questId = NewId();
                var quest = new Quest
                {
                    Id = new MongoId(questId),
                    QuestName = $"{questId} questName",
                    Name = $"{questId} name",
                    Description = $"{questId} description",
                    Note = $"{questId} note",
                    TraderId = new MongoId(_config.TraderIds.RandomItem(_random)),
                    Side = "Pmc",
                    Location = location.Id,
                    Image = _config.DefaultQuest.Image,
                    InstantComplete = false,
                    IsKey = false,
                    Restartable = false,
                    CanShowNotificationsInGame = true,
                    SecretQuest = false,
                    Status = 0,
                    Type = QuestTypeEnum.Discover,
                    ProgressSource = "eft",
                    AcceptanceAndFinishingSource = "eft",
                    GameModes = new List<string>(),
                    RankingModes = new List<string>(),
                    AcceptPlayerMessage = $"{questId} accept",
                    ChangeQuestMessageText = $"{questId} change",
                    CompletePlayerMessage = $"{questId} complete",
                    StartedMessageText = "quest_started_default",
                    SuccessMessageText = "quest_completed_default",
                    FailMessageText = "quest_failed_default",
                    Conditions = new QuestConditionTypes
                    {
                        AvailableForStart = new(),
                        AvailableForFinish = new(),
                        Fail = new()
                    },
                    Rewards = new Dictionary<string, List<Reward>>
                    {
                        ["Started"] = new(),
                        ["Success"] = new(),
                        ["Fail"] = new()
                    }
                };

                // 🎁 Выдаём предмет при старте
                AddItemReward(quest, item.Tpl, 1, item.Name, rewardType: "Started");

                // 📍 Условие: заложить
                var plantCondition = new QuestCondition
                {
                    Id = new MongoId(NewId()),
                    ConditionType = "LeaveItemAtLocation",
                    DynamicLocale = false,
                    Value = 1,
                    Index = index++,
                    ParentId = "",
                    VisibilityConditions = new List<VisibilityCondition>(),
                    PlantTime = delivery.PlantTime,
                    ZoneId = targetPoint,
                    ExtensionData = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["target"] = new[] { item.Tpl },      // ← массив строк
                        ["zoneId"] = targetPoint,
                        ["plantTime"] = delivery.PlantTime
                    }
                };
                
                quest.Conditions.AvailableForFinish.Add(plantCondition);


                // ✅ Выход

                var counterCreator = new QuestCondition
                {
                    Id = new MongoId(NewId()),
                    ConditionType = "CounterCreator",
                    Value = 1,
                    Index = index++,
                    DynamicLocale = false,
                    OneSessionOnly = false,
                    IsNecessary = false,
                    IsResetOnConditionFailed = false,
                    ParentId = "",
                    Type = "Completion",
                    CompleteInSeconds = delivery.PlantTime,
                    VisibilityConditions = new List<VisibilityCondition>(),
                    GlobalQuestCounterId = "",
                    DoNotResetIfCounterCompleted = false,
                    Counter = new()
                    {
                        Conditions = new List<QuestConditionCounterCondition>
                        {
                            new()
                            {
                                Id = new MongoId(NewId()),
                                ConditionType = "ExitStatus",
                                DynamicLocale = false,
                                Status = ["Survived", "Transit"]

                            }
                        }
                    }
 
                };
                    

                quest.Conditions.AvailableForFinish.Add(counterCreator);

                // 🏆 Награды за успех
                AddExperienceReward(quest);
                AddMoneyReward(quest);
                AddRandomItemRewards(quest);
                AddTraderStandingReward(quest);

                if (string.IsNullOrEmpty(quest.Name))
                {
                    _logger.Error($"[RandomQuestGenerator] ❌ Квест {quest.Id} не имеет имени — пропуск");
                    return null;
                }

                if (quest.Conditions?.AvailableForFinish?.Any() != true)
                {
                    _logger.Error($"[RandomQuestGenerator] ❌ Квест {quest.Id} не имеет условий завершения");
                    return null;
                }


                // Сохраняем квест
                CreateAndRegisterQuest(quest);

                _logger.Info($"[GenerateDeliveryQuest] ✅ Квест '{quest.Id}' создан и локализован");
                _logger.Info($"[GenerateDeliveryQuest] ✅ Квест '{quest.Id}' создан");
                return quest;
            }
            catch (Exception ex)
            {
                _logger.Error($"[GenerateDeliveryQuest] 🔥 Ошибка: {ex}");
                return null;
            }
        }

        private void AddExperienceReward(Quest quest)
        {
            var range = _config.DefaultQuest.ExperienceRewardRange;
            int exp = _random.Next(range.Min / range.Step, (range.Max / range.Step) + 1) * range.Step;

            quest.Rewards["Success"].Add(new Reward
            {
                Id = new MongoId(Guid.NewGuid().ToString("N")[..24]),
                Type = RewardType.Experience,
                Value = exp,
                FindInRaid = false,
                IsEncoded = false,
                IsHidden = false
            });
        }

        private void AddMoneyReward(Quest quest)
        {
            if (!_config.RewardMoney.Enabled || string.IsNullOrEmpty(_config.RewardMoney.Tpl)) return;

            int amount = GenerateRandomAmount(_config.RewardMoney.Min, _config.RewardMoney.Max, _config.RewardMoney.Step);
            AddItemReward(quest, _config.RewardMoney.Tpl, amount, "RUB");
        }

        private void AddRandomItemRewards(Quest quest)
        {
            if (!_config.RewardItems.Enabled || !_config.RewardItems.Pool.Any()) return;

            int count = _random.Next(_config.RewardItems.Count.Min, _config.RewardItems.Count.Max + 1);
            var usedTpls = new HashSet<string>();

            for (int i = 0; i < count; i++)
            {
                var rewardItem = _config.RewardItems.Pool.WeightedRandomItem(_random, x => x.Weight);
                if (usedTpls.Contains(rewardItem.Tpl)) continue;

                AddItemReward(quest, rewardItem.Tpl, 1, rewardItem.Name);
                usedTpls.Add(rewardItem.Tpl);
            }
        }

        private void AddTraderStandingReward(Quest quest)
        {
            if (!_config.RewardTraderStanding.Enabled)
                return;

            // Генерируем случайное значение: от Min до Max
            float value = (float)_random.NextDouble() *
                          (_config.RewardTraderStanding.Max - _config.RewardTraderStanding.Min) +
                          _config.RewardTraderStanding.Min;

            quest.Rewards["Success"].Add(new Reward
            {
                Id = new MongoId(Guid.NewGuid().ToString("N")[..24]),
                Type = RewardType.TraderStanding,
                Target = quest.TraderId,
                Value = (float)Math.Round(value, 3), // Округляем до 3 знаков (например: 0.017)
                FindInRaid = false,
                IsEncoded = false,
                IsHidden = false,
                Unknown = false,
                GameMode = new HashSet<string> { "regular", "pve" },
                AvailableInGameEditions = new HashSet<string>()
            });
        }

        private int GenerateRandomAmount(int min, int max, int step)
        {
            int range = (max - min) / step;
            return min + _random.Next(range + 1) * step;
        }

        private void AddItemReward(Quest quest, string tpl, int count, string name = "Item", string rewardType = "Success")
        {
            string itemId = Guid.NewGuid().ToString("N")[..24];
            var gameItem = (Item)Activator.CreateInstance(typeof(Item), nonPublic: true)!;
            gameItem.Id = new MongoId(itemId);

            var templateProp = typeof(Item).GetProperty("Template", BindingFlags.Public | BindingFlags.Instance);
            if (templateProp != null && templateProp.CanWrite)
            {
                templateProp.SetValue(gameItem, new MongoId(tpl));
            }
            else
            {
                _logger.Error("[RandomQuestGenerator] ❌ Не удалось установить 'Template' у Item.");
                return;
            }

            var upd = new Upd();
            typeof(Upd)
                .GetMethod("Set", new[] { typeof(string), typeof(object) })?
                .Invoke(upd, new object[] { "StackObjectsCount", count });

            gameItem.Upd = upd;

            quest.Rewards[rewardType].Add(new Reward
            {
                Id = new MongoId(Guid.NewGuid().ToString("N")[..24]),
                Type = RewardType.Item,
                Target = new MongoId(itemId),
                Value = count,
                FindInRaid = true,
                IsEncoded = false,
                IsHidden = false,
                Unknown = false,
                GameMode = new HashSet<string> { "regular", "pve" },
                AvailableInGameEditions = new HashSet<string>(),
                Items = new List<Item> { gameItem }
            });
        }

        public void CreateAndRegisterQuest(Quest quest)
        {
            if (quest == null) return;

            EnsureRewards(quest);
            EnsureFailConditions(quest);

            try
            {
                // === 🌐 Подготавливаем локали ===
                var locales = new Dictionary<string, Dictionary<string, string>>
                {
                    ["en"] = new Dictionary<string, string>(),
                    ["ru"] = new Dictionary<string, string>()
                };

                // Заполняем локали
                FillQuestLocales(quest, locales["en"], locales["ru"]);

                // === 🛠 Создаём DTO для сервиса ===
                var newQuestDetails = new NewQuestDetails
                {
                    NewQuest = quest,
                    Locales = locales,
                    LockedToSide = null // или PlayerSide.Usec / Bear, если нужно
                };

                // === ✅ Создаём квест через официальный сервис ===
                CreateQuestResult result = _customQuestService.CreateQuest(newQuestDetails);

                if (result.Success)
                {
                    _logger.Info($"[RandomQuestGenerator] ✅ Квест '{quest.Id}' успешно создан и локализован.");
                }
                else
                {
                    foreach (string error in result.Errors)
                    {
                        _logger.Error($"[RandomQuestGenerator] ❌ Ошибка создания квеста: {error}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[RandomQuestGenerator] 🔥 Исключение при регистрации квеста: {ex}");
            }
        }

        private string GetTargetPoint(Quest quest)
        {
            var cond = quest.Conditions?.AvailableForFinish
                .FirstOrDefault(c => c.ConditionType == "VisitPlace" || c.ConditionType == "LeaveItemAtLocation");

            if (cond?.ExtensionData == null)
                return "unknown location";

            // Сначала пробуем zoneId — это обычно строка
            if (cond.ExtensionData.TryGetValue("zoneId", out var zoneObj))
                return zoneObj?.ToString() ?? "unknown zone";

            // Если нет zoneId — пробуем target
            if (cond.ExtensionData.TryGetValue("target", out var targetObj))
            {
                // Обрабатываем случай string[] или string
                return targetObj switch
                {
                    string[] arr when arr.Length > 0 => arr[0],  // Берём первый элемент
                    string str => str,
                    null => "unknown target",
                    _ => targetObj.ToString() // fallback
                };
            }

            return "unknown location";
        }

        private string GetItemName(Quest quest)
        {
            // 1. Находим условие "заложить предмет"
            var plantCond = quest.Conditions?.AvailableForFinish
                .FirstOrDefault(c => c.ConditionType == "LeaveItemAtLocation");

            if (plantCond?.ExtensionData == null)
                return "Unknown Item";

            // 2. Получаем target (может быть строка или массив строк)
            object? targetObj = plantCond.ExtensionData.GetValueOrDefault("target");
            string? tpl = null;

            if (targetObj is string[] arr && arr.Length > 0)
            {
                tpl = arr[0];
            }
            else if (targetObj is string str)
            {
                tpl = str;
            }

            if (string.IsNullOrEmpty(tpl))
            {
                _logger.Warning("[GetItemName] Не удалось извлечь TPL из условия LeaveItemAtLocation");
                return "Unknown Item";
            }

            // ✅ Теперь создаём templateId
            var templateId = new MongoId(tpl);

            // 3. Получаем шаблон предмета
            var templates = _databaseService.GetTemplates();

            if (templates.Items.TryGetValue(templateId, out var templateItem))
            {
                return !string.IsNullOrEmpty(templateItem.Name)
                    ? templateItem.Name
                    : "Item";
            }

            _logger.Warning($"[GetItemName] Предмет с TPL {tpl} не найден в базе данных");
            return "Item";
        }

        private void FillQuestLocales(Quest quest, Dictionary<string, string> enDict, Dictionary<string, string> ruDict)
        {
            if (quest == null) return;

            string id = quest.Id.ToString();

            string itemName = GetItemName(quest);
            string targetPoint = GetTargetPoint(quest);

            // Определяем тип квеста
            bool isDelivery = quest.Type == QuestTypeEnum.Discover; // Discover — это Delivery (закладка)
            bool isExploration = quest.Conditions?.AvailableForFinish.Any(c => c.ConditionType == "VisitPlace") == true;

            // === Генерация текстов в зависимости от типа ===
            string GenerateQuestName()
            {
                if (isDelivery)
                    return $"Deliver: {itemName}";
                else if (isExploration)
                    return $"Explore: {targetPoint}";
                return "Task";
            }

            string GenerateQuestNameRu()
            {
                if (isDelivery)
                    return $"Доставка: {itemName}";
                else if (isExploration)
                    return $"Исследовать: {targetPoint}";
                return "Задание";
            }

            string GenerateDescription()
            {
                if (isDelivery)
                    return $"Receive '{itemName}', hide it at '{targetPoint}' and exit alive.";
                else if (isExploration)
                    return $"Visit the location point '{targetPoint}' and exit alive.";
                return "Complete the task.";
            }

            string GenerateDescriptionRu()
            {
                if (isDelivery)
                    return $"Получи '{itemName}', спрячь в точке '{targetPoint}' и выйди живым.";
                else if (isExploration)
                    return $"Посети точку '{targetPoint}' и выйди живым.";
                return "Выполни задание.";
            }

            // === Добавляем локали ===
            Add($"{id} questName", GenerateQuestName(), GenerateQuestNameRu());
            Add($"{id} name", GenerateQuestName(), GenerateQuestNameRu());
            Add($"{id} description", GenerateDescription(), GenerateDescriptionRu());
            Add($"{id} note",
                isDelivery ? "Delivery task" : "Exploration task",
                isDelivery ? "Задание на доставку" : "Задание на исследование"
            );

            // Сообщения (универсальные)
            Add($"{id} accept", "Quest accepted", "Квест принят");
            Add($"{id} change", "Task updated", "Задание обновлено");
            Add($"{id} complete", "Task completed", "Задание выполнено");

            // === Условия (уже правильно — они считывают данные из ExtensionData) ===
            foreach (var cond in GetConditions(quest))
            {
                string key = cond.Id.ToString();
                string enText, ruText;

                switch (cond.ConditionType)
                {
                    case "LeaveItemAtLocation":
                        var zone = GetExtValue(cond, "zoneId") ?? "unknown";
                        ruText = $"Спрячь предмет в зоне «{zone}»";
                        enText = $"Hide item at «{zone}»";
                        break;

                    case "VisitPlace":
                        var target = GetExtValue(cond, "target") ?? "unknown";
                        ruText = $"Посети точку «{target}»";
                        enText = $"Visit location point «{target}»";
                        break;

                    case "CounterCreator" when cond.Type == "Completion":
                        ruText = "Выйди с локации со статусом «Выжил» или «Транзит»";
                        enText = "Exit with status «Survived» or «Transit»";
                        break;

                    case "ExitStatus":
                        ruText = "Выжить при выходе";
                        enText = "Survive when exiting";
                        break;

                    default:
                        ruText = $"Условие: {cond.ConditionType}";
                        enText = $"Condition: {cond.ConditionType}";
                        break;
                }

                Add(key, enText, ruText);
            }

            void Add(string locKey, string en, string ru)
            {
                enDict[locKey] = en;
                ruDict[locKey] = ru;
                _logger.Debug($"[Localization] Added: {locKey} | EN: '{en}' | RU: '{ru}'");
            }
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

 
        // Вспомогательная функция для получения ExtensionData
        private static string GetExtValue(QuestCondition cond, string key)
        {
            return cond.ExtensionData?.TryGetValue(key, out var v) == true ? v?.ToString() : null;
        }

        // Получаем все условия
        private List<QuestCondition> GetConditions(Quest quest)
        {
            var list = new List<QuestCondition>();
            if (quest.Conditions?.AvailableForStart != null) list.AddRange(quest.Conditions.AvailableForStart);
            if (quest.Conditions?.AvailableForFinish != null) list.AddRange(quest.Conditions.AvailableForFinish);
            if (quest.Conditions?.Fail != null) list.AddRange(quest.Conditions.Fail);
            return list;
        }

    }
    public static class StringExtensions
    {
        public static string ToUpperFirst(this string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpper(s[0]) + s.Substring(1).ToLower();
        }
    }

    public static class ListExtensions
    {
        public static T? RandomItem<T>(this IReadOnlyList<T> list, Random random)
        {
            if (list == null || list.Count == 0)
                return default;
            return list[random.Next(list.Count)];
        }

        public static T WeightedRandomItem<T>(this IList<T> list, Random random, Func<T, int> weightSelector)
        {
            int totalWeight = list.Sum(weightSelector);
            int pick = random.Next(totalWeight);
            int current = 0;

            foreach (var item in list)
            {
                current += weightSelector(item);
                if (pick < current)
                    return item;
            }

            return list[^1]; // fallback
        }
    }
}