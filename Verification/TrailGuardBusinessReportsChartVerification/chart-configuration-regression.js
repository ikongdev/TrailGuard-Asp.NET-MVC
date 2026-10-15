const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

if (process.argv.length !== 4) throw new Error('Usage: node chart-configuration-regression.js <populated-payload.json> <empty-payload.json>');

const source = fs.readFileSync('wwwroot/js/business-reports-charts.js', 'utf8');
const context = { window: {}, console };
vm.createContext(context);
vm.runInContext(source, context, { filename: 'business-reports-charts.js' });
const chartApi = context.window.TrailGuardBusinessReportCharts;
assert.ok(chartApi, 'The browser chart API must be exposed.');

const populated = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const populatedConfigurations = chartApi.createConfigurations(populated, true);
for (const [name, definition] of Object.entries(populatedConfigurations)) {
    const dataset = definition.config.data.datasets[0].data;
    assert.equal(definition.config.data.labels.length, dataset.length, `${name} labels and data must have matching lengths.`);
    assert.ok(definition.config.data.labels.every(label => typeof label === 'string' && label.trim().length > 0 && label !== 'undefined'), `${name} must not have undefined labels.`);
    assert.ok(dataset.every(Number.isFinite), `${name} must not have invalid numeric values.`);
    assert.equal(definition.isEmpty, false, `${name} populated fixture must not use the empty state.`);
}
assert.equal(populatedConfigurations.submissions.config.data.datasets[0].data.filter(value => value > 0).length > 1, true, 'Submission chart must contain multiple nonzero date buckets.');
assert.equal(populatedConfigurations.trails.config.data.labels.length, 9, 'Trail chart must retain Top 8 plus Other.');
assert.equal(populatedConfigurations.trails.config.data.labels.at(-1), 'Other (2 trails)', 'Trail chart must retain the Other label.');
assert.equal(populatedConfigurations.trails.config.data.datasets[0].data.reduce((total, value) => total + value, 0), populated.trailChartTotal, 'Trail chart must reconcile with the full-scope total.');
assert.deepEqual([...populatedConfigurations.statuses.config.data.labels].sort(), ['Cancelled', 'Completed', 'Upcoming'], 'Doughnut labels must match the operational summary statuses.');
assert.deepEqual([...populatedConfigurations.statuses.config.data.datasets[0].data].sort((left, right) => left - right), [2, 4, 10], 'Doughnut counts must match the operational summary.');

const empty = JSON.parse(fs.readFileSync(process.argv[3], 'utf8'));
const emptyConfigurations = chartApi.createConfigurations(empty, true);
assert.equal(emptyConfigurations.submissions.isEmpty, true, 'All-zero submission buckets must use the empty state.');
assert.equal(emptyConfigurations.trails.isEmpty, true, 'No trail entries must use the empty state.');
assert.equal(emptyConfigurations.statuses.isEmpty, true, 'No status entries must use the empty state.');
assert.throws(() => chartApi.createConfigurations({ ...populated, statuses: [{ status: undefined, count: 2 }] }, true), /invalid/, 'Invalid payloads must fail explicitly instead of being silently replaced.');

console.log('Business report chart configuration regression passed.');
