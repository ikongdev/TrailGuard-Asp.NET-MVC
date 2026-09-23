using System.Collections.Generic;
using TrailGuard.Services;

namespace TrailGuard.Models
{
    public class AccountManagementViewModel
    {
        public int TotalAccounts { get; set; }
        public int TotalAdmins { get; set; }
        public int TotalOrganizers { get; set; }
        public int TotalParticipants { get; set; }
        public int ActiveAccounts { get; set; }

        public List<AccountItemViewModel> Accounts { get; set; } = new List<AccountItemViewModel>();
    }

    public class AccountItemViewModel
    {
        public string ? Id { get; set; }
        public string ? FullName { get; set; }
        public string ? Initials { get; set; }
        public string ? Email { get; set; }






        public RoleIntegrityStatus RoleStatus { get; set; }
        public List<string> AssignedRoles { get; set; } = new();

        public string? ProfilePictureUrl { get; set; }
        public bool IsActive { get; set; }
        public string ? DateCreated { get; set; }





        public string? DateCreatedIso { get; set; }




        public bool IsCurrentUser { get; set; }
    }
}
