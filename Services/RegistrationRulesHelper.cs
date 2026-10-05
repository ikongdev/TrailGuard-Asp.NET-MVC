using TrailGuard.Models;

namespace TrailGuard.Services
{
    public static class RegistrationRulesHelper
    {
        public static bool RequiresMedicalClearance(Assessment assessment)
        {



            return assessment.MedicalClearanceRequired || assessment.Result == "Not Recommended";
        }

        public static bool RequiresPreparationPlan(Assessment assessment)
        {
            return assessment.Result == "Not Recommended";
        }

        public static string MedicalClearanceReason(Assessment assessment)
        {


            var requiresHealthClearance = assessment.MedicalClearanceRequired;
            var isNotRecommended = assessment.Result == "Not Recommended";

            if (requiresHealthClearance && isNotRecommended)
                return "Required because your health screening indicates medical clearance is needed and registration policy requires it for a Not Recommended assessment result.";
            if (requiresHealthClearance)
                return "Required because your health screening indicates medical clearance is needed before registration.";
            if (isNotRecommended)
                return "Required by registration policy because your assessment result is Not Recommended.";
            return string.Empty;
        }
    }
}
