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

    public const int MinimumParticipationAge = 18;
    public const int MaximumParticipationAge = 60;
    public const string ParticipationAgePolicyMessage = "TrailGuard's participation policy is for ages 18 through 60, measured in completed years.";

    public static bool IsParticipationAge(int? completedYears) =>
        completedYears is >= MinimumParticipationAge and <= MaximumParticipationAge;

    public static bool IsParticipationAge(DateOnly birthday, DateOnly today) =>
        IsValidBirthday(birthday, today) && IsParticipationAge(CalculateCompletedYears(birthday, today));

    /// <summary>Returns the oldest birthday eligible today, including the February 29 convention.</summary>
    public static DateOnly OldestEligibleBirthday(DateOnly today)
    {
        var birthday = today.AddYears(-(MaximumParticipationAge + 1)).AddDays(1);
        // DateOnly.AddYears maps Feb 29 to Feb 28. Move forward when that
        // candidate has already reached its February 28 anniversary this year.
        while (!IsParticipationAge(birthday, today)) birthday = birthday.AddDays(1);
        return birthday;
    }

    /// <summary>Returns the youngest birthday eligible today.</summary>
    public static DateOnly YoungestEligibleBirthday(DateOnly today) => today.AddYears(-MinimumParticipationAge);

    public static string? ValidateParticipationBirthday(DateOnly? birthday, DateOnly today)
    {
        if (!birthday.HasValue) return null;
        if (!IsValidBirthday(birthday.Value, today)) return "Birthday cannot be in the future.";
        return IsParticipationAge(birthday.Value, today) ? null : ParticipationAgePolicyMessage;
    }

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
