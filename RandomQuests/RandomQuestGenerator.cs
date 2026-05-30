using QuestFilterMod.RandomQuests.Models;
using QuestFilterMod.RandomQuests.Utils;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Services.Mod;
using System.Diagnostics.Metrics;
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

                if (_config.QuestGeneration.Types.Planting && _config.DeliveryQuest.Enabled)
                    candidates.Add(("Delivery", GenerateDeliveryQuest));

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
                _logger.Warning("[GenerateSingleQuest] Все возможные комбинации уже использованы или недоступны.");
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
                        q.Type = QuestTypeEnum.PickUp;

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
            if (!delivery.Enabled || !delivery.ItemPool.Any() || !delivery.Locations.Any())
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

            // Собираем ВСЕ возможные комбинации: (локация, точка, предмет)
            var allCandidates = new List<(LocationConfig Loc, string Target, ItemPoolConfig Item)>();

            foreach (var locEntry in allowedLocations)
            {
                foreach (var target in locEntry.Value.Targets.Where(t => !string.IsNullOrEmpty(t)))
                {
                    foreach (var poolItem in delivery.ItemPool.Where(i => !string.IsNullOrEmpty(i.Tpl)))
                    {
                        allCandidates.Add((locEntry.Value, target, poolItem));
                    }
                }
            }

            if (!allCandidates.Any())
            {
                _logger.Warning("[GenerateDeliveryQuest] ❌ Нет ни одной валидной комбинации.");
                return null;
            }

            // Перемешиваем случайно
            var shuffled = allCandidates.OrderBy(_ => _random.Next()).ToList();

            // Пробуем каждую, пока не найдём свободную
            foreach (var (loc, targetPoint, item) in shuffled)
            {
                var questKey = new QuestKey(
                    LocationId: loc.Id,
                    TargetPoint: targetPoint,
                    ItemTpl: item.Tpl,
                    QuestType: "Delivery"
                );

                // Только если ЕЩЁ не использовалась — пробуем создать
                if (_tracker.TryUse(questKey))
                {
                    return GenerateBaseQuest("Delivery", (q, idFactory) =>
                    {
                        q.Location = loc.Id;
                        q.Type = QuestTypeEnum.Discover;

                        AddItemReward(q, item.Tpl, 1, item.Name, "Started");

                        var plantCond = new QuestCondition
                        {
                            Id = idFactory(),
                            ConditionType = "LeaveItemAtLocation",
                            Value = 1,
                            DynamicLocale = false,
                            PlantTime = delivery.PlantTime,
                            ZoneId = targetPoint,
                            ExtensionData = new Dictionary<string, object>
                            {
                                ["target"] = new[] { item.Tpl },
                                ["zoneId"] = targetPoint,
                                ["plantTime"] = delivery.PlantTime
                            }
                        };
                        q.Conditions ??= new QuestConditionTypes();
                        q.Conditions.AvailableForFinish ??= new List<QuestCondition>();
                        q.Conditions.AvailableForFinish.Add(plantCond);

                        AddExitCondition(q, idFactory, new[] { "Survived", "Transit" }, delivery.PlantTime);
                    });
                }
            }

            _logger.Debug("[GenerateDeliveryQuest] Все возможные комбинации уже использованы.");
            return null;
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
                _logger.Info($"[Localization] Added: {locKey} | EN: '{en}' | RU: '{ru}'");
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