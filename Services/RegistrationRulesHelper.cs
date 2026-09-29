using TrailGuard.Models;

namespace TrailGuard.Services
{
    public static class RegistrationRulesHelper
    {
        public static bool RequiresMedicalClearance(Assessment assessment)
        {



            return assessment.MedicalClearanceRequired;
        }

        public static bool RequiresPreparationPlan(Assessment assessment)
        {
            return assessment.Result == "Not Recommended";
        }

        public static string MedicalClearanceReason(Assessment assessment)
        {


            if (assessment.MedicalClearanceRequired)
                return "Required because your assessment flagged a health condition that needs medical clearance.";
            return string.Empty;
        }
    }
}
