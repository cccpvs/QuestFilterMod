using System.Collections.Generic;
using System.Linq;

namespace QuestFilterMod.QuestFilter;

public static class LocationMapper
{
    public static readonly Dictionary<string, string> IdToName = new()
    {
        { "56f40101d2720b2a4d8b45d6", "bigmap" },
        { "5704e3c2d2720bac5b8b4567", "woods" },
        { "5704e554d2720bac5b8b456e", "shoreline" },
        { "5714dbc024597771384a510d", "interchange" },
        { "5b0fc42d86f7744a585f9105", "laboratory" },
        { "5714dc692459777137212e12", "tarkovstreets" },
        { "5704e5fad2720bc05b8b4567", "rezervbase" },
        { "5704e4dad2720bb55b8b4567", "lighthouse" },
        { "55f2d3fd4bdc2d5f408b4567", "factory4_day" },
        { "6733700029c367a3d40b02af", "labyrinth" },
        { "653e6760052c01c1c805532f", "sandbox" },
        { "65b8d6f5cdde2479cb2a3125", "sandbox_high" },
        { "59fc81d786f774390775787e", "factory4_night" }
    };

    // Обратный словарь: имя → ID
    public static readonly Dictionary<string, string> NameToId = IdToName
        .ToDictionary(kvp => kvp.Value, kvp => kvp.Key);

    /// <summary>
    /// Получить имя локации по её ID.
    /// </summary>
    public static string GetLocationName(string locationId)
    {
        return !string.IsNullOrEmpty(locationId) && IdToName.TryGetValue(locationId, out var name)
            ? name
            : "any";
    }

    /// <summary>
    /// Получить ID локации по её имени (например, из конфига).
    /// </summary>
    public static bool TryGetLocationId(string locationName, out string locationId)
    {
        if (string.IsNullOrEmpty(locationName))
        {
            locationId = null!;
            return false;
        }

        return NameToId.TryGetValue(locationName.ToLowerInvariant(), out locationId);
    }

    /// <summary>
    /// Проверить, существует ли такая локация (по ID).
    /// </summary>
    public static bool IsValidLocationId(string locationId)
    {
        return !string.IsNullOrEmpty(locationId) && IdToName.ContainsKey(locationId);
    }

    /// <summary>
    /// Проверить, существует ли такая локация (по имени).
    /// </summary>
    public static bool IsValidLocationName(string locationName)
    {
        return !string.IsNullOrEmpty(locationName) && NameToId.ContainsKey(locationName.ToLowerInvariant());
    }

    /// <summary>
    /// Получить все поддерживаемые имена локаций.
    /// </summary>
    public static IEnumerable<string> GetAllLocationNames() => NameToId.Keys;
}