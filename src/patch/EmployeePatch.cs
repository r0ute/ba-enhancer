using System.Linq;
using AI.Employees;
using Entities;
using HarmonyLib;

namespace BA.src.patch;

[HarmonyPatch]
internal class EmployeePatch
{
    [HarmonyPatch(typeof(EmployeeInstance), nameof(EmployeeInstance.SendMessage))]
    [HarmonyPrefix]
    static bool OnEmployeeInstanceSendMessage(string messageKey)
    {
        if (Plugin.EMPLOYEE_BLOCKED_MESSAGES.Contains(messageKey) || ComplaintHelper.Complaints.Any(x => x.complaintMessageType == messageKey))
        {
            return false;
        }

        return true;
    }
}