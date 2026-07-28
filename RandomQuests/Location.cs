//Location.cs

using QuestFilterMod.RandomQuests.Models;
using System.Collections.Frozen;

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

        private static readonly FrozenDictionary<string, string> _pascalToJson = new Dictionary<string, string>
        {
            { "Bigmap", "bigmap" },
            { "Develop", "develop" },
            { "Factory4Day", "factory4_day" },
            { "Factory4Night", "factory4_night" },
            { "Interchange", "Interchange" },
            { "Laboratory", "Laboratory" },
            { "Lighthouse", "Lighthouse" },
            { "RezervBase", "RezervBase" },
            { "Shoreline", "Shoreline" },
            { "TarkovStreets", "TarkovStreets" },
            { "Labyrinth", "Labyrinth" },
            { "Woods", "Woods" },
            { "Sandbox", "Sandbox" },
            { "SandboxHigh", "Sandbox_high" }
        }.ToFrozenDictionary();

        private static FrozenDictionary<string, string> PascalToInverse => _pascalToJson.ToFrozenDictionary(kv => kv.Value, kv => kv.Key);

        public static readonly Dictionary<string, string> IdToPascalName = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Initializes the internal mapping between location IDs (from EFT data) and their corresponding PascalCase names.
        /// This method should be invoked exactly once during the mod's initialization phase (e.g., in the OnLoad event).
        /// </summary>
        /// <param name="locations">
        /// A dictionary retrieved from <see cref="SPTarkov.Server.Core.Models.Eft.Common.Location.GetDictionary()"/>.
        /// The keys represent the PascalCase names of the locations (e.g., "Woods", "Factory4Day"),
        /// and the values are the Location objects containing metadata, including the Base ID.
        /// </param>
        /// <remarks>
        /// This method clears any previously stored mapping data before repopulating it. 
        /// Repeated calls to this method are unnecessary and may cause performance overhead.
        /// </remarks>
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
        /// Attempts to retrieve the PascalCase name of a location using its unique ID.
        /// </summary>
        /// <param name="locationId">
        /// The unique identifier (GUID) of the location as defined in the game data (e.g., "5704e3c2d2720bac5b8b4567").
        /// </param>
        /// <param name="pascalName">
        /// When this method returns, contains the PascalCase name of the location (e.g., "Woods", "Lighthouse") 
        /// if the specified ID was found in the internal map; otherwise, it contains <c>null</c>.
        /// This parameter is passed uninitialized.
        /// </param>
        /// <returns>
        /// <c>true</c> if the location ID is successfully found and mapped to a name; otherwise, <c>false</c>.
        /// </returns>
        /// <example>
        /// <code>
        /// if (Location.TryGetPascalName("5704e3c2d2720bac5b8b4567", out string name))
        /// {
        ///     Console.WriteLine($"Location ID found: {name}");
        /// }
        /// </code>
        /// </example>
        public static bool TryGetPascalName(string locationId, out string pascalName)
        {
            return IdToPascalName.TryGetValue(locationId, out pascalName!);
        }

        /// <summary>
        /// Retrieves the PascalCase name of a location by its unique ID.
        /// </summary>
        /// <param name="locationId">
        /// The unique identifier (GUID) of the location.
        /// </param>
        /// <returns>
        /// The PascalCase name of the location (e.g., "Woods").
        /// </returns>
        /// <exception cref="KeyNotFoundException">
        /// Thrown if the specified <paramref name="locationId"/> is not found in the internal mapping.
        /// </exception>
        public static string GetPascalName(string locationId)
        {
            if (TryGetPascalName(locationId, out var name))
                return name;
            throw new KeyNotFoundException($"Location with ID '{locationId}' not found.");
        }

        /// <summary>
        /// Checks whether the specified location is allowed for quest generation according to the current configuration.
        /// </summary>
        /// <param name="locationId">
        /// The unique identifier (GUID) of the location.
        /// </param>
        /// <param name="config">
        /// The current <see cref="QuestConfig"/> instance containing the list of allowed locations.
        /// </param>
        /// <returns>
        /// <c>true</c> if the location's PascalCase name is present in the configuration's allowed list; otherwise, <c>false</c>.
        /// </returns>
        /// <remarks>
        /// This method first resolves the <paramref name="locationId"/> to its PascalCase name using <see cref="TryGetPascalName(string, out string)"/>.
        /// It then checks if this name exists in <paramref name="config"/>.
        /// </remarks>
        public static bool IsAllowed(string locationId, QuestConfig config)
        {
            if (!TryGetPascalName(locationId, out var pascalName))
                return false;

            return config.QuestGeneration.AllowedLocations.GetValueOrDefault(pascalName, false);
        }

        /// <summary>
        /// Converts a PascalCase location name to its corresponding JSON configuration key.
        /// </summary>
        /// <param name="pascalName">
        /// The PascalCase name of the location (e.g., "Factory4Day", "Woods").
        /// </param>
        /// <returns>
        /// The JSON key associated with the location (e.g., "factory4_day", "woods").
        /// Returns <c>null</c> if the PascalCase name is not found in the predefined mappings.
        /// </returns>
        /// <remarks>
        /// This mapping is hardcoded and primarily used for configuring quest generation rules via JSON.
        /// Not all locations have specific JSON keys (some map to themselves).
        /// </remarks>
        public static string GetJsonKey(string pascalName)
        {
            return _pascalToJson.GetValueOrDefault(pascalName);
        }

        /// <summary>
        /// Converts a JSON configuration key back to its corresponding PascalCase location name.
        /// </summary>
        /// <param name="jsonKey">
        /// The JSON key representing the location (e.g., "factory4_day", "woods").
        /// </param>
        /// <returns>
        /// The PascalCase name of the location (e.g., "Factory4Day", "Woods").
        /// Returns <c>null</c> if the JSON key is not found in the predefined mappings.
        /// </returns>
        /// <remarks>
        /// This is the inverse operation of <see cref="GetJsonKey(string)"/>.
        /// </remarks>
        public static string GetPascalNameFromJsonKey(string jsonKey)
        {
            return PascalToInverse.GetValueOrDefault(jsonKey);
        }

        /// <summary>
        /// Enumerates all locations that are currently allowed for quest generation based on the provided configuration.
        /// </summary>
        /// <param name="config">
        /// The <see cref="QuestConfig"/> instance used to determine which locations are permitted.
        /// </param>
        /// <returns>
        /// An enumerable sequence of tuples, where each tuple contains:
        /// 1. <c>PascalName</c>: The PascalCase name of the location (e.g., "Woods").
        /// 2. <c>LocationId</c>: The unique ID of the location (e.g., "5704e3c2d2720bac5b8b4567").
        /// </returns>
        /// <remarks>
        /// This method performs a lazy evaluation (using <c>yield return</c>), making it efficient for use in loops.
        /// It internally calls <see cref="IsAllowed(string, QuestConfig)"/> for each known location.
        /// </remarks>
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