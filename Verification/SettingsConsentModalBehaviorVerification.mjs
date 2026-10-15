import { readFileSync } from 'node:fs';

const source = readFileSync('Views/Settings/Index.cshtml', 'utf8');
const settingsScript = source.match(/<script>([\s\S]*?)<\/script>/)?.[1];
if (!settingsScript) throw new Error('Settings inline script was not found.');

let assertions = 0;
function check(condition, message) {
    if (!condition) throw new Error(message);
    assertions++;
}

class ClassList {
    constructor(...names) { this.names = new Set(names); }
    add(...names) { names.forEach(name => this.names.add(name)); }
    remove(...names) { names.forEach(name => this.names.delete(name)); }
    contains(name) { return this.names.has(name); }
}

class Element {
    constructor(id, options = {}) {
        this.id = id;
        this.tagName = options.tagName ?? 'BUTTON';
        this.classList = new ClassList(...(options.classes ?? []));
        this.dataset = options.dataset ?? {};
        this.attributes = {};
        this.listeners = new Map();
        this.children = [];
        this.disabled = false;
        this.hidden = options.hidden ?? false;
        this.innerHTML = options.html ?? '';
        this.textContent = options.text ?? '';
        this.value = options.value ?? '';
        this.removed = false;
        this.focused = false;
    }
    addEventListener(type, listener) {
        const listeners = this.listeners.get(type) ?? [];
        listeners.push(listener);
        this.listeners.set(type, listeners);
    }
    async emit(type, event = {}) {
        const listeners = this.listeners.get(type) ?? [];
        await Promise.all(listeners.map(listener => listener({ preventDefault() {}, ...event })));
    }
    setAttribute(name, value) { this.attributes[name] = String(value); }
    removeAttribute(name) { delete this.attributes[name]; }
    getAttribute(name) { return this.attributes[name] ?? null; }
    remove() { this.removed = true; }
    focus() { this.focused = true; }
    getClientRects() { return this.hidden || this.removed ? [] : [{}]; }
    closest(selector) {
        if (this.hidden && selector.includes('[hidden]')) return this;
        if (this.choice?.classList.contains('hidden') && selector.includes('.hidden')) return this.choice;
        return null;
    }
}

function createHarness(fetchImplementation) {
    const modal = new Element('profileConsentModal', { classes: ['hidden'], dataset: { autoOpen: 'true', consentAccepted: 'false' } });
    const form = new Element('profileConsentForm', { tagName: 'FORM' });
    form.action = '/Settings/AcceptProfileConsent';
    const version = new Element('Version', { tagName: 'INPUT', value: 'profile-privacy-v1-test' });
    form.elements = { Version: version };
    const choice = new Element('profileConsentChoice', { classes: ['flex'] });
    const checkbox = new Element('profileConsentCheckbox', { tagName: 'INPUT', dataset: { modalInitial: '' } });
    checkbox.choice = choice;
    const agree = new Element('profileConsentAgreeBtn', { text: 'Agree', html: '<i></i> Agree' });
    const cancel = new Element('profileConsentCancelBtn', { text: 'Cancel' });
    const footerClose = new Element('profileConsentFooterClose', { text: 'Close', hidden: true });
    const headerClose = new Element('profileConsentCloseButton', { text: 'Close' });
    const accepted = new Element('profileConsentAccepted', { classes: ['hidden'] });
    const error = new Element('profileConsentError', { classes: ['hidden'] });
    const opener = new Element('viewProfileConsentBtn', { text: 'View Privacy Notice & Consent' });
    modal.focusables = [headerClose, checkbox, cancel, agree, footerClose];
    modal.querySelector = selector => selector === '[data-modal-initial]' && !checkbox.removed ? checkbox : null;
    modal.querySelectorAll = () => modal.focusables.filter(element => !element.removed);

    const elements = new Map([
        [modal.id, modal], [form.id, form], [version.id, version], [choice.id, choice], [checkbox.id, checkbox],
        [agree.id, agree], [cancel.id, cancel], [footerClose.id, footerClose], [headerClose.id, headerClose],
        [accepted.id, accepted], [error.id, error], [opener.id, opener]
    ]);
    const documentListeners = new Map();
    const document = {
        body: { style: {} },
        activeElement: opener,
        getElementById(id) { return elements.get(id) ?? null; },
        querySelector() { return null; },
        querySelectorAll() { return []; },
        addEventListener(type, listener) {
            const listeners = documentListeners.get(type) ?? [];
            listeners.push(listener);
            documentListeners.set(type, listeners);
        },
        async emit(type) {
            await Promise.all((documentListeners.get(type) ?? []).map(listener => listener({ preventDefault() {} })));
        },
        contains(element) { return !!element && !element.removed; }
    };
    const pending = {
        beginCount: 0,
        restoreCount: 0,
        begin(button, label, pendingForm) {
            if (button.dataset.tgPendingActive === 'true') return false;
            this.beginCount++;
            button.dataset.tgPendingActive = 'true';
            button.disabled = true;
            button.textContent = label;
            pendingForm.dataset.tgPending = 'true';
            return true;
        },
        restore(button, pendingForm) {
            this.restoreCount++;
            delete button.dataset.tgPendingActive;
            button.disabled = false;
            button.textContent = 'Agree';
            delete pendingForm.dataset.tgPending;
        }
    };
    let nextTimer = 0;
    const timers = new Map();
    const window = {
        TrailGuardActionPending: pending,
        showToast() {},
        addEventListener() {},
        setTimeout(callback) {
            const id = ++nextTimer;
            timers.set(id, callback);
            return id;
        },
        clearTimeout(id) { timers.delete(id); }
    };
    const execute = new Function('window', 'document', 'requestAnimationFrame', 'FormData', 'fetch', settingsScript);
    execute(window, document, callback => callback(), class FormData { constructor(boundForm) { this.form = boundForm; } }, fetchImplementation);

    return {
        modal, form, version, choice, checkbox, agree, cancel, footerClose, headerClose, accepted, error, opener, pending,
        initialize: () => document.emit('DOMContentLoaded'),
        submit: () => form.emit('submit'),
        expireTimeouts: () => {
            const callbacks = [...timers.values()];
            timers.clear();
            callbacks.forEach(callback => callback());
        },
        pendingTimerCount: () => timers.size
    };
}

async function initialize(harness) {
    await harness.initialize();
    check(harness.modal.classList.contains('flex') && !harness.modal.classList.contains('hidden'),
        'The unaccepted consent modal must open from its auto-open flag.');
}

let successSignal;
const success = createHarness(async (_, request) => {
    successSignal = request.signal;
    return {
    ok: true,
    json: async () => ({ version: 'profile-privacy-v1-test', acceptedAtUtc: '2026-10-16T00:00:00.0000000Z' })
    };
});
await initialize(success);
success.checkbox.checked = true;
await success.submit();
check(success.pending.restoreCount === 1 && !success.agree.disabled && success.form.dataset.tgPending === undefined,
    'A confirmed success must clear the shared pending state before closing.');
check(success.modal.classList.contains('hidden') && success.opener.focused,
    'A confirmed success must close the modal and restore focus to its opener.');
check(success.modal.dataset.consentAccepted === 'true' && success.modal.dataset.autoOpen === 'false'
      && success.modal.dataset.consentVersion === success.version.value,
    'A confirmed response must update the page consent state from the server result.');
check(successSignal && !successSignal.aborted && success.pendingTimerCount() === 0,
    'A completed request must carry an abort signal and clear its timeout.');
check(success.agree.removed && success.cancel.removed && !success.footerClose.hidden
      && success.choice.classList.contains('hidden') && !success.accepted.classList.contains('hidden'),
    'Reopening after acceptance must expose Close only and no agreement controls.');

for (const [name, response] of [
    ['HTTP failure', async () => ({ ok: false, json: async () => ({ message: 'Agreement rejected.' }) })],
    ['malformed JSON success', async () => ({ ok: true, json: async () => null })],
    ['network failure', async () => { throw new TypeError('Network unavailable.'); }]
]) {
    const failure = createHarness(response);
    await initialize(failure);
    failure.checkbox.checked = true;
    await failure.submit();
    check(!failure.modal.classList.contains('hidden') && !failure.agree.disabled
          && failure.form.dataset.tgPending === undefined && !failure.error.classList.contains('hidden'),
        `${name} must keep the modal open, show an error, and restore retry controls.`);
}

let attempts = 0;
const retry = createHarness(async () => {
    attempts++;
    if (attempts === 1) throw new TypeError('Network unavailable.');
    return { ok: true, json: async () => ({ version: 'profile-privacy-v1-test', acceptedAtUtc: '2026-10-16T00:00:00.0000000Z' }) };
});
await initialize(retry);
retry.checkbox.checked = true;
await retry.submit();
await retry.submit();
check(attempts === 2 && retry.modal.dataset.consentVersion === retry.version.value && retry.agree.removed,
    'A retry must resend the same version and transition only after a confirmed idempotent response.');

const timeout = createHarness((_, request) => new Promise((_, reject) => {
    request.signal.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')));
}));
await initialize(timeout);
timeout.checkbox.checked = true;
const timedSubmission = timeout.submit();
timeout.expireTimeouts();
await timedSubmission;
check(!timeout.agree.disabled && timeout.form.dataset.tgPending === undefined
      && timeout.error.textContent.includes('could not confirm')
      && timeout.modal.dataset.consentAccepted !== 'true',
    'A timed-out request must restore retry controls without claiming that consent was accepted.');

console.log(`PASS: ${assertions} Settings consent modal behavior assertions.`);
