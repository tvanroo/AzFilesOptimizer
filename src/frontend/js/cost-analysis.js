let allCosts = [];
let filteredCosts = [];
let jobId = null;

document.addEventListener('DOMContentLoaded', async () => {
    const urlParams = new URLSearchParams(window.location.search);
    jobId = urlParams.get('jobId');
    
    if (!jobId) {
        showError('No job ID provided');
        return;
    }
    
    await loadCosts();
});

async function loadCosts() {
    try {
        const response = await fetch(`/api/discovery/${jobId}/costs`);
        if (!response.ok) {
            throw new Error(`Failed to load costs: ${response.statusText}`);
        }
        
        const data = await response.json();
        allCosts = data.costs || [];
        filteredCosts = [...allCosts];
        
        updateSummary(data.summary);
        renderCosts();
    } catch (error) {
        showError(`Error loading costs: ${error.message}`);
    }
}

function updateSummary(summary) {
    if (!summary) return;
    
    document.getElementById('total-cost').textContent = `$${summary.totalCost.toFixed(2)}`;
    document.getElementById('avg-daily-cost').textContent = `$${(summary.totalCost / 30).toFixed(2)}`;
    document.getElementById('volumes-count').textContent = allCosts.length;
    
    // Determine overall trend
    const increasingCount = allCosts.filter(c => c.forecast?.trend === 'Increasing').length;
    const decreasingCount = allCosts.filter(c => c.forecast?.trend === 'Decreasing').length;
    
    if (increasingCount > decreasingCount) {
        document.getElementById('overall-trend').textContent = '📈 Rising';
    } else if (decreasingCount > increasingCount) {
        document.getElementById('overall-trend').textContent = '📉 Falling';
    } else {
        document.getElementById('overall-trend').textContent = '➡️ Stable';
    }
}

function renderCosts() {
    const container = document.getElementById('costs-container');
    
    if (filteredCosts.length === 0) {
        container.innerHTML = '<p style="text-align: center; color: var(--text-secondary);">No costs found matching your filters</p>';
        return;
    }
    
    // Sort by cost descending
    const sorted = [...filteredCosts].sort((a, b) => b.totalCostForPeriod - a.totalCostForPeriod);
    
    container.innerHTML = sorted.map(cost => createCostCard(cost)).join('');
    
    // Add click handlers
    sorted.forEach(cost => {
        const elem = document.querySelector(`[data-volume-id="${cost.volumeId}"]`);
        if (elem) {
            elem.addEventListener('click', () => showDetailPanel(cost));
        }
    });
}

function createCostCard(cost) {
    const trendIcon = getTrendIcon(cost.forecast?.trend);
    const costRange = getCostRangeClass(cost.totalCostForPeriod);
    
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
            
            <div class="cost-value">$${cost.totalCostForPeriod.toFixed(2)}</div>
            <small style="color: var(--text-secondary);">30-day total</small>
            
            <div class="cost-breakdown">
                <div style="margin-bottom: 0.75rem;">
                    <div style="display: flex; justify-content: space-between; align-items: center;">
                        <strong style="font-size: 0.875rem;">Cost Breakdown</strong>
                        <span style="font-size: 0.75rem; color: var(--text-secondary);">Daily: $${(cost.totalCostPerDay).toFixed(2)}</span>
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
        .filter(([_, value]) => value > 0)
        .sort(([_, a], [__, b]) => b - a);
    
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
            const c = cost.totalCostForPeriod;
            switch (costFilter) {
                case 'low': if (c > 50) return false; break;
                case 'medium': if (c <= 50 || c > 200) return false; break;
                case 'high': if (c <= 200 || c > 500) return false; break;
                case 'very-high': if (c <= 500) return false; break;
            }
        }
        
        // Trend filter
        if (trendFilter && cost.forecast?.trend !== trendFilter) return false;
        
        // Search filter
        if (searchFilter && !cost.volumeName.toLowerCase().includes(searchFilter)) return false;
        
        return true;
    });
    
    renderCosts();
}

function showDetailPanel(cost) {
    const panel = document.getElementById('detail-panel');
    const title = document.getElementById('detail-title');
    const content = document.getElementById('detail-content');
    
    title.textContent = cost.volumeName;
    
    const forecastHtml = cost.forecast ? `
        <h4>Cost Forecast (Next 30 Days)</h4>
        <div style="display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: 1rem; margin-bottom: 1.5rem;">
            <div style="padding: 1rem; background: var(--surface); border-radius: 4px;">
                <div style="font-size: 0.75rem; color: var(--text-secondary); margin-bottom: 0.5rem;">Forecasted Cost</div>
                <div style="font-size: 1.5rem; font-weight: bold; color: var(--primary-color);">$${cost.forecast.forecastedCostFor30Days.toFixed(2)}</div>
            </div>
            <div style="padding: 1rem; background: var(--surface); border-radius: 4px;">
                <div style="font-size: 0.75rem; color: var(--text-secondary); margin-bottom: 0.5rem;">Confidence Level</div>
                <div style="font-size: 1.5rem; font-weight: bold; color: var(--primary-color);">${cost.forecast.confidencePercentage.toFixed(0)}%</div>
            </div>
            <div style="padding: 1rem; background: var(--surface); border-radius: 4px;">
                <div style="font-size: 0.75rem; color: var(--text-secondary); margin-bottom: 0.5rem;">Trend</div>
                <div style="font-size: 1.25rem; font-weight: bold;">${getTrendIcon(cost.forecast.trend)} ${escapeHtml(cost.forecast.trend)}</div>
            </div>
        </div>
        
        <h5>Range Estimate</h5>
        <div style="margin-bottom: 1.5rem;">
            <div style="font-size: 0.875rem; margin-bottom: 0.5rem;">
                Low: $${cost.forecast.lowEstimate30Days.toFixed(2)} | 
                Mid: $${cost.forecast.midEstimate30Days.toFixed(2)} | 
                High: $${cost.forecast.highEstimate30Days.toFixed(2)}
            </div>
            <div style="width: 100%; height: 30px; background: var(--border); border-radius: 4px; position: relative; overflow: hidden;">
                <div style="position: absolute; left: 0; top: 0; height: 100%; width: 33.33%; background: rgba(76, 175, 80, 0.3);"></div>
                <div style="position: absolute; left: 33.33%; top: 0; height: 100%; width: 33.33%; background: rgba(59, 130, 246, 0.3);"></div>
                <div style="position: absolute; left: 66.66%; top: 0; height: 100%; width: 33.34%; background: rgba(244, 67, 54, 0.3);"></div>
            </div>
        </div>
        
        ${cost.forecast.recentChanges && cost.forecast.recentChanges.length > 0 ? `
        <h5>Recent Changes</h5>
        <ul style="margin-bottom: 1.5rem;">
            ${cost.forecast.recentChanges.map(change => `<li>${escapeHtml(change)}</li>`).join('')}
        </ul>
        ` : ''}
        
        ${cost.forecast.riskFactors && cost.forecast.riskFactors.length > 0 ? `
        <h5 style="color: #f44336;">Risk Factors</h5>
        <ul style="margin-bottom: 1.5rem; color: #f44336;">
            ${cost.forecast.riskFactors.map(risk => `<li>${escapeHtml(risk)}</li>`).join('')}
        </ul>
        ` : ''}
        
        ${cost.forecast.recommendations && cost.forecast.recommendations.length > 0 ? `
        <h5 style="color: #4caf50;">Recommendations</h5>
        <ul style="margin-bottom: 1.5rem; color: #4caf50;">
            ${cost.forecast.recommendations.map(rec => `<li>${escapeHtml(rec)}</li>`).join('')}
        </ul>
        ` : ''}
    ` : '';
    
    content.innerHTML = `
        <div style="display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: 1rem; margin-bottom: 1.5rem;">
            <div style="padding: 1rem; background: var(--surface); border-radius: 4px;">
                <div style="font-size: 0.75rem; color: var(--text-secondary); margin-bottom: 0.5rem;">30-Day Cost</div>
                <div style="font-size: 1.5rem; font-weight: bold; color: var(--primary-color);">$${cost.totalCostForPeriod.toFixed(2)}</div>
            </div>
            <div style="padding: 1rem; background: var(--surface); border-radius: 4px;">
                <div style="font-size: 0.75rem; color: var(--text-secondary); margin-bottom: 0.5rem;">Daily Average</div>
                <div style="font-size: 1.5rem; font-weight: bold; color: var(--primary-color);">$${cost.totalCostPerDay.toFixed(2)}</div>
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
                <strong>Capacity:</strong> ${cost.usedGigabytes.toFixed(2)} GB used / ${cost.capacityGigabytes.toFixed(2)} GB total
            </div>
            ${cost.snapshotCount > 0 ? `<div style="margin-bottom: 0.75rem;"><strong>Snapshots:</strong> ${cost.snapshotCount} (${(cost.totalSnapshotSizeGb || 0).toFixed(2)} GB)</div>` : ''}
            <div style="margin-bottom: 0.75rem;"><strong>Backup:</strong> ${cost.backupConfigured ? 'Enabled' : 'Disabled'}</div>
        </div>
        
        <h4>Cost Breakdown</h4>
        <div style="margin-bottom: 1.5rem;">
            ${renderDetailBreakdown(cost.costBreakdown)}
        </div>
        
        ${forecastHtml}
    `;
    
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
            const percentage = total > 0 ? (cost / total) * 100 : 0;
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
    document.getElementById('detail-panel').style.display = 'none';
}

async function runCostAnalysis() {
    try {
        const response = await fetch(`/api/discovery/${jobId}/cost-analysis`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' }
        });
        
        if (response.ok) {
            showToast('Cost analysis queued', 'success');
            setTimeout(() => loadCosts(), 2000);
        } else {
            showToast('Failed to start cost analysis', 'error');
        }
    } catch (error) {
        showToast(`Error: ${error.message}`, 'error');
    }
}

async function exportCosts(format) {
    try {
        const response = await fetch(`/api/discovery/${jobId}/costs/export?format=${format}`);
        
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
        
        showToast('Export successful', 'success');
    } catch (error) {
        showToast(`Export failed: ${error.message}`, 'error');
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