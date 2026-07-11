//Locales.cs

using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using System.Text.Json;

namespace QuestFilterMod.RandomQuests
{
    public partial class Generator
    {
        private static readonly Dictionary<string, Dictionary<string, string>> _loadedLocales = new();
        private static bool _localesLoaded = false;


        /// <summary>
        /// Populates the provided locale dictionary with translated strings for the given quest.
        /// Handles quest type, conditions, events, and descriptions with fallback to English.
        /// </summary>
        /// <param name="quest">The quest object to generate localized content for.</param>
        /// <param name="locales">Output dictionary mapping language codes to locale key-value pairs.</param>
        public void FillQuestLocales(Quest quest, Dictionary<string, Dictionary<string, string>> locales)
        {
            if (quest == null) return;

            string id = quest.Id.ToString();
            LoadLocales();
#if DEBUG
            if(Plugin.Config.Debug)
                _logger.Warning($"[QuestFilterMod][Locales] Filling locales for quest ID: {id}, Type: {quest.Type}");
#endif

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
                baseTypeKey = quest.Type switch
                {
                    QuestTypeEnum.PickUp => "base_type_pickup",
                    QuestTypeEnum.Elimination => "base_type_elimination",
                    QuestTypeEnum.Multi => "base_type_multi",
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
                string template = key;

                foreach (var lang in _loadedLocales.Keys)
                {
                    if (!locales.ContainsKey(lang)) locales[lang] = new();

                    string currentTemplate = key;

                    if (_loadedLocales.TryGetValue(lang, out var dictLang) &&
                        dictLang.TryGetValue(key, out var val) &&
                        !string.IsNullOrEmpty(val))
                    {
                        currentTemplate = val;
                    }
                    else if (lang != "en" && _loadedLocales.TryGetValue("en", out var dictEn) &&
                             dictEn.TryGetValue(key, out var enVal) &&
                             !string.IsNullOrEmpty(enVal))
                    {
                        currentTemplate = enVal;
                    }

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

                    locales[lang][key] = formatted;

#if DEBUG
                    //_logger.Warning($"[QuestFilterMod][Locales] [FINAL] Overrode '{key}' [{lang}] → '{formatted}'");
#endif
                }
            }

            foreach (var lang in _loadedLocales.Keys)
            {
                AddLocalizedQuestLocales(lang, quest, id, last6, baseTypeKey, baseTypeFallback, locales);
            }

            string locationId = quest.Location;
            bool isAllowed = Location.IsAllowed(locationId, ConfigRandom);
            string locationName = isAllowed ? Location.GetPascalName(locationId) : "Unknown";

            if (quest.Conditions?.AvailableForFinish is var conditions && conditions != null)
            {
                foreach (var cond in conditions.Where(c => c?.Id != null))
                {
#if DEBUG
                    //_logger.Warning($"[QuestFilterMod][Locales] CondType: {cond.ConditionType}, Id: {cond.Id}, Target: {cond.Target}, ZoneId: {cond.ZoneId}");
#endif
                    string condKey = cond.Id.ToString();

                    string conditionTypeKey = cond.ConditionType switch
                    {
                        
                        "LeaveItemAtLocation" => "condition_LeaveItemAtLocation",
                        "PlaceBeacon" => "condition_PlaceBeacon",
                        "ExitStatus" => "condition_ExitStatus",
                        "FindItem" => "condition_FindItem",
                        "HandoverItem" => "condition_HandoverItem",
                        "CounterCreator" => cond.Type switch
                        {
                            "Exploration" => "condition_VisitPlace",
                            "Elimination" => "condition_Elimination",
                            "Completion" => "condition_Completion",
                            _ => "condition_default"
                        },
                        _ => "condition_default"
                    };

                    foreach (var lang in _loadedLocales.Keys)
                    {
                        string langTemplate = conditionTypeKey;

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

                            "LeaveItemAtLocation" => new[] {
                                cond.ZoneId.ToString() ?? "",
                                GetItemName(cond.ExtensionData.TryGetValue("_item", out var item) == true ? item.ToString() : "", lang),
                                cond.ExtensionData.TryGetValue("_pascalName", out var pascalNameTry) == true ? pascalNameTry.ToString() : "Unknown"
                            },
                            "PlaceBeacon" => new[] {
                                GetItemName(cond.ExtensionData.TryGetValue("_item", out var item) == true ? item.ToString() : "", lang),
                                cond.ZoneId.ToString() ?? "",
                                cond.ExtensionData.TryGetValue("_pascalName", out var pascalNameTry) == true ? pascalNameTry.ToString() : "Unknown",
                            },
                            "FindItem" => new[]
                            {
                                GetItemName(cond.ExtensionData.TryGetValue("_item", out var item) == true ? item.ToString() : "", lang),
                            },
                            "HandoverItem" => new[]
                            {
                                GetItemName(cond.ExtensionData.TryGetValue("_item", out var item) == true ? item.ToString() : "", lang),
                            },

                            "CounterCreator" => cond.Type switch
                            {


                                "Exploration" => new[] {
                                    cond.Counter.Conditions[0].ExtensionData.TryGetValue("target", out var targetGet) == true ? targetGet.ToString() : "",
                                    cond.Counter.Conditions[0].ExtensionData.TryGetValue("_pascalName", out var pascalNameTry) == true ? pascalNameTry.ToString() : "",
                                    "" },
                                "Elimination" => new[]
                                {
                                   cond.Counter.Conditions[0].ExtensionData.TryGetValue("target", out var targetGet) == true ? targetGet.ToString() : "",
                                   cond.Counter.Conditions[0].ExtensionData.TryGetValue("_pascalName", out var pascalNameTry) == true ? pascalNameTry.ToString() : "",
                                    ""
                                },
                                "Completion" => new[] {
                                    locationName,
                                    cond.Counter?.Conditions?[0]?.ExtensionData?.TryGetValue("_status", out var statusObj) == true
                                        ? statusObj switch
                                        {
                                            string s => s,
                                            System.Collections.IEnumerable list => string.Join(", ", list.Cast<object>().Select(o => o?.ToString() ?? "")),
                                            _ => "Unknown"
                                        }
                                        : "Unknown",
                                    ""
                                },

                                _ => new[] { "", "", "" }
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
                                _logger.Warning($"[QuestFilterMod][Locales] Failed to format locale [{lang}] key={condKey}, template={langTemplate}, values={string.Join(", ", nameValues)}: {ex.Message}");
                            }
                        }

                        if (!locales.ContainsKey(lang)) locales[lang] = new();
                        locales[lang][condKey] = langTemplate;
#if DEBUG
                        //_logger.Warning($"[QuestFilterMod][Locales] Locale added [lang={lang}] key=\"{condKey}\" → \"{langTemplate}\"");
#endif
                    }
                }
            }
        }

        /// <summary>
        /// Generates and adds quest-specific localized entries (e.g., {id} name, {id} description, {id} accept) for a given language.
        /// Consolidates condition details (items, zones, time windows, etc.) into human-readable descriptions.
        /// </summary>
        /// <param name="lang">Target language code (e.g., "en", "ru").</param>
        /// <param name="quest">The quest being localized.</param>
        /// <param name="id">Full quest ID.</param>
        /// <param name="last6">Last 6 characters of quest ID (padded/trimmed).</param>
        /// <param name="baseTypeKey">Locale key for the base quest type (e.g., "base_type_elimination").</param>
        /// <param name="baseTypeFallback">Fallback English name for the quest type.</param>
        /// <param name="locales">Output dictionary to populate with localized text.</param>
        private void AddLocalizedQuestLocales(
                string lang,
                Quest quest,
                string id,
                string last6,
                string baseTypeKey,
                string baseTypeFallback,
                Dictionary<string, Dictionary<string, string>> locales
                )
        {
            List<string> extraConditions = new();
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

            string locationName = Location.IsAllowed(quest.Location, ConfigRandom)
                ? Location.GetPascalName(quest.Location)
                : "Any";

            List<string> itemNames = new();
            string mainTargetName = null;
            string mainZone = null;

            baseTypeName = $"{baseTypeName} #{last6}";

            if (quest.Conditions?.AvailableForFinish is var conditions && conditions != null)
            {
                QuestCondition findItemCond = null;
                QuestCondition handoverItemCond = null;

                foreach (var cond in conditions)
                {
                    if (cond?.ConditionType == "FindItem") findItemCond = cond;
                    else if (cond?.ConditionType == "HandoverItem") handoverItemCond = cond;
                }

                string itemId = findItemCond?.ExtensionData?.TryGetValue("_item", out var itemObj) == true ? itemObj?.ToString() ?? "" : "";
                if (!string.IsNullOrEmpty(itemId))
                {
                    string itemName = GetItemName(itemId, lang);
                    itemNames.Add(itemName);
                }

                foreach (var cond in conditions.Where(c => c?.Id != null))
                {
                    switch (cond.ConditionType)
                    {
                        case "FindItem":
                        case "HandoverItem":
                        case "LeaveItemAtLocation":
                            {
                                string itemId2 = cond.ExtensionData?.TryGetValue("_item", out var itemObjTry) == true ? itemObjTry?.ToString() ?? "" : "";
                                if (!string.IsNullOrEmpty(itemId2))
                                {
                                    string itemName = GetItemName(itemId2, lang);
                                    int count = cond.Counter?.Conditions?.Count ?? 1;
                                    itemNames.Add($"{itemName} (x{count})");
                                }
                                break;
                            }
                        case "CounterCreator" when cond.Type == "Exploration":
                            {
                                if (string.IsNullOrEmpty(mainZone))
                                    mainZone = locationName;
                                break;
                            }

                        case "CounterCreator" when cond.Type == "Elimination":
                            {
                                string targetRaw = cond.Counter.Conditions?[0].ExtensionData.TryGetValue("target", out var targetObj) == true ? targetObj?.ToString() ?? "" : "";
                                mainTargetName = GetTargetNameFromRaw(targetRaw ?? "");
                                string timeText = null;
                                string weaponName = null;


                                if (cond.Counter.Conditions[0].ExtensionData.TryGetValue("_time", out var timeObj) == true)
                                {
                                    var daytime = timeObj as DaytimeCounter;
                                    if (daytime != null && daytime.From.HasValue && daytime.To.HasValue)
                                    {
                                        string timeKey = $"time_{daytime.From}_{daytime.To}";

                                        if (_loadedLocales.TryGetValue(lang, out dictLang) &&
                                            dictLang.TryGetValue(timeKey, out var timeVal) &&
                                            !string.IsNullOrEmpty(timeVal))
                                        {
                                            timeText = timeVal;
                                        }
                                        else
                                        {
                                            timeText = $"{daytime.From.Value:00}:00–{daytime.To.Value:00}:00";
                                        }

                                    }
                                }

                                if (cond.Counter.Conditions[0].ExtensionData.TryGetValue("_weapons", out var weaponObj) == true)
                                {
                                    string weaponId = weaponObj?.ToString() ?? "";
                                    if (!string.IsNullOrEmpty(weaponId))
                                    {
                                        weaponName = GetItemName(weaponId, lang);
                                    }
                                }

                                if (timeText != null || weaponName != null) {
                                    extraConditions.Add($"{timeText} - { weaponName}");
                                }

                                break;
                            }

                        case "CounterCreator" when cond.Type == "Completion":
                            {
                                if (string.IsNullOrEmpty(mainZone))
                                    mainZone = locationName;
                                break;
                            }
                    }
                }
            }

            string descTemplate;

            if (itemNames.Any() || extraConditions.Any())
            {
                var allDetails = new List<string>();
                if (itemNames.Any()) allDetails.AddRange(itemNames);
                if (extraConditions.Any()) allDetails.AddRange(extraConditions);

                string detailsStr = string.Join("\n* ", allDetails);

                if (!string.IsNullOrEmpty(mainZone))
                    descTemplate = $"{baseTypeName}\n* {mainZone}\n* {detailsStr}";
                else
                    descTemplate = $"{baseTypeName}\n* {detailsStr}";
            }
            else if (!string.IsNullOrEmpty(mainTargetName))
            {
                if (!string.IsNullOrEmpty(mainZone))
                    descTemplate = $"{baseTypeName}\n* {mainZone}\n* {mainTargetName}";
                else
                    descTemplate = $"{baseTypeName}\n* {mainTargetName}";
            }
            else if (!string.IsNullOrEmpty(locationName))
            {
                descTemplate = $"{baseTypeName}\n* {locationName}";
            }
            else
            {
                descTemplate = $"{baseTypeName}";
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
                //_logger.Warning($"[QuestFilterMod][Locales] Localequest_complete added [lang={lang}] key=\"{key}\" → \"{value}\"");
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

                string questIdentifier = $"{baseTypeName}"; 
                eventValue = eventValue.Replace("{0}", questIdentifier);

                AddLoc($"{id} {evt}", eventValue);
            }
        }

        /// <summary>
        /// Loads JSON locale files from disk for all supported languages on first invocation.
        /// Caches loaded dictionaries and logs missing/unreadable files.
        /// </summary>
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
                    _logger.Error($"[QuestFilterMod][Locales] Failed to load locale file {path}: {ex.Message}");
                    _loadedLocales[lang] = new Dictionary<string, string>(); 
                }
            }
#if DEBUG
            if (Plugin.Config.Debug)

                //_logger.Warning($"[QuestFilterMod][Locales] Loaded locale languages: {string.Join(", ", loadedLangs)}");
#endif
            if (missingLangs.Count > 0)
#if DEBUG
                if (Plugin.Config.Debug)
                    //_logger.Warning($"[QuestFilterMod][Locales] Missing locale files for languages: {string.Join(", ", missingLangs)}");
#endif

            _localesLoaded = true;
        }
        /// <summary>
        /// Normalizes and translates raw target types (e.g., "savage", "USEC", "BEAR") into localized display names.
        /// </summary>
        /// <param name="targetRaw">Raw target identifier string from quest condition.</param>
        /// <returns>Human-readable target name (e.g., "Scav", "USEC").</returns>
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
        /// <summary>
        /// Retrieves the localized name of an item by its ID across all available languages.
        /// Prioritizes requested language, falls back to English, then any other loaded language.
        /// </summary>
        /// <param name="itemId">The item's unique identifier (e.g., "544900f54bdc2d6f028b456f").</param>
        /// <param name="lang">Preferred language code.</param>
        /// <returns>The localized item name, or the raw ID if not found.</returns>
        private string GetItemName(string itemId, string lang = "en")
        {
            if (string.IsNullOrEmpty(itemId)) return "unknown item";

            string nameKey = itemId + " Name";

            string GetNameInLang(string l)
            {
                if (!_databaseService.GetLocales().Global.TryGetValue(l, out var lazy))
                    return null;

                if (lazy?.Value is not Dictionary<string, string> dict)
                    return null;

                return dict.TryGetValue(nameKey, out var name) && !string.IsNullOrEmpty(name) ? name : null;
            }

            if (!string.IsNullOrEmpty(lang))
            {
                string name = GetNameInLang(lang);
                if (!string.IsNullOrEmpty(name)) return name;
            }

            if (lang != "en")
            {
                string name = GetNameInLang("en");
                if (!string.IsNullOrEmpty(name)) return name;
            }
            foreach (var (l, _) in _databaseService.GetLocales().Global)
            {
                if (l == lang || l == "en") continue;
                string name = GetNameInLang(l);
                if (!string.IsNullOrEmpty(name)) return name;
            }

            return itemId; 
        }
    }
}