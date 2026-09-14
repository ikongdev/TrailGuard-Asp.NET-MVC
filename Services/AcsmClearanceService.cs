namespace TrailGuard.Services
{
    public static class AcsmClearanceService
    {
        // ACSM 2015 (Riebe et al., MSSE 47(11):2473-2479), PLAN.md Stage 2:
        // Rule 1 requires clearance for cardiovascular signs/symptoms regardless
        // of activity or intensity. Rule 2 requires it for inactive people with
        // known CVD; Rule 3 requires it for active people with CVD before vigorous
        // exercise. Organized mountain hiking is 6.0-7.0 METs (Compendium: hiking
        // cross-country 6.0, climbing hills with 0-9 lb load/backpacking 7.0), at
        // or above the ACSM/CDC vigorous threshold of 6.0. Rules 2/3 therefore
        // collapse to known CVD for every Trail in scope, without a demand test.
        // Pulmonary disease/asthma is advisory, not an automatic referral (Rule 4).
        // Joint/knee injury alone does not require clearance, matching v2. Recorded
        // expert dissent (9 missed clearance cases, all 6 round-1 misses involving
        // knee injury) remains pending expert elicitation; it is not an ACSM rule.
        // Agency policy for Not Recommended belongs in RegistrationRulesHelper.
        // No prediction, label, trail, or ML-service dependency belongs here.
        public static bool RequiresMedicalClearance(bool hasSignsSymptoms, bool hasCvd)
            => hasSignsSymptoms || hasCvd;
    }
}
