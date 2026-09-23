namespace TrailGuard.Services
{
    public static class AcsmClearanceService
    {














        public static bool RequiresMedicalClearance(bool hasSignsSymptoms, bool hasCvd)
            => hasSignsSymptoms || hasCvd;
    }
}
