using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using System.Text.Json;

#if DEBUG
/*
*/
#endif


namespace QuestFilterMod.RandomQuests
{
    public partial class RandomQuestGenerator
    {

        private static readonly Dictionary<string, Dictionary<string, string>> _loadedLocales = new();
        private static bool _localesLoaded = false;

        private void FillQuestLocales(Quest quest, Dictionary<string, Dictionary<string, string>> locales)
        {
            if (quest == null) return;

            string id = quest.Id.ToString();
            LoadLocales();

            _logger.Warning($"[QuestFilterMod][LocalesQuest] Filling locales for quest ID: {id}, Type: {quest.Type}");

            void Add(string key, string fallbackTemplate = "")
            {
                foreach (var lang in _loadedLocales.Keys)
                {
                    if (!locales.ContainsKey(lang)) locales[lang] = new();

                    if (_loadedLocales.TryGetValue(lang, out var dict) && dict.TryGetValue(key, out var value))
                        locales[lang][key] = value;
                    else if (lang != "en" && _loadedLocales.TryGetValue("en", out var dictEn) && dictEn.TryGetValue(key, out var enValue))
                        locales[lang][key] = enValue;
                    else
                        locales[lang][key] = string.IsNullOrEmpty(fallbackTemplate) ? key : fallbackTemplate;
                }
            }

            string baseTypeKey;
            string baseTypeFallback;

            if (quest.Type == QuestTypeEnum.Discover && quest.Conditions?.AvailableForFinish is { } condList && condList.Any())
            {
                var firstCondType = condList.FirstOrDefault()?.ConditionType;

                baseTypeKey = firstCondType switch
                {
                    "LeaveItemAtLocation" => "base_type_leave_item",
                    "PlaceBeacon" => "base_type_place_beacon",
                    _ => "base_type_discover"
                };

                baseTypeFallback = firstCondType switch
                {
                    "LeaveItemAtLocation" => "Leave Item",
                    "PlaceBeacon" => "Place Beacon",
                    _ => "Discover point"
                };
            }
            else
            {
                // Стандартный fallback для других типов
                baseTypeKey = quest.Type switch
                {
                    QuestTypeEnum.PickUp => "base_type_pickup",
                    QuestTypeEnum.Elimination => "base_type_elimination",
                    QuestTypeEnum.Discover => "base_type_discover",
                    QuestTypeEnum.Completion => "base_type_completion",
                    _ => "base_type_general"
                };

                baseTypeFallback = quest.Type switch
                {
                    QuestTypeEnum.PickUp => "Pick Up",
                    QuestTypeEnum.Elimination => "Elimination",
                    QuestTypeEnum.Discover => "Discover point",
                    QuestTypeEnum.Completion => "Completion",
                    _ => "General Quest"
                };
            }

            string last6 = id.Length > 6 ? id.Substring(id.Length - 6) : id.PadLeft(6, '0');

            Add(baseTypeKey, baseTypeFallback);

            string[] conditionKeys = {
                "condition_Elimination",
                "condition_VisitPlace",
                "condition_LeaveItemAtLocation",
                "condition_PlaceBeacon",
                "condition_ExitStatus",
                "condition_Location",
                "condition_Exploration",
                "condition_Completion",
                "condition_FindItem",
                "condition_default"
            };
            foreach (var key in conditionKeys)
                Add(key, key);

            string[] eventKeys = {
                "quest_accept",
                "quest_change",
                "quest_complete",
                "quest_started",
                "quest_completed",
                "quest_failed"
            };


            foreach (var key in eventKeys)
            {
                // Получаем шаблон из локалей или fallback
                string template = key;

                // Пробуем текущий язык
                foreach (var lang in _loadedLocales.Keys)
                {
                    if (!locales.ContainsKey(lang)) locales[lang] = new();

                    string currentTemplate = key;

                    // Шаг 1: взять из языка
                    if (_loadedLocales.TryGetValue(lang, out var dictLang) &&
                        dictLang.TryGetValue(key, out var val) &&
                        !string.IsNullOrEmpty(val))
                    {
                        currentTemplate = val;
                    }
                    // Шаг 2: если не нашли в текущем языке — пробуем en
                    else if (lang != "en" && _loadedLocales.TryGetValue("en", out var dictEn) &&
                             dictEn.TryGetValue(key, out var enVal) &&
                             !string.IsNullOrEmpty(enVal))
                    {
                        currentTemplate = enVal;
                    }

                    // 🔥 Шаг 3: подставить ID квеста в {0}
                    string baseTypeName = baseTypeFallback;
                    if (_loadedLocales.TryGetValue(lang, out var dictLang2) &&
                        dictLang2.TryGetValue(baseTypeKey, out var baseTypeVal) &&
                        !string.IsNullOrEmpty(baseTypeVal))
                    {
                        baseTypeName = baseTypeVal;
                    }
                    else if (lang != "en" && _loadedLocales.TryGetValue("en", out var dictEn2) &&
                             dictEn2.TryGetValue(baseTypeKey, out var enBaseTypeVal) &&
                             !string.IsNullOrEmpty(enBaseTypeVal))
                    {
                        baseTypeName = enBaseTypeVal;
                    }

                    string questIdentifier = $"{baseTypeName} #{last6}";
                    string formatted = currentTemplate.Replace("{0}", questIdentifier);

                    // 🔥 Сохраняем под тем же ключом — сервер будет читать это!
                    locales[lang][key] = formatted;

#if DEBUG
                    _logger.Error($"[QuestFilterMod][LocalesQuest] [FINAL] Overrode '{key}' [{lang}] → '{formatted}'");
#endif
                }
            }




            foreach (var lang in _loadedLocales.Keys)
            {
                AddLocalizedQuestLocales(lang, quest, id, last6, baseTypeKey, baseTypeFallback, locales);
            }

            string locationId = quest.Location;
            bool isAllowed = LocationHelper.IsAllowed(locationId, ConfigRandom);
            string locationName = isAllowed ? LocationHelper.GetPascalName(locationId) : "Unknown";

            if (quest.Conditions?.AvailableForFinish is var conditions && conditions != null)
            {
                foreach (var cond in conditions.Where(c => c?.Id != null))
                {
                    _logger.Error($"[QuestFilterMod][LocalesQuest] CondType: {cond.ConditionType}, Id: {cond.Id}, Target: {cond.Target}, ZoneId: {cond.ZoneId}");
                    string condKey = cond.Id.ToString();

                    string conditionTypeKey = cond.ConditionType switch
                    {
                        "VisitPlace" or
                        _ when cond.ConditionType == "CounterCreator" && cond.Type == "Exploration"
                            => "condition_VisitPlace",
                        "LeaveItemAtLocation" => "condition_LeaveItemAtLocation",
                        "PlaceBeacon" => "condition_PlaceBeacon",
                        "ExitStatus" => "condition_ExitStatus",
                        "Location" => "condition_Location",
                        "Kills" or "CounterCreator" => cond.Type switch
                        {
                            "Elimination" => "condition_Elimination",
                            "Completion" => "condition_Completion",
                            _ => "condition_default"
                        },
                        "FindItem" => "condition_FindItem",
                        _ => "condition_default"
                    };

                    foreach (var lang in _loadedLocales.Keys)
                    {
                        string langTemplate = conditionTypeKey;

                        // 🔹 1. Попытка: взять из текущего языка
                        bool foundInCurrentLang = false;
                        if (_loadedLocales.TryGetValue(lang, out var dictLang) &&
                            dictLang.TryGetValue(conditionTypeKey, out var condVal) &&
                            !string.IsNullOrEmpty(condVal))
                        {
                            langTemplate = condVal;
                            foundInCurrentLang = true;
                        }

                        if (!foundInCurrentLang && lang != "en" &&
                            _loadedLocales.TryGetValue("en", out var dictEn) &&
                            dictEn.TryGetValue(conditionTypeKey, out var enVal) &&
                            !string.IsNullOrEmpty(enVal))
                        {
                            langTemplate = enVal;
                        }


                        string[] nameValues = cond.ConditionType switch
                        {
                            "VisitPlace" => new[] { cond.Counter?.Conditions?[0].Target?.ToString() ?? "", locationName, "" },
                            "CounterCreator" when cond.Type == "Exploration" => new[] { cond.Counter?.Conditions?[0]?.ExtensionData?["target"]?.ToString() ?? "", locationName, "" },
                            "LeaveItemAtLocation" => new[] { cond.ZoneId?.ToString() ?? "", GetItemName(cond.ExtensionData?["_item"]?.ToString() ?? "", lang), locationName },
                            "PlaceBeacon" => new[] { cond.ZoneId?.ToString() ?? "", "", locationName },
                            "ExitStatus" => new[] { "", "", "" },
                            "Location" => new[] { locationName, "", "" },
                            "Kills" or "CounterCreator" => cond.Type switch
                            {
                                "Elimination" => new[]
                                {
                                   cond.Counter?.Conditions?[0]?.ExtensionData?["target"]?.ToString() ?? "",
                                    "",
                                    ""
                                },
                                "Completion" => new[] { "", "", "" },
                                _ => new[] { "", "", "" }
                            },
                            "FindItem" => new[]
                            {
                                GetItemName(cond.ExtensionData?["_item"]?.ToString() ?? "", lang), "", ""
                            },
                            _ => new[] { "", "", "" }
                        };

                        if (!string.IsNullOrEmpty(langTemplate) &&
                            (langTemplate.Contains("{0}") || langTemplate.Contains("{1}") || langTemplate.Contains("{2}")))
                        {
                            try
                            {
                                langTemplate = string.Format(langTemplate, nameValues);
                            }
                            catch (Exception ex)
                            {
                                _logger.Warning($"[QuestFilterMod][LocalesQuest] Failed to format locale [{lang}] key={condKey}, template={langTemplate}, values={string.Join(", ", nameValues)}: {ex.Message}");
                            }
                        }

                        if (!locales.ContainsKey(lang)) locales[lang] = new();
                        locales[lang][condKey] = langTemplate;
#if DEBUG
                        _logger.Error($"[QuestFilterMod][LocalesQuest] Locale added [lang={lang}] key=\"{condKey}\" → \"{langTemplate}\"");
#endif
                    }
                }
            }
        }

        private void AddLocalizedQuestLocales(
    string lang,
    Quest quest,
    string id,
    string last6,
    string baseTypeKey,
    string baseTypeFallback,
    Dictionary<string, Dictionary<string, string>> locales)
        {
            // 🔹 Получаем название типа квеста (уже есть)
            string baseTypeName = baseTypeFallback;

            if (_loadedLocales.TryGetValue(lang, out var dictLang) &&
                dictLang.TryGetValue(baseTypeKey, out var baseTypeVal) &&
                !string.IsNullOrEmpty(baseTypeVal))
            {
                baseTypeName = baseTypeVal;
            }
            else if (lang != "en" && _loadedLocales.TryGetValue("en", out var dictEn) &&
                     dictEn.TryGetValue(baseTypeKey, out var enBaseTypeVal) &&
                     !string.IsNullOrEmpty(enBaseTypeVal))
            {
                baseTypeName = enBaseTypeVal;
            }

            // 🔹 Местоположение
            string locationName = LocationHelper.IsAllowed(quest.Location, ConfigRandom)
                ? LocationHelper.GetPascalName(quest.Location)
                : "Unknown";

            // 🔹 Собираем предметы для описания
            List<string> itemNames = new();
            string? mainTargetName = null;
            string? mainZone = null;

            if (quest.Conditions?.AvailableForFinish is var conditions && conditions != null)
            {
                QuestCondition? findItemCond = null;
                QuestCondition? handoverItemCond = null;

                foreach (var cond in conditions)
                {
                    if (cond?.ConditionType == "FindItem") findItemCond = cond;
                    else if (cond?.ConditionType == "HandoverItem") handoverItemCond = cond;
                }

                string itemId = findItemCond?.ExtensionData?["_item"]?.ToString() ?? "";
                if (!string.IsNullOrEmpty(itemId))
                {
                    string itemName = GetItemName(itemId, lang);
                    itemNames.Add(itemName);
                }

                foreach (var cond in conditions.Where(c => c?.Id != null))
                {
                    switch (cond.ConditionType)
                    {
                        case "LeaveItemAtLocation":
                            {
                                string? itemId2 = cond.ExtensionData?["_item"]?.ToString();
                                if (!string.IsNullOrEmpty(itemId2))
                                {
                                    string itemName = GetItemName(itemId2, lang);
                                    int count = cond.Counter?.Conditions?.Count ?? 1;
                                    itemNames.Add($"{itemName} (x{count})");
                                }
                                break;
                            }

                        case "VisitPlace":
                        case "CounterCreator" when cond.Type == "Exploration":
                            {
                                if (string.IsNullOrEmpty(mainZone))
                                    mainZone = locationName;
                                break;
                            }

                        case "Kills" or "CounterCreator":
                            if (cond.Type == "Elimination")
                            {
                                string? targetRaw = cond.Counter?.Conditions?[0]?.ExtensionData?["target"]?.ToString();
                                mainTargetName = GetTargetNameFromRaw(targetRaw ?? "");
                            }
                            break;
                    }
                }
            }

            string descTemplate;

            if (itemNames.Any())
            {
                string itemsStr = string.Join(", ", itemNames);
                if (!string.IsNullOrEmpty(mainZone))
                    descTemplate = $"{baseTypeName} #{last6}\n* {mainZone}\n* {itemsStr}";
                else
                    descTemplate = $"{baseTypeName} #{last6}\n* {itemsStr}";
            }
            else if (!string.IsNullOrEmpty(mainTargetName))
            {
                if (!string.IsNullOrEmpty(mainZone))
                    descTemplate = $"{baseTypeName} #{last6}\n* {mainZone}\n* {mainTargetName}";
                else
                    descTemplate = $"{baseTypeName} #{last6}\n* {mainTargetName}";
            }
            else if (!string.IsNullOrEmpty(locationName))
            {
                descTemplate = $"{baseTypeName} #{last6}\n* {locationName}";
            }
            else
            {
                descTemplate = $"{baseTypeName} #{last6}";
            }

            if (quest.ExtensionData?.TryGetValue("description", out var descObj) == true &&
                descObj?.ToString() is { Length: > 0 } descStr)
            {
                descTemplate += $"\n{descStr}";
            }

            void AddLoc(string key, string value)
            {
                if (!locales.ContainsKey(lang)) locales[lang] = new();
                locales[lang][key] = value;
#if DEBUG
                _logger.Error($"[QuestFilterMod][LocalesQuest] Locale added [lang={lang}] key=\"{key}\" → \"{value}\"");
#endif
            }

            AddLoc($"{id} name", baseTypeName);
            AddLoc($"{id} description", descTemplate);

            string[] eventKeys = { "accept", "change", "complete", "started", "completed", "failed" };
            foreach (var evt in eventKeys)
            {
                string questKey = $"quest_{evt}";
                string eventFallback = $"Quest {evt}: {{0}}";
                string eventValue = eventFallback;

                if (_loadedLocales.TryGetValue(lang, out var dictLang2) &&
                    dictLang2.TryGetValue(questKey, out var questVal) &&
                    !string.IsNullOrEmpty(questVal))
                {
                    eventValue = questVal;
                }
                else if (lang != "en" &&
                         _loadedLocales.TryGetValue("en", out var dictEn2) &&
                         dictEn2.TryGetValue(questKey, out var enVal) &&
                         !string.IsNullOrEmpty(enVal))
                {
                    eventValue = enVal;
                }

                string questIdentifier = $"{baseTypeName} #{last6}"; 
                eventValue = eventValue.Replace("{0}", questIdentifier);

                AddLoc($"{id} {evt}", eventValue);
            }
        }


        private void LoadLocales()
        {
            if (_localesLoaded) return;

            var modBasePath = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "user",
                "mods",
                "questFilterMod"
            );
            string basePath = System.IO.Path.Combine(modBasePath, "Locales");
            string[] languages = { "en", "ru", "ch", "cz", "es-mx", "es", "fr", "ge", "hu", "it", "jp", "kr", "pl", "po", "ro", "sk", "tu" };

            var loadedLangs = new List<string>();
            var missingLangs = new List<string>();

            foreach (var lang in languages)
            {
                string path = System.IO.Path.Combine(basePath, $"{lang}.json");

                if (!File.Exists(path))
                {
                    _loadedLocales[lang] = new Dictionary<string, string>();
                    missingLangs.Add(lang);
                    continue;
                }

                try
                {
                    string json = File.ReadAllText(path);
                    var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (dict != null && dict.Count > 0)
                    {
                        _loadedLocales[lang] = dict;
                    }
                    else
                    {
                        _loadedLocales[lang] = new Dictionary<string, string>();
                    }
                    loadedLangs.Add(lang);
                }
                catch (Exception ex)
                {
                    _logger.Error($"[QuestFilterMod][LocalesQuest] Failed to load locale file {path}: {ex.Message}");
                    _loadedLocales[lang] = new Dictionary<string, string>(); 
                }
            }

            _logger.Warning($"[QuestFilterMod][LocalesQuest] Loaded locale languages: {string.Join(", ", loadedLangs)}");
            if (missingLangs.Count > 0)
                _logger.Error($"[QuestFilterMod][LocalesQuest] Missing locale files for languages: {string.Join(", ", missingLangs)}");

            _localesLoaded = true;
        }

        private string GetTargetNameFromRaw(string targetRaw)
        {
            if (string.IsNullOrEmpty(targetRaw)) return "target";

            string normalized = targetRaw.Trim().ToLowerInvariant();

            return normalized switch
            {
                "any" => "Any",
                "anypmc" => "Any PMC",
                "savage" or "scav" => "Scav",
                "usec" => "USEC",
                "bear" => "BEAR",
                _ => "target"
            };
        }

        private string GetItemName(string itemId, string lang = "en")
        {
            if (string.IsNullOrEmpty(itemId)) return "unknown item";

            string nameKey = itemId + " Name";

            string? GetNameInLang(string l)
            {
                if (!_databaseService.GetLocales().Global.TryGetValue(l, out var lazy))
                    return null;

                if (lazy?.Value is not Dictionary<string, string> dict)
                    return null;

                return dict.TryGetValue(nameKey, out var name) && !string.IsNullOrEmpty(name) ? name : null;
            }

            if (!string.IsNullOrEmpty(lang))
            {
                string? name = GetNameInLang(lang);
                if (!string.IsNullOrEmpty(name)) return name;
            }

            if (lang != "en")
            {
                string? name = GetNameInLang("en");
                if (!string.IsNullOrEmpty(name)) return name;
            }
            foreach (var (l, _) in _databaseService.GetLocales().Global)
            {
                if (l == lang || l == "en") continue; // уже проверили
                string? name = GetNameInLang(l);
                if (!string.IsNullOrEmpty(name)) return name;
            }

            return itemId; 
        }

    }

}