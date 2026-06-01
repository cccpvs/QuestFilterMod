using QuestFilterMod.RandomQuests.Models;
using QuestFilterMod.RandomQuests.Utils;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Services.Mod;
using System.Reflection;

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

            if (Plugin._config.Debug)
                _logger.Info($"[QuestFilterMod][Random] Ищу конфиг: {configPath}");

            if (!File.Exists(configPath))
            {
                if (Plugin._config.Debug)
                    _logger.Error($"[QuestFilterMod][Random]❌ Файл конфигурации не найден: {configPath}");
                throw new FileNotFoundException("Конфигурация квестов не найдена", configPath);
            }

            _config = JsonHelper.LoadFromJson<QuestConfig>(configPath)
                ?? throw new InvalidOperationException("Не удалось загрузить конфигурацию квестов.");
        }

        public Quest? GenerateSingleQuest()
        {
            try
            {
                var candidates = new List<(string Type, Func<Quest?> Generator)>();

                if (_config.QuestGeneration.Types.Exploration)
                    candidates.Add(("Exploration", GenerateExplorationQuest));

                if (_config.QuestGeneration.Types.Delivery && _config.DeliveryQuest.Enabled)
                    candidates.Add(("Delivery", GenerateDeliveryQuest));

                if (_config.QuestGeneration.Types.Kills && _config.KillQuest.Enabled)
                    candidates.Add(("Kill", GenerateKillQuest));

                if (!candidates.Any())
                {
                    if (Plugin._config.Debug)
                        _logger.Error("[QuestFilterMod][Random] ❌ Нет доступных типов квестов для генерации.");
                    return null;
                }
                var locations = _databaseService.GetLocations()?.GetDictionary();
                if (locations != null && !LocationHelper.IdToPascalName.Any())
                {
                    LocationHelper.Initialize(locations);
                }

                // 🔁 Перемешиваем случайно — чтобы не всегда сначала exploration
                candidates = candidates.OrderBy(_ => _random.Next()).ToList();

                // 🔄 Пробуем каждый тип по очереди
                foreach (var (type, generator) in candidates)
                {
                    var quest = generator();
                    if (quest != null)
                    {
                        if (Plugin._config.Debug)
                            _logger.Info($"[QuestFilterMod][Random] ✅ Успешно сгенерирован квест: {type}");
                        return quest;
                    }
                }

                // ❌ Ни один генератор не смог создать квест
                if (Plugin._config.Debug)
                    _logger.Warning("[QuestFilterMod][Random] Все возможные комбинации уже использованы или недоступны.");
                return null;
            }
            catch (Exception e)
            {
                if (Plugin._config.Debug)
                    _logger.Error($"[QuestFilterMod][Random] Ошибка при генерации квеста: {e.Message}\n{e.StackTrace}");
                return null;
            }
        }

        //Общий генератор квестов
        private Quest? GenerateBaseQuest(string type, Action<Quest, Func<MongoId>> build)
        {
            var idFactory = new Func<MongoId>(() => new MongoId(Guid.NewGuid().ToString("N")[..24]));
            var questId = idFactory();

            // Создаём через пустой конструктор + инициализируем все required-поля
            var quest = new Quest
            {
                Id = questId,
                Name = $"{questId} name",
                QuestName = $"{questId} questName",
                Description = $"{questId} description",
                Note = $"{questId} note",
                TraderId = new MongoId(_config.TraderIds.RandomItem(_random)),
                Side = "Pmc",
                Location = "", // будет заполнено в build()
                Image = _config.DefaultQuest.Image ?? "/files/quest/icon/default.jpg",
                Type = QuestTypeEnum.PickUp,
                CanShowNotificationsInGame = true,
                Restartable = false,
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

            // Устанавливаем не-required поля
            quest.InstantComplete = false;
            quest.IsKey = false;
            quest.SecretQuest = false;
            quest.Status = 0;
            quest.ProgressSource = "eft";
            quest.AcceptanceAndFinishingSource = "eft";
            quest.AcceptPlayerMessage = $"{questId}_accept";
            quest.ChangeQuestMessageText = $"{questId}_change";
            quest.CompletePlayerMessage = $"{questId}_complete";
            quest.StartedMessageText = "quest_started_default";
            quest.SuccessMessageText = "quest_completed_default";
            quest.FailMessageText = "quest_failed_default";
            quest.GameModes = new();
            quest.RankingModes = new();

            // Теперь build() может всё изменить
            build(quest, idFactory);

            // Валидация
            if (string.IsNullOrEmpty(quest.Location))
            {
                if (Plugin._config.Debug)
                    _logger.Error($"[QuestFilterMod][Random] ❌ Квест {quest.Id} не имеет Location");
                return null;
            }


            AddRewards(quest);
            CreateAndRegisterQuest(quest);
            if (Plugin._config.Debug)
                _logger.Info($"[QuestFilterMod][Random] ✅ Квест '{quest.Id}' ({type}) создан");
            //_logger.Info(JsonSerializer.Serialize(quest, new JsonSerializerOptions { WriteIndented = true }));
            return quest;
        }
        //Привязка квеста наград
        private void AddRewards(Quest quest)
        {
            AddExperienceReward(quest);
            AddMoneyReward(quest);
            AddRandomItemRewards(quest);
            AddTraderStandingReward(quest);
        }
        private Dictionary<string, object> CreateLocationCondition(Func<MongoId> idFactory, string locationPascalName)
        {
            return new Dictionary<string, object>
            {
                ["id"] = idFactory().ToString(),
                ["conditionType"] = "Location",
                ["target"] = new[] { locationPascalName }
            };
        }
        private Dictionary<string, object> CreateKillCondition(Func<MongoId> idFactory, string killTarget, int value = 0)
        {
            return new Dictionary<string, object>
            {
                ["id"] = idFactory().ToString(),
                ["conditionType"] = "Kills",
                ["value"] = value,
                ["compareMethod"] = ">=",
                ["target"] = killTarget
            };
        }
        private QuestCondition CreateVisitPlaceCondition(Func<MongoId> idFactory, string target)
        {
            return new QuestCondition
            {
                Id = idFactory(),
                ConditionType = "VisitPlace",
                Value = 1,
                DynamicLocale = false,
                ExtensionData = new Dictionary<string, object?>
                {
                    ["target"] = target
                }
            };
        }
        private Dictionary<string, object> CreateExitStatusCondition(Func<MongoId> idFactory, string[] statuses)
        {
            return new Dictionary<string, object>
            {
                ["id"] = idFactory().ToString(),
                ["conditionType"] = "ExitStatus",
                ["dynamicLocale"] = false,
                ["status"] = statuses
            };
        }
        private QuestCondition CreateCounterCondition(
            Func<MongoId> idFactory,
            List<Dictionary<string, object>> innerConditions,
            int requiredValue,
            string counterType = "Elimination")
        {
            var counterId = idFactory();
            var outerId = idFactory();

            return new QuestCondition
            {
                Id = outerId,
                ConditionType = "CounterCreator",
                Type = counterType,
                Value = requiredValue,
                DynamicLocale = false,
                ExtensionData = new Dictionary<string?, object?>
                {
                    ["counter"] = new Dictionary<string?, object?>
                    {
                        ["id"] = counterId.ToString(),
                        ["conditions"] = innerConditions.ToArray()
                    }
                }
            };
        }

        //Квесты Exploration
        private Quest? GenerateExplorationQuest()
        {
            var allowed = LocationHelper.GetAllowedLocations(_config).ToList();

            var allPoints = new List<(LocationConfig Config, string Target)>();

            

            foreach (var (pascalName, locationId) in allowed)
            {
                if (_config.ExplorationQuest.TryGetValue(pascalName, out var config))
                {
                    foreach (var target in config.Targets)
                    {
                        allPoints.Add((config, target));
                    }
                }
            }

            if (!allPoints.Any()) return null;

            foreach (var (loc, target) in allPoints.OrderBy(_ => _random.Next()))
            {
                var key = new QuestKey(loc.Id, target, "__EXPLORATION__", "Exploration");
                if (!_tracker.TryUse(key)) continue;

                return GenerateBaseQuest("Exploration", (q, id) =>
                {
                    q.Location = loc.Id;
                    q.Type = QuestTypeEnum.Discover;

                    if (!LocationHelper.TryGetPascalName(loc.Id, out var pascalName))
                        return; // лог уже из LocationHelper не нужен

                    q.Conditions ??= new QuestConditionTypes();
                    q.Conditions.AvailableForFinish = new List<QuestCondition>
            {
                CreateVisitPlaceCondition(id, target)
            };

                    var locationCond = CreateLocationCondition(id, pascalName);
                    var exitStatusCond = CreateExitStatusCondition(id, new[] { "Survived", "Runner", "Transit" });

                    var exitCounter = CreateCounterCondition(
                        id,
                        new List<Dictionary<string, object>> { locationCond, exitStatusCond },
                        1, "Completion"
                    );

                    exitCounter.CompleteInSeconds = 30;
                    q.Conditions.AvailableForFinish.Add(exitCounter);
                });
            }

            return null;
        }
        //Квесты Delivery
        private Quest? GenerateDeliveryQuest()
        {
            var delivery = _config.DeliveryQuest;
            if (!delivery.Enabled || !delivery.Locations.Any()) return null;

            var allowed = LocationHelper.GetAllowedLocations(_config).ToList();

            var allPoints = new List<(LocationConfig Config, string Target)>();

            foreach (var (pascalName, locationId) in allowed)
            {
                if (delivery.Locations.TryGetValue(pascalName, out var config))
                {
                    foreach (var target in config.Targets.Where(t => !string.IsNullOrEmpty(t)))
                    {
                        allPoints.Add((config, target));
                    }
                }
            }

            if (!allPoints.Any()) return null;

            foreach (var (loc, targetPoint) in allPoints.OrderBy(_ => _random.Next()))
            {
                var itemTpl = GetRandomSpecialItem();
                if (itemTpl == null) continue;

                var key = new QuestKey(loc.Id, targetPoint, itemTpl.ToString(), "Delivery");
                if (!_tracker.TryUse(key)) continue;

                return GenerateBaseQuest("Delivery", (q, id) =>
                {
                    q.Location = loc.Id;
                    q.Type = QuestTypeEnum.PickUp;

                    var itemId = id();
                    GetOrCreateRewardList(q, "Started").Add(new Reward
                    {
                        Id = id(),
                        Type = RewardType.Item,
                        Target = itemId,
                        Value = 1,
                        FindInRaid = false,
                        Items = new List<Item>
                {
                    new Item
                    {
                        Id = itemId,
                        Template = itemTpl.Value,
                        Upd = new Upd { StackObjectsCount = 1 }
                    }
                }
                    });

                    q.Conditions.AvailableForFinish = new List<QuestCondition>
            {
                new QuestCondition
                {
                    Id = id(),
                    ConditionType = "LeaveItemAtLocation",
                    Value = 1,
                    ZoneId = targetPoint,
                    DynamicLocale = false,
                    ExtensionData = new Dictionary<string?, object?>
                    {
                        ["target"] = new[] { itemTpl.ToString() },
                        ["zoneId"] = targetPoint,
                        ["plantTime"] = delivery.PlantTime
                    }
                }
            };
                });
            }

            return null;
        }
        //Квесты на Убийства
        private Quest? GenerateKillQuest()
        {
            var cfg = _config.KillQuest;
            if (!cfg.Enabled) return null;

            var allowed = LocationHelper.GetAllowedLocations(_config).ToList();

            if (!allowed.Any()) return null;

            foreach (var (pascalName, locationId) in allowed.OrderBy(_ => _random.Next()))
            {
                var key = new QuestKey(locationId, "KILL", "", "Kill");
                if (!_tracker.TryUse(key)) continue;

                return GenerateBaseQuest("Kill", (q, id) =>
                {
                    q.Location = locationId;
                    q.Type = QuestTypeEnum.Elimination;

                    var killCond = CreateKillCondition(id, cfg.Target);
                    var locCond = CreateLocationCondition(id, pascalName);

                    var counterCond = CreateCounterCondition(
                        id,
                        new List<Dictionary<string, object>> { killCond, locCond },
                        _random.Next(cfg.MinKills, cfg.MaxKills + 1),
                        "Elimination"
                    );

                    q.Conditions.AvailableForFinish = new() { counterCond };
                });
            }

            return null;
        }
       
        private List<Reward> GetOrCreateRewardList(Quest quest, string status)
        {
            if (quest.Rewards == null)
                quest.Rewards = new Dictionary<string, List<Reward>>();

            return quest.Rewards.TryGetValue(status, out var list)
                ? list
                : (quest.Rewards[status] = new List<Reward>());
        }
        private void AddExperienceReward(Quest quest)
        {
            var range = _config.DefaultQuest.ExperienceRewardRange;
            int exp = _random.Next(range.Min / range.Step, (range.Max / range.Step) + 1) * range.Step;

            GetOrCreateRewardList(quest, "Success").Add(new Reward
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
            if (!_config.RewardItems.Enabled || !_config.RewardItems.Parents.Any()) return;

            var prices = _databaseService.GetPrices();
            var itemsPool = _databaseService.GetTemplates().Items;

            if (prices == null || !prices.Any() || !itemsPool.Any())
            {
                if (Plugin._config.Debug)
                    _logger.Error("[QuestFilterMod][Random] ❌ Не удалось загрузить данные из базы.");
                return;
            }

            // === Используем диапазон из конфига ===
            int minPrice = _config.RewardItems.PriceRange.Min;
            int maxPrice = _config.RewardItems.PriceRange.Max;

            if (minPrice < 0) minPrice = 0;
            if (maxPrice < minPrice) maxPrice = minPrice;

            // === Шаг 1: Выбираем родительские ID с учётом весов ===
            var weightedParents = _config.RewardItems.Parents
                .Where(p => p.Weight > 0 && !string.IsNullOrEmpty(p.Id))
                .ToList();

            if (!weightedParents.Any())
            {
                if (Plugin._config.Debug)
                    _logger.Error("[QuestFilterMod][Random] ❌ Нет активных родителей с весом > 0.");
                return;
            }

            var parentIds = weightedParents.Select(p => new { Id = new MongoId(p.Id), p.Weight }).ToList();

            // === Шаг 2: Фильтруем по цене и категории ===
            var validItemsByParent = new Dictionary<MongoId, List<MongoId>>();

            foreach (var kvp in prices)
            {
                var tplId = kvp.Key;
                if (!itemsPool.TryGetValue(tplId, out var template)) continue;

                double price = kvp.Value;
                if (price < minPrice || price > maxPrice) continue; // ← Теперь из конфига

                var parentId = template.Parent;
                if (!parentIds.Any(p => p.Id == parentId)) continue;

                if (!validItemsByParent.ContainsKey(parentId))
                    validItemsByParent[parentId] = new List<MongoId>();

                validItemsByParent[parentId].Add(tplId);
            }

            // Удаляем пустые категории
            var nonEmptyParents = parentIds
                .Where(p => validItemsByParent.ContainsKey(p.Id) && validItemsByParent[p.Id].Any())
                .ToList();

            /*if (!nonEmptyParents.Any())
            {
                if (Plugin._config.Debug)
                    _logger.Error("[QuestFilterMod][Random] ❌ Нет подходящих предметов по цене и категориям.");
                return;
            }*/

            /*if (Plugin._config.Debug)
                _logger.Info($"[QuestFilterMod][Random] Найдено {nonEmptyParents.Count} категорий с подходящими предметами.");*/

            // Сколько наград выдать
            int count = _random.Next(_config.RewardItems.Count.Min, _config.RewardItems.Count.Max + 1);
            var usedTpls = new HashSet<string>();

            for (int i = 0; i < count; i++)
            {
                var selectedParent = nonEmptyParents.WeightedRandomItem(_random, p => p.Weight);
                var itemsInCategory = validItemsByParent[selectedParent.Id];

                if (!itemsInCategory.Any()) continue;

                MongoId selectedId;
                string selectedTpl;
                int attempts = 0;
                do
                {
                    selectedId = itemsInCategory[_random.Next(itemsInCategory.Count)];
                    selectedTpl = selectedId.ToString();
                    attempts++;
                    if (attempts > 100) break;
                } while (usedTpls.Contains(selectedTpl));

                if (attempts > 100) break;
                usedTpls.Add(selectedTpl);

                string name = "Unknown Item";
                if (itemsPool.TryGetValue(selectedId, out var item) && !string.IsNullOrEmpty(item.Name))
                {
                    name = item.Name;
                }

                AddItemReward(quest, selectedTpl, 1, name);
            }
        }
        private MongoId? GetRandomSpecialItem()
        {
            var parentId = new MongoId("5447e0e74bdc2d3c308b4567"); // Special Items
            var items = _databaseService.GetTemplates().Items;

            var candidates = items
                .Where(kvp => kvp.Value.Parent == parentId)
                .Select(kvp => kvp.Key)
                .ToArray();

            return candidates.Length > 0 ? candidates[_random.Next(candidates.Length)] : null;
        }
        private void AddTraderStandingReward(Quest quest)
        {
            if (!_config.RewardTraderStanding.Enabled)
                return;

            // Генерируем случайное значение: от Min до Max
            float value = (float)_random.NextDouble() *
                          (_config.RewardTraderStanding.Max - _config.RewardTraderStanding.Min) +
                          _config.RewardTraderStanding.Min;

            GetOrCreateRewardList(quest, "Success").Add(new Reward
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
                if (Plugin._config.Debug)
                    _logger.Error("[QuestFilterMod][Random] ❌ Не удалось установить 'Template' у Item.");
                return;
            }

            var upd = new Upd();
            typeof(Upd)
                .GetMethod("Set", new[] { typeof(string), typeof(object) })?
                .Invoke(upd, new object[] { "StackObjectsCount", count });

            gameItem.Upd = upd;


            GetOrCreateRewardList(quest, "Success").Add(new Reward
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
                // Явно назовём переменные — чтобы не перепутать
                var englishLocales = new Dictionary<string, string>();
                var russianLocales = new Dictionary<string, string>();

                FillQuestLocales(quest, englishLocales, russianLocales);

                var locales = new Dictionary<string, Dictionary<string, string>>
                {
                    ["en"] = englishLocales,
                    ["ru"] = russianLocales
                };

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
                    /*if (Plugin._config.Debug)
                        _logger.Info($"[QuestFilterMod][Random] ✅ Квест '{quest.Id}' успешно создан и локализован.");*/
                }
                else
                {
                    foreach (string error in result.Errors)
                    {
                        if (Plugin._config.Debug)
                            _logger.Error($"[QuestFilterMod][Random] ❌ Ошибка создания квеста: {error}");
                    }
                }
            }
            catch (Exception ex)
            {
                if (Plugin._config.Debug)
                    _logger.Error($"[QuestFilterMod][Random] 🔥 Исключение при регистрации квеста: {ex}");
            }
        }
        private void FillQuestLocales(Quest quest, Dictionary<string, string> enDict, Dictionary<string, string> ruDict)
        {
            if (quest == null) return;

            string id = quest.Id.ToString();

            // Вспомогательная функция добавления
            void Add(string key, string en, string ru) => (enDict[key], ruDict[key]) = (en, ru);

            // === Основные локали квеста ===
            Add($"{id} name", "Task", "Задание");
            Add($"{id} questName", "Task", "Задание");
            Add($"{id} description", "Complete the task.", "Выполни задание.");
            Add($"{id} note", "Random task", "Случайное задание");
            Add($"{id} accept", "Quest accepted", "Квест принят");
            Add($"{id} change", "Task updated", "Задание обновлено");
            Add($"{id} complete", "Task completed", "Задание выполнено");

            // === Условия ===
            foreach (var cond in GetConditions(quest))
            {
                string key = cond.Id.ToString();
                string enText = "", ruText = "";

                if (cond.ConditionType == "CounterCreator" && cond.Type == "Elimination")
                {
                    // 🔥 Парсим Kills из counter.conditions
                    if (cond.ExtensionData?.TryGetValue("counter", out var counterObj) is true &&
                        counterObj is Dictionary<string, object> counter &&
                        counter.TryGetValue("conditions", out var conditionsObj) &&
                        conditionsObj is object[] conditions)
                    {
                        var kills = conditions
                            .OfType<Dictionary<string, object>>()
                            .FirstOrDefault(c => c.GetValueOrDefault("conditionType")?.ToString() == "Kills");

                        if (kills != null)
                        {
                            var target = kills.GetValueOrDefault("target")?.ToString()?.ToLower() ?? "";
                            var count = cond.Value;

                            (enText, ruText) = target switch
                            {
                                _ when target.Contains("anypmc") => ($"Kill {count} PMC", $"Убей {count} бойца PMC"),
                                _ when target.Contains("savage") => ($"Kill {count} Scav", $"Убей {count} рейдера"),
                                _ => ($"Kill {count} targets", $"Убей {count} целей")
                            };
                        }
                    }
                }
                else if (cond.ConditionType == "VisitPlace")
                {
                    var target = GetExtValue(cond, "target") ?? "location";
                    enText = $"Visit «{target}»";
                    ruText = $"Посети «{target}»";
                }
                else if (cond.ConditionType == "LeaveItemAtLocation")
                {
                    var zone = GetExtValue(cond, "zoneId") ?? "zone";
                    enText = $"Hide item at «{zone}»";
                    ruText = $"Спрячь предмет в зоне «{zone}»";
                }
                else if (cond.ConditionType == "ExitStatus" ||
                        (cond.ConditionType == "CounterCreator" && cond.Type == "Completion"))
                {
                    enText = "Exit with status Survived or Transit";
                    ruText = "Выйди со статусом Выжил или Транзит";
                }
                else
                {
                    enText = $"Complete condition: {cond.ConditionType}";
                    ruText = $"Выполни условие: {cond.ConditionType}";
                }

                Add(key, enText, ruText);
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
        public void ResetTracker()
        {
            _tracker.Clear();
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