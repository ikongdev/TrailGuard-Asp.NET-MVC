namespace TrailGuard.Models
{
    public class RegistrationWithAssessmentViewModel
    {
        public int RegistrationId { get; set; }
        public int EventId { get; set; }
        public string EventTitle { get; set; } = string.Empty;
        public string EventDate { get; set; } = string.Empty;
        public string EventTime { get; set; } = string.Empty;
        public string EventDifficulty { get; set; } = string.Empty;
        public string ParticipantName { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PickupPoint { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime RegisteredAt { get; set; }
        public bool IsPaid { get; set; }
        public string? PaymentReceiptUrl { get; set; }
        public string? EmergencyContactName { get; set; }
        public string? EmergencyContactNumber { get; set; }
        public int? AssessmentId { get; set; }
        public string? AssessmentResult { get; set; }
        public int? AssessmentTotalScore { get; set; }
        public string? MedicalConditions { get; set; }
        public string? FitnessLevel { get; set; }
        public string? HikingExperience { get; set; }
        public string? GearItems { get; set; }
    }
}