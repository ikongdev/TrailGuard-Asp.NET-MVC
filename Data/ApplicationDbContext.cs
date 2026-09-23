using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TrailGuard.Models;

namespace TrailGuard.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser> 
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Trail> Trails { get; set; }
        public DbSet<TrailPhoto> TrailPhotos { get; set; }
        public DbSet<Event> Events { get; set; }
        public DbSet<EventRegistration> EventRegistrations { get; set; }
        public DbSet<EventFeedback> EventFeedbacks { get; set; }
        public DbSet<Assessment> Assessments { get; set; }
        public DbSet<PostEventAssessment> PostEventAssessments { get; set; }

        public DbSet<SuitabilityResult> SuitabilityResults { get; set; }
        public DbSet<ShapValue> ShapValues { get; set; }
        public DbSet<FinalSuitabilityLabel> FinalSuitabilityLabels { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<Trail>().ToTable("Trails", table =>
                table.HasCheckConstraint("CK_Trails_TypicalDurationHours_Positive", "\"TypicalDurationHours\" > 0"));
            builder.Entity<Event>().ToTable("Events", table =>
                table.HasCheckConstraint("CK_Events_TrailDurationHoursSnapshot_Positive", "\"TrailDurationHoursSnapshot\" > 0"));

            builder.Entity<ApplicationUser>().ToTable("Users");
            builder.Entity<IdentityRole>().ToTable("Roles");
            builder.Entity<IdentityUserRole<string>>().ToTable("UserRoles");
            builder.Entity<IdentityUserClaim<string>>().ToTable("UserClaims");
            builder.Entity<IdentityUserLogin<string>>().ToTable("UserLogins");
            builder.Entity<IdentityRoleClaim<string>>().ToTable("RoleClaims");
            builder.Entity<IdentityUserToken<string>>().ToTable("UserTokens");

            builder.Entity<FinalSuitabilityLabel>()
                .ToTable("FinalSuitabilityLabels", table => table.HasCheckConstraint(
                    "CK_FinalSuitabilityLabels_CompletionReason",
                    "(\"Completed\" AND \"NonCompletionReason\" = 'NotApplicable') OR (NOT \"Completed\" AND \"NonCompletionReason\" IN ('Readiness', 'External', 'Withdrawal'))"));
            builder.Entity<FinalSuitabilityLabel>()
                .HasIndex(f => f.AssessmentId)
                .IsUnique();




            builder.Entity<EventFeedback>()
                .HasIndex(f => new { f.EventId, f.UserId })
                .IsUnique();










            builder.Entity<ApplicationUser>()
                .HasIndex(u => u.PublicProfileId)
                .IsUnique();







            builder.Entity<Event>()
                .HasOne(e => e.Trail)
                .WithMany()
                .HasForeignKey(e => e.TrailId)
                .OnDelete(DeleteBehavior.Restrict);











            builder.Entity<Event>()
                .HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(e => e.OrganizerId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Event>()
                .HasIndex(e => e.OrganizerId);
        }
    }
}
