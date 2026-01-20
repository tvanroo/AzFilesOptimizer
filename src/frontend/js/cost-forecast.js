let allForecasts = [];
let filteredForecasts = [];
let jobId = null;

function getJobIdFromQuery() {
    const params = new URLSearchParams(window.location.search);
    return params.get('jobId');
}

function navigateTo(target) {
    const base = target === 'analysis' ? 'cost-analysis.html' : 'cost-summary.html';
    const url = `${base}?jobId=${encodeURIComponent(jobId)}`;
    window.location.href = url;
}

function normalizeForecast(raw) {
    if (!raw || typeof raw !== 'object') return {};
    return {
        forecastId: raw.ForecastId || '',
        resourceId: raw.ResourceId || '',
        volumeName: raw.VolumeName || '',
        forecastedCostPerDay: typeof raw.ForecastedCostPerDay === 'number' ? raw.ForecastedCostPerDay : 0,
        forecastedCostFor30Days: typeof raw.ForecastedCostFor30Days === 'number' ? raw.ForecastedCostFor30Days : 0,
        lowEstimate30Days: typeof raw.LowEstimate30Days === 'number' ? raw.LowEstimate30Days : 0,
        midEstimate30Days: typeof raw.MidEstimate30Days === 'number' ? raw.MidEstimate30Days : 0,
        highEstimate30Days: typeof raw.HighEstimate30Days === 'number' ? raw.HighEstimate30Days : 0,
        confidencePercentage: typeof raw.ConfidencePercentage === 'number' ? raw.ConfidencePercentage : 0,
        trend: raw.Trend || 'Unknown',
        trendDescription: raw.TrendDescription || '',
        percentageChangeFromHistorical: typeof raw.PercentageChangeFromHistorical === 'number' ? raw.PercentageChangeFromHistorical : 0,
        dailyGrowthRatePercentage: typeof raw.DailyGrowthRatePercentage === 'number' ? raw.DailyGrowthRatePercentage : 0,
        recentChanges: Array.isArray(raw.RecentChanges) ? raw.RecentChanges : [],
        riskFactors: Array.isArray(raw.RiskFactors) ? raw.RiskFactors : [],
        recommendations: Array.isArray(raw.Recommendations) ? raw.Recommendations : [],
        backupCostPercentage: typeof raw.BackupCostPercentage === 'number' ? raw.BackupCostPercentage : 0,
        egressCostPercentage: typeof raw.EgressCostPercentage === 'number' ? raw.EgressCostPercentage : 0,
    };
}

async function loadForecasts() {
    try {
        const response = await fetch(`${API_BASE_URL}/discovery/${jobId}/cost-forecast`);
        if (!response.ok) {
            throw new Error(`Failed to load forecast: ${response.statusText}`);
        }

        const data = await response.json();
        const rawForecasts = data.forecasts || [];
        allForecasts = rawForecasts.map(normalizeForecast);
        filteredForecasts = [...allForecasts];

        updateForecastSummary(data.summary);
        renderForecasts();
    } catch (error) {
        const err = document.getElementById('forecast-error');
        err.style.display = 'block';
        err.innerHTML = `<div style="padding: 1rem; background: #ffebee; border-left: 3px solid #f44336; border-radius: 4px; color: #c62828;">${escapeHtml(error.message)}</div>`;
        console.error('Error loading forecasts:', error);
    }
}

function updateForecastSummary(summary) {
    const totalEl = document.getElementById('forecast-total-cost');
    const avgConfEl = document.getElementById('forecast-avg-confidence');
    const countEl = document.getElementById('forecast-volume-count');
    const riskEl = document.getElementById('forecast-risk-count');

    let total = 0;
    let avgConfidence = 0;
    let riskCount = 0;

    if (summary) {
        if (typeof summary.totalForecastedCost === 'number') total = summary.totalForecastedCost;
        if (typeof summary.averageConfidence === 'number') avgConfidence = summary.averageConfidence;
        if (typeof summary.riskFactorCount === 'number') riskCount = summary.riskFactorCount;
    }

    if (!total && allForecasts.length > 0) {
        total = allForecasts.reduce((s, f) => s + (f.forecastedCostFor30Days || 0), 0);
    }
    if (!avgConfidence && allForecasts.length > 0) {
        avgConfidence = allForecasts.reduce((s, f) => s + (f.confidencePercentage || 0), 0) / allForecasts.length;
    }
    if (!riskCount && allForecasts.length > 0) {
        riskCount = allForecasts.reduce((s, f) => s + (f.riskFactors?.length || 0), 0);
    }

    totalEl.textContent = `$${(total || 0).toFixed(2)}`;
    avgConfEl.textContent = `${(avgConfidence || 0).toFixed(0)}%`;
    countEl.textContent = allForecasts.length;
    riskEl.textContent = riskCount;
}

function applyForecastFilters() {
    const trendFilter = document.getElementById('filter-trend').value;
    const riskFilter = document.getElementById('filter-risk').value;
    const searchFilter = document.getElementById('search-input').value.toLowerCase();

    filteredForecasts = allForecasts.filter(f => {
        if (trendFilter && f.trend !== trendFilter) return false;

        if (riskFilter === 'none' && (f.riskFactors?.length || 0) > 0) return false;
        if (riskFilter === 'any' && (!f.riskFactors || f.riskFactors.length === 0)) return false;

        if (searchFilter && !f.volumeName.toLowerCase().includes(searchFilter)) return false;

        return true;
    });

    renderForecasts();
}

function renderForecasts() {
    const container = document.getElementById('forecast-container');

    if (filteredForecasts.length === 0) {
        container.innerHTML = '<p style="text-align: center; color: var(--text-secondary);">No forecast data available. Run cost analysis first.</p>';
        return;
    }

    const sorted = [...filteredForecasts].sort((a, b) => (b.forecastedCostFor30Days || 0) - (a.forecastedCostFor30Days || 0));

    container.innerHTML = `<div class="forecast-grid">${sorted.map(renderForecastCard).join('')}</div>`;
}

function renderForecastCard(f) {
    const trendIcon = getTrendIcon(f.trend);
    const riskTags = (f.riskFactors || []).slice(0, 4).map(r => `<span class="risk-chip">${escapeHtml(r)}</span>`).join('');

    return `
        <div class="forecast-card">
            <div class="forecast-header">
                <div>
                    <div style="font-weight: 600;">${escapeHtml(f.volumeName || '(Unnamed volume)')}</div>
                    <div class="forecast-range">$${(f.lowEstimate30Days || 0).toFixed(2)} - $${(f.highEstimate30Days || 0).toFixed(2)} (30 days)</div>
                </div>
                <span class="trend-badge">${trendIcon} ${escapeHtml(f.trend)}</span>
            </div>

            <div style="margin-top: 0.75rem;">
                <div style="font-size: 0.875rem; color: var(--text-secondary);">Forecasted 30-day cost</div>
                <div style="font-size: 1.4rem; font-weight: 600; color: var(--primary-color);">$${(f.forecastedCostFor30Days || 0).toFixed(2)}</div>
            </div>

            <div style="margin-top: 0.75rem; display: flex; justify-content: space-between; font-size: 0.8rem; color: var(--text-secondary);">
                <div>Confidence: ${(f.confidencePercentage || 0).toFixed(0)}%</div>
                <div>Δ vs. historical: ${(f.percentageChangeFromHistorical || 0).toFixed(1)}%</div>
            </div>

            <div style="margin-top: 0.75rem; font-size: 0.8rem; color: var(--text-secondary);">
                <div>Backup share: ${(f.backupCostPercentage || 0).toFixed(1)}% &middot; Egress share: ${(f.egressCostPercentage || 0).toFixed(1)}%</div>
            </div>

            ${riskTags ? `<div style="margin-top: 0.75rem;">${riskTags}</div>` : ''}

            ${f.recommendations && f.recommendations.length > 0 ? `
            <div style="margin-top: 0.75rem; font-size: 0.8rem;">
                <div style="font-weight: 600; margin-bottom: 0.25rem;">Recommendations</div>
                <ul style="margin: 0; padding-left: 1.1rem;">
                    ${f.recommendations.slice(0, 3).map(r => `<li>${escapeHtml(r)}</li>`).join('')}
                </ul>
            </div>
            ` : ''}
        </div>
    `;
}

function getTrendIcon(trend) {
    switch (trend) {
        case 'Increasing': return '📈';
        case 'Decreasing': return '📉';
        case 'Stable': return '➡️';
        default: return '❓';
    }
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text || '';
    return div.innerHTML;
}

document.addEventListener('DOMContentLoaded', async () => {
    jobId = getJobIdFromQuery();
    if (!jobId) {
        const err = document.getElementById('forecast-error');
        err.style.display = 'block';
        err.innerHTML = '<div style="padding: 1rem; background: #ffebee; border-left: 3px solid #f44336; border-radius: 4px; color: #c62828;">Missing jobId in query string.</div>';
        return;
    }

    try {
        await authManager.initialize();
        const isAuthenticated = await authManager.requireAuth();
        if (!isAuthenticated) return;
        await loadForecasts();
    } catch (e) {
        console.error('Error initializing forecast page:', e);
    }
});