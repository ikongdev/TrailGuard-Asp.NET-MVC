using System.Reflection;
using TrailGuard.Controllers;
using TrailGuard.Models;
using TrailGuard.Services;

var assertions = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    assertions++;
}

var matchingEvent = NewEvent();
var matchingSnapshot = new WeatherSnapshot
{
    TrailId = matchingEvent.TrailId,
    ForecastDate = matchingEvent.EventDate,
    Condition = "Partly cloudy",
    WeatherCode = 2,
    TemperatureMinC = 21,
    TemperatureMaxC = 29,
    ExpectedRainfallMm = 3.5,
    WindSpeedKmh = 14,
    WindDescription = "Light breeze",
    RiskLevel = "Moderate",
    UpdatedAt = new DateTimeOffset(2026, 10, 1, 3, 30, 0, TimeSpan.Zero)
};
matchingEvent.WeatherSnapshotJson = WeatherSnapshotHelper.Serialize(matchingSnapshot);
matchingEvent.WeatherForecastAdvisory = "Older advisory must not override a matching structured snapshot.";
matchingEvent.WeatherRiskLevel = "Low";
matchingEvent.WeatherReminder = "Organizer-authored reminder.";

var dashboardWeather = SavedWeatherPresentation.Create(matchingEvent);
var detailsWeather = SavedWeatherPresentation.Create(matchingEvent);
Check(dashboardWeather.Source == SavedWeatherSource.MatchingSnapshot, "Matching snapshot was not selected for Dashboard weather.");
Check(detailsWeather.Source == SavedWeatherSource.MatchingSnapshot, "Matching snapshot was not selected for Event Details weather.");
Check(dashboardWeather.Snapshot?.Condition == detailsWeather.Snapshot?.Condition
      && dashboardWeather.Snapshot?.RiskLevel == detailsWeather.Snapshot?.RiskLevel
      && dashboardWeather.Snapshot?.TemperatureMinC == detailsWeather.Snapshot?.TemperatureMinC
      && dashboardWeather.Snapshot?.TemperatureMaxC == detailsWeather.Snapshot?.TemperatureMaxC
      && dashboardWeather.Snapshot?.ExpectedRainfallMm == detailsWeather.Snapshot?.ExpectedRainfallMm
      && dashboardWeather.Snapshot?.WindSpeedKmh == detailsWeather.Snapshot?.WindSpeedKmh
      && dashboardWeather.Snapshot?.UpdatedAt == detailsWeather.Snapshot?.UpdatedAt,
    "Dashboard and Event Details did not resolve identical matching snapshot values.");
Check(dashboardWeather.RiskLevel == "Moderate" && dashboardWeather.Reminder == "Organizer-authored reminder.", "Matching snapshot risk or saved reminder was not preserved.");
Check(dashboardWeather.Advisory is null, "Matching snapshot incorrectly used the legacy advisory.");

var dashboardDisplayWeather = SavedWeatherPresentation.Create(matchingEvent, SavedWeatherDisplayOptions.Dashboard);
Check(!dashboardDisplayWeather.DisplayOptions.ShowRiskBadge && !dashboardDisplayWeather.DisplayOptions.ShowReminder,
    "Dashboard weather display options did not suppress duplicate risk and reminder sections.");
Check(detailsWeather.DisplayOptions.ShowRiskBadge && detailsWeather.DisplayOptions.ShowReminder,
    "Default Event Details weather display options did not retain the risk and reminder sections.");

var legacyEvent = NewEvent();
legacyEvent.WeatherForecastAdvisory = "Legacy saved advisory.";
legacyEvent.WeatherRiskLevel = "Low";
legacyEvent.WeatherReminder = "Legacy organizer reminder.";
var legacyWeather = SavedWeatherPresentation.Create(legacyEvent);
Check(legacyWeather.Source == SavedWeatherSource.LegacyAdvisory, "Legacy advisory was not identified.");
Check(legacyWeather.Advisory == "Legacy saved advisory." && legacyWeather.RiskLevel == "Low", "Legacy advisory values were not preserved.");
Check(legacyWeather.CollapsedLabel == "Legacy saved advisory", "Legacy advisory was not clearly labelled as legacy.");
Check(legacyWeather.Snapshot is null && legacyWeather.Reminder == "Legacy organizer reminder.", "Legacy advisory incorrectly invented structured data or lost its reminder.");

var malformedEvent = NewEvent();
malformedEvent.WeatherSnapshotJson = "{ malformed";
malformedEvent.WeatherForecastAdvisory = "Fallback advisory.";
var malformedWeather = SavedWeatherPresentation.Create(malformedEvent);
Check(malformedWeather.Source == SavedWeatherSource.InvalidSnapshot, "Malformed snapshot was not identified.");
Check(malformedWeather.Snapshot is null && malformedWeather.Advisory == "Fallback advisory." && malformedWeather.FallbackNotice is not null,
    "Malformed snapshot incorrectly exposed structured values or lost its safe advisory fallback.");

var mismatchedEvent = NewEvent();
var mismatchedSnapshot = new WeatherSnapshot
{
    TrailId = mismatchedEvent.TrailId + 1,
    ForecastDate = mismatchedEvent.EventDate,
    Condition = "Must not be displayed as current",
    RiskLevel = "High (Thunderstorm)",
    UpdatedAt = DateTimeOffset.UtcNow
};
mismatchedEvent.WeatherSnapshotJson = WeatherSnapshotHelper.Serialize(mismatchedSnapshot);
mismatchedEvent.WeatherForecastAdvisory = "Last saved advisory.";
mismatchedEvent.WeatherRiskLevel = "Moderate to High";
var mismatchedWeather = SavedWeatherPresentation.Create(mismatchedEvent);
Check(mismatchedWeather.Source == SavedWeatherSource.MismatchedSnapshot, "Trail/date-mismatched snapshot was not identified.");
Check(mismatchedWeather.Snapshot is null && mismatchedWeather.Advisory == "Last saved advisory." && mismatchedWeather.RiskLevel == "Moderate to High",
    "Mismatched structured data was presented as current or its legacy fallback was not retained.");
Check(mismatchedWeather.FallbackNotice?.Contains("previous trail or date", StringComparison.Ordinal) == true,
    "Mismatched snapshot warning was not preserved.");

var missingEvent = NewEvent();
var missingWeather = SavedWeatherPresentation.Create(missingEvent);
Check(missingWeather.Source == SavedWeatherSource.Missing && !missingWeather.HasLegacyAdvisory && !missingWeather.HasReminder,
    "Missing saved weather was not handled as an unavailable saved state.");

var before = (matchingEvent.WeatherSnapshotJson, matchingEvent.WeatherForecastAdvisory, matchingEvent.WeatherRiskLevel, matchingEvent.WeatherReminder);
_ = SavedWeatherPresentation.Create(matchingEvent);
Check(before == (matchingEvent.WeatherSnapshotJson, matchingEvent.WeatherForecastAdvisory, matchingEvent.WeatherRiskLevel, matchingEvent.WeatherReminder),
    "Saved-weather presentation mutated the Event.");
Check(typeof(ParticipantController).GetMethod("GetEventWeather", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is null,
    "Participant weather endpoint still exists.");
Check(!typeof(ParticipantController).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
        .Any(field => field.FieldType == typeof(WeatherService)),
    "Participant weather viewing still retains a WeatherService dependency.");

Console.WriteLine($"PASS: {assertions} participant saved-weather presentation assertions.");

static Event NewEvent() => new()
{
    Id = 1,
    TrailId = 42,
    EventDate = new DateTime(2026, 10, 15),
    EventTitle = "Verification Event"
};
