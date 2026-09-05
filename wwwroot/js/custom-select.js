/*
 * Reusable animated custom-select. Opt in by adding data-custom-select to a
 * native <select>. Reuses the navbar profile dropdown's visual/motion
 * language (dark surface, border, shadow, rounded panel, fade/slide open,
 * chevron rotation - see Views/Shared/Components/_NavbarOrganizer.cshtml)
 * but is otherwise unrelated to it: this module does not touch the navbar,
 * and the navbar's own script is untouched by this file.
 *
 * The native <select> stays the canonical, submitted form control - this
 * only builds an accessible, animated proxy in front of it (WAI-ARIA
 * "select-only combobox": focus stays on the trigger button, the active
 * option is tracked with aria-activedescendant rather than moving DOM focus
 * into the listbox). See CLAUDE.md/AGENTS.md for why the native element must
 * remain the source of truth for validation and submission.
 */
(function () {
    'use strict';

    var OPEN_CLASSES = ['opacity-100', 'visible', 'pointer-events-auto', 'translate-y-0', 'scale-100'];
    var CLOSED_CLASSES = ['opacity-0', 'invisible', 'pointer-events-none', 'translate-y-1', 'scale-[.98]'];
    var VIEWPORT_MARGIN = 8;
    var MENU_GAP = 6;
    var TYPEAHEAD_RESET_MS = 600;

    // enhanced native <select> -> { select, wrapper, trigger, label, menu, options[] }
    var registry = new WeakMap();
    var openInstance = null;
    var typeaheadBuffer = '';
    var typeaheadTimer = null;

    function optionLabel(optionEl) {
        return (optionEl.textContent || '').trim();
    }

    var SVG_NS = 'http://www.w3.org/2000/svg';

    // Component-owned chevron, built with createElementNS (never innerHTML).
    // Replaces the previous fa-solid fa-chevron-down <i> - combining that Font
    // Awesome glyph with the rotate-180 CSS transform used to open/close it
    // rendered as a crossed x/bowtie in some environments instead of a clean
    // flip. A plain stroked SVG path has no such font-glyph/transform conflict.
    // Points down at rest; openMenu/closeMenu rotate it via
    // .custom-select-chevron-open (input.css) rather than Tailwind's
    // rotate-180 utility, which was not producing a visible rotation on this
    // SVG - origin-center and the explicit transform-box:fill-box in that
    // class guarantee the pivot point regardless. The transition itself still
    // comes from this element's own Tailwind classes below.
    function buildChevron() {
        var svg = document.createElementNS(SVG_NS, 'svg');
        svg.setAttribute('viewBox', '0 0 24 24');
        svg.setAttribute('fill', 'none');
        svg.setAttribute('stroke', 'currentColor');
        svg.setAttribute('stroke-width', '2');
        svg.setAttribute('stroke-linecap', 'round');
        svg.setAttribute('stroke-linejoin', 'round');
        svg.setAttribute('aria-hidden', 'true');
        svg.setAttribute('focusable', 'false');
        svg.classList.add('w-4', 'h-4', 'text-gray-400', 'shrink-0', 'origin-center', '[transform-box:fill-box]', 'transition-transform', 'duration-200', 'motion-reduce:transition-none');

        var path = document.createElementNS(SVG_NS, 'path');
        path.setAttribute('d', 'M6 9l6 6 6-6');
        svg.appendChild(path);

        return svg;
    }

    // ---- building --------------------------------------------------------

    // Enhancement is transactional: nothing observable outside the detached
    // wrapper changes (the native select isn't hidden, no label is
    // retargeted, nothing is registered) until every step below - including
    // getting a valid accessible name - has actually succeeded. Any failure
    // rolls the select back to exactly the state captureOriginalState saw,
    // so one broken select can't leave the user without a usable control
    // and can't stop the rest of the page's selects from initializing.
    function enhance(select) {
        // registry.has() is the normal in-memory guard within one evaluation of
        // this script. data-cs-enhanced is a second, DOM-level guard: if this
        // script were ever accidentally included twice, each evaluation gets
        // its own fresh, empty `registry` WeakMap (module state doesn't survive
        // a re-run), so the in-memory guard alone wouldn't stop a second script
        // instance from re-enhancing an already-enhanced select - a DOM
        // attribute persists regardless of which script instance checks it.
        if (registry.has(select) || select.multiple || select.hasAttribute('data-cs-enhanced')) {
            // True multi-selects aren't this component's listbox pattern -
            // leave them as native controls untouched (see task audit).
            return;
        }

        var original = captureOriginalState(select);
        var controller = (typeof AbortController !== 'undefined') ? new AbortController() : null;
        var wrapper = null;

        try {
            wrapper = document.createElement('div');
            wrapper.className = 'relative';
            select.parentNode.insertBefore(wrapper, select);
            wrapper.appendChild(select);

            var triggerId = select.id ? select.id + '-trigger' : '';
            var menuId = (select.id || 'cs') + '-listbox';

            var trigger = document.createElement('button');
            trigger.type = 'button';
            if (triggerId) trigger.id = triggerId;
            trigger.setAttribute('role', 'combobox');
            trigger.setAttribute('aria-haspopup', 'listbox');
            trigger.setAttribute('aria-expanded', 'false');
            trigger.setAttribute('aria-controls', menuId);
            // Every enhanced select gets this same solid dark trigger surface by
            // default. A select that needs to sit on a lighter/translucent field
            // (currently only Event Management's filter bar, which must match its
            // own Search input) opts in via data-cs-trigger-class rather than this
            // becoming a second, divergent trigger style baked into the default -
            // Add/Edit Trail and Add/Edit Event modal selects are untouched by this.
            var triggerClassOverride = select.getAttribute('data-cs-trigger-class');
            trigger.className = triggerClassOverride ||
                'flex w-full items-center justify-between gap-2 px-4 py-3 rounded-xl bg-surface-card border border-gray-700 text-white text-sm text-left focus:outline-none focus:border-accent focus:ring-1 focus:ring-accent transition-colors cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed';

            var labelSpan = document.createElement('span');
            labelSpan.className = 'truncate';
            trigger.appendChild(labelSpan);

            var chevron = buildChevron();
            trigger.appendChild(chevron);

            // Opt-in portal mode: a select whose ancestors include a
            // backdrop-filter/transform/perspective/will-change element (Event
            // Management's filter bar uses backdrop-blur-xl for its glass panel)
            // would otherwise have its position:fixed menu positioned relative to
            // that ancestor instead of the viewport, per the CSS containing-block
            // spec - the same getBoundingClientRect() coordinates in positionMenu()
            // then land in the wrong coordinate space, displacing the menu and
            // trapping it inside that ancestor's stacking context (so it can paint
            // behind later, unrelated page content). Appending straight to
            // document.body - the same top-level target this app's toasts already
            // use - sidesteps both problems without changing positionMenu() at all.
            // ARIA relationships (aria-controls/aria-activedescendant) and outside-
            // click detection are id/reference-based, not DOM-proximity-based, so
            // nothing else needs to change to support this.
            var usePortal = select.hasAttribute('data-custom-select-portal');

            // A non-portalled menu stays trapped inside its ancestor's own local
            // stacking context regardless of its z-index value, so z-70 there only
            // ever competes with siblings inside that same trapped context (e.g. a
            // modal's own content) - safe as the established default. A portalled
            // menu escapes to the root stacking context, where z-70 would wrongly
            // outrank the navbar (z-50) and modal overlays (z-60); z-40 keeps it
            // above normal page content while staying below both.
            var menuZClass = usePortal ? 'z-40' : 'z-70';

            var menu = document.createElement('div');
            menu.id = menuId;
            menu.setAttribute('role', 'listbox');
            // No width utility here by default - positionMenu() always sets
            // menu.style.width itself (see the menuWidthOverride branch
            // there), so a class-based width would only ever be dead CSS,
            // immediately overwritten by that inline style on every open.
            //
            // tg-custom-select-scrollbar (input.css), not the generic, ad hoc
            // "custom-scrollbar" name several pages redefine locally (with
            // several different colors/widths) in their own <style> blocks -
            // a menu appended under document.body in portal mode sits outside
            // every one of those page-scoped blocks' actual intent but would
            // still match them by accident (or match nothing at all on a page
            // that defines no local rule, falling back to the native OS
            // scrollbar). This dedicated class is the single canonical
            // definition for every generated listbox, portalled or not.
            menu.className = 'fixed ' + menuZClass + ' max-h-80 overflow-y-auto tg-custom-select-scrollbar bg-surface-card border border-gray-800 rounded-2xl shadow-2xl p-1.5 origin-top ' +
                // Only the open/close visual properties are transitionable -
                // transition-all also animated the top/left/width/max-height
                // positionMenu() sets on first open, producing a spurious
                // slide-in-from-the-side on the very first dropdown open.
                'transition-[opacity,transform,visibility] duration-200 ease-out motion-reduce:transition-none';
            CLOSED_CLASSES.forEach(function (c) { menu.classList.add(c); });

            // data-cs-menu-class: additive presentation classes only
            // (overflow-x-hidden, etc.) - never affects width/sizing.
            var menuClassExtra = select.getAttribute('data-cs-menu-class');
            if (menuClassExtra) {
                menuClassExtra.split(/\s+/).filter(Boolean).forEach(function (c) { menu.classList.add(c); });
            }

            // data-cs-menu-width-class: the only opt-out of exact
            // trigger-width matching. Appends the given width utility and
            // switches positionMenu() to floor at the trigger's width via
            // min-width instead of forcing width === trigger width. Absent,
            // behavior is pixel-identical to before for every select.
            var menuWidthClass = select.getAttribute('data-cs-menu-width-class');
            var menuWidthOverride = !!menuWidthClass;
            if (menuWidthClass) {
                menuWidthClass.split(/\s+/).filter(Boolean).forEach(function (c) { menu.classList.add(c); });
            }

            wrapper.insertBefore(trigger, select.nextSibling);

            if (usePortal) {
                document.body.appendChild(menu);
            } else {
                wrapper.insertBefore(menu, trigger.nextSibling);
            }

            var describedBy = select.getAttribute('data-cs-describedby');
            if (describedBy) {
                trigger.setAttribute('aria-describedby', describedBy);
            }

            var instance = {
                select: select,
                wrapper: wrapper,
                trigger: trigger,
                labelSpan: labelSpan,
                chevron: chevron,
                menu: menu,
                menuWidthOverride: menuWidthOverride,
                activeIndex: -1,
                signal: controller ? controller.signal : undefined
            };

            buildOptions(instance);
            syncFromSelect(instance);
            wireEvents(instance);

            // Deterministic accessible naming, in priority order: the
            // field's own visible <label> (name + current value, so the
            // trigger announces "Technical Trail Class, 3 - Scrambling,
            // combobox" instead of just the value); then an aria-label the
            // select already carried; then an aria-labelledby it already
            // carried. A combobox with no valid name is worse than no
            // enhancement at all, so this throws rather than building one.
            var label = original.label;
            if (label) {
                if (!label.id) label.id = select.id + '-label';
                if (!labelSpan.id) labelSpan.id = select.id + '-value';
                trigger.setAttribute('aria-labelledby', label.id + ' ' + labelSpan.id);
            } else if (select.hasAttribute('aria-label') && select.getAttribute('aria-label').trim()) {
                trigger.setAttribute('aria-label', select.getAttribute('aria-label'));
            } else if (select.hasAttribute('aria-labelledby') && select.getAttribute('aria-labelledby').trim()) {
                trigger.setAttribute('aria-labelledby', select.getAttribute('aria-labelledby'));
            } else {
                throw new Error('custom-select: no associated <label>, aria-label, or aria-labelledby for select' +
                    (select.id ? ' #' + select.id : ' (no id)') + ' - refusing to build an unlabeled combobox.');
            }

            // Point of no return: the trigger/listbox are fully built, wired,
            // synced, and confirmed to have a valid accessible name. Only now
            // do we touch anything outside the wrapper.
            if (label && triggerId) {
                // Clicking the field's <label> should land on the visible
                // trigger, not the now-hidden native select (native label-for
                // focuses whatever id it names, and the select's id must stay
                // exactly as the model binding requires - so the label is
                // retargeted instead).
                label.setAttribute('for', triggerId);
            }

            // Native select stays "rendered" (not display:none) so it remains
            // focusable and participates in constraint validation - only visually
            // clipped via the project's sr-only pattern, applied here (not baked
            // into the server-rendered markup) so a page with JS disabled still
            // shows and uses the real select.
            select.classList.add('sr-only');
            select.setAttribute('tabindex', '-1');
            select.setAttribute('aria-hidden', 'true');
            select.setAttribute('data-cs-enhanced', 'true');

            // A select styled with appearance-none for its pre-JS/no-JS native
            // appearance may render its own static decorative chevron icon next
            // to it (see data-cs-decorative-chevron below) - once the trigger
            // above has fully taken over that same visual role, that icon is
            // redundant and, left in place, visually doubles the chevron. It's
            // removed only now, at the same point the select itself is hidden,
            // and restored by rollback if enhancement had failed instead.
            if (original.decorativeChevron) {
                original.decorativeChevron.remove();
            }

            registry.set(select, instance);
        } catch (err) {
            rollbackEnhancement(select, original, wrapper, menu, controller);
            if (window.console && console.warn) {
                console.warn('custom-select: enhancement failed for select' +
                    (select.id ? ' #' + select.id : ' (no id)') + ' - left as a native control.',
                    err && err.message ? err.message : err);
            }
        }
    }

    function cssEscape(value) {
        if (window.CSS && window.CSS.escape) return window.CSS.escape(value);
        return String(value).replace(/[^a-zA-Z0-9_-]/g, '\\$&');
    }

    // Snapshots everything enhance() might change, before it changes
    // anything, so a failed enhancement can be undone exactly.
    function captureOriginalState(select) {
        var label = select.id ? document.querySelector('label[for="' + cssEscape(select.id) + '"]') : null;

        // Opt-in: a select can name the id of its own static decorative
        // chevron icon (the markup a native appearance-none select needs
        // before/without JS) via data-cs-decorative-chevron, so enhance() can
        // remove that now-redundant icon once its own trigger chevron takes
        // over, and rollback can put it back exactly where it was.
        var decorativeChevronId = select.getAttribute('data-cs-decorative-chevron');
        var decorativeChevron = decorativeChevronId ? document.getElementById(decorativeChevronId) : null;

        return {
            parent: select.parentNode,
            nextSibling: select.nextSibling,
            className: select.className,
            hadTabIndex: select.hasAttribute('tabindex'),
            tabIndexValue: select.getAttribute('tabindex'),
            hadAriaHidden: select.hasAttribute('aria-hidden'),
            ariaHiddenValue: select.getAttribute('aria-hidden'),
            label: label,
            labelHadId: label ? label.hasAttribute('id') : false,
            labelId: label ? label.getAttribute('id') : null,
            labelFor: label ? label.getAttribute('for') : null,
            decorativeChevron: decorativeChevron,
            decorativeChevronParent: decorativeChevron ? decorativeChevron.parentNode : null,
            decorativeChevronNextSibling: decorativeChevron ? decorativeChevron.nextSibling : null
        };
    }

    // Undoes enhance() back to captureOriginalState's snapshot: removes the
    // generated wrapper/trigger/listbox (plus a portalled menu, which lives
    // under document.body rather than inside the wrapper - see the portal note
    // in enhance()), restores the select's class/tabindex/aria-hidden and the
    // label's for/id, and aborts every listener wireEvents attached (including
    // the ones on select/document/form, which outlive the wrapper and would
    // otherwise leak pointing at removed DOM).
    function rollbackEnhancement(select, original, wrapper, menu, controller) {
        if (controller) controller.abort();

        if (menu && menu.parentNode === document.body) {
            menu.remove();
        }

        if (wrapper && wrapper.parentNode) {
            if (original.parent) {
                original.parent.insertBefore(select, original.nextSibling);
            }
            wrapper.remove();
        }

        select.className = original.className;

        if (original.hadTabIndex) {
            select.setAttribute('tabindex', original.tabIndexValue);
        } else {
            select.removeAttribute('tabindex');
        }

        if (original.hadAriaHidden) {
            select.setAttribute('aria-hidden', original.ariaHiddenValue);
        } else {
            select.removeAttribute('aria-hidden');
        }

        if (original.label) {
            if (original.labelFor === null) {
                original.label.removeAttribute('for');
            } else {
                original.label.setAttribute('for', original.labelFor);
            }
            if (original.labelHadId) {
                original.label.setAttribute('id', original.labelId);
            } else {
                original.label.removeAttribute('id');
            }
        }

        select.removeAttribute('data-cs-enhanced');

        // Only re-insert if enhance() actually removed it (a throw before that
        // point never touched it, so it never left original.decorativeChevronParent).
        if (original.decorativeChevron && !original.decorativeChevron.parentNode && original.decorativeChevronParent) {
            original.decorativeChevronParent.insertBefore(original.decorativeChevron, original.decorativeChevronNextSibling);
        }
    }

    function buildOptions(instance) {
        instance.menu.innerHTML = '';
        instance.options = [];

        Array.from(instance.select.options).forEach(function (optionEl, index) {
            var row = document.createElement('div');
            row.id = instance.menu.id + '-opt-' + index;
            row.setAttribute('role', 'option');
            row.dataset.value = optionEl.value;
            row.dataset.index = String(index);

            var isPlaceholder = optionEl.value === '' && optionEl.disabled;
            if (isPlaceholder) {
                // A disabled placeholder ("-- Select --") can't become the
                // submitted value for a required field - it isn't offered as
                // a selectable row at all, matching how a native required
                // select already refuses to let a user land back on it.
                return;
            }

            row.className = 'flex items-center justify-between gap-2 px-3 py-2.5 rounded-xl text-sm cursor-pointer whitespace-normal wrap-break-word transition-colors';
            row.setAttribute('aria-selected', 'false');
            if (optionEl.disabled) {
                row.setAttribute('aria-disabled', 'true');
                row.classList.add('opacity-40', 'cursor-not-allowed');
            } else {
                row.classList.add('text-gray-300', 'hover:bg-gray-800/60', 'hover:text-white');
            }

            var text = document.createElement('span');
            text.textContent = optionLabel(optionEl); // textContent only - never innerHTML
            row.appendChild(text);

            if (!optionEl.disabled) {
                row.addEventListener('click', function () {
                    selectByIndex(instance, index, true);
                });
            }

            instance.menu.appendChild(row);
            instance.options.push(row);
        });
    }

    // ---- state sync --------------------------------------------------------

    // Re-reads the native select's current value/options/disabled state and
    // updates the trigger label and listbox to match - safe to call any
    // number of times, never dispatches change itself (see refresh() below).
    function syncFromSelect(instance) {
        var select = instance.select;

        if (instance.options.length !== countRealOptions(select)) {
            buildOptions(instance);
        }

        var selectedOption = select.options[select.selectedIndex] || null;
        var hasRealSelection = !!selectedOption && !(selectedOption.value === '' && selectedOption.disabled);
        instance.labelSpan.textContent = hasRealSelection ? optionLabel(selectedOption) : (placeholderText(select));
        instance.labelSpan.classList.toggle('text-gray-500', !hasRealSelection);
        instance.labelSpan.classList.toggle('text-white', hasRealSelection);

        instance.options.forEach(function (row) {
            var isSelected = hasRealSelection && row.dataset.value === selectedOption.value;
            row.setAttribute('aria-selected', isSelected ? 'true' : 'false');
            row.classList.toggle('bg-accent/10', isSelected);
        });

        instance.trigger.disabled = select.disabled;
        instance.trigger.classList.toggle('border-red-500', select.matches(':invalid') && select.dataset.csShowInvalid === 'true');

        var activeRow = hasRealSelection
            ? instance.options.filter(function (r) { return r.dataset.value === selectedOption.value; })[0]
            : instance.options[0];
        instance.activeIndex = activeRow ? instance.options.indexOf(activeRow) : -1;
    }

    function countRealOptions(select) {
        return select.options.length;
    }

    function placeholderText(select) {
        var first = select.options[0];
        if (first && first.value === '' ) return optionLabel(first) || 'Select an option';
        return 'Select an option';
    }

    function selectByIndex(instance, optionIndex, userInitiated) {
        var optionEl = instance.select.options[optionIndex];
        if (!optionEl || optionEl.disabled) return;

        var changed = instance.select.value !== optionEl.value;
        instance.select.value = optionEl.value;
        instance.select.dataset.csShowInvalid = '';

        if (userInitiated && changed) {
            // Dispatch exactly once, only on an actual value change made
            // through the custom UI - existing change listeners (trail
            // preview lookups, weather refetch, etc.) run exactly as they
            // would for a native selection, no more and no less.
            instance.select.dispatchEvent(new Event('change', { bubbles: true }));
        } else {
            // Value assignment alone (or a no-op re-selection) never fires a
            // native change event, so the trigger/listbox must be resynced
            // explicitly here.
            syncFromSelect(instance);
        }

        if (userInitiated) {
            closeMenu(instance);
            instance.trigger.focus();
        }
    }

    // ---- open/close --------------------------------------------------------

    function positionMenu(instance) {
        var rect = instance.trigger.getBoundingClientRect();
        var menu = instance.menu;

        menu.style.left = '0px';
        menu.style.top = '0px';

        if (instance.menuWidthOverride) {
            // data-cs-menu-width-class supplied its own width utility (e.g.
            // w-48) - only floor it at the trigger's own width so the menu
            // can never render narrower than the control that opens it.
            menu.style.width = '';
            menu.style.minWidth = rect.width + 'px';
        } else {
            // Default, unchanged behavior for every other enhanced select:
            // the menu is always exactly as wide as its trigger.
            menu.style.width = rect.width + 'px';
            menu.style.minWidth = '';
        }

        // Read once, after width/min-width are applied, so every
        // width-dependent calculation below uses the menu's real rendered
        // width - identical to rect.width in the default case, but correct
        // for an overridden (wider) menu too.
        var menuWidth = menu.offsetWidth;
        var menuHeight = menu.offsetHeight;
        var spaceBelow = window.innerHeight - rect.bottom - MENU_GAP;
        var spaceAbove = rect.top - MENU_GAP;
        var openUpward = spaceBelow < menuHeight && spaceAbove > spaceBelow;

        var top = openUpward ? Math.max(VIEWPORT_MARGIN, rect.top - menuHeight - MENU_GAP) : rect.bottom + MENU_GAP;
        var maxHeight = openUpward ? Math.min(320, spaceAbove) : Math.min(320, window.innerHeight - rect.bottom - MENU_GAP - VIEWPORT_MARGIN);

        var left = rect.left;
        var maxLeft = window.innerWidth - menuWidth - VIEWPORT_MARGIN;
        if (left > maxLeft) left = Math.max(VIEWPORT_MARGIN, maxLeft);

        menu.style.top = Math.round(top) + 'px';
        menu.style.left = Math.round(left) + 'px';
        menu.style.maxHeight = Math.max(120, Math.round(maxHeight)) + 'px';
        menu.classList.toggle('origin-bottom', openUpward);
        menu.classList.toggle('origin-top', !openUpward);
    }

    function openMenu(instance) {
        if (instance.trigger.disabled) return;
        if (openInstance && openInstance !== instance) closeMenu(openInstance);

        positionMenu(instance);
        CLOSED_CLASSES.forEach(function (c) { instance.menu.classList.remove(c); });
        OPEN_CLASSES.forEach(function (c) { instance.menu.classList.add(c); });
        instance.trigger.setAttribute('aria-expanded', 'true');
        instance.chevron.classList.add('custom-select-chevron-open');
        setActiveDescendant(instance, instance.activeIndex >= 0 ? instance.activeIndex : 0);
        openInstance = instance;

        window.addEventListener('scroll', handleDismissScroll, true);
        window.addEventListener('resize', handleDismissResize);
    }

    function closeMenu(instance) {
        if (!instance) return;
        OPEN_CLASSES.forEach(function (c) { instance.menu.classList.remove(c); });
        CLOSED_CLASSES.forEach(function (c) { instance.menu.classList.add(c); });
        instance.trigger.setAttribute('aria-expanded', 'false');
        instance.trigger.removeAttribute('aria-activedescendant');
        instance.chevron.classList.remove('custom-select-chevron-open');
        if (openInstance === instance) {
            openInstance = null;
            window.removeEventListener('scroll', handleDismissScroll, true);
            window.removeEventListener('resize', handleDismissResize);
        }
    }

    function handleDismissScroll(event) {
        if (!openInstance) return;
        // Scrolling the menu itself (its own internal list) shouldn't close
        // it - only scrolling the modal body or the page behind it should.
        if (openInstance.menu.contains(event.target)) return;
        closeMenu(openInstance);
    }

    function handleDismissResize() {
        if (openInstance) closeMenu(openInstance);
    }

    function setActiveDescendant(instance, index) {
        if (index < 0 || index >= instance.options.length) return;
        instance.activeIndex = index;
        var row = instance.options[index];
        instance.trigger.setAttribute('aria-activedescendant', row.id);
        instance.options.forEach(function (r) { r.classList.remove('bg-gray-800/60'); });
        row.classList.add('bg-gray-800/60');
        row.scrollIntoView({ block: 'nearest' });
    }

    function moveActive(instance, delta) {
        var count = instance.options.length;
        if (count === 0) return;
        var next = instance.activeIndex;
        for (var i = 0; i < count; i++) {
            next = (next + delta + count) % count;
            if (instance.options[next].getAttribute('aria-disabled') !== 'true') {
                setActiveDescendant(instance, next);
                return;
            }
        }
    }

    function firstEnabledIndex(instance) {
        for (var i = 0; i < instance.options.length; i++) {
            if (instance.options[i].getAttribute('aria-disabled') !== 'true') return i;
        }
        return -1;
    }

    function lastEnabledIndex(instance) {
        for (var i = instance.options.length - 1; i >= 0; i--) {
            if (instance.options[i].getAttribute('aria-disabled') !== 'true') return i;
        }
        return -1;
    }

    // ---- typeahead ----------------------------------------------------------

    function handleTypeahead(instance, key) {
        clearTimeout(typeaheadTimer);
        typeaheadBuffer += key.toLowerCase();
        typeaheadTimer = setTimeout(function () { typeaheadBuffer = ''; }, TYPEAHEAD_RESET_MS);

        var count = instance.options.length;
        for (var offset = 1; offset <= count; offset++) {
            var idx = (instance.activeIndex + offset) % count;
            var row = instance.options[idx];
            if (row.getAttribute('aria-disabled') === 'true') continue;
            if (row.textContent.trim().toLowerCase().indexOf(typeaheadBuffer) === 0) {
                if (instance.menu.classList.contains('pointer-events-none')) {
                    selectByIndex(instance, Number(row.dataset.index), true);
                } else {
                    setActiveDescendant(instance, idx);
                }
                return;
            }
        }
    }

    // ---- events ---------------------------------------------------------

    function wireEvents(instance) {
        var select = instance.select;
        var trigger = instance.trigger;
        // Passed to every addEventListener call below so a rolled-back
        // enhancement can remove all of them in one call (controller.abort()
        // in rollbackEnhancement) - including the ones on select/document/
        // form, which aren't removed just by discarding the wrapper since
        // those target elements survive rollback.
        var opts = instance.signal ? { signal: instance.signal } : undefined;

        trigger.addEventListener('click', function () {
            var isOpen = openInstance === instance;
            if (isOpen) {
                closeMenu(instance);
            } else {
                openMenu(instance);
            }
        }, opts);

        trigger.addEventListener('keydown', function (event) {
            var isOpen = openInstance === instance;

            switch (event.key) {
                case 'ArrowDown':
                    event.preventDefault();
                    if (!isOpen) { openMenu(instance); }
                    else { moveActive(instance, 1); }
                    break;
                case 'ArrowUp':
                    event.preventDefault();
                    if (!isOpen) { openMenu(instance); }
                    else { moveActive(instance, -1); }
                    break;
                case 'Home':
                    if (isOpen) { event.preventDefault(); setActiveDescendant(instance, firstEnabledIndex(instance)); }
                    break;
                case 'End':
                    if (isOpen) { event.preventDefault(); setActiveDescendant(instance, lastEnabledIndex(instance)); }
                    break;
                case 'Enter':
                case ' ':
                    event.preventDefault();
                    if (isOpen) {
                        selectByIndex(instance, Number(instance.options[instance.activeIndex]?.dataset.index), true);
                    } else {
                        openMenu(instance);
                    }
                    break;
                case 'Escape':
                    if (isOpen) {
                        // Close only the dropdown - stop the key from also
                        // reaching a parent modal's own Escape handler
                        // (TrailModal.handleKeydown or similar).
                        event.preventDefault();
                        event.stopPropagation();
                        closeMenu(instance);
                        trigger.focus();
                    }
                    break;
                case 'Tab':
                    if (isOpen) closeMenu(instance);
                    break;
                default:
                    if (event.key.length === 1 && !event.ctrlKey && !event.metaKey && !event.altKey) {
                        handleTypeahead(instance, event.key);
                    }
                    break;
            }
        }, opts);

        document.addEventListener('click', function (event) {
            if (openInstance !== instance) return;
            if (trigger.contains(event.target) || instance.menu.contains(event.target)) return;
            closeMenu(instance);
        }, opts);

        select.addEventListener('change', function () {
            syncFromSelect(instance);
        }, opts);

        // Native constraint validation still runs (the select is still
        // "rendered", just visually clipped) - intercept its bubble so focus
        // and the visible error state land on the trigger the user can
        // actually see, not the sr-only native control.
        select.addEventListener('invalid', function (event) {
            event.preventDefault();
            select.dataset.csShowInvalid = 'true';
            trigger.classList.add('border-red-500');
            trigger.focus();
        }, opts);

        var form = select.form;
        if (form) {
            form.addEventListener('reset', function () {
                // The browser has already reset select.value by the time this
                // fires - just resync the visible trigger/listbox to match.
                setTimeout(function () { syncFromSelect(instance); }, 0);
            }, opts);
        }
    }

    // ---- public API -------------------------------------------------------

    function init(root) {
        var scope = root || document;
        var selects = scope.querySelectorAll ? scope.querySelectorAll('select[data-custom-select]') : [];
        Array.prototype.forEach.call(selects, enhance);
    }

    function refresh(select) {
        var instance = registry.get(select);
        if (instance) syncFromSelect(instance);
    }

    function closeAll() {
        if (openInstance) closeMenu(openInstance);
    }

    document.addEventListener('DOMContentLoaded', function () {
        init(document);
    });

    window.CustomSelect = { init: init, refresh: refresh, closeAll: closeAll };
})();
