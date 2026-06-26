using QuestFilterMod.RandomQuests.Models;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace QuestFilterMod.RandomQuests
{
    public static class Location
    {
#if DEBUG
        /*
             [LOC] Bigmap → 56f40101d2720b2a4d8b45d6
             [LOC] Develop → 56db0b3bd2720bb0678b4567
             [LOC] Factory4Day → 55f2d3fd4bdc2d5f408b4567
             [LOC] Factory4Night → 59fc81d786f774390775787e
             [LOC] Hideout → 599319c986f7740dca3070a6
             [LOC] Interchange → 5714dbc024597771384a510d
             [LOC] Laboratory → 5b0fc42d86f7744a585f9105
             [LOC] Lighthouse → 5704e4dad2720bb55b8b4567
             [LOC] PrivateArea → 5704e64ad2720bb55b8b456e
             [LOC] RezervBase → 5704e5fad2720bc05b8b4567
             [LOC] Shoreline → 5704e554d2720bac5b8b456e
             [LOC] Suburbs → 5714dc342459777137212e0b
             [LOC] TarkovStreets → 5714dc692459777137212e12
             [LOC] Labyrinth → 6733700029c367a3d40b02af
             [LOC] Terminal → 5704e5a4d2720bb45b8b4567
             [LOC] Town → 5704e47ed2720bb35b8b4568
             [LOC] Woods → 5704e3c2d2720bac5b8b4567
             [LOC] Sandbox → 653e6760052c01c1c805532f
             [LOC] SandboxHigh → 65b8d6f5cdde2479cb2a3125
        */
#endif

        public static readonly Dictionary<string, string> IdToPascalName = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Инициализирует маппинг между ID локаций и их PascalCase-именами.
        /// Вызывается один раз при старте мода.
        /// </summary>
        /// <param name="locations">Dictionary из Locations.GetDictionary() — ключ: PascalName</param>
        public static void Initialize(Dictionary<string, SPTarkov.Server.Core.Models.Eft.Common.Location> locations)
        {
            IdToPascalName.Clear();

            foreach (var kvp in locations)
            {
                string pascalName = kvp.Key;                    
                string locationId = kvp.Value?.Base?.IdField;  

                if (!string.IsNullOrEmpty(locationId))
                {
                    IdToPascalName[locationId] = pascalName;
                }
            }
        }

        /// <summary>
        /// Попробовать получить PascalName по ID локации.
        /// </summary>
        public static bool TryGetPascalName(string locationId, out string pascalName)
        {
            return IdToPascalName.TryGetValue(locationId, out pascalName!);
        }

        /// <summary>
        /// Получить PascalName по ID. Выбрасывает исключение, если не найдено.
        /// </summary>
        public static string GetPascalName(string locationId)
        {
            if (TryGetPascalName(locationId, out var name))
                return name;
            throw new KeyNotFoundException($"Location with ID '{locationId}' not found.");
        }

        /// <summary>
        /// Проверяет, разрешена ли локация в конфиге.
        /// Использует PascalName напрямую (например: "Woods", а не "woods").
        /// </summary>
        public static bool IsAllowed(string locationId, QuestConfig config)
        {
            if (!TryGetPascalName(locationId, out var pascalName))
                return false;

            // 🔑 Используем PascalName напрямую
            return config.QuestGeneration.AllowedLocations.GetValueOrDefault(pascalName, false);
        }

        /// <summary>
        /// Возвращает список разрешённых локаций: (PascalName, LocationId)
        /// </summary>
        public static IEnumerable<(string PascalName, string LocationId)> GetAllowedLocations(QuestConfig config)
        {
            foreach (var kvp in IdToPascalName)
            {
                string locationId = kvp.Key;
                string pascalName = kvp.Value;
                if (IsAllowed(locationId, config))
                {
                    yield return (pascalName, locationId);
                }
            }
        }
    }
}