using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TrailGuard.Models;
using TrailGuard.Services;

var assertions = 0;
void Assert(bool condition, string check)
{
    if (!condition) throw new InvalidOperationException("FAILED: " + check);
    assertions++;
}

var missingRazorPagesBuilder = WebApplication.CreateBuilder();
missingRazorPagesBuilder.Services.AddControllersWithViews();
using (var missingRazorPagesApp = missingRazorPagesBuilder.Build())
{
    var missingRazorPagesException = false;
    try { missingRazorPagesApp.MapRazorPages(); }
    catch (InvalidOperationException exception) when (exception.Message.Contains("AddRazorPages", StringComparison.Ordinal))
    {
        missingRazorPagesException = true;
    }
    Assert(missingRazorPagesException, "mapping Razor Pages without AddRazorPages reproduces the startup exception");
}

var endpointBuilder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    ApplicationName = typeof(TrailGuard.Controllers.AccountController).Assembly.GetName().Name
});
endpointBuilder.Services.AddControllersWithViews();
using (var endpointApp = endpointBuilder.Build())
{
    TrailGuardEndpointMapping.MapMvcEndpoints(endpointApp);
    var actions = endpointApp.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items;
    var accountActions = actions.OfType<ControllerActionDescriptor>()
        .Where(action => action.ControllerTypeInfo == typeof(TrailGuard.Controllers.AccountController))
        .Select(action => action.ActionName).ToHashSet(StringComparer.Ordinal);
    var expected = new[]
    {
        nameof(TrailGuard.Controllers.AccountController.Login),
        nameof(TrailGuard.Controllers.AccountController.Register),
        nameof(TrailGuard.Controllers.AccountController.CheckEmail),
        nameof(TrailGuard.Controllers.AccountController.ResendVerification),
        nameof(TrailGuard.Controllers.AccountController.ConfirmEmail),
        nameof(TrailGuard.Controllers.AccountController.ConfirmEmailChange)
    };
    Assert(expected.All(accountActions.Contains), "custom Account verification actions remain registered");
    Assert(!actions.OfType<ControllerActionDescriptor>().Any(action =>
            string.Equals(action.ControllerTypeInfo.Namespace, "Microsoft.AspNetCore.Identity.UI.V5.Pages.Account", StringComparison.Ordinal)),
        "Identity UI account actions are not registered");
    Assert(((IEndpointRouteBuilder)endpointApp).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Any(endpoint => endpoint.RoutePattern.RawText == "{controller=Home}/{action=Index}/{id?}"),
        "production conventional MVC route is mapped");
    Assert(endpointApp.Services.GetRequiredService<LinkGenerator>().GetPathByPage("/Identity/Account/Login") is null,
        "removed Identity UI login page is unavailable");
}

var identityServices = new ServiceCollection();
identityServices.AddLogging();
identityServices.AddIdentity<ApplicationUser, IdentityRole>().AddDefaultTokenProviders();
using (var identityProvider = identityServices.BuildServiceProvider())
{
    var scheme = await identityProvider.GetRequiredService<IAuthenticationSchemeProvider>()
        .GetSchemeAsync(IdentityConstants.ApplicationScheme);
    var authentication = identityProvider.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
    Assert(scheme?.HandlerType == typeof(CookieAuthenticationHandler) &&
           authentication.DefaultAuthenticateScheme == IdentityConstants.ApplicationScheme &&
           authentication.DefaultChallengeScheme == IdentityConstants.ApplicationScheme,
        "AddIdentity retains the application cookie defaults");
}
Assert(typeof(TrailGuard.Controllers.AccountController).GetMethod(nameof(TrailGuard.Controllers.AccountController.ResendVerification))!
    .GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), inherit: true).Any(),
    "anonymous resend action requires antiforgery validation");

var services = new ServiceCollection();
services.AddLogging();
services.AddDataProtection();
services.AddSingleton<MemoryUserStore>();
services.AddSingleton<IUserStore<ApplicationUser>>(provider => provider.GetRequiredService<MemoryUserStore>());
services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.Tokens.EmailConfirmationTokenProvider = "verification";
        options.Tokens.ChangeEmailTokenProvider = "verification";
    })
    .AddDefaultTokenProviders()
    .AddTokenProvider<EmailConfirmationTokenProvider<ApplicationUser>>("verification");
services.Configure<EmailConfirmationTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromHours(24));
services.Configure<EmailOptions>(options =>
{
    options.Enabled = true;
    options.FromAddress = "trailguard.notifications@gmail.com";
    options.FromName = "TrailGuard";
    options.PublicBaseUrl = "http://127.0.0.1:5246";
    options.Smtp = new SmtpOptions { Host = "smtp.gmail.com", Port = 587, Username = "trailguard.notifications@gmail.com", Password = "test-only" };
});
services.AddSingleton<InMemoryEmailSender>();
services.AddSingleton<IEmailSender>(provider => provider.GetRequiredService<InMemoryEmailSender>());
services.AddSingleton<EmailVerificationThrottle>();
services.AddSingleton<IEmailVerificationStateStore, TestEmailVerificationStateStore>();
services.AddSingleton(TimeProvider.System);
services.AddSingleton<IWebHostEnvironment>(new TestEnvironment());
services.AddSingleton<IEmailVerificationService, EmailVerificationService>();

using var provider = services.BuildServiceProvider();
var users = provider.GetRequiredService<UserManager<ApplicationUser>>();
var store = provider.GetRequiredService<MemoryUserStore>();
var sender = provider.GetRequiredService<InMemoryEmailSender>();
var verification = provider.GetRequiredService<IEmailVerificationService>();

var validAddresses = new[]
{
    "participant@example.com",
    "participant.name+demo@example.com",
    "participant@sub.example.com"
};
foreach (var address in validAddresses)
    Assert(TrailGuardEmailAddress.TryNormalize(address, out var normalized, out _) && normalized == address,
        $"valid email address accepted: {address}");

var invalidAddresses = new[]
{
    "", "   ", "participant", "@example.com", "participant@gmail", "first@second@example.com",
    "participant @example.com", "participant@example .com", "participant@example.com\nother@example.com",
    "Person <participant@example.com>", "one@example.com, two@example.com", new string('a', 245) + "@example.com"
};
foreach (var address in invalidAddresses)
    Assert(!TrailGuardEmailAddress.TryNormalize(address, out _, out _),
        $"invalid email address rejected: {address.Replace("\n", "\\n", StringComparison.Ordinal)}");
Assert(TrailGuardEmailAddress.TryNormalize("  participant@example.com  ", out var trimmedAddress, out _) &&
       trimmedAddress == "participant@example.com", "surrounding whitespace is trimmed only for a valid address");

foreach (var model in new object[]
{
    new RegisterViewModel { Email = "participant@gmail" },
    new LoginViewModel { Email = "Person <participant@example.com>" },
    new UpdateProfileViewModel { Email = "participant @example.com" },
    new EmailChangeRequestViewModel { NewEmail = "one@example.com, two@example.com" }
})
{
    var results = new List<ValidationResult>();
    Assert(!Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true) &&
           results.Any(result => result.MemberNames.Contains(model is EmailChangeRequestViewModel ? "NewEmail" : "Email")),
        "all account email models enforce the shared format policy");
}

// Direct controller calls bypass MVC's model-validation filter, so these checks
// deliberately prove the controller's own guards rather than only the attributes above.
var registrationSenderCount = sender.Messages.Count;
var registrationUserCount = store.Count;
var invalidRegistrationSignIn = new TrackingSignInManager(users);
var invalidRegistrationController = CreateAccountController(users, verification, invalidRegistrationSignIn);
var invalidRegistration = await invalidRegistrationController.Register(new RegisterViewModel
{
    Email = "participant@gmail",
    Password = "Password1!",
    ConfirmPassword = "Password1!",
    FirstName = "Invalid",
    LastName = "Address"
});
Assert(invalidRegistration is ViewResult &&
       invalidRegistrationController.ModelState.ContainsKey(nameof(RegisterViewModel.Email)) &&
       store.Count == registrationUserCount && sender.Messages.Count == registrationSenderCount &&
       invalidRegistrationSignIn.PasswordChecks == 0 && invalidRegistrationSignIn.SignInCalls == 0,
    "invalid registration creates no account, reaches no role-assignment dependency, sends no email, and issues no session");

var invalidLoginFindCalls = store.FindByEmailCalls;
var invalidLoginSignIn = new TrackingSignInManager(users);
var invalidLoginController = CreateAccountController(users, verification, invalidLoginSignIn);
var invalidLogin = await invalidLoginController.Login(new LoginViewModel
{
    Email = "Person <participant@example.com>",
    Password = "Password1!"
});
Assert(invalidLogin is ViewResult && invalidLoginController.ModelState.ContainsKey(nameof(LoginViewModel.Email)) &&
       store.FindByEmailCalls == invalidLoginFindCalls && invalidLoginSignIn.PasswordChecks == 0 && invalidLoginSignIn.SignInCalls == 0,
    "invalid login performs no account lookup, password sign-in, or authenticated session");

var participant = new ApplicationUser
{
    UserName = "participant@example.com", Email = "participant@example.com", FirstName = "Pat", LastName = "Tester",
    IsActive = true, ParticipantEmailVerificationRequired = true
};
Assert((await users.CreateAsync(participant, "Password1!")).Succeeded, "create participant for valid format flow");
var whitespaceLoginSignIn = new TrackingSignInManager(users);
var whitespaceLoginController = CreateAccountController(users, verification, whitespaceLoginSignIn);
var findCallsBeforeWhitespaceLogin = store.FindByEmailCalls;
var whitespaceLogin = await whitespaceLoginController.Login(new LoginViewModel
{
    Email = "  participant@example.com  ", Password = "Password1!"
});
Assert(whitespaceLogin is ViewResult && store.FindByEmailCalls == findCallsBeforeWhitespaceLogin + 1 &&
       store.LastFindByEmail == users.NormalizeEmail("participant@example.com") && whitespaceLoginSignIn.PasswordChecks == 1,
    "valid surrounding whitespace is trimmed before account lookup and password processing");

var sent = await verification.SendConfirmationAsync(participant, "127.0.0.1");
Assert(sent.Status == VerificationSendStatus.Sent && sender.Messages.Count == registrationSenderCount + 1,
    "valid address continues through fake verification delivery");
Assert(sender.Messages.Last().To == "participant@example.com" &&
       sender.Messages.Last().PlainTextBody.Contains("http://127.0.0.1:5246/Account/ConfirmEmail?", StringComparison.Ordinal),
    "valid verification delivery uses the configured origin and normalized destination");
var deliveredCode = GetQuery(sender.Messages.Last().PlainTextBody, "code");
Assert(await users.VerifyUserTokenAsync(participant, "verification", "EmailConfirmation", deliveredCode),
    "valid delivered confirmation token validates");
Assert((await users.ConfirmEmailAsync(participant, deliveredCode)).Succeeded, "valid delivered confirmation token confirms email");
Assert(!(await users.ConfirmEmailAsync(participant, "tampered")).Succeeded, "tampered confirmation token is rejected");
var messagesBeforeInvalidDispatch = sender.Messages.Count;
Assert((await verification.SendChangeConfirmationAsync(participant, "participant@gmail", "127.0.0.1")).Status == VerificationSendStatus.Unavailable &&
       sender.Messages.Count == messagesBeforeInvalidDispatch, "invalid verification destination dispatches no email");

foreach (var address in validAddresses.Skip(1))
{
    var validFormatUser = new ApplicationUser
    {
        UserName = address, Email = address, FirstName = "Valid", LastName = "Format",
        IsActive = true, ParticipantEmailVerificationRequired = true
    };
    Assert((await users.CreateAsync(validFormatUser, "Password1!")).Succeeded, $"create {address} participant");
    var validFormatDelivery = await verification.SendConfirmationAsync(validFormatUser, "127.0.0.2");
    Assert(validFormatDelivery.Status == VerificationSendStatus.Sent && sender.Messages.Last().To == address,
        $"{address} continues through fake verification delivery");
}

Assert((await verification.SendConfirmationAsync(participant, "127.0.0.1")).Status == VerificationSendStatus.Cooldown,
    "persistent resend cooldown protects the confirmed account");
participant.PendingEmailChange = "replacement@example.test";
Assert((await users.UpdateAsync(participant)).Succeeded, "persist pending replacement");
Assert((await verification.SendChangeConfirmationAsync(participant, participant.PendingEmailChange, "127.0.0.2")).Status == VerificationSendStatus.Cooldown,
    "the same cooldown also protects replacement requests");
participant.LastEmailVerificationSentAtUtc = null;
Assert((await users.UpdateAsync(participant)).Succeeded, "clear test cooldown");
var change = await verification.SendChangeConfirmationAsync(participant, participant.PendingEmailChange, "127.0.0.2");
Assert(change.Status == VerificationSendStatus.Sent, "replacement fake delivery");
var changeCode = GetQuery(sender.Messages.Last().PlainTextBody, "code");
participant.EmailConfirmed = false;
participant.PendingEmailChange = null;
Assert((await users.ChangeEmailAsync(participant, "replacement@example.test", changeCode)).Succeeded,
    "valid delivered replacement token changes email");
Assert((await users.SetUserNameAsync(participant, "replacement@example.test")).Succeeded,
    "supported username synchronization");
Assert(participant.Email == "replacement@example.test" && participant.UserName == "replacement@example.test" && participant.EmailConfirmed,
    "replacement confirmation preserves Identity email and username behavior");

var throttle = provider.GetRequiredService<EmailVerificationThrottle>();
for (var index = 0; index < 5; index++)
    Assert(throttle.TryConsume("test", "account", 5, TimeSpan.FromMinutes(1)), "throttle allows bounded requests");
Assert(!throttle.TryConsume("test", "account", 5, TimeSpan.FromMinutes(1)), "throttle rejects overflow");

Console.WriteLine($"PASS: email format policy, direct controller guards, endpoint initialization, Identity cookies, fake delivery, and token confirmation ({assertions} assertions).");

static TrailGuard.Controllers.AccountController CreateAccountController(
    UserManager<ApplicationUser> users,
    IEmailVerificationService verification,
    SignInManager<ApplicationUser> signInManager)
{
    // The invalid-registration guard must return before this poison-pill role
    // dependency could be invoked. A regression therefore fails the test rather
    // than reaching account creation or role assignment.
    var controller = new TrailGuard.Controllers.AccountController(users, signInManager, null!, verification, null!);
    var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), new ControllerActionDescriptor
    {
        ControllerTypeInfo = typeof(TrailGuard.Controllers.AccountController).GetTypeInfo()
    });
    controller.ControllerContext = new ControllerContext(actionContext);
    controller.Url = new Microsoft.AspNetCore.Mvc.Routing.UrlHelper(actionContext);
    return controller;
}

static string GetQuery(string body, string name)
{
    var url = body.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)
        .First(part => part.StartsWith("http", StringComparison.Ordinal));
    var encoded = QueryHelpers.ParseQuery(new Uri(url).Query)[name].Single()!;
    return System.Text.Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encoded));
}

sealed class TestEnvironment : IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = "Development";
    public string ApplicationName { get; set; } = "TrailGuard.Verification";
    public string WebRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

sealed class TestEmailVerificationStateStore : IEmailVerificationStateStore
{
    public Task<bool> TryReserveAsync(ApplicationUser user, DateTime reservationUtc, TimeSpan cooldown, CancellationToken cancellationToken = default)
    {
        if (user.LastEmailVerificationSentAtUtc is { } last && reservationUtc - last < cooldown) return Task.FromResult(false);
        user.LastEmailVerificationSentAtUtc = reservationUtc;
        return Task.FromResult(true);
    }

    public Task ReleaseReservationAsync(ApplicationUser user, DateTime reservationUtc, CancellationToken cancellationToken = default)
    {
        if (user.LastEmailVerificationSentAtUtc == reservationUtc) user.LastEmailVerificationSentAtUtc = null;
        return Task.CompletedTask;
    }
}

sealed class MemoryUserStore : IUserStore<ApplicationUser>, IUserEmailStore<ApplicationUser>, IUserPasswordStore<ApplicationUser>, IUserSecurityStampStore<ApplicationUser>
{
    private readonly ConcurrentDictionary<string, ApplicationUser> _users = new(StringComparer.Ordinal);
    public int Count => _users.Count;
    public int FindByEmailCalls { get; private set; }
    public string? LastFindByEmail { get; private set; }

    public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken) { _users[user.Id] = user; return Task.FromResult(IdentityResult.Success); }
    public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken) { _users[user.Id] = user; return Task.FromResult(IdentityResult.Success); }
    public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken) { _users.TryRemove(user.Id, out _); return Task.FromResult(IdentityResult.Success); }
    public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) => Task.FromResult(_users.TryGetValue(userId, out var user) ? user : null);
    public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) => Task.FromResult(_users.Values.FirstOrDefault(user => user.NormalizedUserName == normalizedUserName));
    public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.Id);
    public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.UserName);
    public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken) { user.UserName = userName; return Task.CompletedTask; }
    public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.NormalizedUserName);
    public Task SetNormalizedUserNameAsync(ApplicationUser user, string? name, CancellationToken cancellationToken) { user.NormalizedUserName = name; return Task.CompletedTask; }
    public Task SetEmailAsync(ApplicationUser user, string? email, CancellationToken cancellationToken) { user.Email = email; return Task.CompletedTask; }
    public Task<string?> GetEmailAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.Email);
    public Task<bool> GetEmailConfirmedAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.EmailConfirmed);
    public Task SetEmailConfirmedAsync(ApplicationUser user, bool confirmed, CancellationToken cancellationToken) { user.EmailConfirmed = confirmed; return Task.CompletedTask; }
    public Task<ApplicationUser?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        FindByEmailCalls++;
        LastFindByEmail = normalizedEmail;
        return Task.FromResult(_users.Values.FirstOrDefault(user => user.NormalizedEmail == normalizedEmail));
    }
    public Task<string?> GetNormalizedEmailAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.NormalizedEmail);
    public Task SetNormalizedEmailAsync(ApplicationUser user, string? email, CancellationToken cancellationToken) { user.NormalizedEmail = email; return Task.CompletedTask; }
    public Task SetPasswordHashAsync(ApplicationUser user, string? passwordHash, CancellationToken cancellationToken) { user.PasswordHash = passwordHash; return Task.CompletedTask; }
    public Task<string?> GetPasswordHashAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.PasswordHash);
    public Task<bool> HasPasswordAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.PasswordHash is not null);
    public Task SetSecurityStampAsync(ApplicationUser user, string stamp, CancellationToken cancellationToken) { user.SecurityStamp = stamp; return Task.CompletedTask; }
    public Task<string?> GetSecurityStampAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.SecurityStamp);
    public void Dispose() { }
}

sealed class TrackingSignInManager : SignInManager<ApplicationUser>
{
    public int PasswordChecks { get; private set; }
    public int SignInCalls { get; private set; }

    public TrackingSignInManager(UserManager<ApplicationUser> userManager)
        : base(userManager, new HttpContextAccessor(),
            new UserClaimsPrincipalFactory<ApplicationUser>(userManager, Microsoft.Extensions.Options.Options.Create(new IdentityOptions())),
            Microsoft.Extensions.Options.Options.Create(new IdentityOptions()), NullLogger<SignInManager<ApplicationUser>>.Instance,
            new AuthenticationSchemeProvider(Microsoft.Extensions.Options.Options.Create(new AuthenticationOptions())), new AlwaysConfirmedUser())
    {
    }

    public override Task<Microsoft.AspNetCore.Identity.SignInResult> CheckPasswordSignInAsync(ApplicationUser user, string password, bool lockoutOnFailure)
    {
        PasswordChecks++;
        return Task.FromResult(Microsoft.AspNetCore.Identity.SignInResult.Failed);
    }

    public override Task SignInAsync(ApplicationUser user, bool isPersistent, string? authenticationMethod = null)
    {
        SignInCalls++;
        return Task.CompletedTask;
    }
}

sealed class AlwaysConfirmedUser : IUserConfirmation<ApplicationUser>
{
    public Task<bool> IsConfirmedAsync(UserManager<ApplicationUser> manager, ApplicationUser user) => Task.FromResult(true);
}
