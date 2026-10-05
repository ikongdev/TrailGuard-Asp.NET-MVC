using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TrailGuard.Data;
using TrailGuard.Models;
using TrailGuard.Services;

const string ParticipantRole = "Participant";
const string DemoPasswordKey = "DemoParticipants:Password";

var preflightOnly = args.Any(argument => string.Equals(argument, "--preflight-only", StringComparison.OrdinalIgnoreCase));
if (args.Any(argument => !string.Equals(argument, "--preflight-only", StringComparison.OrdinalIgnoreCase)))
{
    Console.Error.WriteLine("Usage: dotnet run --project Verification\\TrailGuardDemoParticipantsSeeder\\TrailGuardDemoParticipantsSeeder.csproj [--preflight-only]");
    return 2;
}

var configuration = new ConfigurationBuilder()
    .AddUserSecrets(typeof(ApplicationUser).Assembly, optional: true)
    .AddEnvironmentVariables()
    .Build();

if (!string.Equals(configuration["Database:Target"], nameof(DatabaseTarget.Supabase), StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Blocked: this utility requires an explicitly selected Database:Target=Supabase. It will not default to Local.");
    return 2;
}

ResolvedDatabaseTarget target;
try
{
    target = DatabaseTargetResolver.Resolve(configuration);
}
catch (InvalidOperationException exception)
{
    Console.Error.WriteLine($"Blocked: {exception.Message}");
    return 2;
}

if (target.Target != DatabaseTarget.Supabase)
{
    Console.Error.WriteLine("Blocked: the resolved target is not Supabase.");
    return 2;
}

var plans = DemoParticipantPlan.CreateAll();
var validationErrors = ValidatePlans(plans, DateOnly.FromDateTime(DateTime.UtcNow));
Console.WriteLine("TrailGuard fictional demo participant preflight");
Console.WriteLine($"Target: {target.SafeEndpointDescription}");
Console.WriteLine($"Planned profiles: {plans.Count}");
PrintPlans(plans);

var rosterPath = Path.Combine(Directory.GetCurrentDirectory(), "Verification", "TrailGuardDemoParticipantsSeeder", "artifacts", "demo-participants-roster.csv");
Directory.CreateDirectory(Path.GetDirectoryName(rosterPath)!);
await File.WriteAllTextAsync(rosterPath, BuildRosterCsv(plans));
Console.WriteLine($"Password-free roster: {rosterPath}");

if (validationErrors.Count > 0)
{
    Console.Error.WriteLine("Blocked: offline roster validation failed.");
    foreach (var error in validationErrors) Console.Error.WriteLine($"- {error}");
    return 2;
}

var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(configuration);
services.AddLogging(logging => logging.AddConsole().SetMinimumLevel(LogLevel.Warning));
services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(target.ConnectionString));
services.AddDefaultIdentity<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();
services.AddScoped<RoleAssignmentService>();

await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
var roleAssignmentService = scope.ServiceProvider.GetRequiredService<RoleAssignmentService>();

try
{
    if (!string.Equals(context.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
    {
        Console.Error.WriteLine("Blocked: configured target is not using the PostgreSQL provider.");
        return 2;
    }

    if (!await context.Database.CanConnectAsync())
    {
        Console.Error.WriteLine("Blocked: the configured Supabase database could not be reached.");
        return 2;
    }

    await context.Users.AsNoTracking()
        .Select(user => new { user.Id, user.FirstName, user.MiddleName, user.LastName, user.PublicProfileId, user.Birthday, user.Gender })
        .OrderBy(user => user.Id)
        .Take(1)
        .ToListAsync();

    if (!await roleManager.RoleExistsAsync(ParticipantRole))
    {
        Console.Error.WriteLine("Blocked: the existing Participant role is missing. This utility will not create roles.");
        return 2;
    }
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Blocked: Supabase preflight failed ({exception.GetType().Name}). Check the selected target, schema, and connectivity.");
    return 2;
}

var namespaceResult = await InspectNamespaceAsync(context, userManager, plans);
if (namespaceResult.Conflicts.Count > 0)
{
    Console.Error.WriteLine("Blocked: existing account namespace conflicts were found. No accounts were changed.");
    foreach (var conflict in namespaceResult.Conflicts) Console.Error.WriteLine($"- {conflict}");
    return 2;
}

Console.WriteLine($"Preflight passed: {namespaceResult.ToCreate.Count} account(s) to create; {namespaceResult.ToSkip.Count} identifiable demo account(s) to preserve.");

if (preflightOnly)
{
    Console.WriteLine("Preflight-only mode completed without database writes.");
    return 0;
}

var password = configuration[DemoPasswordKey];
if (string.IsNullOrWhiteSpace(password))
{
    Console.Error.WriteLine($"Blocked: {DemoPasswordKey} is not configured. Set it with:");
    Console.Error.WriteLine("dotnet user-secrets set --project TrailGuard.csproj \"DemoParticipants:Password\" \"<shared-password>\"");
    Console.Error.WriteLine("Or set DemoParticipants__Password for the current process. The password was not printed.");
    return 2;
}

var passwordErrors = await ValidatePasswordAsync(userManager, password);
if (passwordErrors.Count > 0)
{
    Console.Error.WriteLine("Blocked: shared password does not meet the application Identity policy.");
    foreach (var error in passwordErrors) Console.Error.WriteLine($"- {error}");
    return 2;
}

var created = new List<string>();
var skipped = namespaceResult.ToSkip.Select(plan => plan.Email).ToList();
string? failedEmail = null;
string? failureReason = null;

foreach (var plan in namespaceResult.ToCreate)
{
    var creation = await roleAssignmentService.CreateAccountWithRoleAsync(plan.ToUser(), password, ParticipantRole);
    if (!creation.Succeeded)
    {
        failedEmail = plan.Email;
        failureReason = creation.IdentityErrors.Count > 0
            ? string.Join("; ", creation.IdentityErrors)
            : creation.GenericError ?? "Unknown account-creation failure.";
        break;
    }

    created.Add(plan.Email);
    Console.WriteLine($"Created: {plan.Email}");
}

if (failedEmail is not null)
{
    Console.Error.WriteLine($"Stopped after failure for {failedEmail}: {failureReason}");
    Console.Error.WriteLine($"Completed: {created.Count}; skipped: {skipped.Count}; failed: 1. Rerun safely resumes remaining accounts.");
    return 1;
}

var verification = await VerifySeedAsync(context, userManager, plans, password, created);
Console.WriteLine($"Completed: {created.Count}; skipped: {skipped.Count}; failed: {verification.Errors.Count}.");
if (verification.Errors.Count > 0)
{
    Console.Error.WriteLine("Post-seed verification failed:");
    foreach (var error in verification.Errors) Console.Error.WriteLine($"- {error}");
    return 1;
}

Console.WriteLine("Verified: all 50 demo accounts exist, have populated profiles, hold exactly Participant, and accept the configured shared password.");
return 0;

static List<string> ValidatePlans(IReadOnlyList<DemoParticipantPlan> plans, DateOnly today)
{
    var errors = new List<string>();
    if (plans.Count != 50) errors.Add($"Expected exactly 50 plans, found {plans.Count}.");

    var emailValidator = new EmailAddressAttribute();
    var phoneValidator = new PhoneAttribute();
    var urlValidator = new UrlAttribute();
    var emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var ids = new HashSet<Guid>();

    foreach (var plan in plans)
    {
        if (!emails.Add(plan.Email)) errors.Add($"{plan.Email}: duplicate email.");
        if (!ids.Add(plan.PublicProfileId)) errors.Add($"{plan.Email}: duplicate PublicProfileId.");
        if (!emailValidator.IsValid(plan.Email) || !string.Equals(plan.Email, plan.UserName, StringComparison.Ordinal))
            errors.Add($"{plan.Email}: invalid or mismatched email/user name.");
        if (plan.FirstName.Length is 0 or > 50 || plan.LastName.Length is 0 or > 50 || plan.MiddleName.Length > 50)
            errors.Add($"{plan.Email}: invalid name length.");
        if (!ParticipantDemographics.IsValidGender(plan.Gender))
            errors.Add($"{plan.Email}: unsupported gender.");
        var age = ParticipantDemographics.CalculateCompletedYears(plan.Birthday, today);
        if (!ParticipantDemographics.IsValidBirthday(plan.Birthday, today) || age is < 18 or > 55)
            errors.Add($"{plan.Email}: birthday is outside the intended adult demo range.");
        if (!phoneValidator.IsValid(plan.PhoneNumber) || !plan.PhoneNumber.StartsWith("+63 000 ", StringComparison.Ordinal))
            errors.Add($"{plan.Email}: phone placeholder is invalid.");
        if (!urlValidator.IsValid(plan.FacebookLink)
            || !Uri.TryCreate(plan.FacebookLink, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.Host.EndsWith("example.invalid", StringComparison.Ordinal))
            errors.Add($"{plan.Email}: Facebook placeholder must be HTTPS under example.invalid.");
        if (string.IsNullOrWhiteSpace(plan.Bio) || plan.Bio.Length > 500 || !plan.Bio.StartsWith("Fictional demo account", StringComparison.Ordinal))
            errors.Add($"{plan.Email}: bio is missing the fictional-demo notice.");
        if (plan.PublicProfileId == Guid.Empty) errors.Add($"{plan.Email}: PublicProfileId is empty.");
    }

    return errors;
}

static async Task<IReadOnlyList<string>> ValidatePasswordAsync(UserManager<ApplicationUser> userManager, string password)
{
    var probe = new ApplicationUser { UserName = "password-policy-probe@trailguard.invalid", Email = "password-policy-probe@trailguard.invalid" };
    var errors = new List<string>();
    foreach (var validator in userManager.PasswordValidators)
    {
        var result = await validator.ValidateAsync(userManager, probe, password);
        if (!result.Succeeded) errors.AddRange(result.Errors.Select(error => error.Description));
    }
    return errors;
}

static async Task<NamespaceInspection> InspectNamespaceAsync(
    ApplicationDbContext context,
    UserManager<ApplicationUser> userManager,
    IReadOnlyList<DemoParticipantPlan> plans)
{
    var normalizedIdentifiers = plans.Select(plan => plan.Email.ToUpperInvariant()).ToArray();
    var profileIds = plans.Select(plan => plan.PublicProfileId).ToArray();
    var existing = await context.Users
        .Where(user => normalizedIdentifiers.Contains(user.NormalizedEmail!)
            || normalizedIdentifiers.Contains(user.NormalizedUserName!)
            || profileIds.Contains(user.PublicProfileId))
        .ToListAsync();

    var toCreate = new List<DemoParticipantPlan>();
    var toSkip = new List<DemoParticipantPlan>();
    var conflicts = new List<string>();
    foreach (var plan in plans)
    {
        var normalized = plan.Email.ToUpperInvariant();
        var matches = existing.Where(user =>
            string.Equals(user.NormalizedEmail, normalized, StringComparison.Ordinal)
            || string.Equals(user.NormalizedUserName, normalized, StringComparison.Ordinal)
            || user.PublicProfileId == plan.PublicProfileId).ToList();
        if (matches.Count == 0)
        {
            toCreate.Add(plan);
            continue;
        }

        if (matches.Count == 1
            && string.Equals(matches[0].NormalizedEmail, normalized, StringComparison.Ordinal)
            && string.Equals(matches[0].NormalizedUserName, normalized, StringComparison.Ordinal))
        {
            var integrity = OperationalRolePolicy.Evaluate(await userManager.GetRolesAsync(matches[0]));
            if (integrity.Status == RoleIntegrityStatus.Participant)
            {
                toSkip.Add(plan);
                continue;
            }
        }

        conflicts.Add($"{plan.Email}: an existing user conflicts with this demo email, user name, profile ID, or required Participant-only role.");
    }

    return new NamespaceInspection(toCreate, toSkip, conflicts);
}

static async Task<SeedVerification> VerifySeedAsync(
    ApplicationDbContext context,
    UserManager<ApplicationUser> userManager,
    IReadOnlyList<DemoParticipantPlan> plans,
    string password,
    IReadOnlyCollection<string> createdEmails)
{
    var normalized = plans.Select(plan => plan.Email.ToUpperInvariant()).ToArray();
    var users = await context.Users.Where(user => normalized.Contains(user.NormalizedEmail!)).ToListAsync();
    var errors = new List<string>();
    if (users.Count != 50) errors.Add($"Expected 50 demo emails after seeding, found {users.Count}.");

    var phoneValidator = new PhoneAttribute();
    foreach (var plan in plans)
    {
        var user = users.SingleOrDefault(candidate => string.Equals(candidate.NormalizedEmail, plan.Email.ToUpperInvariant(), StringComparison.Ordinal));
        if (user is null)
        {
            errors.Add($"{plan.Email}: missing after seeding.");
            continue;
        }

        if (!user.IsActive
            || string.IsNullOrWhiteSpace(user.FirstName)
            || string.IsNullOrWhiteSpace(user.LastName)
            || !user.Birthday.HasValue
            || !ParticipantDemographics.IsValidGender(user.Gender)
            || string.IsNullOrWhiteSpace(user.PhoneNumber)
            || !phoneValidator.IsValid(user.PhoneNumber)
            || user.PublicProfileId == Guid.Empty
            || !string.Equals(user.Email, plan.Email, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(user.UserName, plan.UserName, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{plan.Email}: required profile data is not populated or valid.");
        }

        if (createdEmails.Contains(plan.Email, StringComparer.OrdinalIgnoreCase)
            && (user.ProfilePictureUrl is not null || user.DateCreated.Kind != DateTimeKind.Utc))
        {
            errors.Add($"{plan.Email}: newly created profile did not preserve null avatar or UTC DateCreated.");
        }

        var integrity = OperationalRolePolicy.Evaluate(await userManager.GetRolesAsync(user));
        if (integrity.Status != RoleIntegrityStatus.Participant)
            errors.Add($"{plan.Email}: role assignment is not exactly Participant.");
        if (!await userManager.CheckPasswordAsync(user, password))
            errors.Add($"{plan.Email}: shared-password verification failed.");
    }

    return new SeedVerification(errors);
}

static string BuildRosterCsv(IReadOnlyList<DemoParticipantPlan> plans)
{
    var builder = new StringBuilder("FirstName,MiddleName,LastName,Email,Birthday,Gender,PhonePlaceholder,Bio\r\n");
    foreach (var plan in plans)
    {
        builder.AppendLine(string.Join(",", new[]
        {
            Csv(plan.FirstName), Csv(plan.MiddleName), Csv(plan.LastName), Csv(plan.Email),
            Csv(plan.Birthday.ToString("yyyy-MM-dd")), Csv(plan.Gender), Csv(plan.PhoneNumber), Csv(plan.Bio)
        }));
    }
    return builder.ToString();
}

static string Csv(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

static void PrintPlans(IEnumerable<DemoParticipantPlan> plans)
{
    foreach (var plan in plans)
    {
        Console.WriteLine($"{plan.Email} | {plan.FirstName} {plan.MiddleName} {plan.LastName} | {plan.Birthday:yyyy-MM-dd} | {plan.Gender} | {plan.PhoneNumber} | {plan.Bio}");
    }
}

sealed record NamespaceInspection(
    IReadOnlyList<DemoParticipantPlan> ToCreate,
    IReadOnlyList<DemoParticipantPlan> ToSkip,
    IReadOnlyList<string> Conflicts);

sealed record SeedVerification(IReadOnlyList<string> Errors);

sealed record DemoParticipantPlan(
    int Number,
    string FirstName,
    string MiddleName,
    string LastName,
    DateOnly Birthday,
    string Gender,
    string Bio)
{
    public string Email => $"participant{Number:00}@trailguard.com";
    public string UserName => Email;
    public string PhoneNumber => $"+63 000 000 {Number:0000}";
    public string FacebookLink => $"https://participant-{Number:000}.example.invalid/";
    public Guid PublicProfileId => DeterministicGuid(Email);

    public ApplicationUser ToUser() => new()
    {
        UserName = UserName,
        Email = Email,
        EmailConfirmed = true,
        FirstName = FirstName,
        MiddleName = MiddleName,
        LastName = LastName,
        IsActive = true,
        DateCreated = DateTime.UtcNow,
        PhoneNumber = PhoneNumber,
        FacebookLink = FacebookLink,
        Bio = Bio,
        Birthday = Birthday,
        Gender = Gender,
        PublicProfileId = PublicProfileId,
        ProfilePictureUrl = null
    };

    public static IReadOnlyList<DemoParticipantPlan> CreateAll() =>
    [
        new(1, "Aira", "Santos", "Bautista", new(2007, 1, 14), ParticipantDemographics.Female, "Fictional demo account for a weekend day-hike enthusiast."),
        new(2, "Benjie", "Cruz", "Alcantara", new(2005, 6, 22), ParticipantDemographics.Male, "Fictional demo account for a beginner trail-walking participant."),
        new(3, "Clarisse", "Reyes", "Dela Cruz", new(2003, 9, 3), ParticipantDemographics.Female, "Fictional demo account for a campus outdoor-club member."),
        new(4, "Darren", "Flores", "Manalo", new(2001, 4, 18), ParticipantDemographics.Male, "Fictional demo account for a recreational mountain hiker."),
        new(5, "Elena", "Garcia", "Navarro", new(1999, 11, 27), ParticipantDemographics.Female, "Fictional demo account for a sunrise-hike volunteer."),
        new(6, "Francis", "Ramos", "Villanueva", new(1997, 2, 9), ParticipantDemographics.Male, "Fictional demo account for a fitness-focused trail learner."),
        new(7, "Gina", "Lopez", "Mercado", new(1995, 7, 16), ParticipantDemographics.Female, "Fictional demo account for a nature-photography hiker."),
        new(8, "Harold", "Mendoza", "Pascual", new(1993, 12, 5), ParticipantDemographics.Male, "Fictional demo account for a local hiking-group member."),
        new(9, "Isabel", "Torres", "Santiago", new(1991, 3, 21), ParticipantDemographics.Female, "Fictional demo account for a careful weekend explorer."),
        new(10, "Jomar", "Aquino", "Fernandez", new(1989, 8, 30), ParticipantDemographics.Male, "Fictional demo account for a casual summit-day participant."),
        new(11, "Katrina", "Diaz", "Soriano", new(1987, 5, 12), ParticipantDemographics.Female, "Fictional demo account for a trail-readiness practice profile."),
        new(12, "Lorenzo", "Castillo", "Aguilar", new(1985, 10, 24), ParticipantDemographics.Male, "Fictional demo account for an organized hiking-event attendee."),
        new(13, "Mariel", "Chua", "Domingo", new(1983, 1, 7), ParticipantDemographics.Female, "Fictional demo account for a low-impact hiking enthusiast."),
        new(14, "Nathan", "Rivera", "Salazar", new(1981, 6, 19), ParticipantDemographics.Male, "Fictional demo account for a trail logistics volunteer."),
        new(15, "Olivia", "Tan", "Cabrera", new(1979, 9, 28), ParticipantDemographics.Female, "Fictional demo account for a community hiking participant."),
        new(16, "Paolo", "Lim", "Valdez", new(1977, 4, 11), ParticipantDemographics.Male, "Fictional demo account for a steady-pace mountain walker."),
        new(17, "Queenie", "Yu", "Macapagal", new(1975, 11, 2), ParticipantDemographics.Female, "Fictional demo account for a fictional trail-planning group."),
        new(18, "Rafael", "Ocampo", "Borja", new(1973, 2, 15), ParticipantDemographics.Male, "Fictional demo account for a park-and-trail regular."),
        new(19, "Sheila", "Gomez", "Lazaro", new(1971, 7, 26), ParticipantDemographics.Female, "Fictional demo account for a supportive hiking companion."),
        new(20, "Tomas", "Velasco", "Balin", new(2006, 12, 8), ParticipantDemographics.Male, "Fictional demo account for a first organized hike."),
        new(21, "Una", "Benedicto", "Carandang", new(2004, 3, 17), ParticipantDemographics.Female, "Fictional demo account for a student outdoor activity."),
        new(22, "Victor", "Natividad", "Espiritu", new(2002, 8, 29), ParticipantDemographics.Male, "Fictional demo account for a conditioning-plan participant."),
        new(23, "Wendy", "Magno", "Hernandez", new(2000, 5, 6), ParticipantDemographics.Female, "Fictional demo account for a guided nature-hike attendee."),
        new(24, "Xavier", "Pineda", "Lacson", new(1998, 10, 13), ParticipantDemographics.Male, "Fictional demo account for a weekend ridge-walk profile."),
        new(25, "Yasmin", "Barrera", "Del Rosario", new(1996, 1, 25), ParticipantDemographics.Female, "Fictional demo account for a hiking-safety workshop member."),
        new(26, "Zandro", "Evangelista", "Magsaysay", new(1994, 6, 4), ParticipantDemographics.Male, "Fictional demo account for a trail-cleanup volunteer."),
        new(27, "Althea", "Fajardo", "Quintos", new(1992, 9, 18), ParticipantDemographics.Female, "Fictional demo account for a moderate-trail participant."),
        new(28, "Bryan", "Guerrero", "Serrano", new(1990, 4, 29), ParticipantDemographics.Male, "Fictional demo account for a morning-hike participant."),
        new(29, "Celine", "Ignacio", "Tiu", new(1988, 11, 10), ParticipantDemographics.Female, "Fictional demo account for an event-readiness demonstration."),
        new(30, "Diego", "Javier", "Umali", new(1986, 2, 23), ParticipantDemographics.Male, "Fictional demo account for a hiking skills learner."),
        new(31, "Erika", "Kasilag", "Ventura", new(1984, 7, 5), ParticipantDemographics.Female, "Fictional demo account for a fictional trail buddy."),
        new(32, "Felix", "Labra", "Yap", new(1982, 12, 19), ParticipantDemographics.Male, "Fictional demo account for a careful ascent participant."),
        new(33, "Grace", "Marquez", "Zulueta", new(1980, 3, 8), ParticipantDemographics.Female, "Fictional demo account for a scenic route explorer."),
        new(34, "Hector", "Nepomuceno", "Abad", new(1978, 8, 21), ParticipantDemographics.Male, "Fictional demo account for an organized outdoor recreation event."),
        new(35, "Ivy", "Ortega", "Belmonte", new(1976, 5, 14), ParticipantDemographics.Female, "Fictional demo account for a responsible hiking participant."),
        new(36, "Jerome", "Palma", "Cunanan", new(1974, 10, 3), ParticipantDemographics.Male, "Fictional demo account for a weekend summit practice profile."),
        new(37, "Kaye", "Quizon", "David", new(1972, 1, 16), ParticipantDemographics.Female, "Fictional demo account for a community trail-walk attendee."),
        new(38, "Liam", "Rosales", "Enriquez", new(2007, 6, 28), ParticipantDemographics.Male, "Fictional demo account for an adult beginner hiking profile."),
        new(39, "Mia", "Sarmiento", "Fortuna", new(2005, 9, 9), ParticipantDemographics.Female, "Fictional demo account for a fictional outdoor-club participant."),
        new(40, "Nico", "Tadeo", "Galvez", new(2003, 4, 20), ParticipantDemographics.Male, "Fictional demo account for a guided day-hike attendee."),
        new(41, "Princess", "Urbano", "Hilario", new(2001, 11, 6), ParticipantDemographics.Female, "Fictional demo account for a weekend nature explorer."),
        new(42, "Rico", "Villar", "Inocencio", new(1999, 2, 18), ParticipantDemographics.Male, "Fictional demo account for a trail orientation participant."),
        new(43, "Sam", "Wong", "Joaquin", new(1997, 7, 31), ParticipantDemographics.PreferNotToSay, "Fictional demo account for an inclusive hiking-event profile."),
        new(44, "Trixie", "Xavier", "Katigbak", new(1995, 12, 12), ParticipantDemographics.Female, "Fictional demo account for a trail-preparation workshop attendee."),
        new(45, "Ulysses", "Ybañez", "Lanting", new(1993, 3, 27), ParticipantDemographics.Male, "Fictional demo account for a recreational hiking demonstration."),
        new(46, "Via", "Zamora", "Maliksi", new(1991, 8, 7), ParticipantDemographics.Female, "Fictional demo account for a route-awareness practice profile."),
        new(47, "Arman", "Bayan", "Nolasco", new(1989, 5, 23), ParticipantDemographics.Male, "Fictional demo account for a steady hiking-event participant."),
        new(48, "Bea", "Calma", "Olivarez", new(1985, 10, 15), ParticipantDemographics.Female, "Fictional demo account for a fictional sunrise-hike companion."),
        new(49, "Cesar", "De Leon", "Prado", new(1981, 1, 29), ParticipantDemographics.Male, "Fictional demo account for an established local trail walker."),
        new(50, "Diana", "Esteban", "Roxas", new(1977, 6, 10), ParticipantDemographics.Female, "Fictional demo account for a community hike-support profile.")
    ];

    private static Guid DeterministicGuid(string email)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("TrailGuard fictional demo participant v1:" + email));
        var guidBytes = bytes[..16];
        guidBytes[7] = (byte)((guidBytes[7] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }
}
