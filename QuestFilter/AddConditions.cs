//AddConditions.cs

using QuestFilterMod.RandomQuests;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;


#if DEBUG
/*
 * 
 */

#endif
namespace QuestFilterMod.QuestFilter
{
    public partial class FilterService
    {

        private List<string> GetExplorationTargets(string locId)
        {
            var allowed = Location.GetAllowedLocations(ConfigRandom).ToList();
            foreach (var (pascalName, locationId) in allowed)
            {
                if (locationId == locId && ConfigRandom.ExplorationQuest.TryGetValue(pascalName, out var config))
                {
                    return config.Targets.Where(t => !string.IsNullOrEmpty(t)).ToList();
                }
            }
            return new();
        }

        private QuestCondition GenerateRandomFinishCondition(Random random, Func<MongoId> idFactory)
        {
            try
            {
                return GenerateRandomFinishConditionImpl(random, idFactory);
            }
            catch (Exception ex)
            {
                _logger.Error($"[QuestFilterMod][AddConditions] ❌ ERROR in GenerateRandomFinishCondition:");
                _logger.Error($"[QuestFilterMod][AddConditions] Message: {ex.Message}");
                _logger.Error($"[QuestFilterMod][AddConditions] Stack: {ex.StackTrace}");
                return null;
            }
        }

        private QuestCondition GenerateRandomFinishConditionImpl(Random random, Func<MongoId> idFactory)
        {
            if (!Location.IdToPascalName.Any())
            {
                var locations = _databaseService.GetLocations()?.GetDictionary();
                if (locations != null)
                {
                    Location.Initialize(locations);
#if DEBUG
                    if (Plugin.Config.Debug)
                        _logger.Warning($"[QuestFilterMod][AddConditions] ✅ Location initialized: {Location.IdToPascalName.Count} IDs");
#endif
                }
            }

            var allowed = Location.GetAllowedLocations(ConfigRandom).ToList();
            if (!allowed.Any())
            {
#if DEBUG
                _logger.Error("[QuestFilterMod][AddConditions] No allowed locations found.");
#endif
                return null;
            }

            var randomLoc = allowed.OrderBy(_ => random.Next()).First();
            var pascalName = randomLoc.PascalName;

            var comboConfig = Plugin.Config.ModifyBaseQuest;
            if (comboConfig?.Type == null || comboConfig.Type.Length == 0)
                return null;

            var type = comboConfig.Type[random.Next(comboConfig.Type.Length)];

            switch (type)
            {
                case "Exploration":
                    var targets = GetExplorationTargets(randomLoc.LocationId);
                    if (!targets.Any()) return null;

                    var target = targets[random.Next(targets.Count)];

                    return Generator.CounterCreator(
                        idFactory,
                        "Exploration",
                        1,0,
                        true, false,
                        Generator.ConditionVisitPlace(target, pascalName, idFactory)
                    );

                case "Kills":
                    var killConfig = ConfigRandom.KillQuest;
                    var botType = killConfig?.Target?.RandomItem(random) ?? "PmcBot";
                    var randomKill = random.Next(killConfig?.MinKills ?? 5, (killConfig?.MaxKills ?? 10) + 1);
                    string weaponId = null;
                    if (killConfig?.Weapons?.Enable == true && killConfig.Weapons.Ids.Count > 0)
                    {
                        weaponId = killConfig.Weapons.Ids[_random.Next(killConfig.Weapons.Ids.Count)];
                    }

                    return Generator.CounterCreator(
                        idFactory,
                        "Elimination",
                        randomKill,0,
                        true, false,
                        Generator.ConditionKillEnemy(botType, pascalName, idFactory, killConfig, weaponId),
                        Generator.ConditionLocation(pascalName, idFactory)
                    );
                     
                case "Delivery":
                case "Beacon":
                    var isBeacon = type == "Beacon";
                    var deployConfig = isBeacon ? ConfigRandom.BeaconQuest : ConfigRandom.DeliveryQuest;

                    if (!deployConfig.Locations.ContainsKey(pascalName)) return null;

                    var locConfig = deployConfig.Locations[pascalName];
                    var targetPoints = locConfig.Targets.Where(t => !string.IsNullOrEmpty(t)).ToList();
                    if (!targetPoints.Any()) return null;

                    var targetPoint = targetPoints[random.Next(targetPoints.Count)];

                    if (!deployConfig.ItemPlant.Any()) return null;
                    var itemTpl = deployConfig.ItemPlant[random.Next(deployConfig.ItemPlant.Count)];
                    if (string.IsNullOrEmpty(itemTpl)) return null;


                    return Generator.ConditionDeployItem(itemTpl, targetPoint, deployConfig.PlantTime, 0, isBeacon ? "PlaceBeacon" : "LeaveItemAtLocation", pascalName, idFactory);

                case "Transfer":
                    var transferConfig = ConfigRandom.TransferQuest;
                    if (!transferConfig.ItemIds.Any()) return null;

                    var itemId = transferConfig.ItemIds[random.Next(transferConfig.ItemIds.Count)];
                    var count = transferConfig.ItemCount.Length > 1
                        ? random.Next(transferConfig.ItemCount[0], transferConfig.ItemCount[1] + 1)
                        : transferConfig.ItemCount[0];

                    return Generator.ConditionHandoverItem(itemId, count, 0, () => idFactory().ToString(), _random);

                default:
                    return null;
            }
        }

        private void AddConditionLocales(Dictionary<string, Dictionary<string, string>> locales)
        {
            var global = _databaseService.GetLocales().Global;

            foreach (var (lang, entries) in locales)
            {
                if (entries.Count == 0) continue;
                if (!global.TryGetValue(lang, out var lazy)) continue;

                lazy.AddTransformer(localeData =>
                {
                    if (localeData == null) return null;

                    foreach (var (key, value) in entries)
                    {
                        localeData.TryAdd(key, value);
                    }

                    return localeData;
                });
            }
        }

    }
}
