(function () {
    var grid = document.querySelector('[data-achievements-grid]');
    if (!grid) return;



    if (grid.dataset.achievementsInitialized === 'true') return;
    grid.dataset.achievementsInitialized = 'true';

    var cards = Array.from(grid.querySelectorAll('[data-achievement-card]'));
    if (!cards.length) return;

    function triggerFor(card) {
        return card.querySelector('[data-achievement-trigger]');
    }























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







    function closeAllPinned(exceptCard) {
        cards.forEach(function (card) {
            if (card === exceptCard) return;
            if (card.dataset.expanded === 'true' || card.dataset.focusVisible === 'true') {
                setCardState(card, { expanded: false, focusVisible: false });
            }
        });
    }





    grid.addEventListener('click', function (e) {
        var trigger = e.target.closest('[data-achievement-trigger]');
        if (!trigger) return;

        var card = trigger.closest('[data-achievement-card]');
        if (!card || cards.indexOf(card) === -1) return;

        var wasOpen = card.dataset.expanded === 'true';
        closeAllPinned(card);

        if (wasOpen) {


            setCardState(card, { expanded: false, focusVisible: false, focusSuppressed: true });
        } else {
            setCardState(card, { expanded: true, focusSuppressed: false });
        }
    });



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




        setCardState(card, { focusVisible: false, focusSuppressed: false });
    });

    document.addEventListener('keydown', function (e) {
        if (e.key !== 'Escape') return;

        var active = document.activeElement;
        var focusedTrigger = active && active.closest ? active.closest('[data-achievement-trigger]') : null;
        var focusedCard = focusedTrigger ? focusedTrigger.closest('[data-achievement-card]') : null;


        closeAllPinned(null);




        if (focusedCard && cards.indexOf(focusedCard) !== -1) {
            setCardState(focusedCard, { expanded: false, focusVisible: false, focusSuppressed: true });
        }
    });




    document.addEventListener('click', function (e) {
        if (grid.contains(e.target)) return;
        closeAllPinned(null);
    });
})();
