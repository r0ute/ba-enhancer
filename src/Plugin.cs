using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using BusinessLayoutSets;
using Entities;
using HarmonyLib;

namespace BA.src;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{

    internal static new ManualLogSource Logger;

    private void Awake()
    {
        // Plugin startup logic
        Logger = base.Logger;


        Harmony harmony = new(MyPluginInfo.PLUGIN_GUID);
        harmony.PatchAll(typeof(Patches));

        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
    }


    class Patches
    {

        [HarmonyPatch(typeof(WorkoutVarietyCustomerDemand), nameof(WorkoutVarietyCustomerDemand.Fulfilled))]
        [HarmonyPostfix]
        static void OnWorkoutVarietyCustomerDemandFulfilled(BuildingRegistration registration, HashSet<Item> items, ref bool __result)
        {
        }

    }

}
