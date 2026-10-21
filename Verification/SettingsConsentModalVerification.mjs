import { readFileSync } from 'node:fs';

const source = readFileSync('Views/Settings/Index.cshtml', 'utf8');
const layout = readFileSync('Views/Shared/_Layout.cshtml', 'utf8');
const stylesheet = readFileSync('wwwroot/css/input.css', 'utf8');
const phoneHelperSource = readFileSync('wwwroot/js/philippine-mobile-number.js', 'utf8');
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
      && source.includes('TrailGuardPhilippineMobileNumber.attachLocalDigitLimit')
      && source.includes('TrailGuardPhilippineMobileNumber.updateValidity')
      && source.includes("phone.addEventListener('input', function () { validatePhone(true); });"),
    'Participant phone validation must reject empty and whitespace values before password confirmation.');
check(phoneHelperSource.includes('function attachLocalDigitLimit')
      && phoneHelperSource.includes("event.inputType === 'insertFromPaste' && applyCompleteCandidate(candidate)")
      && phoneHelperSource.includes("'Phone Number cannot exceed 10 local digits.'"),
    'The shared helper must normalize complete pasted country formats before enforcing the Settings-only local-digit limit.');

const phoneWindow = {};
new Function('window', phoneHelperSource)(phoneWindow);
const phoneHelper = phoneWindow.TrailGuardPhilippineMobileNumber;
class FakePhoneInput {
    constructor(value = '') {
        this.value = value;
        this.selectionStart = value.length;
        this.selectionEnd = value.length;
        this.validationMessage = '';
        this.listeners = new Map();
    }
    addEventListener(type, callback) {
        const handlers = this.listeners.get(type) ?? [];
        handlers.push(callback);
        this.listeners.set(type, handlers);
    }
    setCustomValidity(message) { this.validationMessage = message; }
    emit(type, properties = {}) {
        const event = {
            inputType: properties.inputType,
            data: properties.data,
            clipboardData: properties.clipboardData,
            defaultPrevented: false,
            preventDefault() { this.defaultPrevented = true; }
        };
        for (const callback of this.listeners.get(type) ?? []) callback(event);
        return event;
    }
}
function attachPhoneLimit(value = '') {
    const input = new FakePhoneInput(value);
    const error = { textContent: '', classList: { remove() {}, add() {}, toggle() {} } };
    phoneHelper.attachLocalDigitLimit(input, error, 'Phone Number is required.');
    return { input, error };
}

const completed = attachPhoneLimit('912 345 6789');
const eleventhDigit = completed.input.emit('beforeinput', { inputType: 'insertText', data: '0' });
check(eleventhDigit.defaultPrevented
      && completed.input.value === '912 345 6789'
      && completed.input.validationMessage.includes('cannot exceed 10'),
    'An eleventh local digit must be blocked without changing a complete number.');

completed.input.selectionStart = 0;
completed.input.selectionEnd = 1;
const replacement = completed.input.emit('beforeinput', { inputType: 'insertText', data: '8' });
check(!replacement.defaultPrevented,
    'Replacing a selected local digit must remain possible at the 10-digit limit.');
completed.input.value = '812 345 6789';
completed.input.emit('input');
check(completed.input.value === '812 345 6789',
    'Middle editing must not silently rewrite an invalid replacement.');

const editable = attachPhoneLimit('912 345 6789');
check(!editable.input.emit('beforeinput', { inputType: 'deleteContentBackward' }).defaultPrevented,
    'Backspace and Delete input types must not be blocked by the digit guard.');
editable.input.value = '912 345 678';
editable.input.selectionStart = editable.input.selectionEnd = editable.input.value.length;
editable.input.emit('input');
check(!editable.input.emit('beforeinput', { inputType: 'insertText', data: '9' }).defaultPrevented,
    'Retyping after deletion must remain possible.');
editable.input.value = '912 345 6789';
editable.input.emit('input');
check(editable.input.value === '912 345 6789', 'A retyped complete local number must retain 3-3-4 formatting.');

for (const accepted of [
    '9123456789',
    '912 345 6789',
    '09123456789',
    '09 123-456-789',
    '639123456789',
    '63 912-345-6789',
    '+639123456789',
    '+63 912 345 6789'
]) {
    const pasted = attachPhoneLimit();
    const paste = pasted.input.emit('paste', { clipboardData: { getData: () => accepted } });
    check(paste.defaultPrevented && pasted.input.value === '912 345 6789',
        `A valid pasted Philippine format must normalize locally: ${accepted}`);
}

const overlongPaste = attachPhoneLimit('912 345 6789');
overlongPaste.input.selectionStart = 0;
overlongPaste.input.selectionEnd = overlongPaste.input.value.length;
const rejectedPaste = overlongPaste.input.emit('paste', { clipboardData: { getData: () => '91234567890' } });
check(rejectedPaste.defaultPrevented
      && overlongPaste.input.value === '912 345 6789'
      && overlongPaste.input.validationMessage.includes('cannot exceed 10'),
    'An overlong paste must be rejected intact without truncation.');

const autofill = attachPhoneLimit();
autofill.input.value = '+639123456789';
autofill.input.emit('input');
check(autofill.input.value === '912 345 6789',
    'The input fallback must safely normalize a complete mobile/autofill value.');

const legacy = attachPhoneLimit('91234567890');
check(legacy.input.value === '91234567890',
    'An invalid legacy value must remain visible and editable on initial load.');
legacy.input.selectionStart = legacy.input.selectionEnd = legacy.input.value.length;
check(legacy.input.emit('beforeinput', { inputType: 'insertText', data: '1' }).defaultPrevented,
    'An invalid legacy value must remain editable without accepting additional digits.');
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
