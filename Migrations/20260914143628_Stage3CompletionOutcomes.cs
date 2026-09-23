using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrailGuard.Migrations
{

    public partial class Stage3CompletionOutcomes : Migration
    {

        protected override void Up(MigrationBuilder migrationBuilder)
        {


            migrationBuilder.Sql("DELETE FROM \"FinalSuitabilityLabels\";");

            migrationBuilder.DropForeignKey(name: "FK_FinalSuitabilityLabels_EventRegistrations_RegistrationId", table: "FinalSuitabilityLabels");
            migrationBuilder.DropForeignKey(name: "FK_FinalSuitabilityLabels_Events_EventId", table: "FinalSuitabilityLabels");
            migrationBuilder.DropForeignKey(name: "FK_FinalSuitabilityLabels_Users_UserId", table: "FinalSuitabilityLabels");
            migrationBuilder.DropIndex(name: "IX_FinalSuitabilityLabels_AssessmentId", table: "FinalSuitabilityLabels");
            migrationBuilder.DropIndex(name: "IX_FinalSuitabilityLabels_EventId", table: "FinalSuitabilityLabels");
            migrationBuilder.DropIndex(name: "IX_FinalSuitabilityLabels_RegistrationId", table: "FinalSuitabilityLabels");
            migrationBuilder.DropIndex(name: "IX_FinalSuitabilityLabels_UserId", table: "FinalSuitabilityLabels");
            migrationBuilder.DropIndex(name: "IX_EventFeedbacks_EventId", table: "EventFeedbacks");
            migrationBuilder.DropColumn(name: "EventId", table: "FinalSuitabilityLabels");
            migrationBuilder.DropColumn(name: "FinalLabel", table: "FinalSuitabilityLabels");
            migrationBuilder.DropColumn(name: "ModelPreHikeLabel", table: "FinalSuitabilityLabels");
            migrationBuilder.DropColumn(name: "OrganizerAssessment", table: "FinalSuitabilityLabels");
            migrationBuilder.DropColumn(name: "PreHikeLabel", table: "FinalSuitabilityLabels");
            migrationBuilder.DropColumn(name: "RegistrationId", table: "FinalSuitabilityLabels");
            migrationBuilder.RenameColumn(name: "UserId", table: "FinalSuitabilityLabels", newName: "NonCompletionReason");
            migrationBuilder.RenameColumn(name: "ResolvedAt", table: "FinalSuitabilityLabels", newName: "RecordedAt");
            migrationBuilder.RenameColumn(name: "ParticipantFeedback", table: "FinalSuitabilityLabels", newName: "DifficultyExperience");

            migrationBuilder.AddColumn<bool>(name: "Completed", table: "EventFeedbacks", type: "boolean", nullable: true);
            migrationBuilder.AddColumn<string>(name: "NonCompletionReason", table: "EventFeedbacks", type: "text", nullable: true);
            migrationBuilder.AddColumn<bool>(name: "Completed", table: "PostEventAssessments", type: "boolean", nullable: true);
            migrationBuilder.AddColumn<string>(name: "NonCompletionReason", table: "PostEventAssessments", type: "text", nullable: true);
            migrationBuilder.AddColumn<bool>(name: "Completed", table: "FinalSuitabilityLabels", type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.CreateIndex(name: "IX_FinalSuitabilityLabels_AssessmentId", table: "FinalSuitabilityLabels", column: "AssessmentId", unique: true);
            migrationBuilder.CreateIndex(name: "IX_EventFeedbacks_EventId_UserId", table: "EventFeedbacks", columns: new[] { "EventId", "UserId" }, unique: true);
            migrationBuilder.AddCheckConstraint(name: "CK_FinalSuitabilityLabels_CompletionReason", table: "FinalSuitabilityLabels",
                sql: "(\"Completed\" AND \"NonCompletionReason\" = 'NotApplicable') OR (NOT \"Completed\" AND \"NonCompletionReason\" IN ('Readiness', 'External', 'Withdrawal'))");

        }


        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(name: "CK_FinalSuitabilityLabels_CompletionReason", table: "FinalSuitabilityLabels");
            migrationBuilder.DropIndex(name: "IX_FinalSuitabilityLabels_AssessmentId", table: "FinalSuitabilityLabels");
            migrationBuilder.DropIndex(name: "IX_EventFeedbacks_EventId_UserId", table: "EventFeedbacks");
            migrationBuilder.DropColumn(name: "Completed", table: "EventFeedbacks");
            migrationBuilder.DropColumn(name: "NonCompletionReason", table: "EventFeedbacks");
            migrationBuilder.DropColumn(name: "Completed", table: "PostEventAssessments");
            migrationBuilder.DropColumn(name: "NonCompletionReason", table: "PostEventAssessments");
            migrationBuilder.DropColumn(name: "Completed", table: "FinalSuitabilityLabels");
            migrationBuilder.RenameColumn(name: "RecordedAt", table: "FinalSuitabilityLabels", newName: "ResolvedAt");
            migrationBuilder.RenameColumn(name: "NonCompletionReason", table: "FinalSuitabilityLabels", newName: "UserId");
            migrationBuilder.RenameColumn(name: "DifficultyExperience", table: "FinalSuitabilityLabels", newName: "ParticipantFeedback");
            migrationBuilder.AddColumn<int>(name: "EventId", table: "FinalSuitabilityLabels", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<string>(name: "FinalLabel", table: "FinalSuitabilityLabels", type: "text", nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(name: "ModelPreHikeLabel", table: "FinalSuitabilityLabels", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "OrganizerAssessment", table: "FinalSuitabilityLabels", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "PreHikeLabel", table: "FinalSuitabilityLabels", type: "text", nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<int>(name: "RegistrationId", table: "FinalSuitabilityLabels", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.CreateIndex(name: "IX_FinalSuitabilityLabels_AssessmentId", table: "FinalSuitabilityLabels", column: "AssessmentId");
            migrationBuilder.CreateIndex(name: "IX_FinalSuitabilityLabels_EventId", table: "FinalSuitabilityLabels", column: "EventId");
            migrationBuilder.CreateIndex(name: "IX_FinalSuitabilityLabels_RegistrationId", table: "FinalSuitabilityLabels", column: "RegistrationId", unique: true);
            migrationBuilder.CreateIndex(name: "IX_FinalSuitabilityLabels_UserId", table: "FinalSuitabilityLabels", column: "UserId");
            migrationBuilder.CreateIndex(name: "IX_EventFeedbacks_EventId", table: "EventFeedbacks", column: "EventId");
            migrationBuilder.AddForeignKey(name: "FK_FinalSuitabilityLabels_EventRegistrations_RegistrationId", table: "FinalSuitabilityLabels", column: "RegistrationId", principalTable: "EventRegistrations", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            migrationBuilder.AddForeignKey(name: "FK_FinalSuitabilityLabels_Events_EventId", table: "FinalSuitabilityLabels", column: "EventId", principalTable: "Events", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            migrationBuilder.AddForeignKey(name: "FK_FinalSuitabilityLabels_Users_UserId", table: "FinalSuitabilityLabels", column: "UserId", principalTable: "Users", principalColumn: "Id", onDelete: ReferentialAction.Cascade);

        }
    }
}
