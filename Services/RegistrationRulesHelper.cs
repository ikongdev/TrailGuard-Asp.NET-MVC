using TrailGuard.Models;

namespace TrailGuard.Services
{
    public static class RegistrationRulesHelper
    {
        public static bool RequiresMedicalClearance(Assessment assessment)
        {



            return assessment.Result == "Not Recommended" || assessment.MedicalClearanceRequired;
        }

        public static bool RequiresPreparationPlan(Assessment assessment)
        {
            return assessment.Result == "Not Recommended";
        }

        public static string MedicalClearanceReason(Assessment assessment)
        {


            if (assessment.MedicalClearanceRequired)
                return "Required because your assessment flagged a health condition that needs medical clearance.";
            return assessment.Result == "Not Recommended"
                ? "Required by agency policy because your assessment result is Not Recommended."
                : string.Empty;
        }
    }
}
