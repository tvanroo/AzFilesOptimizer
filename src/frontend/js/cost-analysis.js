let allCosts = [];
let filteredCosts = [];
let jobId = null;
let currentDetailCost = null;

function navigateTo(target) {
    const base = target === 'forecast' ? 'cost-forecast.html' : 'cost-summary.html';
    const url = `${base}?jobId=${encodeURIComponent(jobId)}`;
    window.location.href = url;
}

document.addEventListener('DOMContentLoaded', async () => {
    const urlParams = new URLSearchParams(window.location.search);
    jobId = urlParams.get('jobId');
    
    if (!jobId) {
        showError('No job ID provided');
        return;
    }

    try {
        await authManager.initialize();
        const isAuthenticated = await authManager.requireAuth();
        if (!isAuthenticated) return;
    } catch (e) {
        console.error('Error initializing auth for cost analysis page:', e);
    }
    
    await loadCosts();
});

function normalizeCost(raw) {
    if (!raw || typeof raw !== 'object') return {};
    return {
        // IDs and identity
        volumeId: raw.VolumeId || '',
        volumeName: raw.VolumeName || '',
        resourceType: raw.ResourceType || '',
        region: raw.Region || '',

        // Aggregates
        totalCostPerDay: typeof raw.TotalCostPerDay === 'number' ? raw.TotalCostPerDay : 0,
        totalCostForPeriod: typeof raw.TotalCostForPeriod === 'number' ? raw.TotalCostForPeriod : 0,

        // Breakdown and components
        costComponents: Array.isArray(raw.CostComponents) ? raw.CostComponents : [],
        costBreakdown: raw.CostBreakdown || {},

        // Period
        periodStart: raw.PeriodStart ? new Date(raw.PeriodStart) : null,
        periodEnd: raw.PeriodEnd ? new Date(raw.PeriodEnd) : null,
        periodDays: typeof raw.PeriodDays === 'number' ? raw.PeriodDays : 0,

        // Capacity / usage
        capacityGigabytes: typeof raw.CapacityGigabytes === 'number' ? raw.CapacityGigabytes : 0,
        usedGigabytes: typeof raw.UsedGigabytes === 'number' ? raw.UsedGigabytes : 0,
        snapshotCount: typeof raw.SnapshotCount === 'number' ? raw.SnapshotCount : 0,
        totalSnapshotSizeGb: typeof raw.TotalSnapshotSizeGb === 'number' ? raw.TotalSnapshotSizeGb : 0,
        backupConfigured: !!raw.BackupConfigured,

        // Forecast and warnings
        forecast: raw.Forecast || null,
        warnings: Array.isArray(raw.Warnings) ? raw.Warnings : [],
    };
}

async function loadCosts() {
    try {
        const response = await fetch(`${API_BASE_URL}/discovery/${jobId}/costs`);
        if (!response.ok) {
            throw new Error(`Failed to load costs: ${response.statusText}`);
        }
        
        const data = await response.json();
        const rawCosts = data.costs || [];
        allCosts = rawCosts.map(normalizeCost);
        filteredCosts = [...allCosts];
        
        updateSummary(data.summary);
        renderCosts();
    } catch (error) {
        showError(`Error loading costs: ${error.message}`);
    }
}

function updateSummary(summary) {
    const totalEl = document.getElementById('total-cost');
    const avgEl = document.getElementById('avg-daily-cost');
    const countEl = document.getElementById('volumes-count');
    const trendEl = document.getElementById('overall-trend');

    // Fallback: derive totals from cost list if backend summary is missing or malformed
    let total = 0;
    if (summary && typeof summary.totalCost === 'number' && !Number.isNaN(summary.totalCost)) {
        total = summary.totalCost;
    } else {
        total = allCosts.reduce((sum, c) => sum + (c.totalCostForPeriod || 0), 0);
    }

    let avgDaily = 0;
    if (summary && typeof summary.averageDailyCost === 'number' && !Number.isNaN(summary.averageDailyCost)) {
        avgDaily = summary.averageDailyCost;
    } else {
        avgDaily = total / 30;
    }

    totalEl.textContent = `$${(total || 0).toFixed(2)}`;
    avgEl.textContent = `$${(avgDaily || 0).toFixed(2)}`;
    countEl.textContent = allCosts.length;

    // Determine overall trend
    const increasingCount = allCosts.filter(c => c.forecast?.Trend === 'Increasing').length;
    const decreasingCount = allCosts.filter(c => c.forecast?.Trend === 'Decreasing').length;

    if (increasingCount > decreasingCount) {
        trendEl.textContent = '📈 Rising';
    } else if (decreasingCount > increasingCount) {
        trendEl.textContent = '📉 Falling';
    } else {
        trendEl.textContent = '➡️ Stable';
    }
}

function renderCosts() {
    const container = document.getElementById('costs-container');
    
    if (filteredCosts.length === 0) {
        container.innerHTML = '<p style="text-align: center; color: var(--text-secondary);">No costs found matching your filters</p>';
        return;
    }
    
    // Sort by cost descending
    const sorted = [...filteredCosts].sort((a, b) => (b.totalCostForPeriod || 0) - (a.totalCostForPeriod || 0));
    
    container.innerHTML = sorted.map(cost => createCostCard(cost)).join('');
    
    // Add click handlers
    sorted.forEach(cost => {
        const elem = document.querySelector(`[data-volume-id="${cost.volumeId}"]`);
        if (elem) {
            elem.addEventListener('click', (event) => {
                event.stopPropagation();
                showDetailPanel(cost, elem);
            });
        }
    });
}

function createCostCard(cost) {
    const trendIcon = getTrendIcon(cost.forecast?.Trend);
    const costRange = getCostRangeClass(cost.totalCostForPeriod || 0);
    
    return `
        <div class="cost-card" data-volume-id="${cost.volumeId}" style="cursor: pointer;">
            <div style="display: flex; justify-content: space-between; align-items: start; margin-bottom: 0.5rem;">
                <div>
                    <h4 style="margin: 0; color: var(--text);">${escapeHtml(cost.volumeName)}</h4>
                    <small style="color: var(--text-secondary);">${cost.resourceType}</small>
                </div>
                <span class="cost-badge" style="background: ${getCostRangeColor(cost.totalCostForPeriod)}; color: white;">
                    ${costRange}
                </span>
            </div>
            
            <div class="cost-value">$${(cost.totalCostForPeriod || 0).toFixed(2)}</div>
            <small style="color: var(--text-secondary);">30-day total</small>
            
            <div class="cost-breakdown">
                <div style="margin-bottom: 0.75rem;">
                    <div style="display: flex; justify-content: space-between; align-items: center;">
                        <strong style="font-size: 0.875rem;">Cost Breakdown</strong>
                        <span style="font-size: 0.75rem; color: var(--text-secondary);">Daily: $${(cost.totalCostPerDay || 0).toFixed(2)}</span>
                    </div>
                </div>
                ${renderBreakdownItems(cost.costBreakdown)}
            </div>
            
            ${cost.forecast ? `
            <div style="margin-top: 1rem; padding-top: 1rem; border-top: 1px solid var(--border);">
                <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 0.5rem;">
                    <small style="font-weight: 500;">Forecast Trend</small>
                    <span>${trendIcon} ${escapeHtml(cost.forecast.trend)}</span>
                </div>
                <div style="display: flex; justify-content: space-between; align-items: center;">
                    <small style="font-weight: 500;">Confidence</small>
                    <div style="width: 100px; height: 6px; background: var(--border); border-radius: 999px; overflow: hidden;">
                        <div style="width: ${cost.forecast.confidencePercentage}%; height: 100%; background: var(--primary-color);"></div>
                    </div>
                </div>
            </div>
            ` : ''}
            
            ${cost.warnings && cost.warnings.length > 0 ? `
            <div style="margin-top: 1rem; padding: 0.75rem; background: #fff3cd; border-radius: 4px; border-left: 3px solid #ffc107;">
                <small style="color: #856404; display: block; font-weight: 500;">⚠ Warnings</small>
                <ul style="margin: 0.5rem 0 0 0; padding-left: 1.25rem; font-size: 0.75rem;">
                    ${cost.warnings.map(w => `<li>${escapeHtml(w)}</li>`).join('')}
                </ul>
            </div>
            ` : ''}
        </div>
    `;
}

function renderBreakdownItems(breakdown) {
    if (!breakdown || Object.keys(breakdown).length === 0) {
        return '<small style="color: var(--text-secondary);">No breakdown data</small>';
    }
    
    const items = Object.entries(breakdown)
        .filter(([_, value]) => (value || 0) > 0)
        .sort(([_, a], [__, b]) => (b || 0) - (a || 0));
    
    if (items.length === 0) {
        return '<small style="color: var(--text-secondary);">No breakdown data</small>';
    }
    
    return items.map(([type, cost]) => `
        <div class="breakdown-item">
            <span>${type}</span>
            <span style="font-weight: 500;">$${cost.toFixed(2)}</span>
        </div>
    `).join('');
}

function applyFilters() {
    const typeFilter = document.getElementById('filter-type').value;
    const costFilter = document.getElementById('filter-cost').value;
    const trendFilter = document.getElementById('filter-trend').value;
    const searchFilter = document.getElementById('search-input').value.toLowerCase();
    
    filteredCosts = allCosts.filter(cost => {
        // Type filter
        if (typeFilter && cost.resourceType !== typeFilter) return false;
        
        // Cost range filter
        if (costFilter) {
            const c = cost.totalCostForPeriod || 0;
            switch (costFilter) {
                case 'low': if (c > 50) return false; break;
                case 'medium': if (c <= 50 || c > 200) return false; break;
                case 'high': if (c <= 200 || c > 500) return false; break;
                case 'very-high': if (c <= 500) return false; break;
            }
        }
        
        // Trend filter
        if (trendFilter && cost.forecast?.Trend !== trendFilter) return false;
        
        // Search filter
        if (searchFilter && !cost.volumeName.toLowerCase().includes(searchFilter)) return false;
        
        return true;
    });
    
    renderCosts();
}

function showDetailPanel(cost, sourceElement) {
    const panel = document.getElementById('detail-panel');
    const title = document.getElementById('detail-title');
    const content = document.getElementById('detail-content');

    title.textContent = cost.volumeName;
    
    const forecastHtml = cost.forecast ? `
        <h4>Cost Forecast (Next 30 Days)</h4>
        <div style="display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: 1rem; margin-bottom: 1.5rem;">
            <div style="padding: 1rem; background: var(--surface); border-radius: 4px;">
                <div style="font-size: 0.75rem; color: var(--text-secondary); margin-bottom: 0.5rem;">Forecasted Cost</div>
                <div style="font-size: 1.5rem; font-weight: bold; color: var(--primary-color);">$${(cost.forecast.ForecastedCostFor30Days || 0).toFixed(2)}</div>
            </div>
            <div style="padding: 1rem; background: var(--surface); border-radius: 4px;">
                <div style="font-size: 0.75rem; color: var(--text-secondary); margin-bottom: 0.5rem;">Confidence Level</div>
                <div style="font-size: 1.5rem; font-weight: bold; color: var(--primary-color);">${(cost.forecast.ConfidencePercentage || 0).toFixed(0)}%</div>
            </div>
            <div style="padding: 1rem; background: var(--surface); border-radius: 4px;">
                <div style="font-size: 0.75rem; color: var(--text-secondary); margin-bottom: 0.5rem;">Trend</div>
                <div style="font-size: 1.25rem; font-weight: bold;">${getTrendIcon(cost.forecast.Trend)} ${escapeHtml(cost.forecast.Trend)}</div>
            </div>
        </div>
        
        <h5>Range Estimate</h5>
        <div style="margin-bottom: 1.5rem;">
            <div style="font-size: 0.875rem; margin-bottom: 0.5rem;">
                Low: $${(cost.forecast.LowEstimate30Days || 0).toFixed(2)} | 
                Mid: $${(cost.forecast.MidEstimate30Days || 0).toFixed(2)} | 
                High: $${(cost.forecast.HighEstimate30Days || 0).toFixed(2)}
            </div>
            <div style="width: 100%; height: 30px; background: var(--border); border-radius: 4px; position: relative; overflow: hidden;">
                <div style="position: absolute; left: 0; top: 0; height: 100%; width: 33.33%; background: rgba(76, 175, 80, 0.3);"></div>
                <div style="position: absolute; left: 33.33%; top: 0; height: 100%; width: 33.33%; background: rgba(59, 130, 246, 0.3);"></div>
                <div style="position: absolute; left: 66.66%; top: 0; height: 100%; width: 33.34%; background: rgba(244, 67, 54, 0.3);"></div>
            </div>
        </div>
        
        ${cost.forecast.RecentChanges && cost.forecast.RecentChanges.length > 0 ? `
        <h5>Recent Changes</h5>
        <ul style="margin-bottom: 1.5rem;">
            ${cost.forecast.RecentChanges.map(change => `<li>${escapeHtml(change)}</li>`).join('')}
        </ul>
        ` : ''}
        
        ${cost.forecast.RiskFactors && cost.forecast.RiskFactors.length > 0 ? `
        <h5 style="color: #f44336;">Risk Factors</h5>
        <ul style="margin-bottom: 1.5rem; color: #f44336;">
            ${cost.forecast.RiskFactors.map(risk => `<li>${escapeHtml(risk)}</li>`).join('')}
        </ul>
        ` : ''}
        
        ${cost.forecast.Recommendations && cost.forecast.Recommendations.length > 0 ? `
        <h5 style="color: #4caf50;">Recommendations</h5>
        <ul style="margin-bottom: 1.5rem; color: #4caf50;">
            ${cost.forecast.Recommendations.map(rec => `<li>${escapeHtml(rec)}</li>`).join('')}
        </ul>
        ` : ''}
    ` : '';
    
    content.innerHTML = `
        <div style="display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: 1rem; margin-bottom: 1.5rem;">
            <div style="padding: 1rem; background: var(--surface); border-radius: 4px;">
                <div style="font-size: 0.75rem; color: var(--text-secondary); margin-bottom: 0.5rem;">30-Day Cost</div>
                <div style="font-size: 1.5rem; font-weight: bold; color: var(--primary-color);">$${(cost.totalCostForPeriod || 0).toFixed(2)}</div>
            </div>
            <div style="padding: 1rem; background: var(--surface); border-radius: 4px;">
                <div style="font-size: 0.75rem; color: var(--text-secondary); margin-bottom: 0.5rem;">Daily Average</div>
                <div style="font-size: 1.5rem; font-weight: bold; color: var(--primary-color);">$${(cost.totalCostPerDay || 0).toFixed(2)}</div>
            </div>
            <div style="padding: 1rem; background: var(--surface); border-radius: 4px;">
                <div style="font-size: 0.75rem; color: var(--text-secondary); margin-bottom: 0.5rem;">Resource Type</div>
                <div style="font-size: 1rem; font-weight: bold;">${escapeHtml(cost.resourceType)}</div>
            </div>
            <div style="padding: 1rem; background: var(--surface); border-radius: 4px;">
                <div style="font-size: 0.75rem; color: var(--text-secondary); margin-bottom: 0.5rem;">Region</div>
                <div style="font-size: 1rem; font-weight: bold;">${escapeHtml(cost.region)}</div>
            </div>
        </div>
        
        <h4>Cost Details</h4>
        <div style="margin-bottom: 1.5rem;">
            <div style="margin-bottom: 0.75rem;">
                <strong>Capacity:</strong> ${(cost.usedGigabytes || 0).toFixed(2)} GB used / ${(cost.capacityGigabytes || 0).toFixed(2)} GB total
            </div>
            ${cost.snapshotCount > 0 ? `<div style="margin-bottom: 0.75rem;"><strong>Snapshots:</strong> ${cost.snapshotCount} (${(cost.totalSnapshotSizeGb || 0).toFixed(2)} GB)</div>` : ''}
            <div style="margin-bottom: 0.75rem;"><strong>Backup:</strong> ${cost.backupConfigured ? 'Enabled' : 'Disabled'}</div>
        </div>
        
        <h4>Cost Breakdown</h4>
        <div style="margin-bottom: 1.5rem;">
            ${renderDetailBreakdown(cost.costBreakdown)}
        </div>

        <div style="margin-top: 0.75rem;">
            <button class="btn" type="button" onclick="exportCostDebug()">Export debug JSON</button>
        </div>
        
        ${forecastHtml}
    `;
    
    currentDetailCost = cost;

    // Move the detail panel directly under the clicked card so the expansion
    // appears inline instead of pinned at the bottom of the page.
    if (sourceElement && sourceElement.parentNode) {
        const parent = sourceElement.parentNode;
        if (panel.parentNode !== parent) {
            parent.insertBefore(panel, sourceElement.nextSibling);
        } else if (panel.previousSibling !== sourceElement) {
            parent.insertBefore(panel, sourceElement.nextSibling);
        }
    }

    panel.style.display = 'block';
}

function renderDetailBreakdown(breakdown) {
    if (!breakdown || Object.keys(breakdown).length === 0) {
        return '<p style="color: var(--text-secondary);">No breakdown data</p>';
    }
    
    const total = Object.values(breakdown).reduce((a, b) => a + b, 0);
    
    return Object.entries(breakdown)
        .filter(([_, value]) => value > 0)
        .sort(([_, a], [__, b]) => b - a)
        .map(([type, cost]) => {
            const percentage = total > 0 ? ((cost || 0) / total) * 100 : 0;
            return `
                <div style="margin-bottom: 1rem;">
                    <div style="display: flex; justify-content: space-between; margin-bottom: 0.5rem;">
                        <span><strong>${escapeHtml(type)}</strong></span>
                        <span>$${cost.toFixed(2)} (${percentage.toFixed(1)}%)</span>
                    </div>
                    <div style="height: 8px; background: var(--border); border-radius: 4px; overflow: hidden;">
                        <div style="height: 100%; width: ${percentage}%; background: var(--primary-color);"></div>
                    </div>
                </div>
            `;
        })
        .join('');
}

function closeDetailPanel() {
    currentDetailCost = null;
    document.getElementById('detail-panel').style.display = 'none';
}

function exportCostDebug() {
    if (!currentDetailCost || !Array.isArray(currentDetailCost.costComponents)) {
        if (typeof Toast !== 'undefined' && Toast.error) {
            Toast.error('No debug cost data available for this volume');
        } else {
            alert('No debug cost data available for this volume');
        }
        return;
    }

    // Derive a simple source indicator for the cost data
    let source = 'Unknown';
    if (currentDetailCost.costComponents.length > 0) {
        const anyEstimated = currentDetailCost.costComponents.some(c => {
            const flag = (typeof c.IsEstimated === 'boolean') ? c.IsEstimated : c.isEstimated;
            return flag === true;
        });
        source = anyEstimated ? 'RetailEstimate' : 'ActualCost';
    }

    const payload = {
        jobId,
        volumeId: currentDetailCost.volumeId,
        volumeName: currentDetailCost.volumeName,
        resourceType: currentDetailCost.resourceType,
        region: currentDetailCost.region,
        periodStart: currentDetailCost.periodStart,
        periodEnd: currentDetailCost.periodEnd,
        totalCostForPeriod: currentDetailCost.totalCostForPeriod,
        totalCostPerDay: currentDetailCost.totalCostPerDay,
        source,
        costComponents: currentDetailCost.costComponents,
    };

    const blob = new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json' });
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `cost-debug-${currentDetailCost.volumeId || 'volume'}.json`;
    document.body.appendChild(a);
    a.click();
    window.URL.revokeObjectURL(url);
    document.body.removeChild(a);
}

async function runCostAnalysis() {
    try {
        const response = await fetch(`${API_BASE_URL}/discovery/${jobId}/cost-analysis`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' }
        });
        
        if (response.ok) {
            Toast.success('Cost analysis queued');
            setTimeout(() => loadCosts(), 2000);
        } else {
            Toast.error('Failed to start cost analysis');
        }
    } catch (error) {
        Toast.error(`Error: ${error.message}`);
    }
}

async function exportCosts(format) {
    try {
        const response = await fetch(`${API_BASE_URL}/discovery/${jobId}/costs/export?format=${format}`);
        
        if (!response.ok) {
            throw new Error('Export failed');
        }
        
        const blob = await response.blob();
        const url = window.URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `costs-${jobId}.${format === 'csv' ? 'csv' : 'json'}`;
        document.body.appendChild(a);
        a.click();
        window.URL.revokeObjectURL(url);
        document.body.removeChild(a);
        
        Toast.success('Export successful');
    } catch (error) {
        Toast.error(`Export failed: ${error.message}`);
    }
}

function showError(message) {
    const container = document.getElementById('error-container');
    container.innerHTML = `<div style="padding: 1rem; background: #ffebee; border-left: 3px solid #f44336; border-radius: 4px; color: #c62828;">${escapeHtml(message)}</div>`;
    container.style.display = 'block';
}

function getTrendIcon(trend) {
    switch (trend) {
        case 'Increasing': return '📈';
        case 'Decreasing': return '📉';
        default: return '➡️';
    }
}

function getCostRangeClass(cost) {
    if (cost < 50) return 'Low';
    if (cost < 200) return 'Medium';
    if (cost < 500) return 'High';
    return 'Very High';
}

function getCostRangeColor(cost) {
    if (cost < 50) return '#4caf50'; // Green
    if (cost < 200) return '#ff9800'; // Orange
    if (cost < 500) return '#f44336'; // Red
    return '#c62828'; // Dark Red
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}