using QuestFilterMod.QuestFilter;
using QuestFilterMod.RandomQuests.Models;
using QuestFilterMod.RandomQuests.Utils;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Services.Mod;
using SPTarkov.Server.Core.Utils.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;



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
                var candidates = new List<(string Type, Func<Quest?> Generator)>();

                if (_config.QuestGeneration.Types.Exploration)
                    candidates.Add(("Exploration", GenerateExplorationQuest));

                if (_config.QuestGeneration.Types.Delivery && _config.DeliveryQuest.Enabled)
                    candidates.Add(("Delivery", GenerateDeliveryQuest));

                if (_config.QuestGeneration.Types.Kills && _config.KillQuest.Enabled)
                    candidates.Add(("Kill", GenerateKillQuest));

                if (!candidates.Any())
                {
                    _logger.Error("[RandomQuestGenerator] ❌ Нет доступных типов квестов для генерации.");
                    return null;
                }


                // 🔁 Перемешиваем случайно — чтобы не всегда сначала exploration
                candidates = candidates.OrderBy(_ => _random.Next()).ToList();

                // 🔄 Пробуем каждый тип по очереди
                foreach (var (type, generator) in candidates)
                {
                    var quest = generator();
                    if (quest != null)
                    {
                        _logger.Info($"[GenerateSingleQuest] ✅ Успешно сгенерирован квест: {type}");
                        return quest;
                    }
                }

                // ❌ Ни один генератор не смог создать квест
                //_logger.Warning("[GenerateSingleQuest] Все возможные комбинации уже использованы или недоступны.");
                return null;
            }
            catch (Exception e)
            {
                _logger.Error($"[RandomQuestGenerator] Ошибка при генерации квеста: {e.Message}\n{e.StackTrace}");
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
                _logger.Error($"[Base] ❌ Квест {quest.Id} не имеет Location");
                return null;
            }


            AddRewards(quest);
            CreateAndRegisterQuest(quest);
            _logger.Info($"[Base] ✅ Квест '{quest.Id}' ({type}) создан");
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

        private void AddExitCondition(Quest q, Func<MongoId> id, string[] statuses, int seconds)
        {
            var exitStatus = new QuestCondition
            {
                Id = id(),
                ConditionType = "ExitStatus",
                DynamicLocale = false,
                ExtensionData = { ["status"] = statuses }
            };

            var counter = new QuestCondition
            {
                Id = id(),
                ConditionType = "CounterCreator",
                Value = 1,
                CompleteInSeconds = seconds,
                Type = "Completion",
                DynamicLocale = false,
                ExtensionData = { ["counter"] = new { id = id(), conditions = new object[] { exitStatus }, dynamicLocale = false } }
            };

            q.Conditions ??= new QuestConditionTypes();
            q.Conditions.AvailableForFinish ??= new List<QuestCondition>();
            q.Conditions.AvailableForFinish.Add(counter);
        }
        //Квесты Exploration
        private Quest? GenerateExplorationQuest()
        {
            var allowedLocations = _config.ExplorationQuest
                .Where(x => _config.QuestGeneration.AllowedLocations.GetValueOrDefault(x.Key))
                .ToList();

            if (!allowedLocations.Any()) return null;

            // Собираем ВСЕ возможные точки
            var allPoints = new List<(LocationConfig Loc, string Target)>();
            foreach (var kvp in allowedLocations)
                foreach (var target in kvp.Value.Targets)
                    allPoints.Add((kvp.Value, target));

            if (!allPoints.Any()) return null;

            // Перемешиваем и пробуем по одной
            var shuffled = allPoints.OrderBy(_ => _random.Next()).ToList();

            foreach (var (loc, target) in shuffled)
            {
                var key = new QuestKey(loc.Id, target, "__EXPLORATION__", "Exploration");

                // Только если ЕЩЁ не использовалась — пробуем
                if (_tracker.TryUse(key))
                {
                    return GenerateBaseQuest("Exploration", (q, id) =>
                    {
                        q.Location = loc.Id;
                        q.Type = QuestTypeEnum.Discover;

                        var visit = new QuestCondition
                        {
                            Id = id(),
                            DynamicLocale = false,
                            ConditionType = "VisitPlace",
                            Value = 1,
                            ExtensionData = { ["target"] = target }
                        };

                        q.Conditions ??= new QuestConditionTypes();
                        q.Conditions.AvailableForFinish ??= new List<QuestCondition>();
                        q.Conditions.AvailableForFinish.Add(visit);

                        AddExitCondition(q, id, new[] { "Survived", "Runner", "Transit" }, 30);
                    });
                }
            }

            _logger.Debug("[GenerateExplorationQuest] Все точки уже использованы.");
            return null;
        }
        //Квесты Delivery
        private Quest? GenerateDeliveryQuest()
        {
            var delivery = _config.DeliveryQuest;
            if (!delivery.Enabled || !delivery.Locations.Any())
            {
                _logger.Info("[GenerateDeliveryQuest] ❌ Доставка отключена или нет данных.");
                return null;
            }

            var allowedLocations = delivery.Locations
                .Where(x => _config.QuestGeneration.AllowedLocations.GetValueOrDefault(x.Key))
                .ToList();

            if (!allowedLocations.Any())
            {
                _logger.Warning("[GenerateDeliveryQuest] ❌ Нет разрешённых локаций.");
                return null;
            }

            // Собираем все точки (локация + зона)
            var allPoints = new List<(LocationConfig Loc, string Target)>();
            foreach (var locEntry in allowedLocations)
            {
                foreach (var target in locEntry.Value.Targets.Where(t => !string.IsNullOrEmpty(t)))
                {
                    allPoints.Add((locEntry.Value, target));
                }
            }

            if (!allPoints.Any())
            {
                _logger.Warning("[GenerateDeliveryQuest] ❌ Нет ни одной валидной комбинации.");
                return null;
            }

            // Перемешиваем
            var shuffled = allPoints.OrderBy(_ => _random.Next()).ToList();

            // Пробуем каждую точку
            foreach (var (loc, targetPoint) in shuffled)
            {
                var itemTpl = GetRandomSpecialItem();
                if (itemTpl == null) continue;

                var questKey = new QuestKey(loc.Id, targetPoint, itemTpl.ToString(), "Delivery");
                if (!_tracker.TryUse(questKey)) continue;

                return GenerateBaseQuest("Delivery", (q, idFactory) =>
                {
                    q.Location = loc.Id;
                    q.Type = QuestTypeEnum.PickUp;

                    var itemId = idFactory(); // уникальный ID в инвентаре

                    GetOrCreateRewardList(q, "Started").Add(new Reward
                    {
                        Id = idFactory(),
                        Type = RewardType.Item,
                        Target = itemId, // ✅ ID предмета
                        Value = 1,
                        FindInRaid = false, // ✅ Не FiR
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

                    // Условие: положить в зону
                    var plantCond = new QuestCondition
                    {
                        Id = idFactory(),
                        ConditionType = "LeaveItemAtLocation",
                        Value = 1,
                        DynamicLocale = false,
                        ZoneId = targetPoint,
                        ExtensionData = new Dictionary<string?, object?>
                        {
                            ["target"] = new[] { itemTpl.ToString() },
                            ["zoneId"] = targetPoint,
                            ["plantTime"] = delivery.PlantTime
                        }
                    };

                    q.Conditions.AvailableForFinish = new() { plantCond };
                    AddExitCondition(q, idFactory, new[] { "Survived", "Transit" }, delivery.PlantTime);

                });

            }
            _logger.Debug("[GenerateDeliveryQuest] Все возможные комбинации уже использованы.");
            return null;
        }
        //Квесты на Убийства
        private Quest? GenerateKillQuest()
        {
            var cfg = _config.KillQuest;
            if (!cfg.Enabled) return null;

            var locs = _databaseService.GetLocations()?.GetDictionary();
            if (locs == null || !locs.Any())
            {
                _logger.Warning("[GenerateKillQuest] Список локаций пуст или не загружен");
                return null;
            }

            _logger.Info($"Всего локаций в базе: {locs.Count}");
            _logger.Info("=== Генерация Kill-квеста: сопоставление через LocationMapper ===");

            var allowed = new List<KeyValuePair<string, SPTarkov.Server.Core.Models.Eft.Common.Location>>();

            foreach (var kvp in locs)
            {
                string pascalName = kvp.Key;
                string snakeName = NormalizeLocationName(pascalName);

                _logger.Info($"Обработка локации: {pascalName} → нормализовано: {snakeName}");

                // Проверяем, есть ли такая локация в маппере
                if (!LocationMapper.NameToId.ContainsKey(snakeName))
                {
                    _logger.Info($"❌ Локация '{pascalName}' не найдена в LocationMapper");
                    continue;
                }

                // Проверяем, разрешена ли она в конфиге (по snake_name)
                if (_config.QuestGeneration.AllowedLocations.GetValueOrDefault(snakeName, false))
                {
                    allowed.Add(kvp);
                    _logger.Info($"✅ Локация '{pascalName}' ({snakeName}) разрешена для квестов");
                }
                else
                {
                    _logger.Info($"⚠️  Локация '{pascalName}' ({snakeName}) не разрешена в AllowedLocations");
                }
            }

            _logger.Info($"Разрешённых локаций после фильтрации: {allowed.Count}");

            if (!allowed.Any())
            {
                _logger.Warning("Нет подходящих локаций. Проверь AllowedLocations — должны быть в формате 'woods', 'interchange' и т.д.");
                return null;
            }

            // Перемешиваем
            allowed = allowed.OrderBy(_ => _random.Next()).ToList();

            foreach (var kvp in allowed)
            {
                string pascalName = kvp.Key;
                string snakeName = NormalizeLocationName(pascalName);

                // Получаем настоящий ID (GUID) из маппера
                if (!LocationMapper.NameToId.TryGetValue(snakeName, out string realLocationId))
                {
                    _logger.Warning($"[GenerateKillQuest] Не удалось получить ID для локации '{snakeName}'");
                    continue;
                }

                var key = new QuestKey(realLocationId, "KILL", "", "Kill");
                if (!_tracker.TryUse(key))
                {
                    _logger.Info($"Ключ квеста уже использован или заблокирован: {realLocationId}");
                    continue;
                }

                _logger.Info($"✅ Сгенерирован квест убийства для локации: {pascalName} (ID: {realLocationId})");

                return GenerateBaseQuest("Kill", (q, idFactory) =>
                {
                    q.Location = realLocationId; // ✅ Настоящий ID
                    q.Type = QuestTypeEnum.Elimination;

                    var killId = idFactory();
                    var locId = idFactory();
                    var counterId = idFactory();
                    var outerId = idFactory();

                    var killCondition = new Dictionary<string, object>
                    {
                        ["id"] = killId.ToString(),
                        ["conditionType"] = "Kills",
                        ["value"] = 0,
                        ["compareMethod"] = ">=",
                        ["target"] = cfg.Target
                    };

                    var locationCondition = new Dictionary<string, object>
                    {
                        ["id"] = locId.ToString(),
                        ["conditionType"] = "Location",
                        ["target"] = new[] { pascalName }
                    };

                    var counter = new Dictionary<string, object>
                    {
                        ["id"] = counterId.ToString(),
                        ["conditions"] = new object[] { killCondition, locationCondition }
                    };

                    var counterCond = new QuestCondition
                    {
                        Id = outerId,
                        ConditionType = "CounterCreator",
                        Type = "Elimination",
                        Value = cfg.MinKills,
                        DynamicLocale = false,
                        ExtensionData = new() { ["counter"] = counter }
                    };

                    q.Conditions.AvailableForFinish = new() { counterCond };
                    //AddExitCondition(q, idFactory, new[] { "Survived", "Runner", "Transit" }, 30);
                });
            }

            return null;
        }
        private string NormalizeLocationName(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (char.IsUpper(c) && i > 0 && !char.IsUpper(input[i - 1]))
                {
                    sb.Append('_');
                }
                else if (char.IsDigit(c) && i > 0 && !char.IsDigit(input[i - 1]) && !char.IsUpper(input[i - 1]))
                {
                    sb.Append('_');
                }
                sb.Append(char.ToLower(c));
            }
            return sb.ToString();
        }
        public void ResetTracker()
        {
            _tracker.Clear();
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
                _logger.Warning("[AddRandomItemRewards] ❌ Не удалось загрузить данные из базы.");
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
                _logger.Warning("[AddRandomItemRewards] ❌ Нет активных родителей с весом > 0.");
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

            if (!nonEmptyParents.Any())
            {
                _logger.Warning("[AddRandomItemRewards] ❌ Нет подходящих предметов по цене и категориям.");
                return;
            }

            _logger.Info($"[AddRandomItemRewards] Найдено {nonEmptyParents.Count} категорий с подходящими предметами.");

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
                _logger.Error("[RandomQuestGenerator] ❌ Не удалось установить 'Template' у Item.");
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