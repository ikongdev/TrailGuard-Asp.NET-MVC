import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const source = readFileSync(new URL('../Views/Event/Index.cshtml', import.meta.url), 'utf8');
let assertions = 0;
function check(condition, message) { if (!condition) throw new Error(message); assertions++; }
function extract(name) {
    const start = source.search(new RegExp(`(?:async )?function ${name}\\(`));
    if (start < 0) throw new Error(`Missing ${name}`);
    const opening = source.indexOf('{', start);
    let depth = 0;
    for (let i = opening; i < source.length; i++) {
        if (source[i] === '{') depth++;
        if (source[i] === '}' && --depth === 0) return source.slice(start, i + 1);
    }
    throw new Error(`Unclosed ${name}`);
}

const controls = new Map();
function element(id = '') {
    const attributes = new Set();
    return { id, value: '', disabled: false, hidden: false, options: [], textContent: '', className: '',
        classList: { add() {}, remove() {} },
        setAttribute(name) { attributes.add(name); if (name === 'hidden') this.hidden = true; },
        removeAttribute(name) { attributes.delete(name); if (name === 'hidden') this.hidden = false; },
        toggleAttribute(name, force) { if (force) this.setAttribute(name); else this.removeAttribute(name); },
        hasAttribute(name) { return attributes.has(name); },
        appendChild(child) { this.options.push(child); },
        querySelector() { return this.options.find(option => option.hasAttribute('data-injected-current-trail')) || null; }
    };
}
for (const [, id] of extract('populateEditEventForm').matchAll(/getElementById\('([^']+)'\)/g)) controls.set(id, element(id));
controls.delete('editEventOrganizerId'); // Organizer projection: no editable organizer selector.
const select = controls.get('editEventTrailId');
select.options = [{ value: '7' }, { value: '8' }];
let payload;
let fetches = 0;
const pending = [];
const sandbox = {
    document: { getElementById: id => controls.get(id) || null, createElement: () => element() },
    window: { CustomSelect: { refresh(target) { target.enhancedDisabled = target.disabled; } } },
    editEventPickupSchedules: [], editEventPickupEditIndex: null, editEventWeatherSnapshot: null,
    editEventDurationEdited: false, editEventTrailRequestToken: 0, editEventOrganizerSelect: null,
    renderEditEventPickupSchedules() {}, setPickupPointOptions() {}, setEditEventWeatherState() {},
    resetEditEventTrailPreview() {}, resetEditEventWeatherDisplay() {}, resetEditEventPickupSchedules() {},
    removeInjectedCurrentTrailOption() {
        select.options = select.options.filter(option => !option.hasAttribute?.('data-injected-current-trail'));
    },
    editValidateStep: () => true, setEditEventSubmitting: value => pending.push(value),
    postJson: async (path, value) => { check(path === '/Event/EditEvent', 'Must retain the existing Edit endpoint.'); payload = value; return { json: async () => ({ success: false, message: 'Stale modal rejected' }) }; },
    showToast() {}, fetch: async () => { fetches++; throw new Error('Unexpected fetch'); }, console
};
vm.createContext(sandbox);
vm.runInContext(['populateEditEventForm', 'resetEditEventDisplay', 'handleEditEventTrailChange', 'handleEditEventSubmit'].map(extract).join('\n'), sandbox);
const data = { id: 41, eventTitle: 'Synthetic Event', description: 'Description', eventDate: '2026-11-20', eventTime: '06:00',
    trailId: 7, trailName: 'Captured trail', trailIsActive: true, canChangeTrail: true,
    estimatedDuration: 5, capacity: 20, pickupSchedules: [], trailDistanceKm: 8, trailElevationGainMeters: 600,
    trailDifficulty: 'Minor Hike', trailLocation: 'Local', trailTerrain: 'Rocky', trailClassLabel: 'Class 3' };

sandbox.populateEditEventForm(data);
check(select.value === '7' && !select.disabled && !select.enhancedDisabled, 'No-record trail must be selected and editable.');
check(controls.get('editEventTrailLockedNote').hidden, 'Unlocked Event must hide locked guidance.');
sandbox.populateEditEventForm({ ...data, canChangeTrail: false });
check(select.value === '7' && select.disabled && select.enhancedDisabled, 'Locked native/enhanced controls must retain the value.');
check(!controls.get('editEventTrailLockedNote').hidden, 'Locked Event must expose guidance.');
await sandbox.handleEditEventTrailChange.call(select);
check(fetches === 0, 'Disabled trail changes must not fetch or reset the duration.');
check(controls.get('editEventDuration').value === 5, 'Hydration/locked change must preserve duration.');
await sandbox.handleEditEventSubmit({ preventDefault() {} });
check(payload.TrailId === 7 && payload.Id === 41 && payload.EstimatedDuration === 5, 'Explicit JSON must retain disabled select values.');
check(!('canChangeTrail' in payload) && !('CanChangeTrail' in payload), 'Availability flag must not become browser authority in the payload.');
check(pending.join(',') === 'true,false', 'Server rejection must restore the submit action for retry.');

sandbox.populateEditEventForm({ ...data, trailId: 19, trailIsActive: false, canChangeTrail: false });
check(select.value === '19' && select.disabled && select.options.some(option => option.value === '19'), 'Inactive current trail must remain selected while locked.');
check(!controls.get('editEventTrailDeactivatedNote').hidden, 'Inactive current trail notice must remain visible.');
await sandbox.handleEditEventSubmit({ preventDefault() {} });
check(payload.TrailId === 19, 'Inactive disabled trail must survive submission.');
sandbox.resetEditEventDisplay();
check(!select.disabled && !select.enhancedDisabled && controls.get('editEventTrailLockedNote').hidden, 'Close/reset must clear locked state and resync enhancement.');
check(!select.options.some(option => option.value === '19'), 'Injected inactive trail must not leak into another Event.');
sandbox.populateEditEventForm(data);
check(select.value === '7' && !select.disabled, 'Reopening a no-record Event must restore editing.');
sandbox.populateEditEventForm({ ...data, canChangeTrail: undefined });
check(select.disabled, 'Missing availability state must not enable replacement.');
console.log(`PASS: ${assertions} Event trail-policy behavioral assertions with controlled DOM/HTTP fixtures; no browser, MVC host, or database.`);
