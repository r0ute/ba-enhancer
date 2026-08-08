using System;
using System.Collections.Generic;
using System.Linq;
using Entities;
using HarmonyLib;
using Helpers;
using UI.Smartphone.Apps.Contacts;

namespace BA.src.patch.biz;

[HarmonyPatch]
internal class BuildingAvailabilityNotificationPatch
{
    private static readonly Dictionary<Address, bool> previousAvailability = new();

    [HarmonyPatch(typeof(BuildingManager), "Awake")]
    [HarmonyPostfix]
    static void OnBuildingManagerAwake()
    {
        GlobalEvents.onNewDay = (Action)Delegate.Combine(
            GlobalEvents.onNewDay,
            CheckBestBuildingsAvailability
        );
    }

    private static void CheckBestBuildingsAvailability()
    {
        Plugin.bestBuildings.Keys
            .Select(BuildingHelper.GetBuildingRegistration)
            .Where(r => r != null)
            .Where(r => !r.BuildingOwnedByPlayer && !r.RentedByPlayer)
            .ToList()
            .ForEach(r =>
            {
                if (r.AvailableForRent &&
                    previousAvailability.TryGetValue(r.Address, out bool wasAvailable) &&
                    !wasAvailable)
                {
                    SendAvailableNotification(r);
                }

                previousAvailability[r.Address] = r.AvailableForRent;
            });
    }

    private static void SendAvailableNotification(BuildingRegistration buildingRegistration)
    {
        Plugin.Logger.LogDebug($"OnBuildingManagerAwake: address={buildingRegistration.Address}, "
            + $"neighborhood={buildingRegistration.Neighborhood}, "
            + $"type={buildingRegistration.BuildingCached.BuildingType}, "
            + $"customerCapacity={buildingRegistration.BuildingCached.GetCustomerCapacity}, "
            + $"trafficIndex={buildingRegistration.BuildingCached.trafficIndex}, "
            + $"availableForRent={buildingRegistration.AvailableForRent}");

        var contact = Contact.GetContact("Market Insider", ContactCategoryName.General, "Special");
        GameManager.SendTextMessage(contact, "ba:messagetype_contacts_message_not_implemented", new Dictionary<string, string>
            {
                {
                    "businessType",
                    $"{buildingRegistration.Neighborhood} {buildingRegistration.Address} " +
                    $"{buildingRegistration.BuildingCached.BuildingType} " +
                    $"capacity={buildingRegistration.BuildingCached.GetCustomerCapacity} " +
                    $"trafficIndex={buildingRegistration.BuildingCached.trafficIndex} " +
                    $"size={buildingRegistration.BuildingCached.BuildingSize} " +
                    $"{buildingRegistration.BuildingCached.BuildingVersion}"
                }
            }
        );
    }
}