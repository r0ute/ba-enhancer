using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using Buildings;
using Controllers;
using HarmonyLib;
using Helpers;

namespace BA.src;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{

    internal static string TRUCKS_DEALERSHIP_LAYOUT_ID = "industrycitycardealershiptrucks";
    internal static List<string> MISSING_TRUCK_IDS = [
        "ba:vehicletype_umcdesert"
    ];

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

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(VehicleContractSettings), "TryAddVehicleByVehicleType")]
        static void TryAddVehicleByVehicleType(object instance, string vehicleTypeName, ShowcaseVehicleController showcaseVehicle = null) => throw new NotImplementedException();

        [HarmonyPatch(typeof(VehicleContractSettings), "SetListOfVehiclesForSale")]
        [HarmonyPostfix]
        static void OnSetListOfVehiclesForSale(ref VehicleContractSettings __instance)
        {
            Address address = DialogController.current.contact.Address;
            BuildingRegistration buildingRegistration = BuildingHelper.GetBuildingRegistration(address);
            if (buildingRegistration == null
                || string.IsNullOrEmpty(buildingRegistration.businessTypeName)
                || string.IsNullOrEmpty(buildingRegistration.Layout)
                || !TRUCKS_DEALERSHIP_LAYOUT_ID.Equals(buildingRegistration.Layout.ToLower()))
            {
                Logger.LogDebug($"SetListOfVehiclesForSale: BuildingRegistration doesn't match {TRUCKS_DEALERSHIP_LAYOUT_ID}");
                return;
            }

            foreach (var truckId in MISSING_TRUCK_IDS)
            {
                Logger.LogInfo($"SetListOfVehiclesForSale: TryAddVehicleByVehicleType truckId={truckId}");
                TryAddVehicleByVehicleType(__instance, truckId);
            }

            var vehicles = Traverse.Create(__instance).Field("_vehicles").GetValue() as List<ContractVehicleForSale>;
            vehicles.ForEach(vehicle =>
            {
                Logger.LogDebug($"SetListOfVehiclesForSale: vehicle={vehicle.VehicleName}");
            });

        }

    }

}
