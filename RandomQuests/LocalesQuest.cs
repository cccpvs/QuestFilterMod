using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Utils.Json;

namespace QuestFilterMod.RandomQuests
{
    public partial class RandomQuestGenerator
    {

        private void FillQuestLocales(Quest quest, Dictionary<string, Dictionary<string, string>> locales)
        {
            if (quest == null) return;

            string id = quest.Id.ToString();

            void Add(string key, string en, string ru)
            {
                // 1. EN
                if (!locales.TryGetValue("en", out var enDict) || enDict == null)
                {
                    enDict = new Dictionary<string, string>();
                    locales["en"] = enDict;
                }
                enDict[key] = en;

                // 2. RU
                if (!locales.TryGetValue("ru", out var ruDict) || ruDict == null)
                {
                    ruDict = new Dictionary<string, string>();
                    locales["ru"] = ruDict;
                }
                ruDict[key] = string.IsNullOrEmpty(ru) ? en : ru;

                // 3. Все остальные — fallback на en
                foreach (var lang in new[] { "ch", "cz", "es-mx", "es", "fr", "ge", "hu", "it", "jp", "kr", "pl", "po", "ro", "sk", "tu" })
                {
                    if (!locales.TryGetValue(lang, out var langDict) || langDict == null)
                    {
                        langDict = new Dictionary<string, string>();
                        locales[lang] = langDict;
                    }
                    langDict[key] = en; 
                }
            }

            string baseTypeEn = quest.Type switch
            {
                QuestTypeEnum.PickUp => "Pick Up",
                QuestTypeEnum.Elimination => "Elimination",
                QuestTypeEnum.Discover => "Discover point",
                QuestTypeEnum.Completion => "Completion",
                _ => "General Quest"
            };

            string baseTypeRu = quest.Type switch
            {
                QuestTypeEnum.PickUp => "Забрать предмет",
                QuestTypeEnum.Elimination => "Устранение",
                QuestTypeEnum.Discover => "Обследовать точку",
                QuestTypeEnum.Completion => "Завершить",
                _ => "Обычный квест"
            };

            var conditions = quest.Conditions?.AvailableForFinish ?? new List<QuestCondition>();
            string locationId = quest.Location;
            bool isAllowed = LocationHelper.IsAllowed(locationId, _config);
            string locationName = isAllowed ? LocationHelper.GetPascalName(locationId) : "Unknown";

            string last6 = id.Length > 6 ? id.Substring(id.Length - 6) : id.PadLeft(6, '0');
            string displayNameEn = $"{baseTypeEn} #{last6}";
            string displayNameRu = $"{baseTypeRu} #{last6}";

            string descEn = $"Complete the {baseTypeEn.ToLower()}";
            string descRu = $"Выполни: {baseTypeRu.ToLower()}";

            foreach (var cond in conditions)
            {
                if (cond.ConditionType == "CounterCreator" && cond.Type == "Elimination")
                {
                    if (cond.ExtensionData?.TryGetValue("counter", out var counterObj) is true &&
                        counterObj is Dictionary<string, object> counter &&
                        counter.TryGetValue("conditions", out var conditionsObj) &&
                        conditionsObj is object[] killsConditions)
                    {
                        var kills = killsConditions
                            .OfType<Dictionary<string, object>>()
                            .FirstOrDefault(c => c.GetValueOrDefault("conditionType")?.ToString() == "Kills");

                        if (kills != null)
                        {
                            var targetRaw = kills.GetValueOrDefault("target")?.ToString() ?? "";
                            var count = cond.Value ?? 1;

                            string targetName = GetTargetNameFromRaw(targetRaw);

                            descEn = $"{locationName}: Kill {count} {targetName}";
                            descRu = $"{locationName}: Убей {count} {targetName}";
                        }
                    }
                }
                else if (cond.ConditionType == "VisitPlace")
                {
                    var place = GetExtValue(cond, "target") ?? locationName;
                    descEn = $"Visit «{place}» ({locationName})";
                    descRu = $"Посети «{place}» ({locationName})";
                }
                else if (cond.ConditionType == "LeaveItemAtLocation")
                {
                    var zone = GetExtValue(cond, "zoneId") ?? "zone";
                    descEn = $"Hide item at «{zone}» ({locationName})";
                    descRu = $"Спрячь предмет в «{zone}» ({locationName})";
                }
            }

            string idStr = quest.Id.ToString();
            string last6Quest = idStr.Length > 6 ? idStr.Substring(idStr.Length - 6) : idStr.PadLeft(6, '0');
            int questNumber = Convert.ToInt32(last6Quest, 16) % 999 + 1;
            string questIdPart = questNumber.ToString("D3");

            displayNameEn = $"{baseTypeEn} #{questIdPart}";
            displayNameRu = $"{baseTypeRu} #{questIdPart}";

            Add($"{id} name", displayNameEn, displayNameRu);
            Add($"{id} questName", displayNameEn, displayNameRu);
            Add($"{id} description", descEn, descRu);
            Add($"{id} accept", $"Quest accepted: {baseTypeEn}", $"Квест принят: {baseTypeRu}");
            Add($"{id} change", $"Task updated: {baseTypeEn}", $"Задание обновлено: {baseTypeRu}");
            Add($"{id} complete", $"Task completed: {baseTypeEn}", $"Задание выполнено: {baseTypeRu}");
            Add($"{id} started", $"Quest started: {baseTypeEn}", $"Квест начался: {baseTypeRu}");
            Add($"{id} completed", $"Quest completed: {baseTypeEn}", $"Квест завершён: {baseTypeRu}");
            Add($"{id} failed", $"Quest failed: {baseTypeEn}", $"Квест провален: {baseTypeRu}");

            foreach (var cond in conditions.Where(c => c?.Id != null))
            {
                string key = cond.Id.ToString();
                string enText = "", ruText = "";

                if (cond.ConditionType == "Kills" || (cond.ConditionType == "CounterCreator" && cond.Type == "Elimination"))
                {
                    enText = "Kill designated targets";
                    ruText = "Убить назначенные цели";
                }
                else if (cond.ConditionType == "VisitPlace" || (cond.ConditionType == "CounterCreator" && cond.Type == "Exploration"))
                {
                    enText = "Visit point";
                    ruText = "Посети точку";
                }
                else if (cond.ConditionType == "PlaceBeacon" || cond.ConditionType == "LeaveItemAtLocation")
                {
                    enText = $"Hide item {cond.Target} at «{cond.ZoneId}»";
                    ruText = $"Спрячь {cond.Target} предмет в зоне «{cond.ZoneId}»";
                }
                else if (cond.ConditionType == "ExitStatus" || (cond.ConditionType == "CounterCreator" && cond.Type == "Completion"))
                {
                    enText = "Survive or Transit";
                    ruText = "Выжить или Транзит";
                }
                else if (cond.ConditionType == "Location" || (cond.ConditionType == "CounterCreator" && cond.Type == "Completion"))
                {
                    enText = "Exit location: " + locationName;
                    ruText = "Выйти с локации: " + locationName;
                }
                else
                {
                    enText = $"Complete condition: {cond.ConditionType}";
                    ruText = $"Выполни условие: {cond.ConditionType}";
                }
                Add(key, enText, ruText);
            }
        }

        private string? GetExtValue(QuestCondition cond, string key)
        {
            if (cond?.ExtensionData?.TryGetValue(key, out var value) == true)
                return value?.ToString();
            return null;
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

    }

}
