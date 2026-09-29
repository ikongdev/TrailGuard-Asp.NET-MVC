using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrailGuard.Migrations
{
    /// <inheritdoc />
    public partial class AddTrailGuardV2ResultProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(name: "CompletionProbability", table: "SuitabilityResults", newName: "ModelScore");
            migrationBuilder.RenameColumn(name: "PredictedLabel", table: "SuitabilityResults", newName: "UiLabel");
            migrationBuilder.RenameColumn(name: "ImpactValue", table: "ShapValues", newName: "ShapContribution");
            migrationBuilder.RenameColumn(name: "RawValue", table: "ShapValues", newName: "OriginalInputValue");

            migrationBuilder.AddColumn<string>(
                name: "BinaryPrediction",
                table: "SuitabilityResults",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "BinaryThreshold",
                table: "SuitabilityResults",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "BorderlineMaximum",
                table: "SuitabilityResults",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BorderlineMaximumOperator",
                table: "SuitabilityResults",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "BorderlineMinimum",
                table: "SuitabilityResults",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BorderlineMinimumOperator",
                table: "SuitabilityResults",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FrozenModelSha256",
                table: "SuitabilityResults",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoodMatchOperator",
                table: "SuitabilityResults",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "GoodMatchThreshold",
                table: "SuitabilityResults",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotRecommendedOperator",
                table: "SuitabilityResults",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "NotRecommendedThreshold",
                table: "SuitabilityResults",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScoreName",
                table: "SuitabilityResults",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SelectedTreeCount",
                table: "SuitabilityResults",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ShapBaseValue",
                table: "SuitabilityResults",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShapContributionInterpretation",
                table: "SuitabilityResults",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ShapRawMargin",
                table: "SuitabilityResults",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShapScale",
                table: "SuitabilityResults",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ShapVerificationAdditivityError",
                table: "SuitabilityResults",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShapVerificationFeatureOrderMatchesFrozenSchema",
                table: "SuitabilityResults",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ShapVerificationPredictionChangeAfterExplanation",
                table: "SuitabilityResults",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShapVerificationPredictionsUnchanged",
                table: "SuitabilityResults",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ShapVerificationProbabilityReconstructionError",
                table: "SuitabilityResults",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ShapVerificationToleranceAbsolute",
                table: "SuitabilityResults",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShapVerificationWithinTolerance",
                table: "SuitabilityResults",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UiLabelPolicyVersion",
                table: "SuitabilityResults",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CardioDuration",
                table: "SuitabilityResults",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DistanceKm",
                table: "SuitabilityResults",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ElevationGainM",
                table: "SuitabilityResults",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExerciseConsistency",
                table: "SuitabilityResults",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExerciseFrequency",
                table: "SuitabilityResults",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GearScore",
                table: "SuitabilityResults",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HardestTrailCompleted",
                table: "SuitabilityResults",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HikingExperience",
                table: "SuitabilityResults",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HikingRecency",
                table: "SuitabilityResults",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TrailClass",
                table: "SuitabilityResults",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "TypicalDurationHours",
                table: "SuitabilityResults",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FeatureOrder",
                table: "ShapValues",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BinaryPrediction",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "BinaryThreshold",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "BorderlineMaximum",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "BorderlineMaximumOperator",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "BorderlineMinimum",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "BorderlineMinimumOperator",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "FrozenModelSha256",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "GoodMatchOperator",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "GoodMatchThreshold",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "NotRecommendedOperator",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "NotRecommendedThreshold",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "ScoreName",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "SelectedTreeCount",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "ShapBaseValue",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "ShapContributionInterpretation",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "ShapRawMargin",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "ShapScale",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "ShapVerificationAdditivityError",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "ShapVerificationFeatureOrderMatchesFrozenSchema",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "ShapVerificationPredictionChangeAfterExplanation",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "ShapVerificationPredictionsUnchanged",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "ShapVerificationProbabilityReconstructionError",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "ShapVerificationToleranceAbsolute",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "ShapVerificationWithinTolerance",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "UiLabelPolicyVersion",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "CardioDuration",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "DistanceKm",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "ElevationGainM",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "ExerciseConsistency",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "ExerciseFrequency",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "GearScore",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "HardestTrailCompleted",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "HikingExperience",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "HikingRecency",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "TrailClass",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "TypicalDurationHours",
                table: "SuitabilityResults");

            migrationBuilder.DropColumn(
                name: "FeatureOrder",
                table: "ShapValues");

            migrationBuilder.RenameColumn(name: "ModelScore", table: "SuitabilityResults", newName: "CompletionProbability");
            migrationBuilder.RenameColumn(name: "UiLabel", table: "SuitabilityResults", newName: "PredictedLabel");
            migrationBuilder.RenameColumn(name: "ShapContribution", table: "ShapValues", newName: "ImpactValue");
            migrationBuilder.RenameColumn(name: "OriginalInputValue", table: "ShapValues", newName: "RawValue");
        }
    }
}
