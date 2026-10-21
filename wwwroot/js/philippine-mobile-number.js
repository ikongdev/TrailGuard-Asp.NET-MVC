(function (window) {
    'use strict';

    function formatLocalDigits(digits) {
        if (digits.length > 6) return digits.slice(0, 3) + ' ' + digits.slice(3, 6) + ' ' + digits.slice(6);
        if (digits.length > 3) return digits.slice(0, 3) + ' ' + digits.slice(3);
        return digits;
    }

    function normalize(raw) {
        var value = raw.trim();
        if (!value) return null;

        var hasPlusPrefix = value.startsWith('+');
        var numberPart = hasPlusPrefix ? value.slice(1) : value;
        if (!numberPart || /[^0-9 -]/.test(numberPart)) return null;

        var digits = numberPart.replace(/[ -]/g, '');
        var localDigits = null;
        if (hasPlusPrefix) {
            if (digits.length === 12 && digits.startsWith('63')) localDigits = digits.slice(2);
        } else if (digits.length === 10) {
            localDigits = digits;
        } else if (digits.length === 11 && digits.startsWith('0')) {
            localDigits = digits.slice(1);
        } else if (digits.length === 12 && digits.startsWith('63')) {
            localDigits = digits.slice(2);
        }

        return localDigits && localDigits.startsWith('9') ? localDigits : null;
    }

    function updateValidity(input, error, formatWhenComplete, requiredMessage) {
        var localDigits = normalize(input.value);
        var trimmedValue = input.value.trim();
        var message = !trimmedValue
            ? (requiredMessage || '')
            : (!localDigits ? 'Enter a complete contact number: 912 345 6789.' : '');
        input.setCustomValidity(message);
        if (error) {
            error.textContent = message;
            error.classList.toggle('hidden', !message);
        }

        if (formatWhenComplete && localDigits) {
            input.value = formatLocalDigits(localDigits);
        }
    }

    function countAsciiDigits(value) {
        return (value.match(/[0-9]/g) || []).length;
    }

    function attachLocalDigitLimit(input, error, requiredMessage) {
        var previousValue = input.value;

        function refreshValidity() {
            updateValidity(input, error, true, requiredMessage);
        }

        function rejectOverlongInput() {
            var message = 'Phone Number cannot exceed 10 local digits.';
            input.setCustomValidity(message);
            if (error) {
                error.textContent = message;
                error.classList.remove('hidden');
            }
        }

        function selectionCandidate(text) {
            var start = typeof input.selectionStart === 'number' ? input.selectionStart : input.value.length;
            var end = typeof input.selectionEnd === 'number' ? input.selectionEnd : start;
            return input.value.slice(0, start) + text + input.value.slice(end);
        }

        function wouldAddPastLimit(text) {
            var start = typeof input.selectionStart === 'number' ? input.selectionStart : input.value.length;
            var end = typeof input.selectionEnd === 'number' ? input.selectionEnd : start;
            var currentDigits = countAsciiDigits(input.value);
            var replacedDigits = countAsciiDigits(input.value.slice(start, end));
            var insertedDigits = countAsciiDigits(text);
            return currentDigits - replacedDigits + insertedDigits > 10 && insertedDigits > replacedDigits;
        }

        function applyCompleteCandidate(candidate) {
            var localDigits = normalize(candidate);
            if (!localDigits) return false;
            input.value = formatLocalDigits(localDigits);
            previousValue = input.value;
            refreshValidity();
            return true;
        }

        input.addEventListener('beforeinput', function (event) {
            if (!event.inputType || !event.inputType.startsWith('insert') || typeof event.data !== 'string') return;

            var candidate = selectionCandidate(event.data);
            if (event.inputType === 'insertFromPaste' && applyCompleteCandidate(candidate)) {
                event.preventDefault();
                return;
            }

            if (wouldAddPastLimit(event.data)) {
                event.preventDefault();
                rejectOverlongInput();
            }
        });

        input.addEventListener('paste', function (event) {
            var clipboard = event.clipboardData;
            if (!clipboard) return;

            var pastedText = clipboard.getData('text');
            var candidate = selectionCandidate(pastedText);
            if (applyCompleteCandidate(candidate)) {
                event.preventDefault();
                return;
            }

            if (countAsciiDigits(candidate) > 10) {
                event.preventDefault();
                rejectOverlongInput();
            }
        });

        input.addEventListener('input', function () {
            if (applyCompleteCandidate(input.value)) return;

            if (countAsciiDigits(input.value) > 10 && countAsciiDigits(input.value) > countAsciiDigits(previousValue)) {
                input.value = previousValue;
                rejectOverlongInput();
                return;
            }

            previousValue = input.value;
        });
    }

    window.TrailGuardPhilippineMobileNumber = {
        formatLocalDigits: formatLocalDigits,
        normalize: normalize,
        updateValidity: updateValidity,
        attachLocalDigitLimit: attachLocalDigitLimit
    };
})(window);
