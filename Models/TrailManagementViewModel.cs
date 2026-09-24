namespace TrailGuard.Models
{




    public class TrailManagementViewModel
    {

        public List<Trail> ActiveTrails { get; set; } = new List<Trail>();




        public int ActiveTrailCount { get; set; }

        public int DeactivatedTrailCount { get; set; }

        public List<DeactivatedTrailRowViewModel> DeactivatedTrails { get; set; } = new List<DeactivatedTrailRowViewModel>();

        public AddTrailInputModel AddTrail { get; set; } = new();
    }




    public class DeactivatedTrailRowViewModel
    {
        public int TrailId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;

        public int UpcomingCount { get; set; }
        public int CompletedCount { get; set; }
        public int CancelledCount { get; set; }




        public int OtherCount { get; set; }

        public int TotalCount { get; set; }
    }
}
