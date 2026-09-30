import { readFileSync } from 'node:fs';
import vm from 'node:vm';

let assertions = 0;
function check(condition, message) {
    if (!condition) throw new Error(message);
    assertions++;
}

class ClassList {
    constructor(owner) { this.owner = owner; this.values = new Set(); }
    add(...values) { values.forEach(value => this.values.add(value)); }
    remove(...values) { values.forEach(value => this.values.delete(value)); }
    contains(value) { return this.values.has(value); }
    toggle(value, force) {
        const enabled = force === undefined ? !this.values.has(value) : !!force;
        if (enabled) this.values.add(value); else this.values.delete(value);
        return enabled;
    }
    setFromText(value) { this.values = new Set(String(value || '').split(/\s+/).filter(Boolean)); }
    toString() { return Array.from(this.values).join(' '); }
}

class Element {
    constructor(tagName, document) {
        this.tagName = tagName.toLowerCase();
        this.document = document;
        this.children = [];
        this.parentNode = null;
        this.parentElement = null;
        this.attributes = new Map();
        this.dataset = {};
        this.listeners = new Map();
        this.classList = new ClassList(this);
        this.style = {};
        this.hidden = false;
        this.disabled = false;
        this._text = '';
        this._value = '';
        this.offsetWidth = 240;
        this.offsetHeight = 100;
    }
    get className() { return this.classList.toString(); }
    set className(value) { this.classList.setFromText(value); }
    get id() { return this.getAttribute('id') || ''; }
    set id(value) { this.setAttribute('id', value); }
    get nextSibling() {
        if (!this.parentNode) return null;
        const index = this.parentNode.children.indexOf(this);
        return this.parentNode.children[index + 1] || null;
    }
    get textContent() { return this._text + this.children.map(child => child.textContent).join(''); }
    set textContent(value) { this._text = String(value); this.children = []; }
    get options() { return this.tagName === 'select' ? this.children.filter(child => child.tagName === 'option') : undefined; }
    get selectedIndex() {
        if (this.tagName !== 'select') return -1;
        const selected = this.options.findIndex(option => option.selected);
        return selected >= 0 ? selected : this.options.findIndex(option => option.value === this._value);
    }
    get value() { return this._value; }
    set value(value) {
        this._value = String(value);
        if (this.tagName === 'select') {
            this.options.forEach(option => { option.selected = option.value === this._value; });
        }
    }
    appendChild(child) {
        if (child.parentNode) child.parentNode.removeChild(child);
        this.children.push(child);
        child.parentNode = this;
        child.parentElement = this;
        return child;
    }
    insertBefore(child, reference) {
        if (child.parentNode) child.parentNode.removeChild(child);
        const index = reference ? this.children.indexOf(reference) : -1;
        if (index < 0) this.children.push(child); else this.children.splice(index, 0, child);
        child.parentNode = this;
        child.parentElement = this;
        return child;
    }
    removeChild(child) {
        const index = this.children.indexOf(child);
        if (index >= 0) this.children.splice(index, 1);
        child.parentNode = null;
        child.parentElement = null;
    }
    remove() { if (this.parentNode) this.parentNode.removeChild(this); }
    replaceChildren(...children) { this.children.slice().forEach(child => this.removeChild(child)); children.forEach(child => this.appendChild(child)); }
    contains(other) { return other === this || this.children.some(child => child.contains(other)); }
    setAttribute(name, value) { this.attributes.set(name, String(value)); if (name.startsWith('data-')) this.dataset[name.slice(5).replace(/-([a-z])/g, (_, c) => c.toUpperCase())] = String(value); }
    getAttribute(name) { return this.attributes.has(name) ? this.attributes.get(name) : null; }
    hasAttribute(name) { return this.attributes.has(name); }
    removeAttribute(name) { this.attributes.delete(name); }
    addEventListener(type, handler) { const handlers = this.listeners.get(type) || []; handlers.push(handler); this.listeners.set(type, handlers); }
    dispatch(event) { (this.listeners.get(event.type) || []).forEach(handler => handler.call(this, event)); }
    focus() { this.document.activeElement = this; }
    scrollIntoView() { }
    getBoundingClientRect() { return { left: 0, top: 0, bottom: 30, width: 240 }; }
    matches(selector) { return matchesSelector(this, selector); }
    closest(selector) { for (let current = this; current; current = current.parentElement) if (matchesSelector(current, selector)) return current; return null; }
    querySelectorAll(selector) { return descendants(this).filter(element => selector.split(',').some(part => matchesSelector(element, part.trim()))); }
    querySelector(selector) { return this.querySelectorAll(selector)[0] || null; }
}

function descendants(root) { return root.children.flatMap(child => [child, ...descendants(child)]); }
function matchesSelector(element, selector) {
    const tag = selector.match(/^[a-z]+/i)?.[0]?.toLowerCase();
    if (tag && element.tagName !== tag) return false;
    if (selector.includes('[tabindex]') && !element.hasAttribute('tabindex')) return false;
    if (selector.includes(':not([disabled])') && element.disabled) return false;
    if (selector.includes(':not([type="hidden"])') && element.getAttribute('type') === 'hidden') return false;
    if (selector.includes(':not([tabindex="-1"])') && element.getAttribute('tabindex') === '-1') return false;
    for (const match of selector.matchAll(/\[([^\]=]+)(?:=["']?([^\]"']+)["']?)?\]/g)) {
        const [, name, value] = match;
        if (name === 'disabled' || name === 'type' || name === 'tabindex') continue;
        if (!element.hasAttribute(name)) return false;
        if (value && element.getAttribute(name) !== value) return false;
    }
    return true;
}

class Document extends Element {
    constructor() { super('document', null); this.document = this; this.body = new Element('body', this); this.appendChild(this.body); this.activeElement = this.body; }
    createElement(tagName) { return new Element(tagName, this); }
    createElementNS(_, tagName) { return this.createElement(tagName); }
    getElementById(id) { return descendants(this).find(element => element.id === id) || null; }
}

function event(type, extras = {}) {
    return { type, defaultPrevented: false, preventDefault() { this.defaultPrevented = true; }, stopPropagation() { }, ...extras };
}

const document = new Document();
const window = {
    document,
    console,
    innerHeight: 900,
    innerWidth: 1400,
    CSS: { escape: value => value },
    requestAnimationFrame: callback => callback(),
    addEventListener() { },
    removeEventListener() { },
    getComputedStyle: () => ({ display: 'block', visibility: 'visible' })
};
document.defaultView = window;

function addSearchableSelect(id) {
    const before = document.createElement('button'); before.id = id + '-before'; before.textContent = 'Before';
    const label = document.createElement('label'); label.setAttribute('for', id); label.textContent = id;
    const select = document.createElement('select'); select.id = id; select.setAttribute('data-custom-select', ''); select.setAttribute('data-cs-searchable', '');
    const placeholder = document.createElement('option'); placeholder.value = ''; placeholder.disabled = true; placeholder.textContent = 'Select'; select.appendChild(placeholder);
    for (const name of ['Alpha', 'Beta']) { const option = document.createElement('option'); option.value = name; option.textContent = name; select.appendChild(option); }
    const after = document.createElement('button'); after.id = id + '-after'; after.textContent = 'After';
    document.body.appendChild(before); document.body.appendChild(label); document.body.appendChild(select); document.body.appendChild(after);
    return { before, select };
}

const first = addSearchableSelect('first');
addSearchableSelect('second');
const source = readFileSync(new URL('../wwwroot/js/custom-select.js', import.meta.url), 'utf8');
vm.runInNewContext(source, { window, document, console, WeakMap, AbortController, Array, String, Number, Event, setTimeout, clearTimeout });
window.CustomSelect.init(document);

const trigger = document.getElementById('first-trigger');
trigger.dispatch(event('click'));
const search = descendants(document.body).find(element => element.tagName === 'input' && element.type === 'search');
check(document.activeElement === search, 'Opening a searchable select focuses its search input.');

search.value = 'does-not-exist';
search.dispatch(event('input'));
const enter = event('keydown', { key: 'Enter' });
search.dispatch(enter);
check(first.select.value === '', 'Enter with no visible result does not select a previous hidden option.');
check(enter.defaultPrevented, 'No-match Enter remains contained by the searchable dropdown.');

const shiftTab = event('keydown', { key: 'Tab', shiftKey: true });
search.dispatch(shiftTab);
check(document.activeElement === first.before, 'Shift+Tab skips the enhanced hidden native select and focuses the preceding visible control.');

const optionIds = descendants(document.body).filter(element => element.getAttribute('role') === 'option').map(element => element.id);
check(optionIds.length === new Set(optionIds).size && optionIds.every(Boolean), 'Searchable option IDs are unique and non-empty across instances.');

console.log(`PASS: ${assertions} custom-select behavioral assertions with a controlled DOM mock.`);
