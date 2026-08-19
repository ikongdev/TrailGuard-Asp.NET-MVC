using TrailGuard.Models;

namespace TrailGuard.Services
{
    public static class RegistrationRulesHelper
    {
        public static bool RequiresMedicalClearance(Assessment assessment)
        {
            // Not Recommended always requires clearance regardless of what the ACSM gate
            // set - a fitness-driven Not Recommended still warrants a look from a
            // physician before proceeding. Otherwise, defer entirely to the gate's own
            // MedicalClearanceRequired flag, not a raw "did they check any condition box"
            // proxy: that proxy both under-triggers (ACSM Rule 3 caps a known-CVD,
            // vigorous-intensity case at Borderline while still requiring clearance) and
            // over-triggers (asthma-only and joint-injury-only conditions are checked
            // boxes but the gate explicitly does not require clearance for either).
            return assessment.Result == "Not Recommended" || assessment.MedicalClearanceRequired;
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
