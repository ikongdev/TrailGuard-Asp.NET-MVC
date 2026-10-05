var assertions = 0;
var root = Directory.GetCurrentDirectory();

void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    assertions++;
}

string Read(params string[] path) => File.ReadAllText(Path.Combine([root, .. path]));

var helper = Read("wwwroot", "js", "action-state.js");
var layout = Read("Views", "Shared", "_Layout.cshtml");
var assessmentForm = Read("Views", "Assessment", "Form.cshtml");
var myRegistrations = Read("Views", "Registration", "MyRegistrations.cshtml");
var settings = Read("Views", "Settings", "Index.cshtml");
var eventIndexView = Read("Views", "Event", "Index.cshtml");
var participantNavbar = Read("Views", "Shared", "Components", "_NavbarParticipant.cshtml");
var toast = Read("wwwroot", "js", "toast.js");
var customSelect = Read("wwwroot", "js", "custom-select.js");
var registration = Read("Views", "Registration", "Register.cshtml");
var registrations = Read("Views", "Registration", "MyRegistrations.cshtml");
var eventDetails = Read("Views", "Event", "Details.cshtml");
var records = Read("Views", "Records", "Index.cshtml");
var eventIndex = Read("Views", "Event", "Index.cshtml");
var trailIndex = Read("Views", "Trail", "Index.cshtml");

Check(helper.Contains("function isSubmitAction"), "The shared helper must classify submit actions explicitly.");
Check(helper.Contains("function isNativeFormSubmit") && helper.Contains("!!element.form")
      && helper.Contains("element.dataset.tgSubmitAction === 'true'"),
    "Only form-associated native submits and explicitly marked mutation controls may be icon-normalized.");
Check(!helper.Contains("a[href]"), "The helper must not normalize navigation links.");
Check(helper.Contains("if (event.defaultPrevented || !form.checkValidity()) return;"),
    "Normal POST pending state must start only after validation accepts submission.");
Check(helper.Contains("form.dataset.tgPending === 'true'"), "Normal POST forms must prevent duplicate submissions.");
Check(helper.Contains("window.addEventListener('pageshow', restoreAll)"), "Back/Forward restoration must clear transient pending state.");
Check(helper.Contains("element.innerHTML = element.dataset.tgOriginalHtml"),
    "Handled async failures must restore non-submit controls without stripping their original icon markup.");
Check(registration.Contains("form.addEventListener('invalid'") && registration.Contains("}, true);"),
    "Registration's capture-phase invalid handler must remain available before pending state begins.");
Check(registration.Contains("normalizePhilippineMobileNumber")
      && registration.Contains("setCustomValidity(message)")
      && !registration.Contains("'+63 ' + input.value"),
    "Registration contact fields must keep native validation and must not mutate local input values by prepending a duplicate country prefix at submit time.");
Check(registrations.Contains("TrailGuardActionPending.restore(submitter, form)")
      && registrations.Contains("TrailGuardActionPending.restore(trigger)"),
    "Receipt upload and registration cancellation must recover after handled or network failures.");
Check(eventDetails.Contains("TrailGuardActionPending.restore(completeBtn)")
      && eventDetails.Contains("TrailGuardActionPending.restore(confirmBtn)"),
    "Event mutation confirmations must recover after handled or network failures.");
Check(records.Contains("TrailGuardActionPending.restore(exportBtn)"),
    "Async export must restore its original control after completion or failure.");
Check(eventIndex.Contains("TrailGuardActionPending.begin(save, deleting ? 'Deleting…' : 'Saving…', form)"),
    "Pickup-point mutations must expose operation-specific pending text.");
Check(trailIndex.Contains("data-tg-submit-action=\"true\"") && trailIndex.Contains("Deleting…"),
    "Trail mutations must be explicitly marked and recoverable text submit actions.");
Check(trailIndex.Contains("TrailGuardActionPending.begin(submitBtn, 'Saving…', form)")
      && trailIndex.Contains("if (!window.TrailGuardActionPending.begin(submitBtn, 'Saving…', form))")
      && trailIndex.Contains("<form id=\"addTrailForm\"")
      && trailIndex.Contains("novalidate"),
    "The custom-validated Add Trail form must start the shared pending state only after validation passes and block a duplicate submit.");
Check(trailIndex.Contains("data-tg-keep-idle-icon=\"true\"") && trailIndex.Contains("trailDeleteOriginalClass"),
    "The Trail-card deletion control must keep its compact trash icon when idle and restore it after a failed request.");
Check(helper.Contains("data-tg-keep-idle-icon"),
    "The shared initializer must respect explicit idle-icon exceptions.");
Check(layout.Contains("type=\"button\" id=\"backToTopBtn\"")
      && layout.Contains("aria-label=\"Back to top\"")
      && layout.Contains("fa-arrow-up")
      && layout.Contains("prefers-reduced-motion: reduce"),
    "Back to Top must remain an immediate, labeled, reduced-motion-aware icon control.");
Check(assessmentForm.Contains("button type=\"button\" onclick=\"closePrivacyModal()\"")
      && myRegistrations.Contains("button type=\"button\" onclick=\"openDetails")
      && myRegistrations.Contains("button type=\"button\" onclick=\"closeDetails()\""),
    "Local modal controls must declare button semantics and keep their own icon/text markup outside submit normalization.");
Check(settings.Contains("fa-solid fa-eye")
      && participantNavbar.Contains("mobileMenuIconBars")
      && toast.Contains("fa-solid fa-xmark")
      && customSelect.Contains("trigger.type = 'button'")
      && eventIndexView.Contains("editIcon.className = 'fa-solid fa-pen text-xs'"),
    "Password toggles, menus, toast dismissal, custom selects, and generated edit controls must retain their established non-submit icons.");

Console.WriteLine($"TrailGuard action-state source verification passed ({assertions} assertions).");
