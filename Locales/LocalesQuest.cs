using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Utils.Json;
using System.Text.Json;


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

            string baseTypeKey = quest.Type switch
            {
                QuestTypeEnum.PickUp => "base_type_pickup",
                QuestTypeEnum.Elimination => "base_type_elimination",
                QuestTypeEnum.Discover => "base_type_discover",
                QuestTypeEnum.Completion => "base_type_completion",
                _ => "base_type_general"
            };

            string baseTypeFallback = quest.Type switch
            {
                QuestTypeEnum.PickUp => "Pick Up",
                QuestTypeEnum.Elimination => "Elimination",
                QuestTypeEnum.Discover => "Discover point",
                QuestTypeEnum.Completion => "Completion",
                _ => "General Quest"
            };

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
                Add(key, key);

            foreach (var lang in _loadedLocales.Keys)
            {
                AddLocalizedQuestLocales(lang, quest, id, last6, baseTypeKey, baseTypeFallback, locales);
            }

            string locationId = quest.Location;
            bool isAllowed = LocationHelper.IsAllowed(locationId, СonfigRandom);
            string locationName = isAllowed ? LocationHelper.GetPascalName(locationId) : "Unknown";

            if (quest.Conditions?.AvailableForFinish is var conditions && conditions != null)
            {
                foreach (var cond in conditions.Where(c => c?.Id != null))
                {
                    _logger.Error($"[QuestFilterMod][LocalesQuest] CondType: {cond.ConditionType}, Id: {cond.Id}, Target: {cond.Target}, ZoneId: {cond.ZoneId}");
                    string condKey = cond.Id.ToString();

                    string conditionTypeKey = cond.ConditionType switch
                    {
                        "VisitPlace" => "condition_VisitPlace",
                        "CounterCreator" when cond.Type == "Exploration" => "condition_Exploration",
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
                            "VisitPlace" => new[] { cond.Target?.ToString() ?? "", locationName, "" },
                            "CounterCreator" when cond.Type == "Exploration" => new[] { "", "", "" },
                            "LeaveItemAtLocation" => new[] { cond.ZoneId?.ToString() ?? "", cond.Target?.ToString() ?? "", locationName },
                            "PlaceBeacon" => new[] { cond.ZoneId?.ToString() ?? "", "", locationName },
                            "ExitStatus" => new[] { "", "", "" },
                            "Location" => new[] { locationName, "", "" },
                            "Kills" or "CounterCreator" => cond.Type switch
                            {
                                "Elimination" => new[] { GetTargetNameFromRaw(cond.Target?.ToString() ?? ""), "", "" },
                                "Completion" => new[] { "", "", "" },
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

            string displayNameTemplate = $"{baseTypeName} #{last6}";
            string descTemplate = $"Complete the {baseTypeName}";

            void AddLoc(string key, string value)
            {
                if (!locales.ContainsKey(lang)) locales[lang] = new();
                locales[lang][key] = value;
#if DEBUG
                _logger.Error($"[QuestFilterMod][LocalesQuest] Locale added [lang={lang}] key=\"{key}\" → \"{value}\"");
#endif
            }

            AddLoc($"{id} name", displayNameTemplate);
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

                AddLoc($"{id} {evt}", eventValue);
            }
        }

        private string GetTargetNameFromRaw(string targetRaw)
        {
            if (string.IsNullOrEmpty(targetRaw)) return "target";
            string lower = targetRaw.ToLower();
            if (lower.Contains("anypmc")) return "PMC";
            if (lower.Contains("savage")) return "Scav";
            if (lower == "usec") return "USEC";
            if (lower == "bear") return "BEAR";
            return "target";
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

    }

}