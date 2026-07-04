//Combo.cs

using QuestFilterMod.RandomQuests.Models;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

#if DEBUG
/*
 * 1. Добавить как настройку условия *выжить* с локации с определенным статусом.
 * 2. Использовать "ConditionLocation" и "ConditionSurvivedExit" как независимые условия.
 * 3. К настройке нужно добавить , нужно ли игроку такое условие выйти с локации со статусом или нет.
 * 4. ExitName ("exitName": "Sandbox_VExit") - условие для выхода с локации, с определенной точки.
 */
#endif


namespace QuestFilterMod.RandomQuests
{
    public partial class Generator
    {
        private Quest GenerateComboQuest()
        {      
            if (!ConfigRandom.QuestGeneration.Types.Combo) 
                return null;

            var cfg = ConfigRandom.ComboQuest;
            if (cfg.Type == null || cfg.Type.Length == 0)
                return null;

            var n = _random.Next(cfg.Conditions[0], cfg.Conditions[1] + 1);
            if (n <= 0) return null;

            string locId = null;
            if (cfg.Location)
            {
                locId = GetRandomAllowedLocationId();
            }

            return GenerateBaseQuest("Multi", (q, idFactory) =>
            {

                q.Location = cfg.Location
                    ? locId
                    : "any";
                q.Type = SPTarkov.Server.Core.Models.Enums.QuestTypeEnum.Multi;

                string pascalName = null;
                var conditions = new List<QuestCondition>();

                for (int i = 0; i < n; i++)
                {
                    var type = cfg.Type[_random.Next(cfg.Type.Length)];
                    var currentLocId = cfg.Location ? locId! : GetRandomAllowedLocationId();

                    switch (type)
                    { 
                        case "Exploration":
                            var explLocId = cfg.Location ? locId! : GetRandomAllowedLocationId();
                            if (string.IsNullOrEmpty(explLocId)) continue;

                            var allowed = Location.GetAllowedLocations(ConfigRandom).ToList();
                            pascalName = allowed.First(x => x.LocationId == currentLocId).PascalName;

                            var targets = GetExplorationTargets(explLocId);
                            if (!targets.Any()) continue;

                            var target = targets[_random.Next(targets.Count)];

                            conditions.Add(CounterCreator(
                                idFactory,
                                "Exploration",
                                1,
                                false,false,
                                ConditionVisitPlace(target, pascalName, idFactory)
                                ));


                            break;

                        case "Kills":
                            var killConfig = ConfigRandom.KillQuest;
                            var killLocId = cfg.Location ? locId! : GetRandomAllowedLocationId();
                            if (string.IsNullOrEmpty(killLocId) || !Location.TryGetPascalName(killLocId, out pascalName))
                                continue;

                            var botType = killConfig?.Target?.RandomItem(_random) ?? "PmcBot";
                            var randomKill = _random.Next(killConfig?.MinKills ?? 5, (killConfig?.MaxKills ?? 10) + 1);

                            string weaponId = null;
                            if (killConfig?.Weapons?.Enable == true && killConfig.Weapons.Ids.Count > 0)
                            {
                                weaponId = killConfig.Weapons.Ids[_random.Next(killConfig.Weapons.Ids.Count)];
                            }

                            if (ConfigRandom.KillQuest.Weapons.Started && !string.IsNullOrEmpty(weaponId))
                            {
                                AddQuestStartedItemReward(q, weaponId, 1, idFactory);
                            }

                            conditions.Add(CounterCreator(
                                idFactory,
                                "Elimination",
                                randomKill,
                                false, false,
                                ConditionKillEnemy(botType, pascalName, idFactory, killConfig, weaponId),
                                ConditionLocation(pascalName, idFactory)
                                ));
                            break;

                        case "Delivery":
                        case "Beacon":
                            var isBeacon = type == "Beacon";
                            var deployConfig = isBeacon ? ConfigRandom.BeaconQuest : ConfigRandom.DeliveryQuest;
                            var deployConditionType = isBeacon ? "PlaceBeacon" : "LeaveItemAtLocation";
                            var deployLocId = cfg.Location ? locId : GetRandomAllowedLocationId();
                            var deployTarget = GetDeployTarget(deployConfig, deployLocId);
                            if (string.IsNullOrEmpty(deployLocId) || !Location.TryGetPascalName(deployLocId, out pascalName))
                                continue;
                            if (deployTarget.HasValue)
                            {
                                var (itemTpl, targetPoint, plantTime) = deployTarget.Value;

                                var key = new QuestKey(deployLocId, targetPoint, "", type);
                                if (!_tracker.TryUse(key))
                                    continue;

                                AddQuestStartedItemReward(q, itemTpl, 1, idFactory);
                                conditions.Add(ConditionDeployItem(itemTpl, targetPoint, plantTime, deployConditionType, pascalName, idFactory));
                            }

                            break;

                        case "Transfer":
                            var transferConfig = ConfigRandom.TransferQuest;
                            if (!transferConfig.ItemIds.Any()) break;

                            var itemId = transferConfig.ItemIds[_random.Next(transferConfig.ItemIds.Count)];
                            var count = transferConfig.ItemCount.Length > 1
                                ? _random.Next(transferConfig.ItemCount[0], transferConfig.ItemCount[1] + 1)
                                : transferConfig.ItemCount[0];

                            conditions.Add(ConditionHandoverItem(itemId, count, () => idFactory().ToString(), _random));
                            break;
                    }
                }

                if (conditions.Count != 0)
                    q.Conditions.AvailableForFinish = conditions;

            });
        }

        private (string itemTpl, string targetPoint, int plantTime)? GetDeployTarget(DeployQuestConfig config, string fixedLocId)
        {
            if (!config.Locations.Any()) return null;

            var allowed = Location.GetAllowedLocations(ConfigRandom).ToList();
            if (!allowed.Any()) return null;

            var targetLocId = string.IsNullOrEmpty(fixedLocId) ? GetRandomAllowedLocationId() : fixedLocId;
            if (string.IsNullOrEmpty(targetLocId)) return null;

            foreach (var (pascalName, locationId) in allowed)
            {
                if (config.Locations.TryGetValue(pascalName, out var locConfig) && locationId == targetLocId)
                {
                    var targetPoint = locConfig.Targets.Where(t => !string.IsNullOrEmpty(t)).OrderBy(_ => _random.Next()).First();
                    var itemTpl = GetRandomDeployItem(config);
                    return (itemTpl, targetPoint, config.PlantTime);
                }
            }

            return null;
        }

        private string GetRandomDeployItem(DeployQuestConfig config)
        {
            var itemTplList = config.ItemPlant.Any()
                ? config.ItemPlant
                : new List<string> { GetRandomSpecialItem()?.ToString() ?? "" };

            var validItems = itemTplList.Where(t => !string.IsNullOrEmpty(t)).ToList();
            return validItems.Any() ? validItems.OrderBy(_ => _random.Next()).First() : "";
        }

        private string GetRandomAllowedLocationId()
        {
            var locs = Location.GetAllowedLocations(ConfigRandom).ToList();
            return locs.Any() ? locs.OrderBy(_ => _random.Next()).First().LocationId : "";
        }

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
    }
}
