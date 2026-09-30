import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const source = readFileSync(new URL('../Views/Event/Index.cshtml', import.meta.url), 'utf8');
let assertions = 0;
function check(condition, message) {
    if (!condition) throw new Error(message);
    assertions++;
}

function extractFunction(name) {
    const start = source.indexOf(`function ${name}`);
    if (start < 0) throw new Error(`Could not find ${name}.`);
    const braceStart = source.indexOf('{', start);
    let depth = 0;
    for (let index = braceStart; index < source.length; index++) {
        if (source[index] === '{') depth++;
        if (source[index] === '}' && --depth === 0) return source.slice(start, index + 1);
    }
    throw new Error(`Could not close ${name}.`);
}

function extractAsyncFunction(name) {
    const start = source.indexOf(`async function ${name}`);
    if (start < 0) throw new Error(`Could not find ${name}.`);
    const braceStart = source.indexOf('{', start);
    let depth = 0;
    for (let index = braceStart; index < source.length; index++) {
        if (source[index] === '{') depth++;
        if (source[index] === '}' && --depth === 0) return source.slice(start, index + 1);
    }
    throw new Error(`Could not close ${name}.`);
}

function deferred() {
    let resolve;
    const promise = new Promise(value => { resolve = value; });
    return { promise, resolve };
}

function createDocument() {
    const controls = new Map();
    const document = {
        body: { style: {}, dataset: {} },
        activeElement: null,
        getElementById(id) { return controls.get(id) || null; },
        addEventListener() { },
        removeEventListener() { },
        querySelectorAll() { return []; }
    };
    function control(id) {
        const classes = new Set();
        const value = {
            id, hidden: false, disabled: false, textContent: '', className: '',
            classList: { add: (...names) => names.forEach(name => classes.add(name)), remove: (...names) => names.forEach(name => classes.delete(name)) },
            focus() { document.activeElement = value; }
        };
        controls.set(id, value);
        return value;
    }
    return { document, controls, control };
}

const intentFunctions = [
    extractFunction('beginModalOpeningIntent'),
    extractFunction('isCurrentModalOpeningIntent'),
    extractFunction('invalidateModalOpeningIntent')
].join('\n');
const addFunction = extractAsyncFunction('openAddEventModal');
const pickupFunction = extractAsyncFunction('openPickupPointsModal');

{
    const firstLoad = deferred();
    const secondLoad = deferred();
    const dom = createDocument();
    const addModal = dom.control('addEventModal');
    const pickupModal = dom.control('pickupPointsModal');
    const search = dom.control('pickupPointsSearch');
    let loadCount = 0;
    let addOpened = 0;
    const sandbox = {
        document: dom.document,
        modalOpeningIntentToken: 0,
        addEventOpenToken: 0,
        pickupPointsModalOpen: false,
        pickupPointsModalLifecycleToken: 0,
        pickupPointsModalOpener: null,
        loadPickupPoints: () => (++loadCount === 1 ? firstLoad.promise : secondLoad.promise),
        goToAddEventStep() { }, showToast() { }, renderPickupPointsCatalog() { }, setPickupPointsBackgroundInert() { }, handlePickupPointsModalKeydown() { },
        AddEventModal: { open() { addOpened++; } },
        window: { CustomSelect: null },
        console
    };
    vm.runInNewContext(`${intentFunctions}\n${addFunction}\n${pickupFunction}\nglobalThis.runAdd = openAddEventModal; globalThis.runPickup = openPickupPointsModal;`, sandbox);
    const addPromise = sandbox.runAdd();
    const pickupPromise = sandbox.runPickup({ id: 'catalog-button', focus() { } });
    firstLoad.resolve(true);
    secondLoad.resolve(true);
    await Promise.all([addPromise, pickupPromise]);
    check(addOpened === 0, 'A delayed Add Event request cannot open after Pickup Points becomes the newer modal intent.');
    check(sandbox.pickupPointsModalOpen, 'The newer Pickup Points intent remains the only modal opened by the race.');
    check(dom.document.activeElement === search, 'Pickup Points management retains its own initial focus after superseding Add Event.');
    check(addModal.hidden !== false || addOpened === 0, 'The stale Add Event request does not change the Add Event modal.');
    check(pickupModal.hidden === false, 'The current Pickup Points modal remains visible.');
}

{
    const eventResponse = deferred();
    const catalogResponse = deferred();
    const dom = createDocument();
    dom.control('editEventModal'); dom.control('pickupPointsModal'); dom.control('pickupPointsSearch');
    let editOpened = 0;
    const editFunction = extractAsyncFunction('openEditModal');
    const sandbox = {
        document: dom.document,
        modalOpeningIntentToken: 0,
        editEventAbortController: null, editEventLoadToken: 0, editEventCurrentEventId: null,
        pickupPointsModalOpen: false, pickupPointsModalLifecycleToken: 0, pickupPointsModalOpener: null,
        fetch: () => eventResponse.promise,
        loadPickupPoints: () => catalogResponse.promise,
        resetEditEventDisplay() { throw new Error('Stale edit hydrated the form.'); }, populateEditEventForm() { throw new Error('Stale edit populated the form.'); },
        showToast() { }, editUpdateStepUI() { }, editCurrentStep: 1, renderPickupPointsCatalog() { }, setPickupPointsBackgroundInert() { }, handlePickupPointsModalKeydown() { },
        EditEventModal: { open() { editOpened++; } }, window: { CustomSelect: null }, AbortController, console
    };
    vm.runInNewContext(`${intentFunctions}\n${editFunction}\n${pickupFunction}\nglobalThis.runEdit = openEditModal; globalThis.runPickup = openPickupPointsModal;`, sandbox);
    const editPromise = sandbox.runEdit({ id: 'edit-button' }, 7);
    const pickupPromise = sandbox.runPickup({ id: 'catalog-button', focus() { } });
    eventResponse.resolve({ json: async () => ({ success: true, id: 7 }) });
    catalogResponse.resolve(true);
    await Promise.all([editPromise, pickupPromise]);
    check(editOpened === 0, 'A delayed Edit Event request cannot hydrate or open after a newer modal intent.');
}

{
    const dom = createDocument();
    const list = dom.control('pickupPointsList'); list.replaceChildren = () => { };
    const loading = dom.control('pickupPointsLoading');
    const empty = dom.control('pickupPointsEmpty');
    const noMatches = dom.control('pickupPointsNoMatches');
    const retry = dom.control('pickupPointsRetry');
    const search = dom.control('pickupPointsSearch');
    const renderFunction = extractFunction('renderPickupPointsCatalog');
    const sandbox = { document: dom.document, pickupPointsLoadState: 'error', pickupPointsLoadError: 'Unable to load pickup points. Please try again.', pickupPoints: [], console };
    vm.runInNewContext(`${renderFunction}\nglobalThis.render = renderPickupPointsCatalog;`, sandbox);
    sandbox.render();
    search.value = 'cubao';
    sandbox.render();
    check(!loading.hidden && loading.textContent.includes('Unable to load'), 'Catalog loading failure remains visible after a search change.');
    check(!retry.hidden, 'Catalog Retry remains available after a search change during an error state.');
    check(empty.hidden, 'The empty catalog message is not shown for a failed load.');
    check(noMatches.hidden, 'The no-matches message is not shown for a failed load.');
}

{
    const dom = createDocument();
    const input = dom.control('pickupPointName'); input.value = 'Cubao';
    const save = dom.control('pickupPointFormSave');
    const cancel = dom.control('pickupPointFormCancel');
    const add = dom.control('pickupPointAddButton');
    const retry = dom.control('pickupPointsRetry');
    const list = dom.control('pickupPointsList'); list.querySelectorAll = () => [];
    dom.document.querySelectorAll = () => [cancel, add, retry, save];
    const pendingFunction = extractFunction('setPickupPointMutationPending');
    const focusFunction = extractFunction('focusPickupPointsControl');
    const saveFunction = extractAsyncFunction('savePickupPoint');
    const sandbox = {
        document: dom.document, pickupPointMutationInFlight: false, pickupPointDeletingId: null, pickupPointEditingId: null,
        pickupPointFormSessionToken: 0, pickupPointMutationOperationId: 0, pickupPointsModalOpen: true,
        postJson: async () => ({ ok: true, json: async () => ({ success: false, message: 'Duplicate pickup point.' }) }),
        setPickupPointFormError() { }, hidePickupPointForm() { }, loadPickupPoints: async () => true, showToast() { }, console
    };
    vm.runInNewContext(`${pendingFunction}\n${focusFunction}\n${saveFunction}\nglobalThis.save = savePickupPoint;`, sandbox);
    await sandbox.save({ preventDefault() { } });
    check(!input.disabled, 'Validation failure re-enables the catalog name input.');
    check(dom.document.activeElement === input, 'Validation failure restores focus only after the input is enabled.');
    sandbox.pickupPointDeletingId = 42;
    await sandbox.save({ preventDefault() { } });
    check(!save.disabled && dom.document.activeElement === save, 'Delete validation failure restores focus to the enabled confirmation control.');
}

console.log(`PASS: ${assertions} Pickup Points behavioral assertions with controlled request and DOM mocks.`);
