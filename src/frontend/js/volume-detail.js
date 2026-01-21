const volumeDetailPage = {
    jobId: null,
    volumeId: null,
    currentData: null,
    workloadProfiles: [],
    undoStack: [],
    autoSaveTimeout: null,
    _downloadUrl: null,

    async init() {
        const params = new URLSearchParams(window.location.search);
        this.jobId = params.get('jobId');
        this.volumeId = params.get('volumeId');

        if (!this.jobId || !this.volumeId) {
            alert('Missing jobId or volumeId');
            window.location.href = 'jobs.html';
            return;
        }

        document.getElementById('breadcrumb-job').href = `job-detail.html?id=${encodeURIComponent(this.jobId)}`;

        const backBtn = document.getElementById('back-to-job-btn');
        if (backBtn) {
            backBtn.onclick = () => {
                window.location.href = `job-detail.html?id=${encodeURIComponent(this.jobId)}`;
            };
        }

        try {
            await authManager.initialize();
            const isAuth = await authManager.requireAuth();
            if (!isAuth) return;
        } catch (e) {
            console.error('Auth init failed', e);
            Toast.error('Failed to initialize authentication');
            return;
        }

        await this.loadWorkloadProfiles();
        await this.loadVolume();
        this.initializeDecisionPanel();
    },

    async loadVolume() {
        const loading = document.getElementById('volume-loading');
        const content = document.getElementById('volume-content');

        try {
            const url = `${API_BASE_URL}/discovery/${this.jobId}/volumes/${this.volumeId}`;
            const response = await fetch(url);
            if (!response.ok) {
                throw new Error(`Failed to load volume (${response.status})`);
            }
            const data = await response.json();
            loading.style.display = 'none';
            content.style.display = 'block';
            this.renderVolume(data);
        } catch (error) {
            console.error('Error loading volume detail:', error);
            loading.innerHTML = `<p class="error-message"><strong>Error:</strong> ${error.message}</p>`;
        }
    },

    renderVolume(model) {
        this.currentData = model; // Store for decision panel
        const v = model;
        const volumeType = v.VolumeType || 'AzureFiles';

        // Update JSON download link
        this.updateDownloadLink(model);
        const vol = v.VolumeData || {};
        const ai = v.AiAnalysis || null;
        const user = v.UserAnnotations || null;

        const props = this.getVolumeProperties(volumeType, vol);
        this.updateVolumeLabels(volumeType);

        // Title / breadcrumb
        const name = props.name || 'Unknown volume';
        document.getElementById('volume-title').textContent = name;
        document.getElementById('breadcrumb-volume').textContent = name;
        document.getElementById('breadcrumb-job').textContent = `Job ${this.jobId.substring(0, 8)}`;

        document.getElementById('volume-subtitle').textContent =
            `${props.parentName || '-'} · ${vol.ResourceGroup || '-'} · ${vol.Location || '-'}`;

        // Summary cards
        const aiWorkload = ai?.SuggestedWorkloadName || 'Unclassified';
        const aiConfidence = ai ? `${(ai.ConfidenceScore * 100).toFixed(0)}%` : '-';
        document.getElementById('summary-ai-workload').textContent = aiWorkload;
        document.getElementById('summary-ai-confidence').textContent = aiConfidence;

        // Cost summary (if available)
        const cost = v.CostSummary || null;
        const costStatus = v.CostStatus || (cost ? 'Completed' : 'Pending');
        const costTotalEl = document.getElementById('summary-cost-30d');
        const costDailyEl = document.getElementById('summary-cost-daily');
        const costSourceEl = document.getElementById('summary-cost-source');
        if (costTotalEl && costDailyEl && costSourceEl) {
            if (cost) {
                costTotalEl.textContent = `$${(cost.TotalCost30Days || 0).toFixed(2)}`;
                costDailyEl.textContent = `$${(cost.DailyAverage || 0).toFixed(2)}`;
                costSourceEl.textContent = cost.IsActual ? 'Actual billed (scaled)' : 'Estimated (retail)';
            } else {
                costTotalEl.textContent = costStatus === 'Pending' ? 'Pending' : '-';
                costDailyEl.textContent = costStatus === 'Pending' ? 'Pending' : '-';
                costSourceEl.textContent = costStatus;
            }
        }

        const userWorkload = user?.ConfirmedWorkloadName || 'Not confirmed';
        document.getElementById('summary-user-workload').textContent = userWorkload;

        const status = this.getMigrationStatusText(user?.MigrationStatus) || 'Candidate';
        const statusEl = document.getElementById('summary-migration-status');
        statusEl.textContent = status;
        statusEl.className = `badge-status ${status}`;

        const capacityGiB = props.capacityGiB ?? 0;
        document.getElementById('summary-capacity').textContent = `${capacityGiB} GiB`;
        const usedSummary = props.usedBytes != null ? this.formatBytes(props.usedBytes) : 'Unknown';
        document.getElementById('summary-used').textContent = usedSummary;

        document.getElementById('summary-location').textContent = vol.Location || '-';
        document.getElementById('summary-tier').textContent = props.tier || '-';

        // Properties
        this.setText('prop-share-name', name);
        this.setText('prop-storage-account', props.parentName || '-');
        this.setText('prop-resource-group', vol.ResourceGroup || '-');
        this.setText('prop-subscription', vol.SubscriptionId || '-');
        this.setText('prop-location', vol.Location || '-');
        this.setText('prop-access-tier', props.tier || 'Unknown');
        this.setText('prop-protocols', props.protocols.join(', '));
        this.setText('prop-sku', props.sku || 'N/A');
        this.setText('prop-quota', capacityGiB.toString());
        const usedDetail = props.usedBytes != null ? this.formatBytes(props.usedBytes) : 'Unknown';
        this.setText('prop-used', usedDetail);
        this.renderTags('prop-tags', vol.Tags);

        // Add type-specific properties
        if (volumeType === 'ManagedDisk') {
            this.addDiskSpecificProperties(vol);
        } else if (volumeType === 'ANF') {
            this.addAnfSpecificProperties(vol);
        }

        // AI categorization
        this.setText('ai-suggested', aiWorkload);
        this.setText('ai-confidence', aiConfidence);
        this.setText('ai-last-analyzed', ai?.LastAnalyzed ? new Date(ai.LastAnalyzed).toLocaleString() : '-');
        this.setText('ai-error', ai?.ErrorMessage || '-');
        this.renderAiPrompts(ai?.AppliedPrompts || []);
        
        // Check Accept AI checkboxes if user has already accepted the AI classification
        const aiAccepted = ai?.SuggestedWorkloadId && user?.ConfirmedWorkloadId === ai.SuggestedWorkloadId;
        const summaryCheckbox = document.getElementById('accept-ai-checkbox-summary');
        const detailCheckbox = document.getElementById('accept-ai-checkbox-detail');
        if (summaryCheckbox) summaryCheckbox.checked = aiAccepted;
        if (detailCheckbox) detailCheckbox.checked = aiAccepted;

        // Human decisions
        this.setText('user-workload', userWorkload);
        this.setText('user-status', this.getMigrationStatusText(user?.MigrationStatus) || 'Candidate');
        this.renderTagArray('user-tags', user?.CustomTags);
        this.setText('user-reviewed-by', user?.ReviewedBy || '-');
        this.setText('user-reviewed-at', user?.ReviewedAt ? new Date(user.ReviewedAt).toLocaleString() : '-');
        const notesEl = document.getElementById('user-notes');
        if (user?.Notes) {
            notesEl.textContent = user.Notes;
        } else {
            notesEl.textContent = 'No review notes recorded yet.';
            notesEl.style.color = 'var(--text-secondary)';
        }

        // Metrics
        this.renderAllMetrics(volumeType, vol);

        // Capacity Sizing
        this.renderCapacitySizing(ai?.CapacitySizing);

        // Populate decision panel
        this.populateDecisionPanel(model);

        // History timeline: combine AI prompts, user review, and annotation history
        this.renderHistoryTimeline(ai, user, v.AnnotationHistory || []);
    },

    renderTags(elementId, tags) {
        const el = document.getElementById(elementId);
        if (!el) return;
        if (!tags || Object.keys(tags).length === 0) {
            el.textContent = 'None';
            el.style.color = 'var(--text-secondary)';
            return;
        }
        el.innerHTML = Object.entries(tags)
            .map(([k, v]) => `<span class="tag-pill">${this.escapeHtml(k)}: ${this.escapeHtml(v)}</span>`)
            .join('');
    },

    renderTagArray(elementId, arr) {
        const el = document.getElementById(elementId);
        if (!el) return;
        if (!arr || arr.length === 0) {
            el.textContent = 'None';
            el.style.color = 'var(--text-secondary)';
            return;
        }
        el.innerHTML = arr
            .map(t => `<span class="tag-pill">${this.escapeHtml(t)}</span>`)
            .join('');
    },

    renderAiPrompts(prompts) {
        const container = document.getElementById('ai-prompts');
        if (!container) return;
        if (!prompts || prompts.length === 0) {
            container.innerHTML = '<p style="font-size:0.85rem; color:var(--text-secondary);">No prompt-level details available.</p>';
            return;
        }
        container.innerHTML = prompts.map(p => {
            const stopped = p.StoppedProcessing ? '<span style="margin-left:6px; font-size:0.75rem; color:#ef6c00;">Stopped Here</span>' : '';
            const evidenceHtml = (p.Evidence && p.Evidence.length)
                ? '<ul style="margin:4px 0 0 1rem; font-size:0.8rem;">' +
                  p.Evidence.map(e => `<li>${this.escapeHtml(e)}</li>`).join('') +
                  '</ul>'
                : '';
            return `
                <div style="border:1px solid #ddd; border-radius:4px; padding:8px 10px; margin-bottom:6px; font-size:0.85rem;">
                    <div style="font-weight:600;">${this.escapeHtml(p.PromptName || '')}${stopped}</div>
                    <div style="margin-top:4px; white-space:pre-wrap;">${this.escapeHtml(p.Result || '')}</div>
                    ${evidenceHtml}
                </div>
            `;
        }).join('');
    },

    renderAllMetrics(volumeType, vol) {
        const container = document.getElementById('metrics-container');
        if (!container) return;
        container.innerHTML = '';

        let panels = 0;

        // Primary resource metrics (share, ANF volume, or managed disk)
        const primaryTitle = volumeType === 'ManagedDisk'
            ? 'Managed Disk Metrics'
            : volumeType === 'ANF'
                ? 'ANF Volume Metrics'
                : 'Share Metrics';
        panels += this.renderMetricsPanel(container, vol.HistoricalMetricsSummary, vol.MonitoringDataAvailableDays, primaryTitle);

        // VM-side metrics for managed disks
        if (volumeType === 'ManagedDisk') {
            panels += this.renderMetricsPanel(container, vol.VmMetricsSummary, vol.VmMonitoringDataAvailableDays, 'VM Data Disk Metrics');
            panels += this.renderMetricsPanel(container, vol.VmOverallMetricsSummary, vol.VmOverallMonitoringDataAvailableDays, 'VM Overall Disk Metrics');
        }

        if (panels === 0) {
            container.innerHTML = '<p style="font-size:0.85rem; color:var(--text-secondary);">No historical metrics summary available.</p>';
        }
    },

    renderMetricsPanel(container, summaryJson, daysAvailable, title) {
        if (!summaryJson) return 0;
        let metrics;
        try {
            metrics = JSON.parse(summaryJson);
        } catch (e) {
            console.error('Failed to parse metrics summary', e);
            container.innerHTML += '<p style="font-size:0.85rem; color:var(--text-secondary);">Could not parse metrics summary.</p>';
            return 0;
        }
        if (!metrics || Object.keys(metrics).length === 0) {
            return 0;
        }
        const labels = {
            'Transactions': 'Transactions (count/hour)',
            'Ingress': 'Ingress (bytes/hour)',
            'Egress': 'Egress (bytes/hour)',
            'SuccessServerLatency': 'Server Latency (ms)',
            'SuccessE2ELatency': 'E2E Latency (ms)',
            'Availability': 'Availability (%)',
            'VolumeLogicalSize': 'Logical Size (bytes)',
            'ReadIops': 'Read IOPS',
            'WriteIops': 'Write IOPS',
            'VolumeThroughputReadBytes': 'Read Throughput (bytes/s)',
            'VolumeThroughputWriteBytes': 'Write Throughput (bytes/s)',
            'VolumeConsumedSizePercentage': 'Consumed %',
            // Managed disk composite metrics
            'Composite Disk Read Bytes/sec': 'Disk Read (bytes/sec)',
            'Composite Disk Write Bytes/sec': 'Disk Write (bytes/sec)',
            'Composite Disk Read Operations/Sec': 'Disk Read IOPS',
            'Composite Disk Write Operations/Sec': 'Disk Write IOPS',
            'DiskPaidBurstIOPS': 'Burst IOPS (paid)',
            // VM data disk metrics (per LUN)
            'Data Disk Bandwidth Consumed Percentage': 'Data Disk Bandwidth Used (%)',
            'Data Disk IOPS Consumed Percentage': 'Data Disk IOPS Used (%)',
            'Data Disk Latency': 'Data Disk Latency (ms)',
            'Data Disk Max Burst Bandwidth': 'Data Disk Max Burst Bandwidth',
            'Data Disk Max Burst IOPS': 'Data Disk Max Burst IOPS',
            'Data Disk Queue Depth': 'Data Disk Queue Depth',
            'Data Disk Read Bytes/sec': 'Data Disk Read (bytes/sec)',
            'Data Disk Write Bytes/sec': 'Data Disk Write (bytes/sec)',
            'Data Disk Read Operations/Sec': 'Data Disk Read IOPS',
            'Data Disk Write Operations/Sec': 'Data Disk Write IOPS',
            'Data Disk Target Bandwidth': 'Data Disk Target Bandwidth',
            'Data Disk Target IOPS': 'Data Disk Target IOPS',
            'Data Disk Used Burst BPS Credits Percentage': 'Data Disk Burst BPS Credits Used (%)',
            'Premium Data Disk Cache Read Hit': 'Premium Disk Cache Read Hit',
            'Premium Data Disk Cache Read Miss': 'Premium Disk Cache Read Miss',
            // VM overall disk metrics
            'Disk Read Bytes': 'VM Disk Read Bytes',
            'Disk Write Bytes': 'VM Disk Write Bytes',
            'Disk Read Operations/Sec': 'VM Disk Read IOPS',
            'Disk Write Operations/Sec': 'VM Disk Write IOPS'
        };

        let html = '<div class="metrics-panel">';
        if (title) {
            html += `<div style="font-size:0.9rem; font-weight:600; margin-bottom:4px;">${this.escapeHtml(title)}</div>`;
        }
        html += `<div style="font-size:0.85rem;">${daysAvailable ? `${daysAvailable} days of hourly metrics` : 'Hourly metrics'} summarized (avg/max).</div>`;
        html += '<div class="metrics-grid">';

        for (const [name, data] of Object.entries(metrics)) {
            if (name.startsWith('_')) continue; // Skip metadata entries
            if (typeof data !== 'object' || data === null) continue;
            const avg = data.average;
            const max = data.max;
            if (avg === undefined && max === undefined) continue;

            const avgVal = avg ?? 0;
            const maxVal = max ?? 0;
            const label = labels[name] || name;
            let avgDisplay = this.formatNumber(avgVal);
            let maxDisplay = this.formatNumber(maxVal);

            if (name.includes('Bytes') || name.includes('Size')) {
                avgDisplay = this.formatBytes(avgVal);
                maxDisplay = this.formatBytes(maxVal);
            } else if (name.includes('Availability') || name.endsWith('Percentage')) {
                avgDisplay = avgVal.toFixed(2) + '%';
                maxDisplay = maxVal.toFixed(2) + '%';
            } else if (name.includes('Latency')) {
                avgDisplay = avgVal.toFixed(2);
                maxDisplay = maxVal.toFixed(2);
            }

            html += `
                <div class="metrics-card">
                    <div class="metrics-label">${this.escapeHtml(label)}</div>
                    <div class="metrics-value">${avgDisplay}</div>
                    <div class="metrics-subvalue">max: ${maxDisplay}</div>
                </div>
            `;
        }

        html += '</div></div>';
        container.innerHTML += html;
        return 1;
    },

    renderCapacitySizing(sizing) {
        const container = document.getElementById('sizing-container');
        if (!container) return;
        
        if (!sizing) {
            container.innerHTML = '<p style="font-size:0.85rem; color:var(--text-secondary);">No capacity sizing analysis available. Run analysis to generate recommendations.</p>';
            return;
        }

        if (!sizing.HasSufficientData) {
            container.innerHTML = `
                <div class="sizing-warning">
                    <strong>⚠ Insufficient Data</strong><br>
                    ${this.escapeHtml(sizing.Warnings || 'Not enough historical metrics to generate reliable sizing recommendations.')}
                </div>
            `;
            return;
        }

        let html = '<div class="sizing-panel">';
        
        // Header with service level badge
        html += '<div class="sizing-header">';
        html += `<div style="font-size:0.9rem; font-weight:600;">Recommended ANF Configuration</div>`;
        if (sizing.SuggestedServiceLevel) {
            html += `<span class="sizing-badge ${sizing.SuggestedServiceLevel}">${sizing.SuggestedServiceLevel} Tier</span>`;
        }
        html += '</div>';

        // Sizing cards
        html += '<div class="sizing-grid">';
        
        // Capacity card
        html += `
            <div class="sizing-card">
                <div class="sizing-card-label">Recommended Capacity</div>
                <div class="sizing-card-value">${sizing.RecommendedCapacityInUnit.toFixed(2)} ${sizing.CapacityUnit}</div>
                <div class="sizing-card-subvalue">Peak: ${sizing.PeakCapacityGiB.toFixed(2)} GiB</div>
            </div>
        `;
        
        // Throughput card
        html += `
            <div class="sizing-card">
                <div class="sizing-card-label">Recommended Throughput</div>
                <div class="sizing-card-value">${sizing.RecommendedThroughputMiBps.toFixed(1)} MiB/s</div>
                <div class="sizing-card-subvalue">Peak: ${sizing.PeakThroughputMiBps.toFixed(1)} MiB/s</div>
            </div>
        `;
        
        // Buffer card
        html += `
            <div class="sizing-card">
                <div class="sizing-card-label">Buffer Applied</div>
                <div class="sizing-card-value">${sizing.BufferPercent > 0 ? '+' : ''}${sizing.BufferPercent}%</div>
                <div class="sizing-card-subvalue">${sizing.DaysAnalyzed} days analyzed</div>
            </div>
        `;
        
        // Data quality card
        const qualityPercent = (sizing.DataQualityScore * 100).toFixed(0);
        const qualityColor = sizing.DataQualityScore >= 0.7 ? '#2e7d32' : sizing.DataQualityScore >= 0.5 ? '#ef6c00' : '#c62828';
        html += `
            <div class="sizing-card">
                <div class="sizing-card-label">Data Quality</div>
                <div class="sizing-card-value" style="color:${qualityColor};">${qualityPercent}%</div>
                <div class="sizing-card-subvalue">${sizing.MetricDataPoints} data points</div>
            </div>
        `;
        
        html += '</div>'; // End sizing-grid
        
        // Detailed breakdown
        if (sizing.PeakReadThroughputMiBps > 0 || sizing.PeakWriteThroughputMiBps > 0) {
            html += '<div class="sizing-grid">';
            
            if (sizing.PeakReadThroughputMiBps > 0) {
                html += `
                    <div class="sizing-card">
                        <div class="sizing-card-label">Peak Read Throughput</div>
                        <div class="sizing-card-value" style="font-size:0.95rem;">${sizing.PeakReadThroughputMiBps.toFixed(1)} MiB/s</div>
                    </div>
                `;
            }
            
            if (sizing.PeakWriteThroughputMiBps > 0) {
                html += `
                    <div class="sizing-card">
                        <div class="sizing-card-label">Peak Write Throughput</div>
                        <div class="sizing-card-value" style="font-size:0.95rem;">${sizing.PeakWriteThroughputMiBps.toFixed(1)} MiB/s</div>
                    </div>
                `;
            }
            
            if (sizing.PeakTotalIOPS > 0) {
                html += `
                    <div class="sizing-card">
                        <div class="sizing-card-label">Peak IOPS</div>
                        <div class="sizing-card-value" style="font-size:0.95rem;">${this.formatNumber(sizing.PeakTotalIOPS)}</div>
                        <div class="sizing-card-subvalue">R: ${this.formatNumber(sizing.PeakReadIOPS)} / W: ${this.formatNumber(sizing.PeakWriteIOPS)}</div>
                    </div>
                `;
            }
            
            html += '</div>';
        }
        
        // AI reasoning
        if (sizing.AiReasoning) {
            html += `
                <div class="sizing-reasoning">
                    <strong>💡 AI Analysis:</strong><br>
                    ${this.escapeHtml(sizing.AiReasoning)}
                </div>
            `;
        }
        
        // Warnings
        if (sizing.Warnings) {
            html += `
                <div class="sizing-warning">
                    <strong>⚠ Considerations:</strong><br>
                    ${this.escapeHtml(sizing.Warnings)}
                </div>
            `;
        }
        
        html += '</div>'; // End sizing-panel
        container.innerHTML = html;
    },

    renderHistoryTimeline(ai, user, history) {
        const container = document.getElementById('history-timeline');
        if (!container) return;
        const items = [];

        // Annotation history entries (oldest to newest)
        if (Array.isArray(history) && history.length) {
            history.forEach(h => {
                items.push({
                    type: 'annotation',
                    title: 'Annotation updated',
                    time: h.Timestamp ? new Date(h.Timestamp).toLocaleString() : null,
                    body: h.Notes || '',
                    meta: [
                        h.UserId ? `User: ${h.UserId}` : null,
                        h.MigrationStatus ? `Status: ${h.MigrationStatus}` : null,
                        h.ConfirmedWorkloadName ? `Workload: ${h.ConfirmedWorkloadName}` : null,
                        h.Source ? `Source: ${h.Source}` : null
                    ].filter(Boolean).join(' | ')
                });
            });
        }

        // AI prompts (logical lineage of AI decision)
        if (ai && ai.AppliedPrompts && ai.AppliedPrompts.length) {
            ai.AppliedPrompts.forEach(p => {
                items.push({
                    type: 'ai-prompt',
                    title: `AI Prompt: ${p.PromptName || ''}`,
                    time: null,
                    body: p.Result || '',
                    meta: p.StoppedProcessing ? 'Stop condition triggered' : null
                });
            });
        }

        // Most recent consolidated user review (for convenience)
        if (user && (user.ReviewedAt || user.ReviewedBy || user.MigrationStatus || user.ConfirmedWorkloadName || user.Notes)) {
            items.push({
                type: 'user-review',
                title: 'Latest human review',
                time: user.ReviewedAt ? new Date(user.ReviewedAt).toLocaleString() : null,
                body: user.Notes || '',
                meta: [
                    user.ReviewedBy ? `Reviewer: ${user.ReviewedBy}` : null,
                    user.MigrationStatus ? `Status: ${user.MigrationStatus}` : null,
                    user.ConfirmedWorkloadName ? `Workload: ${user.ConfirmedWorkloadName}` : null
                ].filter(Boolean).join(' | ')
            });
        }

        if (!items.length) {
            container.innerHTML = '<p style="font-size:0.85rem; color:var(--text-secondary);">No detailed history is available for this volume yet.</p>';
            return;
        }

        // Sort items with dated entries descending, undated last
        items.sort((a, b) => {
            if (!a.time && !b.time) return 0;
            if (!a.time) return 1;
            if (!b.time) return -1;
            return new Date(b.time).getTime() - new Date(a.time).getTime();
        });

        container.innerHTML = items.map(it => {
            const meta = it.meta ? `<div class="timeline-meta">${this.escapeHtml(it.meta)}</div>` : '';
            const time = it.time ? `<div class="timeline-meta">${this.escapeHtml(it.time)}</div>` : '';
            const body = it.body ? `<div class="timeline-body">${this.escapeHtml(it.body)}</div>` : '';
            return `
                <div class="timeline-item">
                    <div class="timeline-title">${this.escapeHtml(it.title)}</div>
                    ${time}
                    ${meta}
                    ${body}
                </div>
            `;
        }).join('');
    },

    updateDownloadLink(model) {
        const link = document.getElementById('download-json-link');
        if (!link) return;

        const json = JSON.stringify(model, null, 2);
        const blob = new Blob([json], { type: 'application/json' });
        if (this._downloadUrl) {
            URL.revokeObjectURL(this._downloadUrl);
        }
        this._downloadUrl = URL.createObjectURL(blob);
        link.href = this._downloadUrl;
        link.download = `job-${this.jobId}-volume-${this.volumeId}.json`;
    },

    // Helpers
    setText(id, value) {
        const el = document.getElementById(id);
        if (el) el.textContent = value;
    },

    formatBytes(bytes) {
        if (!bytes || isNaN(bytes)) return '0 B';
        const k = 1024;
        const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
        const i = Math.floor(Math.log(bytes) / Math.log(k));
        const val = parseFloat((bytes / Math.pow(k, i)).toFixed(2));
        return `${val} ${sizes[i]}`;
    },

    formatNumber(num) {
        if (num === null || num === undefined || isNaN(num)) return '0';
        return Number(num).toLocaleString('en-US', { maximumFractionDigits: 2 });
    },

    escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text || '';
        return div.innerHTML;
    },

    getMigrationStatusText(status) {
        if (!status) return 'Candidate';
        // Handle both string and numeric enum values
        if (typeof status === 'number') {
            const statusNames = ['Candidate', 'Excluded', 'UnderReview', 'Approved'];
            return statusNames[status] || 'Candidate';
        }
        return status.toString();
    },

    getVolumeProperties(volumeType, vol) {
        switch (volumeType) {
            case 'ManagedDisk':
                return {
                    name: vol.DiskName || 'Unknown Disk',
                    parentName: vol.AttachedVmName || 'Not Attached',
                    capacityGiB: vol.DiskSizeGB || 0,
                    // Used capacity for managed disks is unknown; use full size for sizing, but show as unknown in UI
                    usedBytes: null,
                    tier: vol.DiskTier || vol.DiskSku || 'Unknown',
                    protocols: [vol.DiskType || 'Unknown'],
                    sku: vol.DiskSku || 'N/A'
                };
            case 'ANF':
                return {
                    name: vol.VolumeName || 'Unknown Volume',
                    parentName: vol.NetAppAccountName || 'Unknown Account',
                    capacityGiB: vol.ProvisionedSizeBytes ? vol.ProvisionedSizeBytes / (1024 * 1024 * 1024) : 0,
                    usedBytes: vol.ProvisionedSizeBytes || 0,
                    tier: vol.ServiceLevel || 'Unknown',
                    protocols: vol.ProtocolTypes || ['Unknown'],
                    sku: vol.PoolQosType || 'N/A'
                };
            case 'AzureFiles':
            default:
                return {
                    name: vol.ShareName || 'Unknown Share',
                    parentName: vol.StorageAccountName || 'Unknown Account',
                    capacityGiB: vol.ShareQuotaGiB || 0,
                    usedBytes: vol.ShareUsageBytes || 0,
                    tier: vol.AccessTier || 'Unknown',
                    protocols: vol.EnabledProtocols || ['SMB'],
                    sku: vol.StorageAccountSku || 'N/A'
                };
        }
    },

    updateVolumeLabels(volumeType) {
        const labelMap = {
            'ManagedDisk': {
                'Share Name': 'Disk Name',
                'Storage Account': 'VM / Parent',
                'Access Tier': 'Disk Tier',
                'Protocols': 'Disk Type',
                'Quota (GiB)': 'Size (GiB)',
                'ANF Capacity & Throughput Sizing': 'ANF Capacity Sizing'
            },
            'ANF': {
                'Share Name': 'Volume Name',
                'Storage Account': 'NetApp Account',
                'Access Tier': 'Service Level',
                'Protocols': 'Protocols',
                'Quota (GiB)': 'Size (GiB)'
            },
            'AzureFiles': {
                'Share Name': 'Share Name',
                'Storage Account': 'Storage Account',
                'Access Tier': 'Access Tier',
                'Protocols': 'Protocols',
                'Quota (GiB)': 'Quota (GiB)'
            }
        };

        const labels = labelMap[volumeType] || labelMap['AzureFiles'];

        // Update labels in the DOM
        const labelElements = document.querySelectorAll('.metadata-label');
        labelElements.forEach(el => {
            const labelText = el.textContent;
            if (labels[labelText]) {
                el.textContent = labels[labelText];
            }
        });

        // Update sizing section title
        const sizingLabel = document.querySelector('.metadata-section h3');
        if (sizingLabel && labels['ANF Capacity & Throughput Sizing']) {
            sizingLabel.textContent = labels['ANF Capacity & Throughput Sizing'];
        }
    },

    addDiskSpecificProperties(vol) {
        const metadataGrid = document.querySelector('.metadata-grid');
        if (!metadataGrid) return;

        const diskProps = [
            { label: 'Disk State', value: vol.DiskState || '-' },
            { label: 'Provisioning State', value: vol.ProvisioningState || '-' },
            { label: 'Disk Type', value: vol.DiskType || '-' },
            { label: 'Bursting Enabled', value: vol.BurstingEnabled ? 'Yes' : 'No' },
            { label: 'Is OS Disk', value: vol.IsOsDisk ? 'Yes' : 'No' }
        ];

        // Add performance metrics if available
        if (vol.AverageReadIops || vol.AverageWriteIops) {
            const readIops = vol.AverageReadIops ? vol.AverageReadIops.toFixed(1) : '-';
            const writeIops = vol.AverageWriteIops ? vol.AverageWriteIops.toFixed(1) : '-';
            diskProps.push({ label: 'Avg Read IOPS', value: readIops });
            diskProps.push({ label: 'Avg Write IOPS', value: writeIops });
        }

        if (vol.AverageReadThroughputMiBps || vol.AverageWriteThroughputMiBps) {
            const readMb = vol.AverageReadThroughputMiBps ? vol.AverageReadThroughputMiBps.toFixed(1) : '-';
            const writeMb = vol.AverageWriteThroughputMiBps ? vol.AverageWriteThroughputMiBps.toFixed(1) : '-';
            diskProps.push({ label: 'Avg Read Throughput (MiB/s)', value: readMb });
            diskProps.push({ label: 'Avg Write Throughput (MiB/s)', value: writeMb });
        }

        // Add VM details if attached
        if (vol.IsAttached && vol.AttachedVmName) {
            diskProps.push(
                { label: 'VM Name', value: vol.AttachedVmName },
                { label: 'VM Size', value: vol.VmSize || '-' },
                { label: 'VM CPU Cores', value: vol.VmCpuCount ? vol.VmCpuCount.toString() : '-' },
                { label: 'VM Memory (GiB)', value: vol.VmMemoryGiB ? vol.VmMemoryGiB.toString() : '-' },
                { label: 'VM OS Type', value: vol.VmOsType || '-' }
            );
        }

        // Add Time Created
        if (vol.TimeCreated) {
            diskProps.push({ label: 'Time Created', value: new Date(vol.TimeCreated).toLocaleString() });
        }

        // Insert properties after Tags
        const tagsElement = document.getElementById('prop-tags');
        if (tagsElement && tagsElement.parentElement) {
            diskProps.forEach(prop => {
                const labelDiv = document.createElement('div');
                labelDiv.className = 'metadata-label';
                labelDiv.textContent = prop.label;

                const valueDiv = document.createElement('div');
                valueDiv.className = 'metadata-value';
                valueDiv.textContent = prop.value;

                tagsElement.parentElement.after(labelDiv, valueDiv);
            });
        }
    },

    addAnfSpecificProperties(vol) {
        const metadataGrid = document.querySelector('.metadata-grid');
        if (!metadataGrid) return;

        const anfProps = [
            { label: 'Service Level', value: vol.ServiceLevel || '-' },
            { label: 'Capacity Pool', value: vol.CapacityPoolName || '-' },
            { label: 'QoS Type', value: vol.PoolQosType || '-' },
            { label: 'Cool Access', value: vol.CoolAccessEnabled ? 'Enabled' : 'Disabled' }
        ];

        // Add snapshot and backup info
        anfProps.push(
            { label: 'Snapshot Count', value: vol.SnapshotCount !== undefined ? vol.SnapshotCount.toString() : '-' }
        );

        if (vol.TotalSnapshotSizeBytes) {
            anfProps.push({
                label: 'Total Snapshot Size',
                value: this.formatBytes(vol.TotalSnapshotSizeBytes)
            });
        }

        if (vol.BackupPolicyConfigured !== undefined) {
            anfProps.push({
                label: 'Backup Policy',
                value: vol.BackupPolicyConfigured ? 'Configured' : 'Not Configured'
            });
        }

        // Add performance estimates
        if (vol.EstimatedIops || vol.EstimatedThroughputMiBps) {
            anfProps.push(
                { label: 'Estimated IOPS', value: vol.EstimatedIops ? vol.EstimatedIops.toString() : '-' },
                { label: 'Estimated Throughput (MiB/s)', value: vol.EstimatedThroughputMiBps ? vol.EstimatedThroughputMiBps.toString() : '-' }
            );
        }

        // Insert properties after Tags
        const tagsElement = document.getElementById('prop-tags');
        if (tagsElement && tagsElement.parentElement) {
            anfProps.forEach(prop => {
                const labelDiv = document.createElement('div');
                labelDiv.className = 'metadata-label';
                labelDiv.textContent = prop.label;

                const valueDiv = document.createElement('div');
                valueDiv.className = 'metadata-value';
                valueDiv.textContent = prop.value;

                tagsElement.parentElement.after(labelDiv, valueDiv);
            });
        }
    },

    // Decision Panel Methods
    async loadWorkloadProfiles() {
        try {
            const url = `${API_BASE_URL}/workload-profiles`;
            const response = await fetch(url);
            if (response.ok) {
                this.workloadProfiles = await response.json();
            }
        } catch (error) {
            console.error('Error loading workload profiles:', error);
        }
    },

    initializeDecisionPanel() {
        // Populate workload dropdown
        const workloadSelect = document.getElementById('workload-select');
        if (workloadSelect && this.workloadProfiles.length > 0) {
            this.workloadProfiles.forEach(profile => {
                if (!profile.IsExclusionProfile) {
                    const option = document.createElement('option');
                    option.value = profile.ProfileId;
                    option.textContent = profile.Name;
                    workloadSelect.appendChild(option);
                }
            });
        }

        // Quick action buttons
        document.getElementById('btn-accept-ai')?.addEventListener('click', () => this.acceptAiClassification());
        document.getElementById('btn-exclude')?.addEventListener('click', () => this.quickExclude());
        document.getElementById('btn-approve')?.addEventListener('click', () => this.quickApprove());
        document.getElementById('btn-undo')?.addEventListener('click', () => this.undoLastChange());
        
        // Inline accept AI checkboxes
        document.getElementById('accept-ai-checkbox-summary')?.addEventListener('change', (e) => {
            if (e.target.checked) {
                this.acceptAiClassification();
            }
        });
        document.getElementById('accept-ai-checkbox-detail')?.addEventListener('change', (e) => {
            if (e.target.checked) {
                this.acceptAiClassification();
            }
        });

        // Auto-save on changes
        workloadSelect?.addEventListener('change', () => this.scheduleAutoSave());
        document.getElementById('status-select')?.addEventListener('change', () => this.scheduleAutoSave());
        document.getElementById('capacity-override')?.addEventListener('input', () => this.scheduleAutoSave());
        document.getElementById('throughput-override')?.addEventListener('input', () => this.scheduleAutoSave());
        document.getElementById('notes-input')?.addEventListener('input', () => this.scheduleAutoSave());
    },

    populateDecisionPanel(model) {
        const ai = model.AiAnalysis || {};
        const user = model.UserAnnotations || {};
        const sizing = ai.CapacitySizing || {};

        // Set workload
        const workloadSelect = document.getElementById('workload-select');
        if (workloadSelect) {
            workloadSelect.value = user.ConfirmedWorkloadId || '';
        }

        // Set status
        const statusSelect = document.getElementById('status-select');
        if (statusSelect) {
            statusSelect.value = user.MigrationStatus?.toString() || 'Candidate';
        }

        // Set capacity override (check user annotations first, then sizing)
        const capacityInput = document.getElementById('capacity-override');
        if (capacityInput) {
            capacityInput.value = user.TargetCapacityGiB || '';
            capacityInput.placeholder = sizing.RecommendedCapacityGiB 
                ? `AI recommends: ${sizing.RecommendedCapacityGiB.toFixed(2)} GiB`
                : 'Leave empty for AI recommendation';
        }

        // Set throughput override
        const throughputInput = document.getElementById('throughput-override');
        if (throughputInput) {
            throughputInput.value = user.TargetThroughputMiBps || '';
            throughputInput.placeholder = sizing.RecommendedThroughputMiBps
                ? `AI recommends: ${sizing.RecommendedThroughputMiBps.toFixed(1)} MiB/s`
                : 'Leave empty for AI recommendation';
        }

        // Set notes
        const notesInput = document.getElementById('notes-input');
        if (notesInput) {
            notesInput.value = user.Notes || '';
        }
    },

    acceptAiClassification() {
        const ai = this.currentData?.AiAnalysis;
        if (!ai || !ai.SuggestedWorkloadId) {
            Toast.error('No AI classification available to accept');
            return;
        }

        const workloadSelect = document.getElementById('workload-select');
        const statusSelect = document.getElementById('status-select');
        
        if (!workloadSelect || !statusSelect) {
            Toast.error('Decision panel not initialized');
            return;
        }

        this.saveCurrentState();
        workloadSelect.value = ai.SuggestedWorkloadId;
        statusSelect.value = 'UnderReview';
        
        // Update the current data model
        if (!this.currentData.UserAnnotations) {
            this.currentData.UserAnnotations = {};
        }
        this.currentData.UserAnnotations.ConfirmedWorkloadId = ai.SuggestedWorkloadId;
        this.currentData.UserAnnotations.ConfirmedWorkloadName = ai.SuggestedWorkloadName;
        this.currentData.UserAnnotations.MigrationStatus = 'UnderReview';
        
        // Update the summary display immediately
        document.getElementById('summary-user-workload').textContent = ai.SuggestedWorkloadName;
        const statusEl = document.getElementById('summary-migration-status');
        statusEl.textContent = 'UnderReview';
        statusEl.className = 'badge-status UnderReview';
        document.getElementById('user-workload').textContent = ai.SuggestedWorkloadName;
        document.getElementById('user-status').textContent = 'UnderReview';
        
        // Check the Accept AI checkboxes
        const summaryCheckbox = document.getElementById('accept-ai-checkbox-summary');
        const detailCheckbox = document.getElementById('accept-ai-checkbox-detail');
        if (summaryCheckbox) summaryCheckbox.checked = true;
        if (detailCheckbox) detailCheckbox.checked = true;
        
        this.scheduleAutoSave();
        Toast.success('Accepted AI classification');
    },

    quickExclude() {
        this.saveCurrentState();
        document.getElementById('status-select').value = 'Excluded';
        this.scheduleAutoSave();
        Toast.info('Volume excluded from migration');
    },

    quickApprove() {
        this.saveCurrentState();
        document.getElementById('status-select').value = 'Approved';
        const workloadSelect = document.getElementById('workload-select');
        if (!workloadSelect.value) {
            const ai = this.currentData?.AiAnalysis;
            if (ai?.SuggestedWorkloadId) {
                workloadSelect.value = ai.SuggestedWorkloadId;
            }
        }
        this.scheduleAutoSave();
        Toast.success('Volume approved for migration');
    },

    saveCurrentState() {
        const state = {
            workload: document.getElementById('workload-select')?.value,
            status: document.getElementById('status-select')?.value,
            capacity: document.getElementById('capacity-override')?.value,
            throughput: document.getElementById('throughput-override')?.value,
            notes: document.getElementById('notes-input')?.value
        };
        this.undoStack.push(state);
        if (this.undoStack.length > 10) this.undoStack.shift(); // Keep last 10 states
        document.getElementById('btn-undo').disabled = false;
    },

    undoLastChange() {
        if (this.undoStack.length === 0) return;
        
        const state = this.undoStack.pop();
        document.getElementById('workload-select').value = state.workload || '';
        document.getElementById('status-select').value = state.status || 'Candidate';
        document.getElementById('capacity-override').value = state.capacity || '';
        document.getElementById('throughput-override').value = state.throughput || '';
        document.getElementById('notes-input').value = state.notes || '';
        
        document.getElementById('btn-undo').disabled = this.undoStack.length === 0;
        this.scheduleAutoSave();
        
        Toast.info('Undone');
    },

    scheduleAutoSave() {
        // Highlight panel as modified
        const panel = document.getElementById('decision-panel');
        if (panel) panel.classList.add('modified');

        // Debounce auto-save
        if (this.autoSaveTimeout) clearTimeout(this.autoSaveTimeout);
        this.autoSaveTimeout = setTimeout(() => this.saveDecisions(), 1000);
    },

    async saveDecisions() {
        const workloadId = document.getElementById('workload-select')?.value;
        const status = document.getElementById('status-select')?.value;
        const capacity = document.getElementById('capacity-override')?.value;
        const throughput = document.getElementById('throughput-override')?.value;
        const notes = document.getElementById('notes-input')?.value;

        const updates = {
            ConfirmedWorkloadId: workloadId || null,
            MigrationStatus: status || 'Candidate',
            Notes: notes || null
        };

        // Add custom fields for capacity/throughput overrides
        if (capacity) updates.TargetCapacityGiB = parseFloat(capacity);
        if (throughput) updates.TargetThroughputMiBps = parseFloat(throughput);

        try {
            const url = `${API_BASE_URL}/discovery/${this.jobId}/volumes/${this.volumeId}/annotations`;
            const response = await fetch(url, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(updates)
            });

            if (!response.ok) {
                throw new Error(`Failed to save (${response.status})`);
            }

            // Remove modified highlight
            const panel = document.getElementById('decision-panel');
            if (panel) panel.classList.remove('modified');

            // Reload to show updated data
            await this.loadVolume();
        } catch (error) {
            console.error('Error saving decisions:', error);
            Toast.error(`Failed to save: ${error.message}`);
        }
    }
};

document.addEventListener('DOMContentLoaded', () => volumeDetailPage.init());
