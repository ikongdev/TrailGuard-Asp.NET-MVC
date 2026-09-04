(function () {
    var grid = document.querySelector('[data-achievements-grid]');
    if (!grid) return; // No achievement cards on this render (empty state, or a viewer with none).

    // Idempotent: a second inclusion/execution must never bind a second set of
    // listeners - same guard convention as profile-tier-carousel.js.
    if (grid.dataset.achievementsInitialized === 'true') return;
    grid.dataset.achievementsInitialized = 'true';

    var cards = Array.from(grid.querySelectorAll('[data-achievement-card]'));
    if (!cards.length) return;

    function triggerFor(card) {
        return card.querySelector('[data-achievement-trigger]');
    }

    // Single point of truth for a card's state. Every other function in this
    // file goes through here rather than touching data-expanded,
    // aria-expanded, data-focus-visible, or data-focus-suppressed directly,
    // so those four can never drift out of sync with each other - the exact
    // failure mode this file replaces (a plain CSS group-focus-within rule
    // that had no way to learn "the script just closed this").
    //
    // - expanded:         the persistent, tap/click-pinned state. Drives
    //                      aria-expanded and reveals the panel regardless of
    //                      hover/focus.
    // - focusVisible:      a transient keyboard-focus preview (Tab lands on
    //                      the trigger). Reveals the panel but never touches
    //                      aria-expanded - only an explicit activation counts
    //                      as "expanded" for assistive tech.
    // - focusSuppressed:   set only when Escape or a second activation closes
    //                      a card whose trigger still holds DOM focus (no
    //                      blur() is ever called to defeat that). It exists
    //                      so a focusin that fires again before focus has
    //                      actually moved elsewhere - event ordering can vary
    //                      across browsers/input methods - can't immediately
    //                      re-open what was just explicitly closed. Cleared
    //                      the moment focus actually leaves the trigger.
    function setCardState(card, patch) {
        if ('expanded' in patch) {
            card.dataset.expanded = patch.expanded ? 'true' : 'false';
            var trigger = triggerFor(card);
            if (trigger) trigger.setAttribute('aria-expanded', patch.expanded ? 'true' : 'false');
        }
        if ('focusVisible' in patch) {
            card.dataset.focusVisible = patch.focusVisible ? 'true' : 'false';
        }
        if ('focusSuppressed' in patch) {
            card.dataset.focusSuppressed = patch.focusSuppressed ? 'true' : 'false';
        }
    }

    // Closes every persistently-pinned card except the one optionally passed
    // (used when opening a card, so opening one always closes every other).
    // Also clears focusVisible on whatever it closes, since a card being
    // force-closed for a reason unrelated to its own focus state (another
    // card opening, or an outside click) must never keep showing itself
    // through a stale focus preview.
    function closeAllPinned(exceptCard) {
        cards.forEach(function (card) {
            if (card === exceptCard) return;
            if (card.dataset.expanded === 'true' || card.dataset.focusVisible === 'true') {
                setCardState(card, { expanded: false, focusVisible: false });
            }
        });
    }

    // Activation (mouse click, touch tap, or Enter/Space on the focused
    // native button - all surface as a 'click' event) toggles the persistent
    // pin. One delegated listener resolves every activation to exactly one
    // open/close decision, never a per-button handler stack.
    grid.addEventListener('click', function (e) {
        var trigger = e.target.closest('[data-achievement-trigger]');
        if (!trigger) return;

        var card = trigger.closest('[data-achievement-card]');
        if (!card || cards.indexOf(card) === -1) return; // Fail safe: not a recognized card.

        var wasOpen = card.dataset.expanded === 'true';
        closeAllPinned(card);

        if (wasOpen) {
            // Second activation: close, and stay visually closed even though
            // focus remains on this same trigger.
            setCardState(card, { expanded: false, focusVisible: false, focusSuppressed: true });
        } else {
            setCardState(card, { expanded: true, focusSuppressed: false });
        }
    });

    // focusin/focusout bubble (unlike focus/blur), so one delegated pair
    // covers every trigger without a per-button listener.
    grid.addEventListener('focusin', function (e) {
        var trigger = e.target.closest('[data-achievement-trigger]');
        if (!trigger) return;
        var card = trigger.closest('[data-achievement-card]');
        if (!card || cards.indexOf(card) === -1) return;

        if (card.dataset.focusSuppressed !== 'true') {
            setCardState(card, { focusVisible: true });
        }
    });

    grid.addEventListener('focusout', function (e) {
        var trigger = e.target.closest('[data-achievement-trigger]');
        if (!trigger) return;
        var card = trigger.closest('[data-achievement-card]');
        if (!card || cards.indexOf(card) === -1) return;

        // Focus has left this trigger - the temporary preview goes away and
        // any Escape/second-activation suppression resets, so returning
        // focus later reveals the requirement normally again.
        setCardState(card, { focusVisible: false, focusSuppressed: false });
    });

    document.addEventListener('keydown', function (e) {
        if (e.key !== 'Escape') return;

        var active = document.activeElement;
        var focusedTrigger = active && active.closest ? active.closest('[data-achievement-trigger]') : null;
        var focusedCard = focusedTrigger ? focusedTrigger.closest('[data-achievement-card]') : null;

        // Escape always clears every persistent pin...
        closeAllPinned(null);

        // ...and if focus is currently on a trigger, also suppress its
        // preview from reappearing until focus actually moves - without
        // moving focus itself.
        if (focusedCard && cards.indexOf(focusedCard) !== -1) {
            setCardState(focusedCard, { expanded: false, focusVisible: false, focusSuppressed: true });
        }
    });

    // Outside interaction (mouse or touch, both surface as 'click') closes
    // whatever is open. A click inside the grid is handled by the delegated
    // listener above and never reaches here as "outside".
    document.addEventListener('click', function (e) {
        if (grid.contains(e.target)) return;
        closeAllPinned(null);
    });
})();
