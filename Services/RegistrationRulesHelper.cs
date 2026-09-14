using TrailGuard.Models;

namespace TrailGuard.Services
{
    public static class RegistrationRulesHelper
    {
        public static bool RequiresMedicalClearance(Assessment assessment)
        {
            // Combined registration requirement for every participant/organizer UI
            // and POST validator: agency Not Recommended policy OR stored ACSM
            // screening. The agency policy is not part of AcsmClearanceService.
            return assessment.Result == "Not Recommended" || assessment.MedicalClearanceRequired;
        }

        public static bool RequiresPreparationPlan(Assessment assessment)
        {
            return assessment.Result == "Not Recommended";
        }

        public static string MedicalClearanceReason(Assessment assessment)
        {
            // The raw screening flag selects the reason, not whether the upload
            // is required. Python's label-capping GateReason is a separate concept.
            if (assessment.MedicalClearanceRequired)
                return "Required because your assessment flagged a health condition that needs medical clearance.";
            return assessment.Result == "Not Recommended"
                ? "Required by agency policy because your assessment result is Not Recommended."
                : string.Empty;
        }
    }
}
