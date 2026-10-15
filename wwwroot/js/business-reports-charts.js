(() => {
    const chartIds = {
        submissions: { canvas: 'submissionTrend', empty: 'submissionTrendEmpty' },
        trails: { canvas: 'trailAccepted', empty: 'trailAcceptedEmpty' },
        statuses: { canvas: 'eventStatuses', empty: 'eventStatusesEmpty' }
    };

    function chartError(message) {
        return new Error(`Business report chart payload is invalid: ${message}`);
    }

    function validLabel(value, seriesName, index) {
        if (typeof value !== 'string' || value.trim().length === 0)
            throw chartError(`${seriesName}[${index}] has no readable label.`);
        return value;
    }

    function validCount(value, seriesName, index) {
        if (typeof value !== 'number' || !Number.isFinite(value))
            throw chartError(`${seriesName}[${index}] has no finite numeric count.`);
        return value;
    }

    function readSeries(entries, seriesName, labelProperty) {
        if (!Array.isArray(entries)) throw chartError(`${seriesName} must be an array.`);
        const labels = entries.map((entry, index) => {
            if (entry === null || typeof entry !== 'object') throw chartError(`${seriesName}[${index}] must be an object.`);
            return validLabel(entry[labelProperty], seriesName, index);
        });
        const values = entries.map((entry, index) => validCount(entry.count, seriesName, index));
        if (labels.length !== values.length) throw chartError(`${seriesName} labels and values have different lengths.`);
        return { labels, values, isEmpty: values.length === 0 || values.every(value => value === 0) };
    }

    function validatePayload(payload) {
        if (payload === null || typeof payload !== 'object') throw chartError('root must be an object.');
        const submissions = readSeries(payload.submissions, 'submissions', 'label');
        const trails = readSeries(payload.trails, 'trails', 'label');
        const statuses = readSeries(payload.statuses, 'statuses', 'status');
        if (typeof payload.trailChartTotal !== 'number' || !Number.isFinite(payload.trailChartTotal))
            throw chartError('trailChartTotal must be a finite number.');
        if (typeof payload.trailChartIsAggregated !== 'boolean')
            throw chartError('trailChartIsAggregated must be a boolean.');
        if (payload.trails.some((entry, index) => typeof entry.isOther !== 'boolean'))
            throw chartError(`trails[${payload.trails.findIndex(entry => typeof entry.isOther !== 'boolean')}] must declare isOther as a boolean.`);
        return { submissions, trails, statuses, trailChartTotal: payload.trailChartTotal };
    }

    function createConfigurations(payload, reducedMotion) {
        const data = validatePayload(payload);
        const base = { responsive: true, maintainAspectRatio: false, animation: reducedMotion ? false : undefined, plugins: { legend: { labels: { color: '#d1d5db' } } } };
        return {
            submissions: {
                isEmpty: data.submissions.isEmpty,
                config: { type: 'line', data: { labels: data.submissions.labels, datasets: [{ label: 'Registration submissions', data: data.submissions.values, borderColor: '#8b5cf6', backgroundColor: 'rgba(139,92,246,.15)', fill: true, tension: .3 }] }, options: { ...base, scales: { x: { ticks: { color: '#9ca3af' }, grid: { display: false } }, y: { beginAtZero: true, ticks: { color: '#9ca3af', precision: 0 }, grid: { color: 'rgba(255,255,255,.08)' } } } } }
            },
            trails: {
                isEmpty: data.trails.isEmpty,
                config: { type: 'bar', data: { labels: data.trails.labels, datasets: [{ label: `Accepted registrations (full scope: ${data.trailChartTotal})`, data: data.trails.values, backgroundColor: payload.trails.map(entry => entry.isOther ? '#6b7280' : '#8b5cf6'), borderRadius: 4 }] }, options: { ...base, indexAxis: 'y', plugins: { legend: { display: true }, tooltip: { callbacks: { label: context => `${context.dataset.label}: ${context.parsed.x}` } } }, scales: { x: { beginAtZero: true, ticks: { color: '#9ca3af', precision: 0 }, grid: { color: 'rgba(255,255,255,.08)' } }, y: { ticks: { color: '#d1d5db', autoSkip: false }, grid: { display: false } } } } }
            },
            statuses: {
                isEmpty: data.statuses.isEmpty,
                config: { type: 'doughnut', data: { labels: data.statuses.labels, datasets: [{ data: data.statuses.values, backgroundColor: ['#8b5cf6', '#34d399', '#f59e0b', '#94a3b8', '#f87171'], borderWidth: 0 }] }, options: base }
            }
        };
    }

    function setEmptyState(canvas, empty, isEmpty, message) {
        if (!canvas || !empty) return;
        canvas.hidden = isEmpty;
        empty.classList.toggle('hidden', !isEmpty);
        empty.classList.toggle('flex', isEmpty);
        if (message) empty.textContent = message;
    }

    function destroyExisting(canvas) {
        const existing = window.Chart?.getChart?.(canvas);
        if (existing) existing.destroy();
    }

    function initialize(dataElement) {
        if (!dataElement || !window.Chart) return;
        let configurations;
        try {
            configurations = createConfigurations(JSON.parse(dataElement.textContent));
        } catch (error) {
            console.error(error);
            Object.values(chartIds).forEach(ids => {
                const canvas = document.getElementById(ids.canvas);
                destroyExisting(canvas);
                setEmptyState(canvas, document.getElementById(ids.empty), true, 'Chart data is unavailable.');
            });
            return;
        }

        Object.entries(configurations).forEach(([name, definition]) => {
            const ids = chartIds[name];
            const canvas = document.getElementById(ids.canvas);
            const empty = document.getElementById(ids.empty);
            destroyExisting(canvas);
            setEmptyState(canvas, empty, definition.isEmpty);
            if (canvas && !definition.isEmpty) new window.Chart(canvas, definition.config);
        });
    }

    window.TrailGuardBusinessReportCharts = { createConfigurations, initialize, validatePayload };
})();
