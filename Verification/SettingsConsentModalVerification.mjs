import { readFileSync } from 'node:fs';

const source = readFileSync('Views/Settings/Index.cshtml', 'utf8');
const layout = readFileSync('Views/Shared/_Layout.cshtml', 'utf8');
const stylesheet = readFileSync('wwwroot/css/input.css', 'utf8');
let assertions = 0;
function check(condition, message) {
    if (!condition) throw new Error(message);
    assertions++;
}

check(source.includes('function isVisibleSettingsFocusable(element)'),
    'Settings modals need one visibility-aware focusability check.');
const settingsScript = source.match(/<script>([\s\S]*?)<\/script>/)?.[1];
let hasValidSyntax = false;
try {
    new Function(settingsScript ?? 'throw new Error("Settings script was not found.");');
    hasValidSyntax = true;
} catch {
    hasValidSyntax = false;
}
check(hasValidSyntax, 'The Settings inline script must have valid JavaScript syntax.');
check(layout.indexOf('@RenderBody()') < layout.indexOf('@await RenderSectionAsync("Modals"')
      && source.includes("document.addEventListener('DOMContentLoaded', function initializeProfileConsent() {"),
    'Consent initialization must wait until the later-rendered modal section exists.');
check(source.includes('var automaticallyOpenProfileConsent = isParticipant && ViewBag.IsActive == true && !hasCurrentProfileConsent;')
      && source.includes('data-auto-open="@automaticallyOpenProfileConsent.ToString().ToLowerInvariant()"')
      && source.includes("if (modal.dataset.autoOpen === 'true') openProfileConsentModal"),
    'The server boolean must render canonically and trigger opening only for unaccepted active participants.');
check(!source.includes('birthdayPolicyHint')
      && !source.includes('<p id="birthdayPolicyHint"')
      && source.includes('aria-describedby="birthdayClientError"'),
    'The static birthday policy helper must be absent while its red validation error remains associated.');
check(source.includes('participant-phone-required')
      && source.includes("const message = phone.value.trim() ? '' : 'Phone Number is required.';")
      && source.includes("phone.setCustomValidity(message);")
      && source.includes("phone.addEventListener('input', function () { validatePhone(true); });"),
    'Participant phone validation must reject empty and whitespace values before password confirmation.');
check(source.includes("if (facebookLink && !facebookLink.value.trim()) facebookLink.value = '';")
      && source.includes('Facebook Link <span class="text-gray-500 text-xs">(Optional)</span>'),
    'Facebook whitespace must be normalized as an optional value before native form validation.');
check(source.includes("!element.closest('.hidden, [hidden], [aria-hidden=\"true\"], [inert]')")
      && source.includes('element.getClientRects().length > 0'),
    'Hidden or inert elements must not be focus targets.');
check(source.includes('const target = isVisibleSettingsFocusable(initial) ? initial : settingsFocusable(modal)[0];'),
    'Initial modal focus must reject a hidden consent checkbox and fall back to a visible control.');
check(source.includes('id="profileConsentCloseButton"')
      && source.includes('const closeButton = document.getElementById(\'profileConsentCloseButton\');'),
    'Accepted consent needs an explicitly addressable visible close control.');

const acceptedTransition = source.indexOf("choice.classList.add('hidden');");
const pendingRestore = source.indexOf('window.TrailGuardActionPending.restore(agree, form);');
const successClose = source.indexOf('closeProfileConsentModal();', pendingRestore);
check(acceptedTransition >= 0 && pendingRestore > acceptedTransition && successClose > pendingRestore,
    'Accepting consent must clear pending state before closing the modal.');
check(source.includes('const requestAbort = new AbortController();')
      && source.includes('signal: requestAbort.signal')
      && source.includes('window.clearTimeout(requestTimeout);')
      && source.includes('It may have been recorded; reopen Settings to check before retrying.'),
    'A stalled request must become an explicitly uncertain, retryable outcome.');
check(source.includes('@if (hasCurrentProfileConsent)')
      && source.includes('id="profileConsentFooterClose"')
      && source.includes('id="profileConsentCancelBtn"')
      && source.includes('agree.remove();'),
    'Accepted consent must render or transition to a Close-only footer without an Agree action.');
check(source.includes('tg-settings-consent-scroll overflow-y-auto')
      && stylesheet.includes('.tg-settings-consent-scroll {')
      && stylesheet.includes('.tg-settings-consent-scroll::-webkit-scrollbar {'),
    'Only the consent modal body must hide its native scrollbar while remaining scrollable.');
check(source.includes('id="modalPasswordInput" data-modal-initial')
      && source.includes("openSettingsModal('passwordModal', document.getElementById('saveProfileBtn'));"),
    'Password confirmation must retain its own initial-focus and opener behavior.');
check(source.includes('document.contains(settingsModalTrigger)) settingsModalTrigger.focus();')
      && source.includes("if (event.key !== 'Tab') return;"),
    'Modal close focus restoration and Tab trapping must remain in place.');

console.log(`PASS: ${assertions} Settings consent modal source-contract assertions.`);
