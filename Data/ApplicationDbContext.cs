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

            builder.Entity<ApplicationUser>().ToTable("Users");
            builder.Entity<IdentityRole>().ToTable("Roles");
            builder.Entity<IdentityUserRole<string>>().ToTable("UserRoles");
            builder.Entity<IdentityUserClaim<string>>().ToTable("UserClaims");
            builder.Entity<IdentityUserLogin<string>>().ToTable("UserLogins");
            builder.Entity<IdentityRoleClaim<string>>().ToTable("RoleClaims");
            builder.Entity<IdentityUserToken<string>>().ToTable("UserTokens");

            builder.Entity<FinalSuitabilityLabel>()
                .HasIndex(f => f.RegistrationId)
                .IsUnique();

            // Trail is a shared catalog entity, not owned by any single event - deleting
            // an event must never take a trail's other events down with it, and (more to
            // the point here) deleting a trail must never cascade into the events still
            // referencing it. TrailId is a required int, so EF's convention default would
            // otherwise be Cascade. The controller blocks the deletion first; this is the
            // database-level backstop for a race or any other deletion path.
            builder.Entity<Event>()
                .HasOne(e => e.Trail)
                .WithMany()
                .HasForeignKey(e => e.TrailId)
                .OnDelete(DeleteBehavior.Restrict);

            // Stable Organizer ownership (Event.OrganizerId) - a scalar FK to
            // the Identity users table with deliberately no navigation
            // property on either side (HasOne<ApplicationUser>() takes no
            // navigation expression), so an Event can never accidentally pull
            // an ApplicationUser - and its PasswordHash/SecurityStamp/etc. -
            // into the change tracker or a careless Include(). Restrict, not
            // Cascade: deleting an Organizer account must never take their
            // past Events down with it; a null OrganizerId Event is simply
            // "unresolved ownership," handled explicitly everywhere
            // Organizer-only authorization is checked.
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