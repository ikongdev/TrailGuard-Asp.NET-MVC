(function () {
    'use strict';

    var pendingClassNames = ['pointer-events-none', 'opacity-70'];
    function text(value) {
        return (value || '').replace(/\s+/g, ' ').trim();
    }

    function visibleLabel(element) {
        var original = text(element.textContent);
        if (original) return original;

        if (element.tagName === 'INPUT') {
            var inputValue = text(element.value);
            if (inputValue) return inputValue;
        }

        var accessibleName = text(element.getAttribute('aria-label') || element.getAttribute('title'));
        if (!accessibleName) return '';
        return accessibleName;
    }

    function isNativeFormSubmit(element) {
        return !!element
            && !!element.form
            && ((element.tagName === 'BUTTON' || element.tagName === 'INPUT')
                && element.type === 'submit');
    }

    function isSubmitAction(element) {
        return !!element
            && !element.hasAttribute('data-tg-keep-idle-icon')
            && (element.dataset.tgSubmitAction === 'true' || isNativeFormSubmit(element));
    }

    function removeSubmitIcons(element) {
        if (!isSubmitAction(element)) return;

        var icons = element.querySelectorAll('i[class*="fa-"], svg');
        if (!icons.length) return;

        Array.prototype.forEach.call(icons, function (icon) {
            var wrapper = icon.parentElement;
            icon.remove();
            if (wrapper && wrapper.tagName === 'SPAN' && !text(wrapper.textContent) && !wrapper.children.length) {
                wrapper.remove();
            }
        });

        if (text(element.textContent)) return;

        var label = visibleLabel(element);
        if (!label) return;

        element.textContent = label;
        ['w-6', 'w-8', 'h-6', 'h-8', 'w-10', 'h-10', 'w-11', 'w-12'].forEach(function (className) {
            element.classList.remove(className);
        });
        element.classList.add('px-3', 'py-2', 'text-xs', 'whitespace-nowrap');
    }

    function normalizeActionControls(root) {
        if (!root) return;
        if (root.nodeType === Node.ELEMENT_NODE && root.matches && root.matches('button, input[type="submit"]')) {
            removeSubmitIcons(root);
        }
        if (!root.querySelectorAll) return;
        root.querySelectorAll('button, input[type="submit"]').forEach(removeSubmitIcons);
    }

    function pendingLabel(element) {
        var configured = text(element.getAttribute('data-tg-pending-label'));
        if (configured) return configured;

        var current = visibleLabel(element).toLowerCase();
        if (current === 'sign in') return 'Signing in…';
        if (current === 'sign out' || current === 'log out') return 'Signing out…';
        if (current === 'register now' || current === 'register') return 'Registering…';
        if (current === 'submit registration') return 'Submitting…';
        if (current.indexOf('upload') >= 0 || current.indexOf('replace receipt') >= 0) return 'Uploading…';
        if (current.indexOf('delete') >= 0 || current.indexOf('remove') >= 0) return 'Deleting…';
        if (current.indexOf('cancel') >= 0) return 'Cancelling…';
        if (current.indexOf('create') >= 0) return 'Creating…';
        if (current.indexOf('update') >= 0) return 'Updating…';
        if (current.indexOf('save') >= 0) return 'Saving…';
        if (current.indexOf('submit') >= 0) return 'Submitting…';
        return 'Working…';
    }

    function begin(element, label, form) {
        if (!element || element.dataset.tgPendingActive === 'true') return false;

        element.dataset.tgOriginalHtml = element.innerHTML;
        if (element.tagName === 'INPUT') element.dataset.tgOriginalValue = element.value;
        removeSubmitIcons(element);
        element.dataset.tgPendingActive = 'true';
        element.dataset.tgOriginalLabel = visibleLabel(element);
        if (element.tagName === 'INPUT') element.value = label || pendingLabel(element);
        else element.textContent = label || pendingLabel(element);
        element.setAttribute('aria-busy', 'true');
        element.setAttribute('aria-disabled', 'true');
        pendingClassNames.forEach(function (className) { element.classList.add(className); });
        if ('disabled' in element) element.disabled = true;

        if (form) {
            form.dataset.tgPending = 'true';
            form.setAttribute('aria-busy', 'true');
        }
        return true;
    }

    function restore(element, form) {
        if (!element || element.dataset.tgPendingActive !== 'true') return;

        if (element.tagName === 'INPUT') {
            element.value = element.dataset.tgOriginalValue || element.dataset.tgOriginalLabel || visibleLabel(element);
            delete element.dataset.tgOriginalValue;
        } else {
            element.innerHTML = element.dataset.tgOriginalHtml || element.dataset.tgOriginalLabel || visibleLabel(element);
        }
        delete element.dataset.tgOriginalHtml;
        delete element.dataset.tgOriginalLabel;
        delete element.dataset.tgPendingActive;
        element.removeAttribute('aria-busy');
        element.removeAttribute('aria-disabled');
        pendingClassNames.forEach(function (className) { element.classList.remove(className); });
        if ('disabled' in element) element.disabled = false;

        if (form) {
            delete form.dataset.tgPending;
            form.removeAttribute('aria-busy');
        }
    }

    function findSubmitter(form, event) {
        if (event.submitter) return event.submitter;
        var external = form.id
            ? document.querySelector('[form="' + CSS.escape(form.id) + '"][type="submit"]:not([disabled])')
            : null;
        return external || form.querySelector('[type="submit"]:not([disabled])');
    }

    function bindNormalPostForm(form) {
        if (form.dataset.tgPendingBound === 'true' || form.noValidate) return;
        form.dataset.tgPendingBound = 'true';

        form.addEventListener('submit', function (event) {
            if (form.dataset.tgPending === 'true') {
                event.preventDefault();
                return;
            }
            if (event.defaultPrevented || !form.checkValidity()) return;

            var submitter = findSubmitter(form, event);
            if (submitter) begin(submitter, pendingLabel(submitter), form);
        });
    }

    function restoreAll() {
        document.querySelectorAll('[data-tg-pending-active="true"]').forEach(function (element) {
            restore(element, element.form || element.closest('form'));
        });
    }

    function initialize() {
        normalizeActionControls(document);
        document.querySelectorAll('form').forEach(bindNormalPostForm);

        var observer = new MutationObserver(function (records) {
            records.forEach(function (record) {
                record.addedNodes.forEach(function (node) {
                    if (node.nodeType === Node.ELEMENT_NODE) normalizeActionControls(node);
                });
            });
        });
        observer.observe(document.body, { childList: true, subtree: true });
    }

    window.TrailGuardActionPending = {
        begin: begin,
        restore: restore,
        normalize: normalizeActionControls
    };

    window.addEventListener('pageshow', restoreAll);
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', initialize);
    else initialize();
})();
