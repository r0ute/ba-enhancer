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

        Plugin.Logger.LogInfo($"OnBizManScheduleLoadScheduler: businessName={ScheduleHelper.Business.buildingRegistration.BusinessName}");
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
                    skill,
                    hours = weeklyOpenHours
                }))
            .GroupBy(x => x.skill)
            .Select(group => new
            {
                skill = group.Key,
                required = Mathf.CeilToInt(group.Sum(x => x.hours) / (float)MAX_WEEKLY_HOURS_PER_EMPLOYEE)
            });


        var assignedEmployees = EmployeeHelper.GetEmployeeInstances(
                new EmployeeInstancesQueryInfo
                {
                    withAssignedAddress = registration.Address
                })
            .GroupBy(employee => employee.GetPrimarySkill())
            .ToDictionary(group => group.Key, group => group.Count());

        requiredEmployees.Select(requirement =>
            {
                assignedEmployees.TryGetValue(requirement.skill, out var assigned);

                return new
                {
                    requirement.skill,
                    requirement.required,
                    assigned
                };
            })
            .ToList()
            .ForEach(result =>
            {
                Plugin.Logger.LogDebug($"OnBizManScheduleLoadScheduler: employeeType={result.skill}, " +
                    $"employeeCount={result.assigned}/{result.required}");
            });
    }
}