import fs from 'node:fs';
import vm from 'node:vm';

const source = fs.readFileSync('wwwroot/js/email-address.js', 'utf8');
const listeners = new Map();
const context = {
    window: {},
    document: {
        readyState: 'loading',
        addEventListener: (name, handler) => listeners.set(name, handler),
        querySelectorAll: () => []
    }
};
context.window = context;
vm.runInNewContext(source, context, { filename: 'email-address.js' });

let assertions = 0;
function check(condition, message) {
    if (!condition) throw new Error(message);
    assertions++;
}

const email = context.TrailGuardEmailAddress;
for (const value of ['participant@example.com', 'participant.name+demo@example.com', 'participant@sub.example.com']) {
    check(email.validate(value).valid, `client accepts ${value}`);
}
for (const value of ['', '   ', 'participant', '@example.com', 'participant@gmail', 'a@b@c.example', 'participant @example.com',
    'participant@example .com', 'participant@example.com\nother@example.com', 'Person <participant@example.com>',
    'one@example.com, two@example.com', `${'a'.repeat(245)}@example.com`]) {
    check(!email.validate(value).valid, `client rejects ${JSON.stringify(value)}`);
}
const trimmed = email.validate('  participant@example.com  ');
check(trimmed.valid && trimmed.value === 'participant@example.com', 'client returns the same trimmed valid value used by the server');
check(email.validate('participant@gmail').message.includes('name@example.com'), 'client provides the inline format guidance');

console.log(`Email-address client behavioral verification passed (${assertions} assertions).`);
