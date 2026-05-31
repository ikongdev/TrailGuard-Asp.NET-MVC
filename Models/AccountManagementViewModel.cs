using System.Collections.Generic;

namespace TrailGuard.Models
{
    public class AccountManagementViewModel
    {
        public int TotalAccounts { get; set; }
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
        public string ? Role { get; set; }
        public bool IsActive { get; set; }
        public string ? DateCreated { get; set; }
    }
}