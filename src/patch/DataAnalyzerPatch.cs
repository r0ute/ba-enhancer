using System.Collections.Generic;
using System.Linq;
using AI.Citizens;
using BigAmbitions.Items;
using HarmonyLib;
using Helpers;
using UnityEngine;
using UnityEngine.UIElements.Collections;

namespace BA.src.patch;

[HarmonyPatch]
internal class DataAnalyzerPatch
{

    private static Dictionary<string, int> neighborhoodMinTrafficFor100Promotion = [];

    [HarmonyPatch(typeof(GameManager), "Awake")]
    [HarmonyPostfix]
    static void OnGameManagerAwake()
    {

        logNeighborhoodData();
        logBusinessOpeningHours();
        logProductsWithReducedDemand();
        logBusinessMaxPurchaseAmountPerProduct();
        logBestBuildingsToPurchase();
    }

    private static void logNeighborhoodData()
    {
        Plugin.Logger.LogInfo($"OnGameManagerAwake: NeighborhoodsData");
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

                Plugin.Logger.LogDebug($"OnGameManagerAwake: neighbourhood={neighborhoodData.neighbourhood}, "
                    + $"dominantSocialClass={dominantSocialClass}, "
                    + $"socialClassPercentage={maxSocialClassPercentage}, "
                    + $"maxAcceptableRelativePrice={maxAcceptableRelativePrice}, "
                    + $"averagePriceIndex={CitizenHelper.averagePriceIndicesInNeighborhoods.Get(neighborhoodData.neighbourhood)}, "
                    + $"marketingStrength={neighborhoodData.marketingStrength}, "
                    + $"customerDemandsWeight={neighborhoodData.customerDemandsWeight}, "
                    + $"minTrafficFor100Promotion={minTrafficFor100Promotion}");
            });

    }

    private static void logBusinessOpeningHours()
    {
        Plugin.Logger.LogInfo($"OnGameManagerAwake: Opening Hours");
        BusinessTypeHelper.GetAllPlayerAvailableBusinesses()
            .ToList()
            .ForEach(businessType =>
            {
                Plugin.Logger.LogDebug($"OnGameManagerAwake: businessType={businessType}");

                businessType.dayFactorMultipliers.ForEach(dayFactorMultiplier =>
                {
                    Plugin.Logger.LogDebug($"OnGameManagerAwake: dayFactorMultiplier: "
                        + $"dayOfWeek={dayFactorMultiplier.dayOfWeekOrdered}, multiplier={dayFactorMultiplier.multiplier}");
                });

                businessType.hourlyFactorMultipliers.ForEach(hourlyFactorMultiplier =>
                {
                    Plugin.Logger.LogDebug($"OnGameManagerAwake: hourlyFactorMultiplier: "
                        + $"startingHour={hourlyFactorMultiplier.startingHour}, endingHour={hourlyFactorMultiplier.endingHour}, multiplier={hourlyFactorMultiplier.multiplier}");
                });
            });
    }

    private static void logProductsWithReducedDemand()
    {
        Plugin.Logger.LogInfo($"OnGameManagerAwake: Products with reduced demand");
        BusinessTypeHelper.GetAllPlayerAvailableBusinesses()
            .Where(businessType => businessType.businessProducts.Any(product => product.impact < 1))
            .ToList()
            .ForEach(businessType =>
            {
                Plugin.Logger.LogDebug($"OnGameManagerAwake: businessType={businessType}");

                businessType.businessProducts
                    .Where(product => product.impact < 1)
                    .ToList()
                    .ForEach(product =>
                    {
                        Plugin.Logger.LogDebug($"OnGameManagerAwake: itemName={ItemsGetter.GetByName(product.itemName)}, "
                            + $"impact={product.impact}");
                    });
            });
    }

    private static void logBusinessMaxPurchaseAmountPerProduct()
    {
        Plugin.Logger.LogInfo($"OnGameManagerAwake: Business max purchase amount per product");
        BusinessTypeHelper.GetAllPlayerAvailableBusinesses()
            .Where(businessType => businessType.maxAmountPerProduct > 1)
            .OrderByDescending(businessType => businessType.maxAmountPerProduct)
            .ThenBy(businessType => businessType.businessTypeName)
            .ToList()
            .ForEach(businessType =>
            {
                Plugin.Logger.LogDebug($"OnGameManagerAwake: businessType={businessType}, maxAmountPerProduct={businessType.maxAmountPerProduct}");
            });
    }

    private static void logBestBuildingsToPurchase()
    {

        Plugin.Logger.LogInfo($"OnGameManagerAwake: Best buildings to purchase");
        Plugin.BIZ_SEARCH_BUILDING_TYPES.ForEach(criteria =>
            {
                if (criteria.buildingType == "ba:buildingtype_warehouse")
                    searchWarehouses(criteria);
                else
                    searchBuildings(criteria);
            });
    }
    private static void searchBuildings((string buildingType, int searchLimit) criteria)
    {
        var (buildingType, searchLimit) = criteria;

        BuildingHelper.AllNeighbourhoodBuildings
            .SelectMany(neighbourhood => neighbourhood.Value
                .Where(building => !building.SpecialService
                    && building.BuildingType == buildingType)
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
                .Take(searchLimit))
            .OrderBy(entry => entry.Neighbourhood)
            .ThenBy(entry => entry.Building.BuildingType)
            .ToList()
            .ForEach(entry =>
            {
                var building = entry.Building;
                Plugin.bestBuildings.Add(entry.Building.Address);
                Plugin.Logger.LogDebug($"OnGameManagerAwake: neighborhood={entry.Neighbourhood}, "
                    + $"type={entry.Building.BuildingType}, "
                    + $"customerCapacity={building.GetCustomerCapacity}, "
                    + $"trafficIndex={building.trafficIndex}, "
                    + $"100Promotion={building.trafficIndex >= neighborhoodMinTrafficFor100Promotion.Get(entry.Neighbourhood)}, "
                    + $"address={building.Address}");
            });
    }

    private static void searchWarehouses((string buildingType, int searchLimit) criteria)
    {
        var (buildingType, searchLimit) = criteria;

        BuildingHelper.AllNeighbourhoodBuildings
            .SelectMany(neighbourhood => neighbourhood.Value
                .Where(building => !building.SpecialService
                    && building.BuildingType == buildingType)
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
                .Take(searchLimit))
            .OrderBy(entry => entry.Neighbourhood)
            .ThenBy(entry => entry.Building.BuildingType)
            .ToList()
            .ForEach(entry =>
            {
                var building = entry.Building;
                Plugin.bestBuildings.Add(entry.Building.Address);
                Plugin.Logger.LogDebug($"OnGameManagerAwake: neighborhood={entry.Neighbourhood}, "
                    + $"type={entry.Building.BuildingType}, "
                    + $"size={building.BuildingSize}{building.BuildingVersion}, "
                    + $"address={building.Address}");
            });
    }
}