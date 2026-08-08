using System;
using System.Collections.Generic;
using Buildings;
using Controllers;
using HarmonyLib;
using Helpers;

namespace BA.src.patch;

[HarmonyPatch]
internal class VehiclePatch
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
            || !Plugin.VEHICLE_TRUCKS_DEALERSHIP_LAYOUT_ID.Equals(buildingRegistration.Layout.ToLower()))
        {
            Plugin.Logger.LogDebug($"SetListOfVehiclesForSale: BuildingRegistration doesn't match {Plugin.VEHICLE_TRUCKS_DEALERSHIP_LAYOUT_ID}");
            return;
        }

        foreach (var truckId in Plugin.VEHICLE_MISSING_TRUCK_IDS)
        {
            Plugin.Logger.LogDebug($"SetListOfVehiclesForSale: TryAddVehicleByVehicleType truckId={truckId}");
            TryAddVehicleByVehicleType(__instance, truckId);
        }

        var vehicles = Traverse.Create(__instance).Field("_vehicles").GetValue() as List<ContractVehicleForSale>;
        vehicles.ForEach(vehicle =>
        {
            Plugin.Logger.LogDebug($"SetListOfVehiclesForSale: vehicle={vehicle.VehicleName}");
        });

    }

    [HarmonyPatch(typeof(VehicleDeformationController), "OnCollisionEnter")]
    [HarmonyPrefix]
    static bool OnVehicleDeformationControllerCollisionEnter()
    {
        Plugin.Logger.LogDebug($"OnVehicleDeformationControllerCollisionEnter");
        return false;
    }

    [HarmonyPatch(typeof(CarController), "OnVehicleCollision", [])]
    [HarmonyPrefix]
    static bool OnCarControllerVehicleCollision(ref CarController __instance)
    {
        Plugin.Logger.LogDebug($"OnCarControllerVehicleCollision");
        __instance.Repair();
        return false;
    }


}