using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace BA.src;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{

    internal static readonly string VEHICLE_TRUCKS_DEALERSHIP_LAYOUT_ID = "industrycitycardealershiptrucks";
    internal static readonly List<string> VEHICLE_MISSING_TRUCK_IDS = [
        "ba:vehicletype_umcdesert",
    ];

    internal static readonly List<(string buildingType, int searchLimit)> BIZ_SEARCH_BUILDING_TYPES = [
        ("ba:buildingtype_cinema", 3),
        ("ba:buildingtype_theater", 3),
        ("ba:buildingtype_office", 6),
        ("ba:buildingtype_retail", 14),
        ("ba:buildingtype_warehouse", 15),
    ];
    internal static readonly Dictionary<string, int> BIZ_DELIVERY_CUSTOM_MULTIPLIERS = new()
    {
        ["ba:itemname_haircareproduct"] = 2,
        ["ba:itemname_popcorn"] = 2,
    };
    internal static readonly string BIZ_PLAYER_MONOPOLY_INDICATOR = "(!)";

    // Rent <=100%: no tenant loss;
    // Rent >100%: adds 6.67% daily tenant loss chance;
    // Rent >=120%: cannot gain tenants and only loses occupancy;
    // Rent 105%-110%: tradeoff zone between higher revenue and occupancy risk (15%-10% daily gain chance, 6.67% daily loss chance).
    internal static readonly float REAL_ESTATE_OPTIMAL_RENT_MULTIPLIER = 1.05f;
    internal static readonly float REAL_ESTATE_MIN_RENT_MULTIPLIER = 0.80f;
    internal static readonly float REAL_ESTATE_MAX_RENT_MULTIPLIER = 1.20f;

    internal static readonly HashSet<string> EMPLOYEE_BLOCKED_MESSAGES =
    [
        "ba:messagetype_employee_contact_message_new_demand",
        "ba:messagetype_headhunter_expected_completion_days_range",
        "ba:messagetype_headhunter_expected_completion_1_day",
        "ba:messagetype_employee_contact_message_quit",
        "ba:messagetype_employee_contact_message_retire",
        "ba:messagetype_employee_contact_message_retirement_notice",
        "ba:messagetype_employee_contact_message_low_satisfaction",
    ];

    internal static new ManualLogSource Logger;

    internal static Dictionary<Address, int> bestBuildings = [];

    private void Awake()
    {
        // Plugin startup logic
        Logger = base.Logger;

        Harmony harmony = new(MyPluginInfo.PLUGIN_GUID);
        harmony.PatchAll();

        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
    }

}