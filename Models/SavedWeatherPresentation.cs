using TrailGuard.Services;

namespace TrailGuard.Models;

public enum SavedWeatherSource
{
    MatchingSnapshot,
    LegacyAdvisory,
    MismatchedSnapshot,
    InvalidSnapshot,
    Missing
}

public sealed record SavedWeatherDisplayOptions(bool ShowRiskBadge = true, bool ShowReminder = true)
{
    public static SavedWeatherDisplayOptions Dashboard { get; } = new(ShowRiskBadge: false, ShowReminder: false);
}

public sealed class SavedWeatherPresentation
{
    private SavedWeatherPresentation(
        SavedWeatherSource source,
        WeatherSnapshot? snapshot,
        string? advisory,
        string? riskLevel,
        string? reminder,
        SavedWeatherDisplayOptions displayOptions)
    {
        Source = source;
        Snapshot = snapshot;
        Advisory = advisory;
        RiskLevel = riskLevel;
        Reminder = reminder;
        DisplayOptions = displayOptions;
    }

    public SavedWeatherSource Source { get; }
    public WeatherSnapshot? Snapshot { get; }
    public string? Advisory { get; }
    public string? RiskLevel { get; }
    public string? Reminder { get; }
    public SavedWeatherDisplayOptions DisplayOptions { get; }

    public bool HasMatchingSnapshot => Source == SavedWeatherSource.MatchingSnapshot;
    public bool HasLegacyAdvisory => !string.IsNullOrWhiteSpace(Advisory);
    public bool HasReminder => !string.IsNullOrWhiteSpace(Reminder);

    public string CollapsedLabel => Source switch
    {
        SavedWeatherSource.MatchingSnapshot => "Saved forecast",
        SavedWeatherSource.LegacyAdvisory => "Legacy saved advisory",
        SavedWeatherSource.MismatchedSnapshot => "Saved advisory",
        SavedWeatherSource.InvalidSnapshot => "Saved advisory",
        _ => "Weather not recorded"
    };

    public string? FallbackNotice => Source switch
    {
        SavedWeatherSource.MismatchedSnapshot => "This structured forecast was saved for a previous trail or date and no longer matches this event. Showing the last saved advisory instead.",
        SavedWeatherSource.InvalidSnapshot => "This event's structured forecast is unavailable. Showing the last saved advisory instead.",
        _ => null
    };

    public static SavedWeatherPresentation Create(Event eventItem, SavedWeatherDisplayOptions? displayOptions = null)
    {
        displayOptions ??= new SavedWeatherDisplayOptions();
        var snapshot = WeatherSnapshotHelper.TryDeserialize(eventItem.WeatherSnapshotJson);
        if (snapshot != null && WeatherSnapshotHelper.TryValidateForSubmission(snapshot, eventItem.TrailId, eventItem.EventDate, out _))
        {
            return new SavedWeatherPresentation(
                SavedWeatherSource.MatchingSnapshot,
                snapshot,
                advisory: null,
                riskLevel: snapshot.RiskLevel,
                reminder: eventItem.WeatherReminder,
                displayOptions: displayOptions);
        }

        var source = snapshot != null
            ? SavedWeatherSource.MismatchedSnapshot
            : !string.IsNullOrWhiteSpace(eventItem.WeatherSnapshotJson)
                ? SavedWeatherSource.InvalidSnapshot
                : !string.IsNullOrWhiteSpace(eventItem.WeatherForecastAdvisory)
                    ? SavedWeatherSource.LegacyAdvisory
                    : SavedWeatherSource.Missing;

        return new SavedWeatherPresentation(
            source,
            snapshot: null,
            advisory: eventItem.WeatherForecastAdvisory,
            riskLevel: eventItem.WeatherRiskLevel,
            reminder: eventItem.WeatherReminder,
            displayOptions: displayOptions);
    }

    public static string RiskBadgeClasses(string? riskLevel) => riskLevel switch
    {
        "Low" => "bg-green-500/20 text-green-400 border-green-500/30",
        "Moderate" => "bg-amber-500/20 text-amber-400 border-amber-500/30",
        "Moderate to High" => "bg-orange-500/20 text-orange-400 border-orange-500/30",
        "High (Thunderstorm)" => "bg-red-500/20 text-red-400 border-red-500/30",
        _ => "bg-gray-500/20 text-gray-400 border-gray-500/30"
    };
}
