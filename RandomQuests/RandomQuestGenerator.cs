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
using SPTarkov.Server.Core.Models.Spt.Server;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
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
        private ServerLocalisationService _cachedLocalisationService;


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

        public RandomQuestGenerator(ISptLogger<Plugin> logger, DatabaseService databaseService)
        {
            _logger = logger;
            _databaseService = databaseService;


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

            var quest = new Quest
            {
                Id = new MongoId(NewId()),
                Name = $"Visit a point on the location ({targetPoint})",
                Description = $"Visit the location point ({targetPoint})",
                TraderId = new MongoId(_config.TraderIds.RandomItem(_random)),
                Side = "Pmc",
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
                AcceptPlayerMessage = "Accept Player Message",
                ChangeQuestMessageText = "Change Quest Message Text",
                CompletePlayerMessage = "Complete PlayerMessage",
                Note = "Note",
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
                DynamicLocale = true,
                Value = 1,
                Index = index++,
                ParentId = "",
                VisibilityConditions = new()
            };


            // 📌 Лог для проверки
            _logger.Info($"[QuestGen] Added VisitPlace condition: ID={visitCondition.Id}, Target={targetPoint}");


            visitCondition.ExtensionData ??= new(StringComparer.Ordinal);
            visitCondition.ExtensionData["target"] = targetPoint;
            quest.Conditions.AvailableForFinish.Add(visitCondition);

            // Условие выхода
            var exitStatus = new QuestCondition
            {
                Id = new MongoId(NewId()),
                ConditionType = "ExitStatus",
                DynamicLocale = true,
                ExtensionData = new()
                {
                    ["status"] = new[] { "Survived", "Runner", "Transit" }
                }
            };

            var counterCreator = new QuestCondition
            {
                Id = new MongoId(NewId()),
                ConditionType = "CounterCreator",
                DynamicLocale = true,
                Value = 1,
                Index = index++,
                OneSessionOnly = true,
                CompleteInSeconds = 30,
                DoNotResetIfCounterCompleted = false,
                ExtensionData = new()
                {
                    ["counter"] = new
                    {
                        id = new MongoId(NewId()),
                        conditions = new[] { exitStatus }
                    }
                }
            };

            quest.Conditions.AvailableForFinish.Add(counterCreator);

            AddExperienceReward(quest);
            AddMoneyReward(quest);
            AddRandomItemRewards(quest);
            AddTraderStandingReward(quest);

            _logger.Info($"[RandomQuestGenerator] ✅ Квест '{quest.Id}' (Exploration): {targetPoint} на {locationKey}");
            // 🔥 Локализуем
            var tables = _databaseService.GetTables();
            var localisationService = FindLocalisationService();
            if (localisationService == null)
            {
                _logger.Error("[Localization] ❌ Не удалось получить ServerLocalisationService");
                return null;
            }
            AddFullQuestLocalization(tables, quest, localisationService);

            var localeField = localisationService.GetType().GetField("_serverLocale", BindingFlags.NonPublic | BindingFlags.Instance);
            var currentLocale = localeField?.GetValue(localisationService)?.ToString();
            _logger.Info($"[Localization] _serverLocale = '{currentLocale}'");

            var testId = quest.Conditions.AvailableForFinish.First(c => c.ConditionType == "LeaveItemAtLocation").Id.ToString();
            _logger.Info($"[Localization] ✅ Кеш локалей обновлён для квеста '{quest.Id}'");
            // Сохраняем квест
            AddQuestToServer(quest);

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

                var quest = new Quest
                {
                    Id = new MongoId(NewId()),
                    QuestName = $"Deliver: {item.Name}",
                    Name = $"Deliver: {item.Name}",
                    Description = $"Receive '{item.Name}', hide it at '{targetPoint}' and exit alive.",
                    TraderId = new MongoId(_config.TraderIds.RandomItem(_random)),
                    Side = "Pmc",
                    
                    Location = location.Id,
                    Image = _config.DefaultQuest.Image,
                    InstantComplete = false,
                    IsKey = false,
                    Restartable = false,
                    CanShowNotificationsInGame = true,
                    SecretQuest = false,
                    
                    //Status = (int)QuestStatusEnum.Locked,
                    Status = 0,
                    Type = QuestTypeEnum.Discover,
                    ProgressSource = "eft",
                    AcceptanceAndFinishingSource = "eft",
                    GameModes = new List<string>(),
                    RankingModes = new List<string>(),
                    AcceptPlayerMessage = "accept",
                    ChangeQuestMessageText = "change",
                    CompletePlayerMessage = "complete",
                    Note = "note",
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
                    DynamicLocale = true,
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
                // 🔥 Добавляем локализацию
                
                quest.Conditions.AvailableForFinish.Add(plantCondition);


                // ✅ Выход

                var counterCreator = new QuestCondition
                {
                    Id = new MongoId(NewId()),
                    ConditionType = "CounterCreator",
                    Value = 1,
                    Index = index++,
                    DynamicLocale = true,
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
                                DynamicLocale = true,
                                Status = ["Survived", "Transit"]

                            }
                        }
                    }
                    /*VisibilityConditions = new List<VisibilityCondition>
                    {
                        new() { ConditionType = "CompleteCondition", Id = new MongoId(NewId()), Target = plantCondition.Id.ToString() }
                    }*/
 
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

                // 🔥 Локализуем
                var tables = _databaseService.GetTables();
                var localisationService = FindLocalisationService();
                if (localisationService == null)
                {
                    _logger.Error("[Localization] ❌ Не удалось получить ServerLocalisationService");
                    return null;
                }
                AddFullQuestLocalization(tables, quest, localisationService);

                var localeField = localisationService.GetType().GetField("_serverLocale", BindingFlags.NonPublic | BindingFlags.Instance);
                var currentLocale = localeField?.GetValue(localisationService)?.ToString();
                _logger.Info($"[Localization] _serverLocale = '{currentLocale}'");

                var testId = quest.Conditions.AvailableForFinish.First(c => c.ConditionType == "LeaveItemAtLocation").Id.ToString();
                _logger.Info($"[Localization] ✅ Кеш локалей обновлён для квеста '{quest.Id}'");
                // Сохраняем квест
                AddQuestToServer(quest);

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


        private ServerLocalisationService FindLocalisationService()
        {
            if (_cachedLocalisationService != null)
                return _cachedLocalisationService;

            try
            {
                var type = _databaseService.GetType();
                FieldInfo field = null;

                foreach (var f in type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (f.FieldType == typeof(ServerLocalisationService))
                    {
                        field = f;
                        break;
                    }
                }

                if (field == null)
                {
                    _logger.Error("[Localization] ❌ Не найдено поле типа ServerLocalisationService");
                    return null;
                }

                var service = field.GetValue(_databaseService) as ServerLocalisationService;
                if (service == null)
                {
                    _logger.Error("[Localization] ❌ Экземпляр ServerLocalisationService = NULL");
                    return null;
                }

                _cachedLocalisationService = service; // 🔥 Кешируем
                _logger.Info($"[Localization] ✅ Кеширован сервис: '{field.Name}'");
                return service;
            }
            catch (Exception ex)
            {
                _logger.Error($"[Localization] 🔥 Ошибка при поиске сервиса: {ex.Message}");
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

        public void AddQuestToServer(Quest quest)
        {
            if (quest == null) return;

            EnsureRewards(quest);
            EnsureFailConditions(quest);

            try
            {
                var quests = _databaseService.GetQuests();
                quests[quest.Id] = quest;
                _logger.Info($"[RandomQuestGenerator] ✅ Квест '{quest.Id}' добавлен в базу.");

                var localisationService = FindLocalisationService();
                if (localisationService != null)
                {
                    // Вызываем Hydrate, чтобы обновить внутренние кеши
                    var hydrateMethod = localisationService.GetType().GetMethod("HydrateServerLocales", BindingFlags.NonPublic | BindingFlags.Instance);
                    hydrateMethod?.Invoke(localisationService, null);
                    _logger.Info("[Localization] 🔁 HydrateServerLocales() вызван");

                    // ⚠️ ВАЖНО: Разослать локали всем подключённым клиентам
                    var sendMethod = localisationService.GetType().GetMethod("SendServerLocalesToClient", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (sendMethod != null)
                    {
                        sendMethod.Invoke(localisationService, new object[] { null }); // null = всем клиентам
                        _logger.Info("[Localization] 📤 Локали отправлены всем клиентам");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[RandomQuestGenerator] 🔥 Ошибка при сохранении: {ex}");
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

        public void AddFullQuestLocalization(DatabaseTables tables, Quest quest, ServerLocalisationService localisationService)
        {
            if (quest == null || string.IsNullOrEmpty(quest.Id)) return;

            var locId = quest.Id.ToString();

            var globalLocales = tables.Locales.Global;
            if (!globalLocales.TryGetValue("en", out var enDict) || !globalLocales.TryGetValue("ru", out var ruDict))
            {
                _logger.Error("[Localization] ❌ Не найдены глобальные локали 'ru' или 'en'");
                return;
            }

            // Убедимся, что Value — это Dictionary<string, string>
            var ruLocale = ruDict.Value;
            var enLocale = enDict.Value;

            void AddLoc(string key, string ru, string en)
            {
                ruLocale[key] = ru;
                enLocale[key] = en;
                _logger.Info($"[Localization] ✅ Добавлено: {key} | RU: '{ru}' | EN: '{en}'");
            }

            // === 1. Сам квест: Name, Description и т.д. ===
            // Используем ID как префикс, но без дублирования полей
            AddLoc($"{locId} Name", quest.Name, quest.Name);
            AddLoc($"{locId} Description", quest.Description, quest.Description);
            AddLoc($"{locId} Note", quest.Note ?? "", quest.Note ?? "");

            // Эти поля должны быть ключами, а не текстом!
            // Поэтому локализуем сами ключи, если они кастомные
            if (quest.StartedMessageText.StartsWith("quest_started_"))
            {
                AddLoc(quest.StartedMessageText, "Квест начат", "Quest started");
            }
            if (quest.SuccessMessageText.StartsWith("quest_completed_"))
            {
                AddLoc(quest.SuccessMessageText, "Квест выполнен!", "Quest completed!");
            }
            if (quest.FailMessageText.StartsWith("quest_failed_"))
            {
                AddLoc(quest.FailMessageText, "Квест провален", "Quest failed");
            }

            // === 2. Условия ===
            foreach (var cond in GetConditions(quest))
            {
                string condKey = cond.Id.ToString();
                string ruText, enText;

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

                AddLoc(condKey, ruText, enText);
            }
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