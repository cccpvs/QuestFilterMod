//Patch.cs

using HarmonyLib;
using SPTarkov.Server.Core.Controllers;
using QuestFilterMod.RepeatableQuestCleaner;

namespace QuestFilterMod.Patch
{
    [HarmonyPatch(typeof(RepeatableQuestController), nameof(RepeatableQuestController.GetClientRepeatableQuests))]
    public class Patch
    {
        private static Clear _cleaner;

        public static void Setup(Clear cleaner) => _cleaner = cleaner;

        public static bool Prefix(ref global::System.Collections.Generic.List<SPTarkov.Server.Core.Models.Eft.Common.Tables.PmcDataRepeatableQuest> __result)
        {
            __result = new();
            return false;
        }
    }
}