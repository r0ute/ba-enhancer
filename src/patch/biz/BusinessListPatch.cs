using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UI.Smartphone.Apps.BizMan;

namespace BA.src.patch.biz;

internal class BusinessListPatch
{
    [HarmonyPatch(typeof(BusinessScrollerController), "PopulateAllModels")]
    [HarmonyPrefix]
    static bool OnBusinessScrollerControllerPopulateAllModels(List<BusinessCellView.BusinessModel> allModels)
    {
        Plugin.Logger.LogDebug($"OnBusinessScrollerControllerPopulateAllModels: buildingRegistrationCount={SaveGameManager.Current.BuildingRegistrations.Count}");
        SaveGameManager.Current.BuildingRegistrations
            .Where(buildingRegistration => (buildingRegistration.RentedByPlayer && IsVisibleBusiness(buildingRegistration))
                || buildingRegistration.BuildingOwnedByPlayer)
            .OrderByDescending(GetBuildingCategory)
            .ThenBy(buildingRegistration => buildingRegistration.businessTypeName)
            .ThenBy(buildingRegistration => buildingRegistration.GetDisplayName())
            .ToList()
            .ForEach(buildingRegistration =>
            {
                allModels.Add(buildingRegistration.RentedByPlayer && IsVisibleBusiness(buildingRegistration)
                        ? new BusinessCellView.BusinessModel(buildingRegistration)
                        : new BusinessCellView.BusinessModel(buildingRegistration, isRealEstate: true)
                );
            });

        return false;
    }

    [HarmonyReversePatch]
    [HarmonyPatch(typeof(BusinessScrollerController), "IsVisibleBusiness")]
    static bool IsVisibleBusiness(BuildingRegistration registration) => throw new NotImplementedException();

    private static int GetBuildingCategory(BuildingRegistration buildingRegistration)
    {
        if (buildingRegistration.RentedByPlayer
            && buildingRegistration.HasEstablishedBusiness
            && buildingRegistration.businessTypeName != "ba:businesstype_factory"
            && buildingRegistration.businessTypeName != "ba:businesstype_warehouse")
        {
            return 1;
        }
        else if (buildingRegistration.BuildingOwnedByPlayer)
        {
            return 0;
        }
        else
        {
            return -1;
        }
    }
}