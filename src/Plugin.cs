using System;
using System.Collections.Generic;
using System.Linq;
using AI.Citizens;
using BepInEx;
using BepInEx.Logging;
using BigAmbitions.Rivals;
using Buildings;
using Controllers;
using Entities;
using HarmonyLib;
using Helpers;
using UI.Smartphone.Apps.BizMan.PurchasingAgent;
using UI.Smartphone.Apps.Contacts;
using UnityEngine;
using UnityEngine.UIElements.Collections;

namespace BA.src;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{

    internal static readonly string VEHICLE_TRUCKS_DEALERSHIP_LAYOUT_ID = "industrycitycardealershiptrucks";
    internal static readonly List<string> VEHICLE_MISSING_TRUCK_IDS = [
        "ba:vehicletype_umcdesert"
    ];

    internal static readonly List<(string buildingType, int searchLimit)> BIZ_SEARCH_BUILDING_TYPES = [
        ("ba:buildingtype_cinema", 1),
        ("ba:buildingtype_theater", 1),
        ("ba:buildingtype_office", 6),
        ("ba:buildingtype_retail", 14)
    ];

    internal static new ManualLogSource Logger;

    internal static Dictionary<string, int> neighborhoodMinTrafficFor100Promotion = [];

    internal static HashSet<Address> foundAddresses = [];

    private void Awake()
    {
        // Plugin startup logic
        Logger = base.Logger;

        Harmony harmony = new(MyPluginInfo.PLUGIN_GUID);
        harmony.PatchAll(typeof(GameManagerPatches));
        harmony.PatchAll(typeof(VehiclePatches));
        harmony.PatchAll(typeof(BizPatches));

        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
    }

    class GameManagerPatches
    {
        [HarmonyPatch(typeof(GameManager), "Awake")]
        [HarmonyPostfix]
        static void OnGameManagerAwake()
        {
            NeighborhoodHelper.NeighborhoodsData
                .ToList()
                .ForEach(neighborhoodData =>
                {
                    SocialClass dominantSocialClass = SocialClass.Working;
                    float maxSocialClassPercentage = neighborhoodData.workingClassPercentage;

                    if (neighborhoodData.middleClassPercentage > maxSocialClassPercentage)
                    {
                        dominantSocialClass = SocialClass.Middle;
                        maxSocialClassPercentage = neighborhoodData.middleClassPercentage;
                    }

                    if (neighborhoodData.upperClassClassPercentage > maxSocialClassPercentage)
                    {
                        dominantSocialClass = SocialClass.Upper;
                        maxSocialClassPercentage = neighborhoodData.upperClassClassPercentage;
                    }

                    var maxAcceptableRelativePrice = CitizenHelper.MaxAcceptableRelativePrice(dominantSocialClass, neighborhoodData.neighbourhood);
                    var minTrafficFor100Promotion = Mathf.RoundToInt(100 * (1 - neighborhoodData.marketingStrength));
                    neighborhoodMinTrafficFor100Promotion.Add(neighborhoodData.neighbourhood, minTrafficFor100Promotion);

                    Logger.LogDebug($"OnGameManagerAwake: neighbourhood={neighborhoodData.neighbourhood}, "
                        + $"dominantSocialClass={dominantSocialClass}, "
                        + $"socialClassPercentage={maxSocialClassPercentage}, "
                        + $"maxAcceptableRelativePrice={maxAcceptableRelativePrice}, "
                        + $"averagePriceIndex={CitizenHelper.averagePriceIndicesInNeighborhoods.Get(neighborhoodData.neighbourhood)}, "
                        + $"marketingStrength={neighborhoodData.marketingStrength}, "
                        + $"customerDemandsWeight={neighborhoodData.customerDemandsWeight}, "
                        + $"minTrafficFor100Promotion={minTrafficFor100Promotion}");
                });

            BusinessTypeHelper.GetAllPlayerAvailableBusinesses()
                .ToList()
                .ForEach(businessType =>
                {
                    Logger.LogDebug($"OnGameManagerAwake: businessType={businessType}");

                    businessType.dayFactorMultipliers.ForEach(dayFactorMultiplier =>
                    {
                        Logger.LogDebug($"OnGameManagerAwake: dayFactorMultiplier: "
                            + $"dayOfWeek={dayFactorMultiplier.dayOfWeekOrdered}, multiplier={dayFactorMultiplier.multiplier}");
                    });

                    businessType.hourlyFactorMultipliers.ForEach(hourlyFactorMultiplier =>
                    {
                        Logger.LogDebug($"OnGameManagerAwake: hourlyFactorMultiplier: "
                            + $"startingHour={hourlyFactorMultiplier.startingHour}, endingHour={hourlyFactorMultiplier.endingHour}, multiplier={hourlyFactorMultiplier.multiplier}");
                    });
                });

            BIZ_SEARCH_BUILDING_TYPES.ForEach(searchBuildings);
        }

        internal static void searchBuildings((string buildingType, int searchLimit) criteria)
        {
            BuildingHelper.AllNeighbourhoodBuildings
                .SelectMany(neighbourhood => neighbourhood.Value
                    .Where(building => !building.SpecialService
                        && criteria.buildingType.Equals(building.BuildingType)
                        && building.trafficIndex >= neighborhoodMinTrafficFor100Promotion.Get(neighbourhood.Key))
                    .Select(building => new
                    {
                        Neighbourhood = neighbourhood.Key,
                        Building = building
                    }))
                .GroupBy(entry => new
                {
                    entry.Neighbourhood,
                    entry.Building.BuildingType
                })
                .SelectMany(group => group
                    .OrderByDescending(entry => entry.Building.GetCustomerCapacity)
                    .ThenByDescending(entry => entry.Building.trafficIndex)
                    .Take(criteria.searchLimit))
                .OrderBy(entry => entry.Neighbourhood)
                .ThenBy(entry => entry.Building.BuildingType)
                .ToList()
                .ForEach(entry =>
                {
                    var building = entry.Building;
                    foundAddresses.Add(entry.Building.Address);
                    Logger.LogDebug($"OnGameManagerAwake: neighborhood={entry.Neighbourhood}, "
                        + $"type={entry.Building.BuildingType}, "
                        + $"customerCapacity={building.GetCustomerCapacity}, "
                        + $"trafficIndex={building.trafficIndex}, "
                        + $"address={building.Address}");
                });
        }
    }

    class VehiclePatches
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
                || !VEHICLE_TRUCKS_DEALERSHIP_LAYOUT_ID.Equals(buildingRegistration.Layout.ToLower()))
            {
                Logger.LogDebug($"SetListOfVehiclesForSale: BuildingRegistration doesn't match {VEHICLE_TRUCKS_DEALERSHIP_LAYOUT_ID}");
                return;
            }

            foreach (var truckId in VEHICLE_MISSING_TRUCK_IDS)
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

    class BizPatches
    {
        [HarmonyPatch(typeof(BizManPresentation), nameof(BizManPresentation.SetAiOwned))]
        [HarmonyPostfix]
        static void OnSetAiOwned(ref BizManPresentation __instance)
        {
            var bizManBusiness = Traverse.Create(__instance).Field("bizManBusiness").GetValue() as BizManBusiness;

            if (bizManBusiness.building.SpecialService == null)
            {
                float minOfferPrice = CompetitionHelper.CalculateAiOwnedValuation(bizManBusiness.buildingRegistration)
                    * RivalsHelper.GetOvertakeBusinessAcceptRate(bizManBusiness.buildingRegistration.businessOwnerRivalId, bizManBusiness.buildingRegistration.Address);
                Logger.LogDebug($"OnSendOvertakeOffer: bizManBusiness={bizManBusiness.buildingRegistration.Address}, minOfferPrice={minOfferPrice}");

                __instance.offerAmountInputField.text = Math.Round(minOfferPrice + 0.01f, 0, MidpointRounding.AwayFromZero).ToString();
            }

        }

        [HarmonyPatch(typeof(BuildingManager), "Awake")]
        [HarmonyPostfix]
        static void OnBuildingManagerAwake()
        {
            GlobalEvents.onBuildingRegistrationChange = (Action<Address>)Delegate.Combine(GlobalEvents.onBuildingRegistrationChange, (Action<Address>)delegate (Address address)
            {

                var buildingRegistration = BuildingHelper.GetBuildingRegistration(address);

                Logger.LogDebug($"OnBuildingManagerAwake: address={address}, "
                    + $"neighborhood={buildingRegistration.Neighborhood}, "
                    + $"type={buildingRegistration.BuildingCached.BuildingType}, "
                    + $"customerCapacity={buildingRegistration.BuildingCached.GetCustomerCapacity}, "
                    + $"trafficIndex={buildingRegistration.BuildingCached.trafficIndex}, "
                    + $"availableForRent={buildingRegistration.AvailableForRent}");

                if (buildingRegistration.AvailableForRent && foundAddresses.Contains(address))
                {
                    var contact = Contact.GetContact("market_insider", ContactCategoryName.General, "Market Insider");
                    GameManager.SendTextMessage(contact, "ba:messagetype_contacts_message_not_implemented",
                        new Dictionary<string, string> { { "businessType", $"{buildingRegistration.Neighborhood} {address} "
                        + $"{buildingRegistration.BuildingCached.BuildingType} "
                        + $"capacity={buildingRegistration.BuildingCached.GetCustomerCapacity} "
                        + $"trafficIndex={buildingRegistration.BuildingCached.trafficIndex}"} });
                }
            });
        }

        [HarmonyPatch(typeof(PurchasingAgentProductsScrollerController), nameof(PurchasingAgentProductsScrollerController.LoadProducts))]
        [HarmonyPostfix]
        static void OnPurchasingAgentProductsScrollerControllerLoadProducts(ref PurchasingAgentProductsScrollerController __instance)
        {
            var productModel = __instance.data.First();

            // TODO: remove smart delivery check later?
            if (!productModel.isTarget || productModel?.productRef?.assignedWarehouse == null)
            {
                Logger.LogWarning($"OnPurchasingAgentProductsScrollerControllerLoadProducts: no assigned warehouse");
                return;
            }

            var warehouse = productModel.warehouses.FirstOrDefault(building => building.Address == productModel.productRef.assignedWarehouse);
            Logger.LogDebug($"OnPurchasingAgentProductsScrollerControllerLoadProducts: warehouse={warehouse}");
            Dictionary<string, int> itemsToPurchase;

            if (warehouse.businessTypeName == "ba:businesstype_factory")
            {
                itemsToPurchase = handleFactoryPurchases(warehouse);
            }
            else if (warehouse.businessTypeName == "ba:businesstype_factory")
            {
                Logger.LogDebug($"OnPurchasingAgentProductsScrollerControllerLoadProducts: {warehouse.businessTypeName}");
                return; // todo: handle it
            }
            else
            {
                Logger.LogWarning($"OnPurchasingAgentProductsScrollerControllerLoadProducts: Unsupported building type: {warehouse.businessTypeName}");
                return;
            }

            __instance.scroller.ReloadData(0f);
        }

        internal static Dictionary<string, int> handleFactoryPurchases(BuildingRegistration buildingRegistration)
        {
            Logger.LogDebug($"OnPurchasingAgentProductsScrollerControllerLoadProducts: {buildingRegistration.businessTypeName}");
            var recipeItems = new Dictionary<string, int>();

            buildingRegistration.itemInstances.Values
                .OfType<FactoryWorkstationInstance>()
                .Where(instance => !string.IsNullOrEmpty(instance.selectedRecipeId))
                .ToList()
                .ForEach(instance =>
                {
                    Logger.LogDebug($"OnPurchasingAgentProductsScrollerControllerLoadProducts: selectedRecipe={instance.SelectedRecipe}");

                    instance.SelectedRecipe.ingredients
                        .ToList()
                        .ForEach(ingredient =>
                            recipeItems[ingredient.item] = recipeItems.GetValueOrDefault(ingredient.item) + ingredient.amount);
                });

            var weeklyHours = buildingRegistration.scheduleDays
                .SelectMany(day => day.openingHourSlots)
                .Sum(slot => slot.GetDurationInHours);

            Logger.LogDebug($"OnPurchasingAgentProductsScrollerControllerLoadProducts: weeklyHours={weeklyHours}");

            recipeItems.Keys
                .ToList()
                .ForEach(key =>
                {
                    Logger.LogDebug($"OnPurchasingAgentProductsScrollerControllerLoadProducts: recipeItem={key}, total={recipeItems[key]}");
                    recipeItems[key] *= weeklyHours;
                });

            return recipeItems;
        }

    }
}
