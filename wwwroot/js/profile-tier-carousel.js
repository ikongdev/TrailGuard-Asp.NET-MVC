(function () {
    var carousel = document.querySelector('[data-tier-carousel]');
    if (!carousel) return; // Organizer/Admin visitor view has no carousel markup at all.

    // Idempotent: a second inclusion/execution of this script (or a second call to
    // this IIFE for any reason) must never bind a second set of listeners on the
    // same buttons.
    if (carousel.dataset.tierCarouselInitialized === 'true') return;
    carousel.dataset.tierCarouselInitialized = 'true';

    var slides = Array.from(carousel.querySelectorAll('[data-tier-slide]'));
    var prevButton = carousel.querySelector('[data-tier-prev]');
    var nextButton = carousel.querySelector('[data-tier-next]');
    if (!slides.length || !prevButton || !nextButton) return;

    var count = slides.length;

    // The server already rendered the correct initial slide/disabled state (the
    // participant's actual current tier, at rest, unanimated) directly in the
    // markup, so a no-JS visit is already a meaningful, correct presentation. This
    // index is only where browsing starts from - it is never itself animated to.
    var parsedInitialIndex = parseInt(carousel.dataset.initialIndex, 10);
    var index = (!isNaN(parsedInitialIndex) && parsedInitialIndex >= 0 && parsedInitialIndex < count)
        ? parsedInitialIndex
        : 0;

    var prefersReducedMotion = !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);

    // Deterministic transition lock - the single source of truth for "an animation
    // is in flight." Every navigation attempt while this is true is dropped
    // outright (not queued), which is what guarantees rapid clicks/keys can never
    // overlap two animations, leave two slides visible at once, or skip past a
    // valid boundary.
    var isAnimating = false;
    var TRANSITION_MS = 100; // exit and enter phases: ~100ms + ~100ms = ~200ms total.

    // Explicit, literal Tailwind utility class names only (all of these are
    // scanned from this file - see wwwroot/css/input.css's `@source` list - so
    // none of this is ever built from string fragments at runtime).
    var CLASS_HIDDEN = 'hidden';
    var CLASS_OPACITY_VISIBLE = 'opacity-100';
    var CLASS_OPACITY_HIDDEN = 'opacity-0';
    var CLASS_X_REST = 'translate-x-0';
    var CLASS_X_RIGHT = 'translate-x-3';
    var CLASS_X_LEFT = '-translate-x-3';

    function settle(slide) {
        slide.classList.remove(CLASS_OPACITY_HIDDEN, CLASS_X_RIGHT, CLASS_X_LEFT);
        slide.classList.add(CLASS_OPACITY_VISIBLE, CLASS_X_REST);
    }

    function updateButtons() {
        prevButton.disabled = index <= 0;
        nextButton.disabled = index >= count - 1;
    }

    // Renders the given index immediately, with no transition - used for
    // prefers-reduced-motion. The live region and button states still update
    // exactly once, same as the animated path.
    function showImmediately(newIndex) {
        for (var i = 0; i < count; i++) {
            var isActive = i === newIndex;
            slides[i].classList.toggle(CLASS_HIDDEN, !isActive);
            if (isActive) settle(slides[i]);
        }
        index = newIndex;
        updateButtons();
    }

    // Sequential exit-then-enter: the old slide fades/slides out while still the
    // only visible slide, is then hidden in the same synchronous step the new
    // slide is revealed (so the accessible tree changes exactly once, and at no
    // point are two slides simultaneously un-hidden), and the new slide fades/
    // slides in from the opposite edge to its settled state. Never touches the
    // progress block below, which reads only from server-rendered actual
    // Trail Points data regardless of which slide is being previewed.
    function animateTo(newIndex) {
        var forward = newIndex > index;
        var oldSlide = slides[index];
        var newSlide = slides[newIndex];
        isAnimating = true;

        // Exit: current slide fades out while sliding toward the direction of
        // travel (next -> exits left, previous -> exits right).
        oldSlide.classList.remove(CLASS_OPACITY_VISIBLE, CLASS_X_REST);
        oldSlide.classList.add(CLASS_OPACITY_HIDDEN, forward ? CLASS_X_LEFT : CLASS_X_RIGHT);

        setTimeout(function () {
            // Swap, in one synchronous step: hide the old slide and reset it to a
            // settled state ready for its next appearance; reveal the new slide at
            // its own "enter from" offset (still invisible at this exact instant).
            oldSlide.classList.add(CLASS_HIDDEN);
            settle(oldSlide);

            newSlide.classList.remove(CLASS_HIDDEN);
            newSlide.classList.remove(CLASS_OPACITY_VISIBLE, CLASS_X_REST);
            newSlide.classList.add(CLASS_OPACITY_HIDDEN, forward ? CLASS_X_RIGHT : CLASS_X_LEFT);

            index = newIndex;

            // Two nested frames so the browser registers the "enter from" state in
            // its own paint before we transition to rest - applying both states in
            // the same tick would collapse into one style recalc and skip the
            // transition entirely.
            requestAnimationFrame(function () {
                requestAnimationFrame(function () {
                    settle(newSlide);

                    setTimeout(function () {
                        isAnimating = false;
                        updateButtons();
                    }, TRANSITION_MS);
                });
            });
        }, TRANSITION_MS);
    }

    function go(nextIndex) {
        if (isAnimating) return;
        if (nextIndex < 0 || nextIndex >= count || nextIndex === index) return;

        if (prefersReducedMotion) {
            showImmediately(nextIndex);
            return;
        }

        animateTo(nextIndex);
    }

    prevButton.addEventListener('click', function () { go(index - 1); });
    nextButton.addEventListener('click', function () { go(index + 1); });

    // Scoped to the carousel container itself - Left/Right only navigate while
    // focus is inside this control (the buttons are its only focusable
    // descendants), never globally on the page.
    carousel.addEventListener('keydown', function (e) {
        if (e.key === 'ArrowLeft') {
            e.preventDefault();
            go(index - 1);
        } else if (e.key === 'ArrowRight') {
            e.preventDefault();
            go(index + 1);
        }
    });

    // Initial setup only - never animated. The server-rendered markup already
    // matches this index exactly, so this only wires up the correct initial
    // disabled state.
    updateButtons();
})();
