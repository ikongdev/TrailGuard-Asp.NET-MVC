import fs from 'node:fs';
import path from 'node:path';

const source = fs.readFileSync(path.join(process.cwd(), 'wwwroot', 'js', 'action-state.js'), 'utf8');
const pageShowHandlers = [];
const buttons = [];
const document = {
    readyState: 'complete',
    body: {},
    querySelectorAll(selector) {
        if (selector === '[data-tg-pending-active="true"]') {
            return buttons.filter((button) => button.dataset.tgPendingActive === 'true');
        }
        return [];
    },
    addEventListener() {}
};
const window = {
    addEventListener(type, handler) {
        if (type === 'pageshow') pageShowHandlers.push(handler);
    }
};

class MutationObserver {
    constructor() {}
    observe() {}
}

const Node = { ELEMENT_NODE: 1 };
const CSS = { escape(value) { return value; } };
new Function('window', 'document', 'Node', 'MutationObserver', 'CSS', source)(window, document, Node, MutationObserver, CSS);

function makeButton(disabled) {
    const classes = new Set();
    return {
        dataset: { tgSubmitAction: 'true' },
        tagName: 'BUTTON',
        innerHTML: '<i class="fa-solid fa-flag-checkered"></i> Mark as Complete',
        textContent: 'Mark as Complete',
        disabled,
        classList: { add(...names) { names.forEach((name) => classes.add(name)); }, remove(...names) { names.forEach((name) => classes.delete(name)); } },
        querySelectorAll() { return []; },
        getAttribute() { return null; },
        setAttribute() {},
        removeAttribute() {},
        hasAttribute() { return false; },
        closest() { return null; }
    };
}

function check(condition, message) {
    if (!condition) throw new Error(message);
}

const disabledFutureButton = makeButton(true);
buttons.push(disabledFutureButton);
check(window.TrailGuardActionPending.begin(disabledFutureButton, 'Completing…'), 'A synthetic pending state should begin for restoration coverage.');
pageShowHandlers.forEach((handler) => handler());
check(disabledFutureButton.disabled, 'pageshow restoration must preserve a control that was natively disabled before pending state.');

const eligibleButton = makeButton(false);
buttons.push(eligibleButton);
window.TrailGuardActionPending.begin(eligibleButton, 'Completing…');
window.TrailGuardActionPending.restore(eligibleButton);
check(!eligibleButton.disabled, 'Handled failure restoration must re-enable a control that was eligible before pending state.');

console.log('PASS: action-state disabled restoration behavior.');
