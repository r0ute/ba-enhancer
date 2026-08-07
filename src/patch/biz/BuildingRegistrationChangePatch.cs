using System;
using System.Collections.Generic;
using Entities;
using HarmonyLib;
using Helpers;
using UI.Smartphone.Apps.Contacts;

namespace BA.src.patch.biz;

internal class BuildingRegistrationChangePatch
{

    [HarmonyPatch(typeof(BuildingManager), "Awake")]
    [HarmonyPostfix]
    static void OnBuildingManagerAwake()
    {
        GlobalEvents.onBuildingRegistrationChange = (Action<Address>)Delegate.Combine(GlobalEvents.onBuildingRegistrationChange, delegate (Address address)
        {

            var buildingRegistration = BuildingHelper.GetBuildingRegistration(address);

            Plugin.Logger.LogDebug($"OnBuildingManagerAwake: address={address}, "
                + $"neighborhood={buildingRegistration.Neighborhood}, "
                + $"type={buildingRegistration.BuildingCached.BuildingType}, "
                + $"customerCapacity={buildingRegistration.BuildingCached.GetCustomerCapacity}, "
                + $"trafficIndex={buildingRegistration.BuildingCached.trafficIndex}, "
                + $"availableForRent={buildingRegistration.AvailableForRent}");

            if (!buildingRegistration.BuildingOwnedByPlayer && !buildingRegistration.RentedByPlayer && Plugin.bestBuildings.Contains(address))
            {
                var contact = Contact.GetContact("market_insider", ContactCategoryName.General, "Market Insider");
                GameManager.SendTextMessage(contact, "ba:messagetype_contacts_message_not_implemented",
                    new Dictionary<string, string> { { "businessType", $"{buildingRegistration.Neighborhood} {address} "
                        + $"{buildingRegistration.BuildingCached.BuildingType} "
                        + $"capacity={buildingRegistration.BuildingCached.GetCustomerCapacity} "
                        + $"trafficIndex={buildingRegistration.BuildingCached.trafficIndex}"
                        + $"size={buildingRegistration.BuildingCached.BuildingSize} {buildingRegistration.BuildingCached.BuildingVersion}"}
                });
            }
        });
    }
}