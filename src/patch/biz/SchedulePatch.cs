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

        var businessSkills = BusinessTypeHelper
            .GetData(registration.businessTypeName)
            .employeePrimarySkills
            .ToHashSet();

        var requiredEmployees = registration.itemInstances.Values
            .Where(item => item.ItemCached.assignable)
            .SelectMany(item => item.ItemCached.suitableSkills ?? Enumerable.Empty<string>())
            .Where(skill => businessSkills.Contains(skill) || skill == "ba:skill_cleaning")
            .GroupBy(skill => skill)
            .Select(group => new
            {
                skill = group.Key,
                required = Mathf.CeilToInt(group.Count() * weeklyOpenHours / (float)MAX_WEEKLY_HOURS_PER_EMPLOYEE)
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
            var message = $"OnBizManScheduleLoadScheduler: employeeType={result.skill}, "
                + $"employeeCount={result.assigned}/{result.required}, diff={result.required - result.assigned}";

            if (result.assigned != result.required)
            {
                Plugin.Logger.LogWarning(message);
            }
            else
            {
                Plugin.Logger.LogDebug(message);
            }

        });

        var missingSlots = registration.scheduleDays
            .Where(day => day.isOpen)
            .SelectMany(day => day.openingHourSlots.SelectMany(slot =>
                Enumerable.Range(slot.startingHour, slot.endingHour - slot.startingHour)
                    .Select(hour => new
                    {
                        day,
                        hour
                    })))
            .SelectMany(x => registration.itemInstances.Values
                .Where(item => item.ItemCached.assignable)
                .Where(item => !item.IsCleaningStation() ||
                    item.ItemCached.suitableSkills.Contains("ba:skill_cleaning"))
                .Where(item => !x.day.workShifts.Any(shift =>
                    shift.itemInstanceId == item.id &&
                    shift.startingHour <= x.hour &&
                    shift.endingHour > x.hour))
                .Select(item => new
                {
                    item,
                    x.day,
                    x.hour
                }));


        if (missingSlots.Any())
        {
            Plugin.Logger.LogWarning(
                $"OnBizManScheduleLoadScheduler: Missing slots: businessName={registration.BusinessName}");
        }
    }
}