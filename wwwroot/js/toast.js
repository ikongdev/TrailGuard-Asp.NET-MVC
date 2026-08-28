// Canonical global toast system. Single source of truth for showToast(message, type)
// across the whole app - see CLAUDE.md/DESIGN.md notification-centralization task.
//
// Replaces N near-identical page-local showToast() copies (Event/Index.cshtml,
// Trail/Index.cshtml, Registration/MyRegistrations.cshtml, Event/Details.cshtml,
// Organizer/PostEventAssessment.cshtml, Organizer/Registrations.cshtml,
// Organizer/RegistrationDetails.cshtml, Assessment/Form.cshtml) that each
// document.body.appendChild()'d a fixed z-50 div - which rendered behind, and
// inside the inert subtree of, any open Add/Edit Event or Trail modal (those
// modals inert every sibling of their ancestor chain up to <body>, and a toast
// appended straight to <body> is exactly such a sibling).
//
// This module never touches business logic, controllers, or endpoints - it only
// owns presentation of already-produced success/error/warning/info messages.
(function () {
    'use strict';

    var HOST_ID = 'tg-toast-host';
    var AUTO_DISMISS_MS = 3000;
    var EXIT_FALLBACK_MS = 320;
    var ENTRY_FALLBACK_MS = 320;

    var TYPES = ['success', 'error', 'warning', 'info'];

    // Callers that used a two-state (success/error) local showToast, or passed
    // something close-but-not-exact, are normalized here rather than made to
    // update every call site - see "Global API" call-shape requirement.
    var ALIASES = {
        ok: 'success', successful: 'success', done: 'success', good: 'success',
        err: 'error', danger: 'error', failure: 'error', fail: 'error', failed: 'error',
        warn: 'warning', caution: 'warning',
        information: 'info', notice: 'info', note: 'info', default: 'info'
    };

    var CONFIG = {
        success: { icon: 'fa-solid fa-circle-check', role: 'status', live: 'polite', label: 'success' },
        error: { icon: 'fa-solid fa-triangle-exclamation', role: 'alert', live: 'assertive', label: 'error' },
        warning: { icon: 'fa-solid fa-triangle-exclamation', role: 'alert', live: 'assertive', label: 'warning' },
        info: { icon: 'fa-solid fa-circle-info', role: 'status', live: 'polite', label: 'info' }
    };

    function normalizeType(type) {
        if (typeof type !== 'string') return 'info';
        var t = type.toLowerCase().trim();
        if (TYPES.indexOf(t) !== -1) return t;
        if (Object.prototype.hasOwnProperty.call(ALIASES, t)) return ALIASES[t];
        return 'info';
    }

    function getHost() {
        return document.getElementById(HOST_ID);
    }

    // Tracks the last element that had focus outside the toast host, so a
    // toast dismissed via keyboard can hand focus back somewhere sensible
    // instead of dropping it to <body>. One listener for the whole page -
    // registered once, at module load, not per toast.
    var lastNonToastFocus = null;
    document.addEventListener('focusin', function (event) {
        var host = getHost();
        if (host && host.contains(event.target)) return;
        lastNonToastFocus = event.target;
    }, true);

    function isFocusable(el) {
        return !!(el && document.contains(el) && typeof el.focus === 'function' &&
            !el.disabled && el.getAttribute('aria-hidden') !== 'true' && el.offsetParent !== null);
    }

    function restoreFocusAwayFrom(toast) {
        if (!toast.contains(document.activeElement)) return;

        if (isFocusable(lastNonToastFocus)) {
            lastNonToastFocus.focus();
            return;
        }

        // Fall back to whichever modal is currently open, if any - every modal
        // in this app uses an id ending in "Modal" and is shown via .flex.
        var openModal = document.querySelector('[id$="Modal"].flex, [id$="Modal"].modal-visible');
        if (openModal) {
            var firstFocusable = openModal.querySelector(
                'a[href], button:not([disabled]), textarea:not([disabled]), ' +
                'input:not([disabled]):not([type="hidden"]), select:not([disabled]), ' +
                '[tabindex]:not([tabindex="-1"])'
            );
            if (isFocusable(firstFocusable)) {
                firstFocusable.focus();
                return;
            }
            if (typeof openModal.focus === 'function') {
                if (!openModal.hasAttribute('tabindex')) openModal.setAttribute('tabindex', '-1');
                openModal.focus();
                return;
            }
        }

        if (document.body) {
            if (!document.body.hasAttribute('tabindex')) document.body.setAttribute('tabindex', '-1');
            document.body.focus();
        }
    }

    function createToast(message, type) {
        var host = getHost();
        if (!host) return null;

        var cfg = CONFIG[type];

        var toast = document.createElement('div');
        toast.className = 'tg-toast tg-toast-' + type;
        toast.setAttribute('role', cfg.role);
        toast.setAttribute('aria-live', cfg.live);
        toast.setAttribute('aria-atomic', 'true');

        var content = document.createElement('div');
        content.className = 'tg-toast-content';

        var icon = document.createElement('i');
        icon.className = cfg.icon + ' tg-toast-icon';
        icon.setAttribute('aria-hidden', 'true');

        var text = document.createElement('p');
        text.className = 'tg-toast-message';
        text.textContent = message;

        var closeBtn = document.createElement('button');
        closeBtn.type = 'button';
        closeBtn.className = 'tg-toast-close';
        closeBtn.setAttribute('aria-label', 'Dismiss ' + cfg.label + ' notification');

        var closeIcon = document.createElement('i');
        closeIcon.className = 'fa-solid fa-xmark';
        closeIcon.setAttribute('aria-hidden', 'true');
        closeBtn.appendChild(closeIcon);

        content.appendChild(icon);
        content.appendChild(text);
        content.appendChild(closeBtn);

        var progress = document.createElement('div');
        progress.className = 'tg-toast-progress';
        var progressBar = document.createElement('div');
        progressBar.className = 'tg-toast-progress-bar';
        progressBar.style.animationDuration = AUTO_DISMISS_MS + 'ms';
        progress.appendChild(progressBar);

        toast.appendChild(content);
        toast.appendChild(progress);
        host.appendChild(toast);

        var remaining = AUTO_DISMISS_MS;
        var startedAt = null;
        var timerId = null;
        var pauseReasons = {};
        var pauseCount = 0;
        var exiting = false;
        var removed = false;

        function pauseTimer() {
            if (timerId === null) return;
            clearTimeout(timerId);
            timerId = null;
            remaining -= (Date.now() - startedAt);
            if (remaining < 0) remaining = 0;
            progressBar.style.animationPlayState = 'paused';
        }

        function resumeTimer() {
            if (exiting || removed || timerId !== null || pauseCount > 0) return;
            if (remaining <= 0) {
                beginExit();
                return;
            }
            startedAt = Date.now();
            timerId = setTimeout(beginExit, remaining);
            progressBar.style.animationPlayState = 'running';
        }

        function addPause(reason) {
            if (pauseReasons[reason]) return;
            pauseReasons[reason] = true;
            pauseCount++;
            pauseTimer();
        }

        function removePauseReason(reason) {
            if (!pauseReasons[reason]) return;
            delete pauseReasons[reason];
            pauseCount--;
            if (pauseCount <= 0) {
                pauseCount = 0;
                resumeTimer();
            }
        }

        function onMouseEnter() { addPause('hover'); }
        function onMouseLeave() { removePauseReason('hover'); }
        function onFocusIn() { addPause('focus'); }
        function onFocusOut(event) {
            if (!toast.contains(event.relatedTarget)) removePauseReason('focus');
        }
        function onVisibilityChange() {
            if (document.hidden) addPause('hidden');
            else removePauseReason('hidden');
        }
        function onCloseClick() { beginExit(); }

        function detachListeners() {
            toast.removeEventListener('mouseenter', onMouseEnter);
            toast.removeEventListener('mouseleave', onMouseLeave);
            toast.removeEventListener('focusin', onFocusIn);
            toast.removeEventListener('focusout', onFocusOut);
            document.removeEventListener('visibilitychange', onVisibilityChange);
            closeBtn.removeEventListener('click', onCloseClick);
        }

        toast.addEventListener('mouseenter', onMouseEnter);
        toast.addEventListener('mouseleave', onMouseLeave);
        toast.addEventListener('focusin', onFocusIn);
        toast.addEventListener('focusout', onFocusOut);
        document.addEventListener('visibilitychange', onVisibilityChange);
        closeBtn.addEventListener('click', onCloseClick);

        function beginExit() {
            if (exiting || removed) return;
            exiting = true;
            if (timerId !== null) { clearTimeout(timerId); timerId = null; }
            restoreFocusAwayFrom(toast);
            detachListeners();

            var prefersReducedMotion = window.matchMedia &&
                window.matchMedia('(prefers-reduced-motion: reduce)').matches;

            toast.classList.remove('tg-toast-visible');
            toast.classList.add('tg-toast-exit');

            var finished = false;
            function finalize() {
                if (finished) return;
                finished = true;
                removed = true;
                if (toast.parentNode) toast.parentNode.removeChild(toast);
            }

            toast.addEventListener('transitionend', function onEnd(event) {
                if (event.target !== toast) return;
                toast.removeEventListener('transitionend', onEnd);
                finalize();
            });

            // Deterministic fallback: reduced-motion CSS can drop the
            // transition entirely, which would otherwise never fire
            // transitionend and strand the toast in the DOM.
            setTimeout(finalize, prefersReducedMotion ? 60 : EXIT_FALLBACK_MS);
        }

        // Entry: wait one frame so the initial (pre-enter) state actually
        // paints before switching to the visible state, otherwise the browser
        // can coalesce both states into a single frame and skip the transition.
        requestAnimationFrame(function () {
            requestAnimationFrame(function () {
                toast.classList.add('tg-toast-visible');

                var started = false;
                function startTimer() {
                    if (started) return;
                    started = true;
                    resumeTimer();
                }

                toast.addEventListener('transitionend', function onEntryEnd(event) {
                    if (event.target !== toast) return;
                    toast.removeEventListener('transitionend', onEntryEnd);
                    startTimer();
                });
                setTimeout(startTimer, ENTRY_FALLBACK_MS);
            });
        });

        return { element: toast, dismiss: beginExit };
    }

    function showToast(message, type) {
        if (message === null || typeof message === 'undefined' || message === '') return null;
        return createToast(String(message), normalizeType(type));
    }

    window.showToast = showToast;

    // Shared exemption used by every modal's background-inert sweep (see
    // makeBackgroundInert in Event/Index.cshtml and Trail/Index.cshtml) so a
    // toast fired while a modal is open never gets swept up as an inert
    // "background sibling" of the modal.
    window.tgToastHostExempt = function (el) {
        return !!(el && el.id === HOST_ID);
    };

    // Shared helper letting a modal's own Tab focus-trap treat any currently
    // visible toast dismiss button as a permitted extra stop, without opening
    // the trap to anything else in the background.
    window.tgGetToastCloseButtons = function () {
        var host = getHost();
        if (!host) return [];
        return Array.prototype.slice.call(host.querySelectorAll('.tg-toast-close'));
    };

    // Bridges server-rendered TempData["Success"/"Error"/"Warning"/"Info"]
    // (read once, centrally, in _Layout.cshtml and passed through safely
    // encoded data-attributes - never interpolated into script text) into the
    // same toast pipeline used by every client-side call site.
    document.addEventListener('DOMContentLoaded', function () {
        var bridge = document.getElementById('tg-toast-tempdata');
        if (!bridge) return;

        var pairs = [
            ['success', bridge.getAttribute('data-success')],
            ['error', bridge.getAttribute('data-error')],
            ['warning', bridge.getAttribute('data-warning')],
            ['info', bridge.getAttribute('data-info')]
        ];

        pairs.forEach(function (pair) {
            if (pair[1]) showToast(pair[1], pair[0]);
        });
    });
})();
