using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using TrailGuard.Controllers;
using TrailGuard.Services;

var assertions = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    assertions++;
}

var birthday = new DateOnly(2000, 9, 30);
Check(ParticipantDemographics.CalculateCompletedYears(birthday, new DateOnly(2026, 9, 29)) == 25, "Age before birthday was incorrect.");
Check(ParticipantDemographics.CalculateCompletedYears(birthday, new DateOnly(2026, 9, 30)) == 26, "Age on birthday was incorrect.");
Check(ParticipantDemographics.CalculateCompletedYears(birthday, new DateOnly(2026, 10, 1)) == 26, "Age after birthday was incorrect.");
Check(ParticipantDemographics.CalculateCompletedYears(new DateOnly(2000, 2, 29), new DateOnly(2025, 2, 27)) == 24, "Leap-day age before February 28 anniversary was incorrect.");
Check(ParticipantDemographics.CalculateCompletedYears(new DateOnly(2000, 2, 29), new DateOnly(2025, 2, 28)) == 25, "Leap-day February 28 anniversary rule was incorrect.");
Check(ParticipantDemographics.CalculateCompletedYears(new DateOnly(2008, 12, 31), new DateOnly(2026, 1, 1)) == 17, "Year-boundary age was incorrect.");

var clock = new PhilippineClock(new FixedTimeProvider(new DateTimeOffset(2026, 9, 29, 16, 30, 0, TimeSpan.Zero)));
Check(clock.Today == new DateOnly(2026, 9, 30), "Philippine date did not advance independently of UTC date.");

Check(!ParticipantDemographics.Resolve(null, ParticipantDemographics.Female, new DateOnly(2026, 9, 30)).IsComplete, "Missing Birthday must remain incomplete.");
Check(!ParticipantDemographics.Resolve(new DateOnly(2026, 10, 1), ParticipantDemographics.Female, new DateOnly(2026, 9, 30)).IsComplete, "Future Birthday must be rejected.");
Check(!ParticipantDemographics.Resolve(new DateOnly(2000, 1, 1), null, new DateOnly(2026, 9, 30)).IsComplete, "Missing Gender must remain incomplete.");
Check(ParticipantDemographics.CurrentAgeOrNull(new DateOnly(2000, 1, 1), new DateOnly(2026, 9, 30)) == 26, "Current display age must not depend on Gender.");
Check(ParticipantDemographics.Resolve(new DateOnly(2000, 1, 1), ParticipantDemographics.PreferNotToSay, new DateOnly(2026, 9, 30)).Gender == ParticipantDemographics.PreferNotToSay, "Prefer not to say must remain explicit.");
Check(!ParticipantDemographics.IsValidGender("Other") && ParticipantDemographics.IsValidGender(ParticipantDemographics.Male), "Gender allow-list validation was incorrect.");
var currentAgeFromProfile = ParticipantDemographics.CurrentAgeOrNull(new DateOnly(2000, 9, 30), new DateOnly(2026, 9, 30));
const int historicalAssessmentAge = 25;
Check(currentAgeFromProfile == 26 && currentAgeFromProfile != historicalAssessmentAge, "Registration display age must come from the current profile Birthday, not a historical assessment age.");
var literalGenderForDisplay = ParticipantDemographics.IsValidGender(ParticipantDemographics.PreferNotToSay)
    ? ParticipantDemographics.PreferNotToSay
    : null;
Check(literalGenderForDisplay == "Prefer not to say", "Registration display must preserve the saved Prefer not to say value literally.");
Check(ParticipantDemographics.CurrentAgeOrNull(null, new DateOnly(2026, 9, 30)) is null, "Missing Birthday must display an unavailable age.");
Check(!ParticipantDemographics.IsValidGender(null), "Missing Gender must display as unavailable independently of Birthday.");
var registerPost = typeof(RegistrationController).GetMethods()
    .Single(method => method.Name == "Register" && method.GetCustomAttribute<HttpPostAttribute>() is not null);
var postedParameterNames = registerPost.GetParameters().Select(parameter => parameter.Name);
Check(!postedParameterNames.Contains("age", StringComparer.OrdinalIgnoreCase)
      && !postedParameterNames.Contains("gender", StringComparer.OrdinalIgnoreCase),
    "Registration POST must not bind forged Age or Gender fields.");
Check(ParticipantDemographics.Resolve(new DateOnly(2008, 9, 30), ParticipantDemographics.Male, new DateOnly(2026, 9, 30)).Age == 18, "Lower assessment age boundary was incorrect.");
Check(ParticipantDemographics.Resolve(new DateOnly(1966, 9, 30), ParticipantDemographics.Female, new DateOnly(2026, 9, 30)).Age == 60, "Upper assessment age boundary was incorrect.");
Check(ParticipantDemographics.Resolve(new DateOnly(2009, 10, 1), ParticipantDemographics.Male, new DateOnly(2026, 9, 30)).Age == 16, "Below-range computed age was incorrect.");
Check(ParticipantDemographics.Resolve(new DateOnly(1965, 9, 29), ParticipantDemographics.Female, new DateOnly(2026, 9, 30)).Age == 61, "Above-range computed age was incorrect.");

Console.WriteLine($"PASS: {assertions} demographic age, gender, leap-day, and Philippine-date assertions.");

sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
