using System;
using System.Linq;
using BigAmbitions.Rivals;
using HarmonyLib;
using Helpers;

namespace BA.src.patch.biz;

internal class BuildingPricePatch
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
        Plugin.Logger.LogDebug($"OnBizManPresentationSetAiOwned: bizManBusiness={bizManBusiness.buildingRegistration.Address}, minOfferPrice={minOfferPrice}");
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
        Plugin.Logger.LogDebug($"OnBizManPresentationSetAiOwned: bizManBusiness={bizManBusiness.buildingRegistration.Address}, minBuildingPrice={minBuildingPrice}");
        __instance.buyBuildingAmountInputField.text = Math.Round(minBuildingPrice + 1, 0, MidpointRounding.AwayFromZero).ToString();
    }
}