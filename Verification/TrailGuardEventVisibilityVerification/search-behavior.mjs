import { readFileSync } from 'node:fs';
import vm from 'node:vm';

// Execute the production search initializer with a controlled DOM and history.
// This is a simulation, not a browser or native-form-navigation check.
const view = readFileSync(new URL('../../Views/Event/Index.cshtml', import.meta.url), 'utf8');
const source = view.match(/\(function initEventSearch\(\) \{[\s\S]*?\n    \}\)\(\);/)?.[0];
if (!source) throw new Error('Production Event search initializer was not found.');
let assertions = 0;
function check(condition, message) {
    if (!condition) throw new Error(message);
    assertions++;
}
const card = text => ({ hidden: false, getAttribute: name => name === 'data-search' ? text : null });
const upcomingCards = [card('Owned event Batangas Trail 1'), card('Other event Laguna Trail 2')];
const completedCards = [card('Completed hike Batangas Trail 1')];
function section(cards) {
    const count = { textContent: '' };
    return { hidden: false, count, querySelectorAll: () => cards, querySelector: () => count };
}
const sections = [section(upcomingCards), section(completedCards)];
const listeners = new Map();
const search = { value: '', addEventListener: (name, handler) => listeners.set(name, handler) };
const announcer = { textContent: '' };
const noMatch = { hidden: true };
const nodes = {
    searchInput: search, resultCountAnnouncer: announcer, noSearchResults: noMatch,
    eventsContainer: { querySelectorAll: () => sections }
};
const url = 'http://localhost/Event?visibility=all&trailId=1&difficulty=Minor+Hike&status=Upcoming&sortOrder=date_desc';
const window = { location: { href: url }, addEventListener: (name, handler) => listeners.set(name, handler) };
const history = {
    state: { retained: true }, replaceState(state, title, next) {
        this.state = state;
        window.location.href = new URL(next, window.location.href).href;
    }
};
let timerId = 0;
const timers = new Map();
vm.runInNewContext(source.replace('@totalEventCount', '3'), {
    document: { getElementById: id => nodes[id] }, window, history, URL,
    setTimeout: callback => { timers.set(++timerId, callback); return timerId; },
    clearTimeout: id => timers.delete(id)
});
function input(value) { search.value = value; listeners.get('input')(); }
function flush() { for (const callback of timers.values()) callback(); timers.clear(); }
check(sections[0].count.textContent === 2 && sections[1].count.textContent === 1, 'Initial final-result section counts synchronize.');
input('other');
check(upcomingCards[0].hidden && !upcomingCards[1].hidden && sections[1].hidden, 'Search filters both owned and browse-only cards.');
check(sections[0].count.textContent === 1, 'Visible section counts reflect the search.');
flush();
let current = new URL(window.location.href);
check(current.searchParams.get('visibility') === 'all' && current.searchParams.get('searchString') === 'other', 'Search preserves explicit All Events scope.');
check(['trailId', 'difficulty', 'status', 'sortOrder'].every(name => current.searchParams.get(name) === new URL(url).searchParams.get(name)), 'Search preserves the other selected query filters.');
input('missing');
check(!noMatch.hidden && sections.every(item => item.hidden), 'No matches produces the search empty state.');
input('owned');
input('');
check(timers.size === 1, 'Repeated inputs retain one debounced URL update.');
flush();
current = new URL(window.location.href);
check(current.searchParams.get('visibility') === 'all' && !current.searchParams.has('searchString'), 'Clearing search preserves All Events and removes only the search parameter.');
check(upcomingCards.every(item => !item.hidden) && !sections[1].hidden && noMatch.hidden, 'Clearing search restores cards, sections and counts.');
window.location.href += '&searchString=Completed';
listeners.get('popstate')();
check(search.value === 'Completed' && sections[0].hidden && !completedCards[0].hidden, 'Existing URL-history search synchronization remains functional.');
check(listeners.size === 2 && history.state.retained, 'One input handler and one history handler preserve existing history state.');
console.log(`PASS: ${assertions} production-search assertions in a controlled DOM/history simulation.`);
