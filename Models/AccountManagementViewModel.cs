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

        // RoleAssignmentService.GetRoleIntegrityAsync is what populates these -
        // Admin/Organizer/Participant when the account holds exactly one
        // operational role, Conflict/Missing otherwise. AssignedRoles carries
        // the actual role name(s) so a Conflict row can show what's assigned
        // instead of just the fact that something is wrong.
        public RoleIntegrityStatus RoleStatus { get; set; }
        public List<string> AssignedRoles { get; set; } = new();

        public string? ProfilePictureUrl { get; set; }
        public bool IsActive { get; set; }
        public string ? DateCreated { get; set; }

        // ISO 8601 - kept separate from the display-formatted DateCreated
        // above (same split Records/Index.cshtml uses for its date columns)
        // so client-side Newest/Oldest sorting has a real timestamp to
        // compare instead of parsing the "MMM dd, yyyy" display string.
        public string? DateCreatedIso { get; set; }

        // Drives whether the row's role-change control is shown at all - a
        // normally configured single-role Admin can never change their own
        // role through this page (see RoleAssignmentService.ReplaceRoleAsync).
        public bool IsCurrentUser { get; set; }
    }
}