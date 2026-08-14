using TrailGuard.Models;

namespace TrailGuard.Services
{
    public static class RegistrationRulesHelper
    {
        public static bool RequiresMedicalClearance(Assessment assessment)
        {
            if (assessment.Result == "Not Recommended") return true;
            return HasAnyMedicalCondition(assessment.MedicalConditions);
        }

        public static bool RequiresPreparationPlan(Assessment assessment)
        {
            return assessment.Result == "Not Recommended";
        }

        public static bool HasAnyMedicalCondition(string? medicalConditions)
        {
            if (string.IsNullOrWhiteSpace(medicalConditions)) return false;

            return medicalConditions
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(c => !c.Equals("None of the above", StringComparison.OrdinalIgnoreCase));
        }
    }
}
