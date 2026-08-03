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
using Extensions;
using HarmonyLib;
using Helpers;
using UI.Smartphone.Apps.BizMan;
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

    internal static readonly int BIZ_WAREHOUSE_RETAIIL_DELIVERY_MULTIPLIER = 4;

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

                    if (!neighborhoodMinTrafficFor100Promotion.ContainsKey(neighborhoodData.neighbourhood))
                    {
                        neighborhoodMinTrafficFor100Promotion.Add(neighborhoodData.neighbourhood, minTrafficFor100Promotion);
                    }

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
        }

        [HarmonyPatch(typeof(BizManPresentation), nameof(BizManPresentation.LoadLeftSide))]
        [HarmonyPostfix]
        static void OnBizManPresentationSLoadLeftSide(ref BizManPresentation __instance)
        {
            var bizManBusiness = Traverse.Create(__instance).Field("bizManBusiness").GetValue() as BizManBusiness;

            if (bizManBusiness.building.SpecialService != null)
            {
                return;
            }

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
        static void OnLogisticsManagerPlanUILoadProducts(LogisticsManagerPlanDestination planDestination)
        {
            Logger.LogInfo($"OnLogisticsManagerPlanUILoadProducts: deliveryTargetAddress={planDestination.deliveryTargetAddress}, stockTargetsCount={planDestination.stockTargets.Count}");
            var buildingRegistration = BuildingHelper.GetBuildingRegistration(planDestination.deliveryTargetAddress);

            if (buildingRegistration?.GetBuildingType() != "ba:buildingtype_retail")
            {
                return;
            }

            var itemCapacity = buildingRegistration.itemInstances.Values
                .Where(itemInstance => (itemInstance.ItemCached.type & ItemType.ShowcaseShelf) != 0)
                .Select(itemInstance => new
                {
                    ItemInstance = itemInstance,
                    StockInstance = itemInstance.GetStockInstance()
                })
                .Where(x => !string.IsNullOrEmpty(x.StockInstance.itemName)
                    && (ItemsGetter.GetByName(x.StockInstance.itemName).type & ItemType.ServiceProduct) == 0)
                .GroupBy(x => x.StockInstance.itemName)
                .ToDictionary(
                    group => group.Key,
                    group => group.Sum(x => x.ItemInstance.ItemCached.addedCustomersPerHour
                        * BIZ_WAREHOUSE_RETAIIL_DELIVERY_MULTIPLIER));

            itemCapacity.ToList()
                .ForEach(entry =>
                {
                    Logger.LogDebug($"OnLogisticsManagerPlanUILoadProducts: address={buildingRegistration.GetDisplayName()}, "
                        + $"itemName={entry.Key}, "
                        + $"addedCustomersPerHour={entry.Value}");
                });

            if (BusinessTypeHelper.GetData(buildingRegistration).HasTag(TagRef.Businesstag.customersneedpaperbags))
            {
                itemCapacity["ba:itemname_paperbag"] = itemCapacity.Values.Sum();
            }

            planDestination.stockTargets.Clear();
            itemCapacity
                .Select(entry => new ItemAmountTarget(entry.Key, entry.Value))
                .ToList()
                .ForEach(planDestination.stockTargets.Add);
        }


        [HarmonyPatch(typeof(BusinessScrollerController), "PopulateAllModels")]
        [HarmonyPrefix]
        static void OnBusinessScrollerControllerPopulateAllModels(ref BusinessScrollerController __instance, ref bool __runOriginal,
            List<BusinessCellView.BusinessModel> allModels)
        {
            __runOriginal = false;

            Logger.LogDebug($"OnBusinessScrollerControllerPopulateAllModels: buildingRegistrationCount={SaveGameManager.Current.BuildingRegistrations.Count}");
            SaveGameManager.Current.BuildingRegistrations
                .Where(buildingRegistration =>
                    (buildingRegistration.RentedByPlayer && IsVisibleBusiness(buildingRegistration))
                    || buildingRegistration.BuildingOwnedByPlayer)
                .OrderByDescending(buildingRegistration => buildingRegistration.businessTypeName)
                .ThenBy(buildingRegistration => buildingRegistration.GetDisplayName())
                .ToList()
                .ForEach(buildingRegistration =>
                {
                    allModels.Add(
                        buildingRegistration.RentedByPlayer && IsVisibleBusiness(buildingRegistration)
                            ? new BusinessCellView.BusinessModel(buildingRegistration)
                            : new BusinessCellView.BusinessModel(buildingRegistration, isRealEstate: true)
                    );
                });
        }

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(BusinessScrollerController), "IsVisibleBusiness")]
        static bool IsVisibleBusiness(BuildingRegistration registration) => throw new NotImplementedException();


        [HarmonyPatch(typeof(WarehouseList), nameof(WarehouseList.Load))]
        [HarmonyPrefix]
        static void OnWarehouseListLoad(WarehouseList __instance, ref bool __runOriginal)
        {
            __runOriginal = false;

            Logger.LogDebug($"OnWarehouseListLoad: buildingRegistrationCount={SaveGameManager.Current.BuildingRegistrations.Count}");
            Traverse.Create(__instance).Field("warehouseEntry").GetValue<Transform>().ResetTemplate();
            SaveGameManager.Current.BuildingRegistrations
                .OrderByDescending(x => x.businessTypeName)
                .ThenBy(x => x.GetDisplayName())
                .Where(x => x.RentedByPlayer && x.GetBuildingType() == "ba:buildingtype_warehouse")
                .ToList()
                .ForEach(item => SetUpEntry(__instance, (Warehouse)item));
        }


        [HarmonyReversePatch]
        [HarmonyPatch(typeof(WarehouseList), "SetUpEntry")]
        static void SetUpEntry(object instance, Warehouse warehouse) => throw new NotImplementedException();


        [HarmonyPatch(typeof(ItemHelper), nameof(ItemHelper.GetLowestMarketPrice))]
        [HarmonyPrefix]
        static void OnItemHelperGetLowestMarketPrice(ref bool __runOriginal, ref float __result, string itemName, string neighborhood, bool forceUpdate = false)
        {
            __runOriginal = false;


            if (!forceUpdate && ItemHelper.LmpDictionary.TryGetValue((itemName, neighborhood), out var value))
            {
                __result = value;
                return;
            }

            if (forceUpdate && ItemHelper.LmpDictionary.ContainsKey((itemName, neighborhood)))
            {
                ItemHelper.LmpDictionary.Remove((itemName, neighborhood));
            }

            float lowestPrice = -1f;
            float lowestPriceExcludePlayer = lowestPrice;

            foreach (BuildingRegistration buildingRegistration in SaveGameManager.Current.BuildingRegistrations)
            {
                if (buildingRegistration.temporarilyClosed || buildingRegistration.retailPrices == null || buildingRegistration.Neighborhood != neighborhood || string.IsNullOrEmpty(buildingRegistration.BusinessName))
                {
                    continue;
                }

                foreach (RetailPrice retailPrice in buildingRegistration.retailPrices)
                {
                    if (!(retailPrice.itemName != itemName) && (buildingRegistration.RentedByPlayer || !(PlayerItemPurchaser.GetShelfFillState(itemName, buildingRegistration) <= 0f)))
                    {
                        if (lowestPrice < 0f || retailPrice.price < lowestPrice)
                        {
                            lowestPrice = retailPrice.price;
                        }

                        if (!(buildingRegistration.RentedByPlayer || buildingRegistration.BuildingOwnedByPlayer)
                            && (lowestPriceExcludePlayer < 0f || retailPrice.price < lowestPriceExcludePlayer))
                        {
                            lowestPriceExcludePlayer = retailPrice.price;
                        }

                        break;
                    }
                }
            }

            if (lowestPrice < 0f)
            {
                lowestPrice = ItemHelper.CalculateOptimalPriceByNeighborhood(itemName, neighborhood);
            }

            if (lowestPriceExcludePlayer < 0f)
            {
                lowestPriceExcludePlayer = ItemHelper.CalculateOptimalPriceByNeighborhood(itemName, neighborhood);
            }

            ItemHelper.LmpDictionary.Add((itemName, neighborhood), lowestPrice);

            __result = forceUpdate ? lowestPriceExcludePlayer : lowestPrice;
            Logger.LogDebug($"OnItemHelperGetLowestMarketPrice: neighborhood={neighborhood}, itemName={itemName}, lowestPrice={__result}, forceUpdate={forceUpdate}");

        }

    }
}
