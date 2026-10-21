using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc.ViewFeatures.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TrailGuard.Controllers;
using TrailGuard.Models;
using TrailGuard.Services;

var assertions = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    assertions++;
}

var currentNotice = ParticipantProfileConsent.CurrentNotice;
var equivalentNotice = currentNotice with { Statements = currentNotice.Statements.ToArray() };
Check(ParticipantProfileConsent.CurrentVersion == currentNotice.Version
      && currentNotice.Version == equivalentNotice.Version
      && currentNotice.Version.Length <= 100,
    "Unchanged consent content must produce one stable identity that fits the persisted column.");

var lineFeedNotice = currentNotice with { Description = "Line one\nLine two" };
var carriageReturnLineFeedNotice = currentNotice with { Description = "Line one\r\nLine two" };
Check(lineFeedNotice.Version == carriageReturnLineFeedNotice.Version,
    "Consent identity must be invariant across line endings.");

var revisedNotice = currentNotice with { Description = currentNotice.Description + " Updated." };
var changedLinkNotice = currentNotice with { PrivacyPolicyAction = "PrivacyUpdated" };
Check(revisedNotice.Version != currentNotice.Version
      && changedLinkNotice.Version != currentNotice.Version
      && !ParticipantProfileConsent.IsCurrent(revisedNotice.Version),
    "Changed consent wording or notice-link identity must invalidate a prior acceptance.");

var user = new ApplicationUser { Id = "participant-1", UserName = "participant@example.test", Email = "participant@example.test", FirstName = "Pat", LastName = "Tester", IsActive = true };
user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, "current-password");
var store = new TestUserStore(user, ["Participant"]);
var manager = CreateUserManager(store);
var controller = CreateController(manager, user.Id);

var populateIndexViewData = typeof(SettingsController).GetMethod("PopulateIndexViewDataAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
async Task<bool> HasCurrentConsentViewFlagAsync(ApplicationUser candidate)
{
    var candidateController = CreateController(manager, candidate.Id);
    await (Task)populateIndexViewData.Invoke(candidateController, [candidate])!;
    return candidateController.ViewData["HasCurrentProfileConsent"] is true;
}

Check(!await HasCurrentConsentViewFlagAsync(user),
    "A participant with null consent fields must not be treated as having current consent.");
user.ParticipantProfileConsentVersion = "retired-version";
user.ParticipantProfileConsentedAtUtc = DateTime.UtcNow;
Check(!await HasCurrentConsentViewFlagAsync(user),
    "A participant with a stale consent version must not be treated as having current consent.");
user.ParticipantProfileConsentVersion = ParticipantProfileConsent.CurrentVersion;
Check(await HasCurrentConsentViewFlagAsync(user),
    "A participant with the persisted current version must render the current-consent flag as true.");
user.ParticipantProfileConsentVersion = null;
user.ParticipantProfileConsentedAtUtc = null;

var action = typeof(SettingsController).GetMethod(nameof(SettingsController.AcceptProfileConsent))!;
Check(action.GetCustomAttribute<HttpPostAttribute>() is not null && action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>() is not null,
    "Consent acceptance must be an antiforgery-protected POST action.");
Check(action.GetParameters().Single().ParameterType == typeof(ParticipantProfileConsentRequest)
      && typeof(ParticipantProfileConsentRequest).GetProperty("UserId") is null,
    "Consent acceptance must not accept a client-supplied account identifier.");

var missing = await controller.AcceptProfileConsent(new ParticipantProfileConsentRequest { Version = ParticipantProfileConsent.CurrentVersion, Accepted = false });
Check(missing is BadRequestObjectResult && user.ParticipantProfileConsentVersion is null, "A missing affirmative checkbox must not persist consent.");

var stale = await controller.AcceptProfileConsent(new ParticipantProfileConsentRequest { Version = "retired-version", Accepted = true });
Check(stale is ConflictObjectResult && user.ParticipantProfileConsentVersion is null, "A stale notice version must not persist consent.");

var before = DateTime.UtcNow;
var accepted = await controller.AcceptProfileConsent(new ParticipantProfileConsentRequest { Version = ParticipantProfileConsent.CurrentVersion, Accepted = true });
var recordedAt = user.ParticipantProfileConsentedAtUtc;
Check(accepted is OkObjectResult && user.ParticipantProfileConsentVersion == ParticipantProfileConsent.CurrentVersion
      && recordedAt is { Kind: DateTimeKind.Utc } && recordedAt >= before && store.UpdateCount == 1,
    "Acceptance must persist the current version with a server-generated UTC timestamp.");

var again = await controller.AcceptProfileConsent(new ParticipantProfileConsentRequest { Version = ParticipantProfileConsent.CurrentVersion, Accepted = true });
Check(again is OkObjectResult && user.ParticipantProfileConsentedAtUtc == recordedAt && store.UpdateCount == 1,
    "Reaccepting an already-current version must be idempotent and preserve its timestamp.");

var otherAccountController = CreateController(manager, "different-account");
Check(await otherAccountController.AcceptProfileConsent(new ParticipantProfileConsentRequest { Version = ParticipantProfileConsent.CurrentVersion, Accepted = true }) is UnauthorizedObjectResult,
    "The action must resolve the account from the authenticated identity only.");

user.ParticipantProfileConsentVersion = null;
user.ParticipantProfileConsentedAtUtc = null;
var fakeStorage = new CountingStorage();
var profileController = CreateController(manager, user.Id, fakeStorage);
var rejectedProfile = await profileController.UpdateProfile(ProfileInput(phone: "09171234567", facebook: "   ", image: true), "unused");
Check(rejectedProfile is ViewResult && fakeStorage.UploadCount == 0 && user.FirstName == "Pat"
      && !HasModelError(profileController, nameof(UpdateProfileViewModel.FacebookLink))
      && HasModelError(profileController, string.Empty, "Profile Privacy Notice"),
    "An empty optional Facebook link must not obscure the persisted-consent requirement or reach upload/mutation.");

user.ParticipantProfileConsentVersion = ParticipantProfileConsent.CurrentVersion;
user.ParticipantProfileConsentedAtUtc = DateTime.UtcNow;

var invalidEmailStorage = new CountingStorage();
var invalidEmailImage = new TrackingImageFile();
var invalidEmailUpdates = store.UpdateCount;
var invalidEmailController = CreateController(manager, user.Id, invalidEmailStorage);
var invalidEmailProfile = await invalidEmailController.UpdateProfile(
    ProfileInput(email: "participant@gmail", profileImage: invalidEmailImage), "unused");
Check(invalidEmailProfile is ViewResult
      && HasModelError(invalidEmailController, nameof(UpdateProfileViewModel.Email), "valid email address")
      && invalidEmailImage.OpenReadStreamCount == 0
      && invalidEmailStorage.UploadCount == 0
      && store.UpdateCount == invalidEmailUpdates,
    "An invalid Settings profile email must fail before image processing, upload, or profile mutation.");

var pendingBeforeInvalidChange = user.PendingEmailChange;
var invalidChangeUpdates = store.UpdateCount;
var invalidChange = await CreateController(manager, user.Id).RequestEmailChange(new EmailChangeRequestViewModel
{
    NewEmail = "Person <participant@example.com>",
    CurrentPassword = "current-password"
});
Check(invalidChange is RedirectToActionResult && user.PendingEmailChange == pendingBeforeInvalidChange && store.UpdateCount == invalidChangeUpdates,
    "An invalid replacement email must not create pending-email state or persist a mutation.");

// These accepted-format checks deliberately stop at the password gate; the
// dedicated persistence-seam assertion below separately proves canonical storage.
foreach (var acceptedPhone in new[]
{
    "9123456789",
    "912 345 6789",
    "09123456789",
    "09 123-456-789",
    "639123456789",
    "63 912-345-6789",
    "+639123456789",
    "+63 912 345 6789"
})
{
    var acceptedPhoneController = CreateController(manager, user.Id, fakeStorage);
    var acceptedPhoneResult = await acceptedPhoneController.UpdateProfile(ProfileInput(phone: acceptedPhone), "");
    Check(acceptedPhoneResult is ViewResult { Model: UpdateProfileViewModel returned }
          && returned.PhoneNumber == "912 345 6789"
          && !HasModelError(acceptedPhoneController, nameof(UpdateProfileViewModel.PhoneNumber)),
        $"Participant Settings must accept '{acceptedPhone}' and redisplay it as local 3-3-4 digits before the password gate.");
}

var updatesBeforeCanonicalPersistence = store.UpdateCount;
var persistingController = CreatePersistingController(manager, user.Id, fakeStorage);
var persistedCanonicalPhone = await persistingController.UpdateProfile(ProfileInput(phone: "+63 912 345 6789"), "current-password");
Check(persistedCanonicalPhone is RedirectToActionResult
      && persistingController.PersistedPhoneNumber == "+63 912 345 6789"
      && user.PhoneNumber == "+63 912 345 6789"
      && store.UpdateCount == updatesBeforeCanonicalPersistence + 1,
    "A valid participant phone must reach the persistence seam as the canonical value, not merely the password gate.");

foreach (var invalidPhone in new[]
{
    "912 345",
    "91234567890",
    "8123456789",
    "9123456789x",
    "９１２３４５６７８９",
    "+62 9123456789"
})
{
    var phoneBefore = user.PhoneNumber;
    var updatesBefore = store.UpdateCount;
    var invalidPhoneStorage = new CountingStorage();
    var invalidPhoneImage = new TrackingImageFile();
    var invalidPhoneController = CreateController(manager, user.Id, invalidPhoneStorage);
    var invalidPhoneResult = await invalidPhoneController.UpdateProfile(ProfileInput(phone: invalidPhone, profileImage: invalidPhoneImage), "unused");
    Check(invalidPhoneResult is ViewResult
          && HasModelError(invalidPhoneController, nameof(UpdateProfileViewModel.PhoneNumber), "complete contact number")
          && invalidPhoneImage.OpenReadStreamCount == 0
          && invalidPhoneStorage.UploadCount == 0
          && user.PhoneNumber == phoneBefore
          && store.UpdateCount == updatesBefore,
        $"A forged Settings POST with '{invalidPhone}' must fail before image upload or profile mutation.");
}

Check(PhilippineMobileNumber.TryNormalize("+63 912 345 6789", out var canonicalPhone)
      && PhilippineMobileNumber.ToLocalDisplay(canonicalPhone) == "912 345 6789",
    "The stored canonical profile value must have one local display form for both Settings and Registration.");

var settingsControllerSource = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Controllers", "SettingsController.cs"));
Check(settingsControllerSource.IndexOf("model.PhoneNumber = canonicalPhoneNumber;", StringComparison.Ordinal) >= 0
      && settingsControllerSource.IndexOf("model.PhoneNumber = canonicalPhoneNumber;", StringComparison.Ordinal)
         < settingsControllerSource.IndexOf("user.PhoneNumber = model.PhoneNumber;", StringComparison.Ordinal),
    "The canonical participant phone value must be assigned before the profile persistence assignment.");

var settingsViewSource = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Views", "Settings", "Index.cshtml"));
var registrationViewSource = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Views", "Registration", "Register.cshtml"));
Check(settingsViewSource.Contains("inputmode=\"tel\"")
      && settingsViewSource.Contains("autocomplete=\"tel-national\"")
      && settingsViewSource.Contains(">+63</span>")
      && settingsViewSource.Contains("TrailGuardPhilippineMobileNumber.updateValidity"),
    "Participant Settings must render the visible +63 prefix and use the shared client validator.");
Check(registrationViewSource.Contains("TrailGuardPhilippineMobileNumber")
      && registrationViewSource.Contains("TrailGuard.Services.PhilippineMobileNumber.ToLocalDisplay")
      && !registrationViewSource.Contains("'+63 ' + input.value"),
    "Registration must retain the shared local-phone formatter and avoid a duplicate visible country prefix.");

var emptyFacebookController = CreateController(manager, user.Id, fakeStorage);
var emptyFacebook = await emptyFacebookController.UpdateProfile(ProfileInput(phone: "09171234567", facebook: "   "), "");
Check(emptyFacebook is ViewResult && emptyFacebookController.ModelState.IsValid
      && !HasModelError(emptyFacebookController, nameof(UpdateProfileViewModel.FacebookLink)),
    "With current consent, an empty or whitespace Facebook link must pass validation and continue to the password gate.");

var invalidFacebookController = CreateController(manager, user.Id, fakeStorage);
var invalidFacebook = await invalidFacebookController.UpdateProfile(ProfileInput(phone: "09171234567", facebook: "ftp://example.test/profile"), "unused");
Check(invalidFacebook is ViewResult && HasModelError(invalidFacebookController, nameof(UpdateProfileViewModel.FacebookLink), "http or https"),
    "A supplied Facebook link must remain limited to absolute http or https URLs.");

var missingPhoneController = CreateController(manager, user.Id, fakeStorage);
var missingPhone = await missingPhoneController.UpdateProfile(ProfileInput(phone: "   "), "unused");
Check(missingPhone is ViewResult && HasModelError(missingPhoneController, nameof(UpdateProfileViewModel.PhoneNumber), "required")
      && fakeStorage.UploadCount == 0,
    "A participant whitespace phone number must be rejected before uploads or mutation.");

var invalidBirthdayController = CreateController(manager, user.Id, fakeStorage);
var invalidBirthday = await invalidBirthdayController.UpdateProfile(ProfileInput(phone: "09171234567", birthday: new DateOnly(2010, 1, 1)), "unused");
Check(invalidBirthday is ViewResult && HasModelError(invalidBirthdayController, nameof(UpdateProfileViewModel.Birthday), "ages 18 through 60"),
    "The existing red birthday validation must remain effective.");

var passwordController = CreateController(manager, user.Id, fakeStorage);
var passwordResult = await passwordController.ChangePassword(new UpdateProfileViewModel { PhoneNumber = "   ", FacebookLink = "not a URL" });
Check(passwordResult is RedirectToActionResult,
    "Change Password must remain independent of the profile phone and Facebook requirements.");

user.IsActive = false;
var inactive = await CreateController(manager, user.Id).AcceptProfileConsent(new ParticipantProfileConsentRequest { Version = ParticipantProfileConsent.CurrentVersion, Accepted = true });
Check(inactive is ForbidResult, "Inactive accounts must not record participant consent.");
user.IsActive = true;
store.Roles = ["Organizer"];
var organizer = await CreateController(manager, user.Id).AcceptProfileConsent(new ParticipantProfileConsentRequest { Version = ParticipantProfileConsent.CurrentVersion, Accepted = true });
Check(organizer is ForbidResult, "Organizer settings must not use participant profile consent.");

Console.WriteLine($"PASS: {assertions} participant Settings privacy-consent assertions.");

static UpdateProfileViewModel ProfileInput(string? phone = "09171234567", string? facebook = null, DateOnly? birthday = null, bool image = false, IFormFile? profileImage = null, string email = "participant@example.test") => new()
{
    FirstName = "Changed",
    LastName = "Tester",
    Email = email,
    PhoneNumber = phone,
    FacebookLink = facebook,
    Birthday = birthday ?? new DateOnly(2000, 1, 1),
    Gender = ParticipantDemographics.Female,
    ProfileImage = profileImage ?? (image ? new FormFile(new MemoryStream([0xFF, 0xD8, 0xFF]), 0, 3, "ProfileImage", "photo.jpg") : null)
};

static bool HasModelError(Controller controller, string key, string? messageFragment = null) =>
    controller.ModelState.TryGetValue(key, out var entry)
    && entry.Errors.Any(error => messageFragment is null || error.ErrorMessage.Contains(messageFragment, StringComparison.OrdinalIgnoreCase));

static UserManager<ApplicationUser> CreateUserManager(TestUserStore store) => new(
    store,
    Options.Create(new IdentityOptions()),
    new PasswordHasher<ApplicationUser>(),
    [], [],
    new UpperInvariantLookupNormalizer(),
    new IdentityErrorDescriber(),
    null!,
    NullLogger<UserManager<ApplicationUser>>.Instance);

static SettingsController CreateController(UserManager<ApplicationUser> manager, string authenticatedId, IUploadStorage? storage = null)
{
    var controller = new SettingsController(manager, null!, null!, NullLogger<SettingsController>.Instance,
        new FixedPhilippineClock(new DateOnly(2026, 10, 16)), storage ?? new CountingStorage(), null!, new FakeEmailVerificationService());
    var http = new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, authenticatedId)], "test"))
    };
    controller.ControllerContext = new ControllerContext { HttpContext = http };
    controller.TempData = new TempDataDictionary(http, new DictionaryTempDataProvider());
    return controller;
}

static PersistingSettingsController CreatePersistingController(UserManager<ApplicationUser> manager, string authenticatedId, IUploadStorage? storage = null)
{
    var controller = new PersistingSettingsController(manager, new TestSignInManager(manager), NullLogger<SettingsController>.Instance,
        new FixedPhilippineClock(new DateOnly(2026, 10, 16)), storage ?? new CountingStorage());
    var http = new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, authenticatedId)], "test"))
    };
    controller.ControllerContext = new ControllerContext { HttpContext = http };
    controller.TempData = new TempDataDictionary(http, new DictionaryTempDataProvider());
    return controller;
}

sealed class FixedPhilippineClock(DateOnly today) : IPhilippineClock
{
    public DateOnly Today => today;
}

sealed class TestUserStore(ApplicationUser user, List<string> roles) : IUserStore<ApplicationUser>, IUserRoleStore<ApplicationUser>, IUserPasswordStore<ApplicationUser>
{
    public ApplicationUser User { get; } = user;
    public List<string> Roles { get; set; } = roles;
    public int UpdateCount { get; private set; }
    public void Dispose() { }
    public Task<string> GetUserIdAsync(ApplicationUser value, CancellationToken cancellationToken) => Task.FromResult(value.Id);
    public Task<string?> GetUserNameAsync(ApplicationUser value, CancellationToken cancellationToken) => Task.FromResult(value.UserName);
    public Task SetUserNameAsync(ApplicationUser value, string? name, CancellationToken cancellationToken) { value.UserName = name; return Task.CompletedTask; }
    public Task<string?> GetNormalizedUserNameAsync(ApplicationUser value, CancellationToken cancellationToken) => Task.FromResult(value.NormalizedUserName);
    public Task SetNormalizedUserNameAsync(ApplicationUser value, string? name, CancellationToken cancellationToken) { value.NormalizedUserName = name; return Task.CompletedTask; }
    public Task<IdentityResult> CreateAsync(ApplicationUser value, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
    public Task<IdentityResult> UpdateAsync(ApplicationUser value, CancellationToken cancellationToken) { UpdateCount++; return Task.FromResult(IdentityResult.Success); }
    public Task<IdentityResult> DeleteAsync(ApplicationUser value, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
    public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) => Task.FromResult(userId == User.Id ? User : null);
    public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) => Task.FromResult<ApplicationUser?>(null);
    public Task AddToRoleAsync(ApplicationUser value, string roleName, CancellationToken cancellationToken) { Roles.Add(roleName); return Task.CompletedTask; }
    public Task RemoveFromRoleAsync(ApplicationUser value, string roleName, CancellationToken cancellationToken) { Roles.Remove(roleName); return Task.CompletedTask; }
    public Task<IList<string>> GetRolesAsync(ApplicationUser value, CancellationToken cancellationToken) => Task.FromResult<IList<string>>(Roles);
    public Task<bool> IsInRoleAsync(ApplicationUser value, string roleName, CancellationToken cancellationToken) => Task.FromResult(Roles.Contains(roleName));
    public Task<IList<ApplicationUser>> GetUsersInRoleAsync(string roleName, CancellationToken cancellationToken) => Task.FromResult<IList<ApplicationUser>>(Roles.Contains(roleName) ? [User] : []);
    public Task SetPasswordHashAsync(ApplicationUser value, string? passwordHash, CancellationToken cancellationToken) { value.PasswordHash = passwordHash; return Task.CompletedTask; }
    public Task<string?> GetPasswordHashAsync(ApplicationUser value, CancellationToken cancellationToken) => Task.FromResult(value.PasswordHash);
    public Task<bool> HasPasswordAsync(ApplicationUser value, CancellationToken cancellationToken) => Task.FromResult(!string.IsNullOrEmpty(value.PasswordHash));
}

sealed class TestSignInManager(UserManager<ApplicationUser> userManager) : SignInManager<ApplicationUser>(
    userManager,
    new HttpContextAccessor(),
    new UserClaimsPrincipalFactory<ApplicationUser>(userManager, Microsoft.Extensions.Options.Options.Create(new IdentityOptions())),
    Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
    NullLogger<SignInManager<ApplicationUser>>.Instance,
    new AuthenticationSchemeProvider(Microsoft.Extensions.Options.Options.Create(new AuthenticationOptions())),
    new DefaultUserConfirmation<ApplicationUser>())
{
    public override Task RefreshSignInAsync(ApplicationUser user) => Task.CompletedTask;
}

sealed class PersistingSettingsController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ILogger<SettingsController> logger,
    IPhilippineClock clock,
    IUploadStorage storage)
    : SettingsController(userManager, signInManager, null!, logger, clock, storage, null!, new FakeEmailVerificationService())
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    public string? PersistedPhoneNumber { get; private set; }

    protected override async Task PersistProfileUpdateAsync(ApplicationUser user, UploadAttempt uploads, Action<IdentityResult> setResult)
    {
        PersistedPhoneNumber = user.PhoneNumber;
        var result = await _userManager.UpdateAsync(user);
        setResult(result);
        if (!result.Succeeded) throw new InvalidOperationException("Profile update rejected.");
    }
}

sealed class FakeEmailVerificationService : IEmailVerificationService
{
    public Task<VerificationSendResult> SendConfirmationAsync(ApplicationUser user, string? remoteIp, CancellationToken cancellationToken = default) =>
        Task.FromResult(new VerificationSendResult(VerificationSendStatus.Sent));
    public Task<VerificationSendResult> SendChangeConfirmationAsync(ApplicationUser user, string newEmail, string? remoteIp, CancellationToken cancellationToken = default) =>
        Task.FromResult(new VerificationSendResult(VerificationSendStatus.Sent));
}

sealed class CountingStorage : IUploadStorage
{
    public int UploadCount { get; private set; }
    public Task<string> UploadAsync(UploadCategory category, byte[] bytes, string extension, CancellationToken cancellationToken = default) { UploadCount++; return Task.FromResult("profiles/test.jpg"); }
    public Task<StoredDocument?> ReadDocumentAsync(RegistrationDocumentKind kind, string? reference, CancellationToken cancellationToken = default) => Task.FromResult<StoredDocument?>(null);
    public Task<byte[]?> ReadPublicLocalAsync(string reference, CancellationToken cancellationToken = default) => Task.FromResult<byte[]?>(null);
    public Task DeleteOwnedAsync(UploadCategory category, string reference, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

sealed class TrackingImageFile : IFormFile
{
    public int OpenReadStreamCount { get; private set; }
    public string ContentType => "image/jpeg";
    public string ContentDisposition => "form-data";
    public IHeaderDictionary Headers { get; } = new HeaderDictionary();
    public long Length => 3;
    public string Name => "ProfileImage";
    public string FileName => "photo.jpg";
    public void CopyTo(Stream target) => throw new NotSupportedException();
    public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Stream OpenReadStream()
    {
        OpenReadStreamCount++;
        return new MemoryStream([0xFF, 0xD8, 0xFF]);
    }
}

sealed class DictionaryTempDataProvider : ITempDataProvider
{
    public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
    public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
}
