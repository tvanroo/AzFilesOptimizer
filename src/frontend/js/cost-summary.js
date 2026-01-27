let summaryJobId = null;
let summaryCosts = [];
let summaryForecasts = [];

function getJobId() {
    const params = new URLSearchParams(window.location.search);
    return params.get('jobId');
}

function navigateTo(target) {
    const base = target === 'analysis' ? 'cost-analysis.html' : 'cost-forecast.html';
    const url = `${base}?jobId=${encodeURIComponent(summaryJobId)}`;
    window.location.href = url;
}

function normalizeCost(raw) {
    if (!raw || typeof raw !== 'object') return {};
    return {
        volumeName: raw.VolumeName || '',
        resourceType: raw.ResourceType || '',
        totalCostForPeriod: typeof raw.TotalCostForPeriod === 'number' ? raw.TotalCostForPeriod : 0,
        costBreakdown: raw.CostBreakdown || {},
    };
}

function normalizeForecast(raw) {
    if (!raw || typeof raw !== 'object') return {};
    return {
        volumeName: raw.VolumeName || '',
        forecastedCostFor30Days: typeof raw.ForecastedCostFor30Days === 'number' ? raw.ForecastedCostFor30Days : 0,
    };
}

async function loadSummaryData() {
    try {
        const [costResp, forecastResp] = await Promise.all([
            fetch(`${API_BASE_URL}/discovery/${summaryJobId}/costs`),
            fetch(`${API_BASE_URL}/discovery/${summaryJobId}/cost-forecast`)
        ]);

        if (!costResp.ok) throw new Error(`Failed to load costs: ${costResp.statusText}`);
        if (!forecastResp.ok) throw new Error(`Failed to load forecasts: ${forecastResp.statusText}`);

        const costData = await costResp.json();
        const forecastData = await forecastResp.json();

        const rawCosts = costData.costs || [];
        const rawForecasts = forecastData.forecasts || [];

        summaryCosts = rawCosts.map(normalizeCost);
        summaryForecasts = rawForecasts.map(normalizeForecast);

        renderSummary(costData.summary, forecastData.summary);
    } catch (error) {
        const err = document.getElementById('summary-error');
        err.style.display = 'block';
        err.innerHTML = `<div style="padding: 1rem; background: #ffebee; border-left: 3px solid #f44336; border-radius: 4px; color: #c62828;">${escapeHtml(error.message)}</div>`;
        console.error('Error loading summary data:', error);
    }
}

function renderSummary(costSummary, forecastSummary) {
    const totalEl = document.getElementById('summary-total-cost');
    const avgEl = document.getElementById('summary-avg-daily-cost');
    const countsEl = document.getElementById('summary-volume-counts');
    const maxForecastEl = document.getElementById('summary-max-forecast');

    let total = 0;
    let avgDaily = 0;
    if (costSummary) {
        if (typeof costSummary.totalCost === 'number') total = costSummary.totalCost;
        if (typeof costSummary.averageDailyCost === 'number') avgDaily = costSummary.averageDailyCost;
    }
    if (!total && summaryCosts.length > 0) {
        total = summaryCosts.reduce((s, c) => s + (c.totalCostForPeriod || 0), 0);
    }
    if (!avgDaily && total) avgDaily = total / 30;

    totalEl.textContent = `$${(total || 0).toFixed(2)}`;
    avgEl.textContent = `$${(avgDaily || 0).toFixed(2)}`;

    // Volume counts by type
    let files = 0, anf = 0, disks = 0;
    summaryCosts.forEach(c => {
        if (c.resourceType === 'AzureFile') files++;
        else if (c.resourceType === 'ANF') anf++;
        else if (c.resourceType === 'ManagedDisk') disks++;
    });
    countsEl.textContent = `${files} / ${anf} / ${disks}`;

    // Max forecast
    let maxForecast = 0;
    if (forecastSummary && typeof forecastSummary.totalForecastedCost === 'number') {
        // Not quite "max", but we only have total and avg; derive from list when possible
        maxForecast = summaryForecasts.reduce((m, f) => Math.max(m, f.forecastedCostFor30Days || 0), 0);
    } else if (summaryForecasts.length > 0) {
        maxForecast = summaryForecasts.reduce((m, f) => Math.max(m, f.forecastedCostFor30Days || 0), 0);
    }
    maxForecastEl.textContent = `$${(maxForecast || 0).toFixed(2)}`;

    renderTopVolumes(total);
    renderComponentBreakdown(total);
    renderTypeBreakdown();
}

function renderTopVolumes(totalCost) {
    const tbody = document.querySelector('#top-volumes-table tbody');
    if (!tbody) return;

    if (!summaryCosts.length) {
        tbody.innerHTML = '<tr><td colspan="4" style="padding: 1rem; text-align: center; color: var(--text-secondary);">No cost data available.</td></tr>';
        return;
    }

    const sorted = [...summaryCosts].sort((a, b) => (b.totalCostForPeriod || 0) - (a.totalCostForPeriod || 0)).slice(0, 10);
    tbody.innerHTML = sorted.map(c => {
        const share = totalCost > 0 ? ((c.totalCostForPeriod || 0) / totalCost) * 100 : 0;
        return `<tr>
            <td>${escapeHtml(c.volumeName || '(Unnamed volume)')}</td>
            <td>${escapeHtml(c.resourceType || '')}</td>
            <td>$${(c.totalCostForPeriod || 0).toFixed(4)}</td>
            <td>${share.toFixed(1)}%</td>
        </tr>`;
    }).join('');
}

function renderComponentBreakdown(totalCost) {
    const list = document.getElementById('component-breakdown');
    if (!list) return;

    if (!summaryCosts.length) {
        list.innerHTML = '<li style="color: var(--text-secondary);">No data.</li>';
        return;
    }

    const aggregate = {};
    summaryCosts.forEach(c => {
        const breakdown = c.costBreakdown || {};
        Object.entries(breakdown).forEach(([k, v]) => {
            aggregate[k] = (aggregate[k] || 0) + (v || 0);
        });
    });

    const rows = Object.entries(aggregate).sort((a, b) => (b[1] || 0) - (a[1] || 0));
    list.innerHTML = rows.map(([k, v]) => {
        const share = totalCost > 0 ? (v / totalCost) * 100 : 0;
        return `<li><strong>${escapeHtml(k)}</strong>: $${v.toFixed(4)} (${share.toFixed(1)}%)</li>`;
    }).join('');
}

function renderTypeBreakdown() {
    const tbody = document.querySelector('#type-breakdown-table tbody');
    if (!tbody) return;

    if (!summaryCosts.length) {
        tbody.innerHTML = '<tr><td colspan="3" style="padding: 1rem; text-align: center; color: var(--text-secondary);">No cost data available.</td></tr>';
        return;
    }

    const map = {};
    summaryCosts.forEach(c => {
        const key = c.resourceType || 'Unknown';
        if (!map[key]) map[key] = { cost: 0, count: 0 };
        map[key].cost += c.totalCostForPeriod || 0;
        map[key].count += 1;
    });

    tbody.innerHTML = Object.entries(map).map(([k, v]) => `
        <tr>
            <td>${escapeHtml(k)}</td>
            <td>$${(v.cost || 0).toFixed(4)}</td>
            <td>${v.count}</td>
        </tr>
    `).join('');
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text || '';
    return div.innerHTML;
}

document.addEventListener('DOMContentLoaded', async () => {
    summaryJobId = getJobId();
    if (!summaryJobId) {
        const err = document.getElementById('summary-error');
        err.style.display = 'block';
        err.innerHTML = '<div style="padding: 1rem; background: #ffebee; border-left: 3px solid #f44336; border-radius: 4px; color: #c62828;">Missing jobId in query string.</div>';
        return;
    }

    try {
        await authManager.initialize();
        const isAuthenticated = await authManager.requireAuth();
        if (!isAuthenticated) return;
        await loadSummaryData();
    } catch (e) {
        console.error('Error initializing cost summary page:', e);
    }
});