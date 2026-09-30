namespace TrailGuard.Services;

public static class ParticipantDemographics
{
    public const string Male = "Male";
    public const string Female = "Female";
    public const string PreferNotToSay = "Prefer not to say";

    public static readonly IReadOnlySet<string> Genders = new HashSet<string>(StringComparer.Ordinal)
    {
        Male, Female, PreferNotToSay
    };

    public static bool IsValidGender(string? gender) => !string.IsNullOrWhiteSpace(gender) && Genders.Contains(gender);

    public static bool IsValidBirthday(DateOnly birthday, DateOnly today) => birthday <= today;

    /// <summary>
    /// Calculates completed years. A February 29 birthday uses February 28 as its anniversary in non-leap years.
    /// </summary>
    public static int CalculateCompletedYears(DateOnly birthday, DateOnly today)
    {
        var anniversaryDay = birthday.Day;
        if (birthday.Month == 2 && birthday.Day == 29 && !DateTime.IsLeapYear(today.Year)) anniversaryDay = 28;

        var age = today.Year - birthday.Year;
        if (today.Month < birthday.Month || (today.Month == birthday.Month && today.Day < anniversaryDay)) age--;
        return age;
    }

    public static int? CurrentAgeOrNull(DateOnly? birthday, DateOnly today) =>
        birthday.HasValue && IsValidBirthday(birthday.Value, today)
            ? CalculateCompletedYears(birthday.Value, today)
            : null;

    public static ParticipantDemographicsResult Resolve(DateOnly? birthday, string? gender, DateOnly today)
    {
        var age = CurrentAgeOrNull(birthday, today);
        if (!birthday.HasValue) return ParticipantDemographicsResult.Incomplete("Add your Birthday in Settings before continuing.");
        if (!age.HasValue) return ParticipantDemographicsResult.Incomplete("Update your Birthday in Settings before continuing.");
        if (!IsValidGender(gender)) return ParticipantDemographicsResult.Incomplete("Select your Gender in Settings before continuing.");

        return ParticipantDemographicsResult.Complete(age.Value, gender!);
    }
}

public sealed record ParticipantDemographicsResult(bool IsComplete, int? Age, string? Gender, string? Error)
{
    public static ParticipantDemographicsResult Complete(int age, string gender) => new(true, age, gender, null);
    public static ParticipantDemographicsResult Incomplete(string error) => new(false, null, null, error);
}
