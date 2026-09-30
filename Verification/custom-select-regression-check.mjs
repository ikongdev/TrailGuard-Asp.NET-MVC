import { readFileSync } from 'node:fs';

let assertions = 0;
function check(condition, message) {
    if (!condition) throw new Error(message);
    assertions++;
}

const customSelect = readFileSync(new URL('../wwwroot/js/custom-select.js', import.meta.url), 'utf8');
const eventView = readFileSync(new URL('../Views/Event/Index.cshtml', import.meta.url), 'utf8');

check(customSelect.includes("var menuId = instanceKey + '-listbox';"), 'Each custom-select instance must derive a stable listbox ID.');
check(customSelect.includes("row.id = instance.listboxId + '-opt-' + index;"), 'Option IDs must derive from the listbox ID, not the menu wrapper.');
check(customSelect.includes("searchInput.setAttribute('aria-controls', menuId);"), 'The focused search combobox must reference its listbox.');
check(customSelect.includes("if (!isEnabledVisibleIndex(instance, index))"), 'Invalid active options must clear active-descendant state.');
check(customSelect.includes("if (isEnabledVisibleIndex(instance, instance.activeIndex))"), 'Enter must select only an enabled visible option.');
check(customSelect.includes("focusAdjacentToTrigger(instance, event.shiftKey);"), 'Search Tab navigation must leave hidden dropdown content.');
check(eventView.includes('matchingCatalogPoint.name !== selected'), 'Case-only catalog changes must retain the event row’s original text.');
check(eventView.includes("focusAddEventField('pickupScheduleLocationInput');"), 'Add Event validation must focus the visible custom-select trigger.');
check(eventView.includes("focusAddEventField('editPickupScheduleLocationInput');"), 'Edit Event validation must focus the visible custom-select trigger.');

console.log(`PASS: ${assertions} custom-select and Pickup Points source regression assertions.`);
