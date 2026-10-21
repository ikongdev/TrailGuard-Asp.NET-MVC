(function (window) {
    'use strict';

    var requiredMessage = 'Email address is required.';
    var invalidMessage = 'Enter a valid email address, for example name@example.com.';
    var maximumLength = 256;

    function validate(raw) {
        var value = (raw || '').trim();
        if (!value) return { valid: false, message: requiredMessage };
        if (value.length > maximumLength || /[\s\u0000-\u001F\u007F]/.test(value)) return { valid: false, message: invalidMessage };
        var at = value.indexOf('@');
        if (at <= 0 || at !== value.lastIndexOf('@') || at === value.length - 1) return { valid: false, message: invalidMessage };
        var domain = value.slice(at + 1);
        if (domain.indexOf('.') <= 0 || domain.endsWith('.') || !/^(?=.{1,253}$)(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)+[A-Za-z]{2,63}$/.test(domain)) {
            return { valid: false, message: invalidMessage };
        }
        if (!/^[^@<>(),;:\\"\[\]\s]+@/.test(value)) return { valid: false, message: invalidMessage };
        return { valid: true, value: value, message: '' };
    }

    function update(input, showError) {
        var result = validate(input.value);
        var error = document.getElementById(input.getAttribute('data-tg-email-error'));
        input.setCustomValidity(result.message);
        input.setAttribute('aria-invalid', result.valid ? 'false' : 'true');
        input.classList.toggle('border-red-400', !result.valid && showError);
        input.classList.toggle('border-gray-700', result.valid || !showError);
        if (error && (showError || result.valid)) {
            error.textContent = result.message;
            error.classList.toggle('hidden', result.valid);
        }
        return result;
    }

    function attach(input) {
        var errorId = input.getAttribute('data-tg-email-error');
        if (errorId) {
            var describedBy = (input.getAttribute('aria-describedby') || '').split(/\s+/).filter(Boolean);
            if (describedBy.indexOf(errorId) < 0) describedBy.push(errorId);
            input.setAttribute('aria-describedby', describedBy.join(' '));
        }
        update(input, false);
        input.addEventListener('input', function () { update(input, false); });
        input.addEventListener('blur', function () { update(input, true); });
        if (input.form && input.form.dataset.tgEmailValidationBound !== 'true') {
            input.form.dataset.tgEmailValidationBound = 'true';
            input.form.addEventListener('submit', function (event) {
                var firstInvalid = null;
                input.form.querySelectorAll('[data-tg-email-address]').forEach(function (field) {
                    if (!update(field, true).valid && !firstInvalid) firstInvalid = field;
                });
                if (firstInvalid) {
                    event.preventDefault();
                    firstInvalid.focus();
                }
            }, true);
        }
    }

    function initialize() {
        document.querySelectorAll('[data-tg-email-address]').forEach(attach);
    }

    window.TrailGuardEmailAddress = { validate: validate, update: update };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', initialize);
    else initialize();
})(window);
