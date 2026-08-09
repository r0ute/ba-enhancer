using System.Linq;
using HarmonyLib;
using Helpers;
using UI.Smartphone.Apps.BizMan.Schedule;
using UnityEngine;

namespace BA.src.patch.biz;

[HarmonyPatch]
internal class BizManSchedulePatch
{
    private const int MAX_WEEKLY_HOURS_PER_EMPLOYEE = 50;

    [HarmonyPatch(typeof(BizManSchedule), nameof(BizManSchedule.LoadScheduler))]
    [HarmonyPostfix]
    static void OnBizManScheduleLoadScheduler()
    {
        if (ScheduleHelper.IsHeadquarters)
        {
            return;
        }



        Plugin.Logger.LogDebug($"OnBizManScheduleLoadScheduler: businessName={ScheduleHelper.Business.buildingRegistration.BusinessName}");
        var registration = ScheduleHelper.Business.buildingRegistration;


        int weeklyOpenHours = registration.scheduleDays
            .Where(day => day.isOpen)
            .Sum(day => day.openingHourSlots
                .Sum(slot => slot.endingHour - slot.startingHour));


        var requiredEmployees = registration.itemInstances.Values
            .Where(item => item.ItemCached.suitableSkills != null)
            .SelectMany(item => item.ItemCached.suitableSkills
                .Select(skill => new
                {
                    Skill = skill,
                    Hours = weeklyOpenHours
                }))
            .GroupBy(x => x.Skill)
            .Select(group => new
            {
                Skill = group.Key,
                Required = Mathf.CeilToInt(
                    group.Sum(x => x.Hours)
                    /
                    (float)MAX_WEEKLY_HOURS_PER_EMPLOYEE)
            });


        var assignedEmployees = EmployeeHelper.GetEmployeeInstances(
                new EmployeeInstancesQueryInfo
                {
                    withAssignedAddress = registration.Address
                })
            .GroupBy(employee => employee.GetPrimarySkill())
            .ToDictionary(
                group => group.Key,
                group => group.Count());


        foreach (var requirement in requiredEmployees)
        {
            assignedEmployees.TryGetValue(
                requirement.Skill,
                out int assigned);

            Plugin.Logger.LogInfo(
                $"OnBizManScheduleLoadScheduler: " +
                $"businessName={registration.BusinessName}, " +
                $"employeeType={requirement.Skill}, " +
                $"employeeCount={assigned}/{requirement.Required}");
        }
    }
}