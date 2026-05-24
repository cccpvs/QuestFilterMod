using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

namespace QuestFilterMod.RandomQuests
{
    public partial class RandomQuestGenerator
    {

        
        private void FillQuestLocales(Quest quest, Dictionary<string, string> enDict, Dictionary<string, string> ruDict)
        {
            if (quest == null) return;

            string id = quest.Id.ToString();

            void Add(string key, string en, string ru) => (enDict[key], ruDict[key]) = (en, ru);

            // 🔹 Тип квеста
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

            var conditions = GetConditions(quest) ?? new List<QuestCondition>();
            string locationId = quest.Location;

            bool isAllowed = LocationHelper.IsAllowed(locationId, _config);
            string locationName = isAllowed
                ? LocationHelper.GetPascalName(locationId)
                : "Unknown";

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

                            string targetName = targetRaw.ToLower() switch
                            {
                                _ when targetRaw.Contains("anypmc") => "PMC",
                                _ when targetRaw.Contains("savage") => "Scav",
                                "usec" => "USEC",
                                "bear" => "BEAR",
                                _ => "target"
                            };

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
            string last6 = idStr.Substring(idStr.Length - 6);
            int questNumber = Convert.ToInt32(last6, 16) % 999 + 1; 

            string questIdPart = questNumber.ToString("D3");

            string displayNameEn = $"{baseTypeEn} #{questIdPart}";
            string displayNameRu = $"{baseTypeRu} #{questIdPart}";



            // === Основные локали ===
            Add($"{id} name", displayNameEn, displayNameRu);
            Add($"{id} questName", displayNameEn, displayNameRu);
            Add($"{id} description", descEn, descRu);
            //Add($"{id} note", $"Random {baseTypeEn}", $"Случайный {baseTypeRu}");
            Add($"{id} accept", $"Quest accepted: {baseTypeEn}", $"Квест принят: {baseTypeRu}");
            Add($"{id} change", $"Task updated: {baseTypeEn}", $"Задание обновлено: {baseTypeRu}");
            Add($"{id} complete", $"Task completed: {baseTypeEn}", $"Задание выполнено: {baseTypeRu}");
            Add($"{id} started", $"Quest started: {baseTypeEn}", $"Квест начался: {baseTypeRu}");
            Add($"{id} completed", $"Quest completed: {baseTypeEn}", $"Квест завершён: {baseTypeRu}");
            Add($"{id} failed", $"Quest failed: {baseTypeEn}", $"Квест провален: {baseTypeRu}");

            // === Условия ===
            foreach (var cond in conditions)
            {
                string key = cond.Id.ToString();
                string enText = "", ruText = "";



                if (cond.ConditionType == "Kills" ||
                        (cond.ConditionType == "CounterCreator" && cond.Type == "Elimination"))
                {
                    var place = GetExtValue(cond, "target") ?? locationName;
                    enText = $"Kill designated targets «{place}»";
                    ruText = $"Убить назначенные цели «{place}»";
                }

                else if (cond.ConditionType == "VisitPlace" ||
                        (cond.ConditionType == "CounterCreator" && cond.Type == "Exploration"))
                {
                    var place = GetExtValue(cond, "target") ?? locationName;
                    enText = $"Visit point «{place}»";
                    ruText = $"Посети точку «{place}»";
                }

                else if (cond.ConditionType == "PlaceBeacon")
                {
                    enText = $"Hide item {cond.Target} at «{cond.ZoneId}»";
                    ruText = $"Спрячь {cond.Target} предмет в зоне «{cond.ZoneId}»";
                }

                else if (cond.ConditionType == "LeaveItemAtLocation")
                {
                    enText = $"Hide item {cond.Target} at «{cond.ZoneId}»";
                    ruText = $"Спрячь {cond.Target} предмет в зоне «{cond.ZoneId}»";
                }

                else if (cond.ConditionType == "ExitStatus" ||
                        (cond.ConditionType == "CounterCreator" && cond.Type == "Completion"))
                {
                    enText = "Survive or Transit";
                    ruText = "Выжить или Транзит";
                }
                else if (cond.ConditionType == "Location" ||
                        (cond.ConditionType == "CounterCreator" && cond.Type == "Completion"))
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


    }

}
