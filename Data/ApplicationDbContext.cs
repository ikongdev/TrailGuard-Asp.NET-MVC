using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TrailGuard.Models;
using TrailGuard.Services;

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
        public DbSet<PickupPoint> PickupPoints { get; set; }
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
            builder.Entity<PickupPoint>(entity =>
            {
                entity.ToTable("PickupPoints", table => table.HasCheckConstraint(
                    "CK_PickupPoints_Name_Valid",
                    "length(btrim(\"Name\")) > 0 AND \"Name\" = btrim(\"Name\") AND position(E'\\n' in \"Name\") = 0 AND position(E'\\r' in \"Name\") = 0 AND position('—' in \"Name\") = 0"));
                entity.Property(point => point.Name).HasMaxLength(PickupPointCatalogHelper.MaxNameLength).IsRequired();
                entity.Property(point => point.NormalizedName).HasMaxLength(PickupPointCatalogHelper.MaxNameLength).IsRequired();
                entity.HasIndex(point => point.NormalizedName).IsUnique();
            });

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

            builder.Entity<SuitabilityResult>(entity =>
            {
                entity.Property(result => result.FrozenModelSha256).HasColumnType("text");
                entity.Property(result => result.ScoreName).HasColumnType("text");
                entity.Property(result => result.BinaryPrediction).HasColumnType("text");
                entity.Property(result => result.BinaryThreshold).HasColumnType("double precision");
                entity.Property(result => result.UiLabelPolicyVersion).HasColumnType("text");
                entity.Property(result => result.GoodMatchOperator).HasColumnType("text");
                entity.Property(result => result.GoodMatchThreshold).HasColumnType("double precision");
                entity.Property(result => result.BorderlineMinimumOperator).HasColumnType("text");
                entity.Property(result => result.BorderlineMinimum).HasColumnType("double precision");
                entity.Property(result => result.BorderlineMaximumOperator).HasColumnType("text");
                entity.Property(result => result.BorderlineMaximum).HasColumnType("double precision");
                entity.Property(result => result.NotRecommendedOperator).HasColumnType("text");
                entity.Property(result => result.NotRecommendedThreshold).HasColumnType("double precision");
                entity.Property(result => result.ExerciseFrequency).HasColumnType("text");
                entity.Property(result => result.CardioDuration).HasColumnType("text");
                entity.Property(result => result.ExerciseConsistency).HasColumnType("text");
                entity.Property(result => result.HikingExperience).HasColumnType("text");
                entity.Property(result => result.HikingRecency).HasColumnType("text");
                entity.Property(result => result.HardestTrailCompleted).HasColumnType("text");
                entity.Property(result => result.DistanceKm).HasColumnType("double precision");
                entity.Property(result => result.TypicalDurationHours).HasColumnType("double precision");
                entity.Property(result => result.ShapBaseValue).HasColumnType("double precision");
                entity.Property(result => result.ShapRawMargin).HasColumnType("double precision");
                entity.Property(result => result.ShapScale).HasColumnType("text");
                entity.Property(result => result.ShapContributionInterpretation).HasColumnType("text");
                entity.Property(result => result.ShapVerificationAdditivityError).HasColumnType("double precision");
                entity.Property(result => result.ShapVerificationProbabilityReconstructionError).HasColumnType("double precision");
                entity.Property(result => result.ShapVerificationPredictionChangeAfterExplanation).HasColumnType("double precision");
                entity.Property(result => result.ShapVerificationToleranceAbsolute).HasColumnType("double precision");
            });

            builder.Entity<ShapValue>()
                .Property(value => value.FeatureOrder)
                .HasColumnType("integer");

            builder.Entity<SuitabilityResult>()
                .HasOne(result => result.Assessment)
                .WithMany(assessment => assessment.SuitabilityResults)
                .HasForeignKey(result => result.AssessmentId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Assessment>()
                .HasIndex(assessment => assessment.EventId);

            builder.Entity<Assessment>()
                .HasIndex(assessment => new { assessment.EventId, assessment.UserId })
                .HasDatabaseName(ParticipantEventWorkflowLock.ActiveAssessmentUniqueIndexName)
                .IsUnique()
                .HasFilter("\"IsActive\" = TRUE");




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
