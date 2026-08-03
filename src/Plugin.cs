using System;
using System.Collections.Generic;
using System.Linq;
using AI.Citizens;
using BepInEx;
using BepInEx.Logging;
using BigAmbitions.Items;
using BigAmbitions.Rivals;
using BigAmbitions.Tags;
using Buildings;
using Controllers;
using Entities;
using HarmonyLib;
using Helpers;
using UI.Smartphone.Apps.BizMan.LogisticsManagers;
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
        ("ba:buildingtype_cinema", 3),
        ("ba:buildingtype_theater", 3),
        ("ba:buildingtype_office", 6),
        ("ba:buildingtype_retail", 14),
        ("ba:buildingtype_warehouse", 15)
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

            BIZ_SEARCH_BUILDING_TYPES.ForEach(criteria =>
                {
                    if (criteria.buildingType == "ba:buildingtype_warehouse")
                        searchWarehouses(criteria);
                    else
                        searchBuildings(criteria);
                });
        }
        internal static void searchBuildings((string buildingType, int searchLimit) criteria)
        {
            BuildingHelper.AllNeighbourhoodBuildings
                .SelectMany(neighbourhood => neighbourhood.Value
                    .Where(building => !building.SpecialService
                        && criteria.buildingType.Equals(building.BuildingType))
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
                        + $"100Promotion={building.trafficIndex >= neighborhoodMinTrafficFor100Promotion.Get(entry.Neighbourhood)}, "
                        + $"address={building.Address}");
                });
        }

        internal static void searchWarehouses((string buildingType, int searchLimit) criteria)
        {
            BuildingHelper.AllNeighbourhoodBuildings
                .SelectMany(neighbourhood => neighbourhood.Value
                    .Where(building => !building.SpecialService
                        && criteria.buildingType.Equals(building.BuildingType))
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
                    .OrderByDescending(entry => entry.Building.BuildingSize)
                    .ThenByDescending(entry => entry.Building.BuildingVersion)
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
                        + $"size={building.BuildingSize}{building.BuildingVersion}, "
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
        [HarmonyPatch(typeof(BizManPresentation), nameof(BizManPresentation.LoadLeftSide))]
        [HarmonyPostfix]
        static void OnBizManPresentationSetAiOwned(ref BizManPresentation __instance)
        {
            var bizManBusiness = Traverse.Create(__instance).Field("bizManBusiness").GetValue() as BizManBusiness;

            if (bizManBusiness.building.SpecialService != null)
            {
                return;
            }

            float minOfferPrice = CompetitionHelper.CalculateAiOwnedValuation(bizManBusiness.buildingRegistration)
                * RivalsHelper.GetOvertakeBusinessAcceptRate(bizManBusiness.buildingRegistration.businessOwnerRivalId, bizManBusiness.buildingRegistration.Address);
            Logger.LogDebug($"OnBizManPresentationSetAiOwned: bizManBusiness={bizManBusiness.buildingRegistration.Address}, minOfferPrice={minOfferPrice}");
            __instance.offerAmountInputField.text = Math.Round(minOfferPrice + 1, 0, MidpointRounding.AwayFromZero).ToString();

            BuildingForSale buildingForSale = SaveGameManager.Current.buildingsForSale.FirstOrDefault((BuildingForSale x) => x.address == bizManBusiness.building.Address);
            float minBuildingPrice = (buildingForSale == null)
                ? (bizManBusiness.building.GetMarketValue()
                    * (1f + RivalsHelper.GetBuyBuildingAcceptRate(bizManBusiness.buildingRegistration.buildingOwnerRivalId) / 100f))
                : (buildingForSale.buildingPrice * buildingForSale.acceptOfferRate);
            Logger.LogDebug($"OnBizManPresentationSetAiOwned: bizManBusiness={bizManBusiness.buildingRegistration.Address}, minBuildingPrice={minBuildingPrice}");
            __instance.buyBuildingAmountInputField.text = Math.Round(minBuildingPrice + 1, 0, MidpointRounding.AwayFromZero).ToString();
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
                        + $"trafficIndex={buildingRegistration.BuildingCached.trafficIndex}"
                        + $"size={buildingRegistration.BuildingCached.BuildingSize} {buildingRegistration.BuildingCached.BuildingVersion}"}
                    });
                }
            });
        }

        [HarmonyPatch(typeof(PurchasingAgentPlanUI), "StartOrder")]
        [HarmonyPostfix]
        static void OnPurchasingAgentPlanUIStartOrder(ref PurchasingAgentPlanUI __instance)
        {
            Logger.LogInfo($"OnPurchasingAgentPlanUIStartOrder: Start");

            var warehouses = __instance.productsScrollerController.data
                .Where(product => product.isTarget && product?.productRef?.assignedWarehouse != null)
                .Select(product => product.warehouses
                    .FirstOrDefault(warehouse => warehouse.Address == product.productRef.assignedWarehouse))
                .Where(warehouse => warehouse != null)
                .Distinct()
                .ToList();

            if (warehouses.Count == 0)
            {
                Logger.LogWarning($"OnPurchasingAgentPlanUIStartOrder: no assigned warehouses");
                return;
            }
            else
            {
                Logger.LogDebug($"OnPurchasingAgentPlanUIStartOrder: warehousesCount={warehouses.Count}");
            }

            var itemsToPurchase = new Dictionary<string, int>();

            warehouses.ForEach(warehouse =>
            {
                Logger.LogDebug($"OnPurchasingAgentPlanUIStartOrder: assignedWarehouse={warehouse.Address}");
                Dictionary<string, int> otherItemsToPurchase;

                if (warehouse.businessTypeName == "ba:businesstype_factory")
                {
                    otherItemsToPurchase = handleFactoryPurchases(warehouse);
                }
                else if (warehouse.businessTypeName == "ba:businesstype_warehouse")
                {
                    Logger.LogDebug($"OnPurchasingAgentPlanUIStartOrder: {warehouse.businessTypeName}");
                    return; // todo: handle it
                }
                else
                {
                    Logger.LogWarning($"OnPurchasingAgentPlanUIStartOrder: Unsupported building type: {warehouse.businessTypeName}");
                    return;
                }

                otherItemsToPurchase.ToList()
                    .ForEach(item =>
                        itemsToPurchase[item.Key] = itemsToPurchase.GetValueOrDefault(item.Key) + item.Value);
            });

            if (itemsToPurchase.Count == 0)
            {
                return;
            }

            __instance.productsScrollerController.data.ForEach(productModel =>
                {
                    productModel.UpdateAmount(itemsToPurchase.GetValueOrDefault(productModel.productRef.itemName));
                    Logger.LogDebug($"OnPurchasingAgentPlanUIStartOrder: item={productModel.productRef.itemName},"
                        + $"amount={productModel.productRef.amount}");
                });
            __instance.productsScrollerController.scroller.ReloadData(0f);
            Logger.LogInfo($"OnPurchasingAgentPlanUIStartOrder: End");
        }

        internal static Dictionary<string, int> handleFactoryPurchases(BuildingRegistration buildingRegistration)
        {
            Logger.LogDebug($"OnPurchasingAgentPlanUIStartOrder: {buildingRegistration.businessTypeName}");
            var recipeItems = new Dictionary<string, int>();

            buildingRegistration.itemInstances.Values
                .OfType<FactoryWorkstationInstance>()
                .Where(instance => !string.IsNullOrEmpty(instance.selectedRecipeId))
                .ToList()
                .ForEach(instance =>
                {
                    Logger.LogDebug($"OnPurchasingAgentPlanUIStartOrder: selectedRecipe={instance.SelectedRecipe}");

                    instance.SelectedRecipe.ingredients
                        .ToList()
                        .ForEach(ingredient =>
                            recipeItems[ingredient.item] = recipeItems.GetValueOrDefault(ingredient.item) + ingredient.amount);
                });

            var weeklyHours = buildingRegistration.scheduleDays
                .SelectMany(day => day.openingHourSlots)
                .Sum(slot => slot.GetDurationInHours);

            Logger.LogDebug($"OnPurchasingAgentPlanUIStartOrder: weeklyHours={weeklyHours}");

            recipeItems.Keys
                .ToList()
                .ForEach(key =>
                {
                    Logger.LogDebug($"OnPurchasingAgentPlanUIStartOrder: recipeItem={key}, total={recipeItems[key]}");
                    recipeItems[key] *= weeklyHours;
                });

            return recipeItems;
        }


        [HarmonyPatch(typeof(LogisticsManagerPlanUI), "LoadProducts")]
        [HarmonyPrefix]
        static void OnLogisticsManagerPlanUILoadProducts(ref LogisticsManagerPlanUI __instance,
            LogisticsManagerPlanDestination planDestination, LogisticsManagerDestinationUI destinationEntry)
        {
            Logger.LogInfo($"OnLogisticsManagerPlanUILoadProducts: deliveryTargetAddress={planDestination.deliveryTargetAddress}, stockTargetsCount={planDestination.stockTargets.Count}");

            var buildingRegistration = BuildingHelper.GetBuildingRegistration(planDestination.deliveryTargetAddress);

            if (buildingRegistration?.GetBuildingType() != "ba:buildingtype_retail")
            {
                return;
            }

            Dictionary<string, int> itemCapacity = [];
            var totalItemCapacity = 0;

            foreach (var itemInstance in buildingRegistration.itemInstances.Values)
            {
                if ((itemInstance.ItemCached.type & ItemType.ShowcaseShelf) != 0)
                {
                    CargoInstance stockInstance = itemInstance.GetStockInstance();

					if (!string.IsNullOrEmpty(stockInstance.itemName) 
                        && (ItemsGetter.GetByName(stockInstance.itemName).type & ItemType.ServiceProduct) == 0)
					{
						itemCapacity[stockInstance.itemName] = itemCapacity.GetValueOrDefault(stockInstance.itemName)
                            + itemInstance.ItemCached.addedCustomersPerHour;
                        totalItemCapacity += itemInstance.ItemCached.addedCustomersPerHour;

                        Logger.LogDebug($"OnLogisticsManagerPlanUILoadProducts: address={buildingRegistration.GetDisplayName()}, "
                            + $"itemName={stockInstance.itemName}, "
                            + $"addedCustomersPerHour={itemInstance.ItemCached.addedCustomersPerHour}");
					}
                }
            }

            if (BusinessTypeHelper.GetData(buildingRegistration).HasTag(TagRef.Businesstag.customersneedpaperbags))
            {
                itemCapacity.Add("ba:itemname_paperbag", totalItemCapacity);
            }

            planDestination.stockTargets.Clear();

            foreach (var entry in itemCapacity)
            {
                planDestination.stockTargets.Add(new ItemAmountTarget(entry.Key, entry.Value));
            }

        }
    }
}
