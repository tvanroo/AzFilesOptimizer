const volumeDetailPage = {
    jobId: null,
    volumeId: null,
    currentData: null,
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

        document.getElementById('breadcrumb-job').href = `job-detail.html?id=${encodeURIComponent(this.jobId)}&tab=analysis`;

        const backBtn = document.getElementById('back-to-job-btn');
        if (backBtn) {
            backBtn.onclick = () => {
                window.location.href = `job-detail.html?id=${encodeURIComponent(this.jobId)}&tab=analysis`;
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

        await this.loadVolume();
        this.initializeDecisionPanel();

        // Load job activity logs for this volume's job
        this.fetchJobLogs();
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
        const user = v.UserAnnotations || null;

        const props = this.getVolumeProperties(volumeType, vol);
        this.updateVolumeLabels(volumeType);

        // Title / breadcrumb
        const name = props.name || 'Unknown volume';
        const titleEl = document.getElementById('volume-title');
        if (titleEl) {
            let iconHtml = '';
            if (volumeType === 'ManagedDisk') {
                iconHtml = '<img src="images/icon-managed-disk.png" class="vol-type-icon" alt="Managed Disk">';
            } else if (volumeType === 'ANF') {
                iconHtml = '<img src="images/icon-anf.png" class="vol-type-icon" alt="ANF Volume">';
            } else {
                iconHtml = '<img src="images/icon-storage-account.png" class="vol-type-icon" alt="Azure Files Share">';
            }
            titleEl.innerHTML = `<span class="vol-type">${iconHtml}<span>${this.escapeHtml(name)}</span></span>`;
        }
        document.getElementById('breadcrumb-volume').textContent = name;
        document.getElementById('breadcrumb-job').textContent = `Job ${this.jobId.substring(0, 8)}`;

        document.getElementById('volume-subtitle').textContent =
            `${props.parentName || '-'} · ${vol.ResourceGroup || '-'} · ${vol.Location || '-'}`;


        // Cost summary (if available)
        const cost = v.CostSummary || null;
        const costStatus = v.CostStatus || (cost ? 'Completed' : 'Pending');
        const costTotalEl = document.getElementById('summary-cost-30d');
        const costDailyEl = document.getElementById('summary-cost-daily');
        const costSourceEl = document.getElementById('summary-cost-source');
        if (costTotalEl && costDailyEl && costSourceEl) {
            if (cost) {
                const totalCost = `$${(cost.TotalCost30Days || 0).toFixed(2)}`;
                const dailyCost = `$${(cost.DailyAverage || 0).toFixed(2)}`;
                
                // Show estimates in grey text
                if (cost.IsActual) {
                    costTotalEl.textContent = totalCost;
                    costDailyEl.textContent = dailyCost;
                    costTotalEl.style.color = '';
                    costDailyEl.style.color = '';
                } else {
                    costTotalEl.textContent = totalCost;
                    costDailyEl.textContent = dailyCost;
                    costTotalEl.style.color = '#999';
                    costDailyEl.style.color = '#999';
                }
                
                costSourceEl.textContent = cost.IsActual ? 'Actual billed' : 'Estimated (retail)';
            } else {
                costTotalEl.textContent = costStatus === 'Pending' ? 'Pending' : '-';
                costDailyEl.textContent = costStatus === 'Pending' ? 'Pending' : '-';
                costSourceEl.textContent = costStatus;
                costTotalEl.style.color = '';
                costDailyEl.style.color = '';
            }
        }


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


        // Human decisions
        this.setText('user-status', this.getMigrationStatusText(user?.MigrationStatus) || 'Candidate');
        this.renderTagArray('user-tags', user?.CustomTags);
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
        this.renderCapacitySizing(v.AiAnalysis?.CapacitySizing);

        // Populate decision panel
        this.populateDecisionPanel(model);

        // History timeline: combine user review and annotation history
        this.renderHistoryTimeline(user, v.AnnotationHistory || []);
        
        // Load cool data assumptions if applicable
        this.loadCoolAssumptions();
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
            // Azure Files / generic storage metrics
            'Transactions': 'Transactions (count/hour)',
            'Ingress': 'Ingress (bytes/hour)',
            'Egress': 'Egress (bytes/hour)',
            'SuccessServerLatency': 'Server Latency (ms)',
            'SuccessE2ELatency': 'E2E Latency (ms)',
            'Availability': 'Availability (%)',

            // ANF capacity metrics
            'VolumeLogicalSize': 'Logical Size (bytes)',
            'VolumeAllocatedSize': 'Allocated Size (bytes)',
            'VolumeConsumedSizePercentage': 'Consumed Capacity (%)',
            'VolumeSnapshotSize': 'Snapshot Size (bytes)',
            'VolumeBackupBytes': 'Backup Size (bytes)',

            // ANF IOPS metrics (handle casing variants)
            'ReadIOPS': 'Read IOPS',
            'WriteIOPS': 'Write IOPS',
            'ReadIops': 'Read IOPS',
            'WriteIops': 'Write IOPS',
            'OtherIOPS': 'Other IOPS',
            'OtherIops': 'Other IOPS',
            'OtherOps': 'Other IOPS',

            // ANF throughput metrics
            'ReadThroughput': 'Read Throughput (MiB/s)',
            'WriteThroughput': 'Write Throughput (MiB/s)',
            'OtherThroughput': 'Other Throughput (MiB/s)',
            'TotalThroughput': 'Total Throughput (MiB/s)',
            'VolumeThroughputReadBytes': 'Read Throughput (bytes/s)',
            'VolumeThroughputWriteBytes': 'Write Throughput (bytes/s)',

            // ANF QoS / latency / replication / inode metrics
            'QosLatencyDelta': 'QoS Latency Delta (ms)',
            'QoS latency delta': 'QoS Latency Delta (ms)',
            'VolumeReplicationProgress': 'Replication Progress (%)',
            'VolumeReplicationLagTime': 'Replication Lag Time (s)',
            'VolumeReplicationStatusHealthy': 'Replication Status Healthy',
            'IsVolumeReplicationSuspended': 'Replication Suspended',
            'Is Volume Backup suspended': 'Backup Suspended',
            'Is volume replication status healthy': 'Replication Status Healthy',
            'Is volume replication transferring': 'Replication Transferring',
            'VolumeInodesUsed': 'Inodes Used',
            'VolumeInodesTotal': 'Inodes Total',
            'VolumeInodesPercentage': 'Inodes Used (%)',

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
                <div class="sizing-card-value" title="Full precision: ${sizing.RecommendedThroughputMiBps.toFixed(8)} MiB/s">${sizing.RecommendedThroughputMiBps.toFixed(1)} MiB/s</div>
                <div class="sizing-card-subvalue" title="Full precision: ${sizing.PeakThroughputMiBps.toFixed(8)} MiB/s">Peak: ${sizing.PeakThroughputMiBps.toFixed(1)} MiB/s</div>
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
                        <div class="sizing-card-value" style="font-size:0.95rem;" title="Full precision: ${sizing.PeakReadThroughputMiBps.toFixed(8)} MiB/s">${sizing.PeakReadThroughputMiBps.toFixed(1)} MiB/s</div>
                    </div>
                `;
            }
            
            if (sizing.PeakWriteThroughputMiBps > 0) {
                html += `
                    <div class="sizing-card">
                        <div class="sizing-card-label">Peak Write Throughput</div>
                        <div class="sizing-card-value" style="font-size:0.95rem;" title="Full precision: ${sizing.PeakWriteThroughputMiBps.toFixed(8)} MiB/s">${sizing.PeakWriteThroughputMiBps.toFixed(1)} MiB/s</div>
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

    async fetchJobLogs() {
        try {
            const logs = await apiClient.getJobLogs(this.jobId);
            if (!logs || logs.length === 0) return;

            const logBody = document.getElementById('volume-discovery-log-body');
            if (!logBody) return;

            logBody.innerHTML = logs.map(log => {
                const timestamp = new Date(log.Timestamp).toLocaleTimeString();
                let cssClass = 'discovery-log-entry';
                const message = log.Message || '';
                if (message.includes('ERROR') || message.includes('✗') || message.includes('Failed')) {
                    cssClass += ' error';
                } else if (message.includes('WARNING') || message.includes('⚠')) {
                    cssClass += ' warning';
                } else if (message.includes('✓') || message.includes('complete') || message.includes('Found')) {
                    cssClass += ' success';
                }
                return `<div class="${cssClass}"><span class="discovery-log-timestamp">${timestamp}</span>${this.escapeHtml(message)}</div>`;
            }).join('');

            logBody.scrollTop = logBody.scrollHeight;
        } catch (err) {
            console.error('Error loading job logs for volume view:', err);
        }
    },

    copyLogContent(elementId, btnElement) {
        const logBody = document.getElementById(elementId);
        if (!logBody) return;

        const logText = logBody.innerText || '';
        if (!logText) {
            alert('No log content to copy');
            return;
        }

        navigator.clipboard.writeText(logText).then(() => {
            const originalText = btnElement.textContent;
            btnElement.textContent = '✓ Copied!';
            setTimeout(() => {
                btnElement.textContent = originalText;
            }, 2000);
        }).catch(err => {
            console.error('Failed to copy log:', err);
            alert('Failed to copy log. Please select and copy manually.');
        });
    },

    toggleLogConsole() {
        const logBody = document.getElementById('volume-discovery-log-body');
        const toggleBtn = event.target;
        if (!logBody) return;

        if (logBody.style.display === 'none') {
            logBody.style.display = 'block';
            toggleBtn.textContent = 'Collapse';
        } else {
            logBody.style.display = 'none';
            toggleBtn.textContent = 'Expand';
        }
    },

    renderHistoryTimeline(user, history) {
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
                        h.MigrationStatus ? `Status: ${h.MigrationStatus}` : null,
                        h.Source ? `Source: ${h.Source}` : null
                    ].filter(Boolean).join(' | ')
                });
            });
        }

        // Most recent consolidated user review (for convenience)
        if (user && (user.MigrationStatus || user.Notes)) {
            items.push({
                type: 'user-review',
                title: 'Latest human review',
                body: user.Notes || '',
                meta: [
                    user.MigrationStatus ? `Status: ${user.MigrationStatus}` : null
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
            const readMb = vol.AverageReadThroughputMiBps ? `<span title="Full precision: ${vol.AverageReadThroughputMiBps.toFixed(8)} MiB/s">${vol.AverageReadThroughputMiBps.toFixed(1)}</span>` : '-';
            const writeMb = vol.AverageWriteThroughputMiBps ? `<span title="Full precision: ${vol.AverageWriteThroughputMiBps.toFixed(8)} MiB/s">${vol.AverageWriteThroughputMiBps.toFixed(1)}</span>` : '-';
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

        const anfProps = [];

        if (vol.MountPath) {
            anfProps.push({ label: 'Mount Path', value: vol.MountPath });
        }

        anfProps.push(
            { label: 'Service Level', value: vol.ServiceLevel || '-' },
            { label: 'Capacity Pool', value: vol.CapacityPoolName || '-' },
            { label: 'QoS Type', value: vol.PoolQosType || '-' }
        );

        if (vol.MaximumNumberOfFiles !== undefined && vol.MaximumNumberOfFiles !== null) {
            anfProps.push({ label: 'Max Files', value: vol.MaximumNumberOfFiles.toString() });
        }

        if (vol.VirtualNetworkName || vol.SubnetName) {
            const vnetSubnet = `${vol.VirtualNetworkName || ''}${vol.VirtualNetworkName && vol.SubnetName ? '/' : ''}${vol.SubnetName || ''}` || '-';
            anfProps.push({ label: 'Virtual Network / Subnet', value: vnetSubnet });
        }

        if (vol.VolumeType) {
            anfProps.push({ label: 'Volume Type', value: vol.VolumeType });
        }

        const coolAccessValue = vol.CoolAccessEnabled === true
            ? 'Enabled'
            : vol.CoolAccessEnabled === false
                ? 'Disabled'
                : 'Unknown';
        anfProps.push({ label: 'Cool Access', value: coolAccessValue });

        if (vol.CoolnessPeriodDays !== undefined && vol.CoolnessPeriodDays !== null) {
            anfProps.push({ label: 'Coolness Period', value: `${vol.CoolnessPeriodDays} days` });
        }

        if (vol.NetworkFeatures) {
            anfProps.push({ label: 'Network Features', value: vol.NetworkFeatures });
        }

        if (vol.SecurityStyle) {
            anfProps.push({ label: 'Security Style', value: vol.SecurityStyle });
        }

        if (vol.IsKerberosEnabled !== undefined && vol.IsKerberosEnabled !== null) {
            anfProps.push({
                label: 'Kerberos',
                value: vol.IsKerberosEnabled ? 'Enabled' : 'Disabled'
            });
        }

        if (vol.EncryptionKeySource) {
            anfProps.push({ label: 'Encryption Key Source', value: vol.EncryptionKeySource });
        }

        if (vol.IsLdapEnabled !== undefined && vol.IsLdapEnabled !== null) {
            anfProps.push({
                label: 'LDAP',
                value: vol.IsLdapEnabled ? 'Enabled' : 'Disabled'
            });
        }

        if (vol.UnixPermissions) {
            anfProps.push({ label: 'Unix Permissions', value: vol.UnixPermissions });
        }

        if (vol.AvailabilityZone) {
            anfProps.push({ label: 'Availability Zone', value: vol.AvailabilityZone });
        }

        if (vol.IsLargeVolume !== undefined && vol.IsLargeVolume !== null) {
            anfProps.push({
                label: 'Large Volume',
                value: vol.IsLargeVolume ? 'Yes' : 'No'
            });
        }

        if (vol.AvsDataStore) {
            anfProps.push({ label: 'Azure VMware Solution', value: vol.AvsDataStore });
        }

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

    initializeDecisionPanel() {
        // Quick action buttons
        document.getElementById('btn-exclude')?.addEventListener('click', () => this.quickExclude());
        document.getElementById('btn-approve')?.addEventListener('click', () => this.quickApprove());
        document.getElementById('btn-undo')?.addEventListener('click', () => this.undoLastChange());

        // Auto-save on changes
        document.getElementById('status-select')?.addEventListener('change', () => this.scheduleAutoSave());
        document.getElementById('capacity-override')?.addEventListener('input', () => {
            this.updateCapacityOverrideState();
            this.scheduleAutoSave();
        });
        document.getElementById('throughput-override')?.addEventListener('input', () => {
            this.updateThroughputOverrideState();
            this.scheduleAutoSave();
        });
        document.getElementById('notes-input')?.addEventListener('input', () => this.scheduleAutoSave());

        // Revert buttons
        document.getElementById('capacity-revert-btn')?.addEventListener('click', () => this.revertCapacity());
        document.getElementById('throughput-revert-btn')?.addEventListener('click', () => this.revertThroughput());
    },

    populateDecisionPanel(model) {
        const user = model.UserAnnotations || {};

        // Store calculated values from model (used in ANF cost calculations)
        this.calculatedCapacityGiB = model.RequiredCapacityGiB;
        this.calculatedThroughputMiBps = model.RequiredThroughputMiBps;

        // Set status
        const statusSelect = document.getElementById('status-select');
        if (statusSelect) {
            statusSelect.value = user.MigrationStatus?.toString() || 'Candidate';
        }

        // Set capacity - show calculated value, indicate if overridden
        const capacityInput = document.getElementById('capacity-override');
        const capacityLabel = document.getElementById('capacity-calculated-label');
        const capacityStatus = document.getElementById('capacity-status');
        const capacityRevertBtn = document.getElementById('capacity-revert-btn');
        
        if (capacityInput) {
            const hasOverride = user.TargetCapacityGiB != null;
            const calculatedValue = this.calculatedCapacityGiB;
            
            if (hasOverride) {
                // Show override value
                capacityInput.value = user.TargetCapacityGiB;
            } else if (calculatedValue != null) {
                // Show calculated value
                capacityInput.value = Math.ceil(calculatedValue);
            } else {
                capacityInput.value = '';
            }
            
            // Update label and status
            if (capacityLabel) {
                capacityLabel.textContent = calculatedValue != null ? `(calculated: ${Math.ceil(calculatedValue)} GiB)` : '';
            }
            if (capacityStatus) {
                if (hasOverride && calculatedValue != null) {
                    capacityStatus.innerHTML = '<span style="color: #ff9800;">⚠ Manual override</span>';
                } else if (calculatedValue != null) {
                    capacityStatus.innerHTML = '<span style="color: #4caf50;">✓ Using calculated value</span>';
                } else {
                    capacityStatus.innerHTML = '<span style="color: #999;">No calculated value available</span>';
                }
            }
            if (capacityRevertBtn) {
                capacityRevertBtn.style.display = hasOverride && calculatedValue != null ? 'inline-block' : 'none';
            }
        }

        // Set throughput - show calculated value, indicate if overridden
        const throughputInput = document.getElementById('throughput-override');
        const throughputLabel = document.getElementById('throughput-calculated-label');
        const throughputStatus = document.getElementById('throughput-status');
        const throughputRevertBtn = document.getElementById('throughput-revert-btn');
        
        if (throughputInput) {
            const hasOverride = user.TargetThroughputMiBps != null;
            const calculatedValue = this.calculatedThroughputMiBps;
            
            if (hasOverride) {
                // Show override value
                throughputInput.value = user.TargetThroughputMiBps;
            } else if (calculatedValue != null) {
                // Show calculated value (round to 1 decimal)
                throughputInput.value = Math.round(calculatedValue * 10) / 10;
            } else {
                throughputInput.value = '';
            }
            
            // Update label and status
            if (throughputLabel) {
                throughputLabel.textContent = calculatedValue != null ? `(calculated: ${(Math.round(calculatedValue * 10) / 10)} MiB/s)` : '';
            }
            if (throughputStatus) {
                if (hasOverride && calculatedValue != null) {
                    throughputStatus.innerHTML = '<span style="color: #ff9800;">⚠ Manual override</span>';
                } else if (calculatedValue != null) {
                    throughputStatus.innerHTML = '<span style="color: #4caf50;">✓ Using calculated value</span>';
                } else {
                    throughputStatus.innerHTML = '<span style="color: #999;">No calculated value available</span>';
                }
            }
            if (throughputRevertBtn) {
                throughputRevertBtn.style.display = hasOverride && calculatedValue != null ? 'inline-block' : 'none';
            }
        }

        // Set notes
        const notesInput = document.getElementById('notes-input');
        if (notesInput) {
            notesInput.value = user.Notes || '';
        }
    },

    updateCapacityOverrideState() {
        const capacityInput = document.getElementById('capacity-override');
        const capacityStatus = document.getElementById('capacity-status');
        const capacityRevertBtn = document.getElementById('capacity-revert-btn');
        
        if (!capacityInput) return;
        
        const currentValue = capacityInput.value ? parseFloat(capacityInput.value) : null;
        const calculatedValue = this.calculatedCapacityGiB;
        const isOverridden = currentValue != null && calculatedValue != null && 
                            Math.ceil(currentValue) !== Math.ceil(calculatedValue);
        
        if (capacityStatus) {
            if (isOverridden) {
                capacityStatus.innerHTML = '<span style="color: #ff9800;">⚠ Manual override</span>';
            } else if (calculatedValue != null) {
                capacityStatus.innerHTML = '<span style="color: #4caf50;">✓ Using calculated value</span>';
            }
        }
        if (capacityRevertBtn) {
            capacityRevertBtn.style.display = isOverridden ? 'inline-block' : 'none';
        }
    },

    updateThroughputOverrideState() {
        const throughputInput = document.getElementById('throughput-override');
        const throughputStatus = document.getElementById('throughput-status');
        const throughputRevertBtn = document.getElementById('throughput-revert-btn');
        
        if (!throughputInput) return;
        
        const currentValue = throughputInput.value ? parseFloat(throughputInput.value) : null;
        const calculatedValue = this.calculatedThroughputMiBps;
        const calcRounded = calculatedValue != null ? Math.round(calculatedValue * 10) / 10 : null;
        const isOverridden = currentValue != null && calcRounded != null && 
                            Math.abs(currentValue - calcRounded) > 0.05;
        
        if (throughputStatus) {
            if (isOverridden) {
                throughputStatus.innerHTML = '<span style="color: #ff9800;">⚠ Manual override</span>';
            } else if (calculatedValue != null) {
                throughputStatus.innerHTML = '<span style="color: #4caf50;">✓ Using calculated value</span>';
            }
        }
        if (throughputRevertBtn) {
            throughputRevertBtn.style.display = isOverridden ? 'inline-block' : 'none';
        }
    },

    revertCapacity() {
        const capacityInput = document.getElementById('capacity-override');
        if (capacityInput && this.calculatedCapacityGiB != null) {
            this.saveCurrentState();
            capacityInput.value = Math.ceil(this.calculatedCapacityGiB);
            this.updateCapacityOverrideState();
            this.scheduleAutoSave();
            Toast.info('Capacity reverted to calculated value');
        }
    },

    revertThroughput() {
        const throughputInput = document.getElementById('throughput-override');
        if (throughputInput && this.calculatedThroughputMiBps != null) {
            this.saveCurrentState();
            throughputInput.value = Math.round(this.calculatedThroughputMiBps * 10) / 10;
            this.updateThroughputOverrideState();
            this.scheduleAutoSave();
            Toast.info('Throughput reverted to calculated value');
        }
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
        this.scheduleAutoSave();
        Toast.success('Volume approved for migration');
    },

    saveCurrentState() {
        const state = {
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
        document.getElementById('status-select').value = state.status || 'Candidate';
        document.getElementById('capacity-override').value = state.capacity || '';
        document.getElementById('throughput-override').value = state.throughput || '';
        document.getElementById('notes-input').value = state.notes || '';
        
        // Update override state indicators
        this.updateCapacityOverrideState();
        this.updateThroughputOverrideState();
        
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
        const status = document.getElementById('status-select')?.value;
        const capacity = document.getElementById('capacity-override')?.value;
        const throughput = document.getElementById('throughput-override')?.value;
        const notes = document.getElementById('notes-input')?.value;

        const updates = {
            MigrationStatus: status || 'Candidate',
            Notes: notes || null
        };

        // Only save as override if value differs from calculated
        // If value matches calculated, send null to clear the override
        if (capacity) {
            const capacityVal = parseFloat(capacity);
            const calcCapacity = this.calculatedCapacityGiB;
            if (calcCapacity != null && Math.ceil(capacityVal) === Math.ceil(calcCapacity)) {
                updates.TargetCapacityGiB = null; // Clear override
            } else {
                updates.TargetCapacityGiB = capacityVal;
            }
        } else {
            updates.TargetCapacityGiB = null;
        }

        if (throughput) {
            const throughputVal = parseFloat(throughput);
            const calcThroughput = this.calculatedThroughputMiBps;
            const calcRounded = calcThroughput != null ? Math.round(calcThroughput * 10) / 10 : null;
            if (calcRounded != null && Math.abs(throughputVal - calcRounded) < 0.05) {
                updates.TargetThroughputMiBps = null; // Clear override
            } else {
                updates.TargetThroughputMiBps = throughputVal;
            }
        } else {
            updates.TargetThroughputMiBps = null;
        }

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
    },
    
    // Cool Data Assumptions Functions
    async loadCoolAssumptions() {
        const vol = this.currentData?.VolumeData;
        if (!vol) return;
        
        // Only show for ANF volumes with cool access
        const isAnf = this.currentData.VolumeType === 'ANF';
        const hasCoolAccess = vol.CoolAccessEnabled === true;
        
        const section = document.getElementById('cool-assumptions-section');
        if (!isAnf || !hasCoolAccess) {
            section.style.display = 'none';
            return;
        }
        
        section.style.display = 'block';
        
        // Load assumptions for this volume
        try {
            const encodedVolumeId = encodeURIComponent(this.volumeId);
            const response = await fetch(`${API_BASE_URL}/cool-assumptions/volume/${this.jobId}/${encodedVolumeId}`, {
                headers: authManager.isSignedIn() ? {
                    'Authorization': `Bearer ${await authManager.getAccessToken()}`
                } : {}
            });
            
            if (response.ok) {
                const data = await response.json();
                document.getElementById('volume-cool-data-percentage').value = data.coolDataPercentage || data.CoolDataPercentage || '';
                document.getElementById('volume-cool-retrieval-percentage').value = data.coolDataRetrievalPercentage || data.CoolDataRetrievalPercentage || '';
                
                // Show status
                const statusDiv = document.getElementById('volume-assumptions-status');
                const source = data.source || data.Source || 'Global';
                const hasMetrics = this.currentData?.CostSummary?.CoolDataAssumptionsUsed?.HasMetrics;
                
                if (hasMetrics) {
                    statusDiv.innerHTML = '✓ Using actual metrics from monitoring';
                    statusDiv.style.background = '#e8f5e9';
                    statusDiv.style.color = '#2e7d32';
                } else if (source === 'Volume') {
                    statusDiv.innerHTML = '✓ Volume-specific override active';
                    statusDiv.style.background = '#fff3e0';
                    statusDiv.style.color = '#ef6c00';
                } else if (source === 'Job') {
                    statusDiv.innerHTML = `Using job-wide assumptions`;
                    statusDiv.style.background = '#e3f2fd';
                    statusDiv.style.color = '#1565c0';
                } else {
                    statusDiv.innerHTML = `Using global defaults`;
                    statusDiv.style.background = '#f5f5f5';
                    statusDiv.style.color = '#666';
                }
            }
        } catch (error) {
            console.error('Error loading volume assumptions:', error);
        }
    },
    
    async saveVolumeCoolAssumptions() {
        const coolDataPercentage = parseFloat(document.getElementById('volume-cool-data-percentage').value);
        const coolRetrievalPercentage = parseFloat(document.getElementById('volume-cool-retrieval-percentage').value);
        
        if (isNaN(coolDataPercentage) || coolDataPercentage < 0 || coolDataPercentage > 100) {
            Toast.error('Cool data percentage must be between 0 and 100');
            return;
        }
        
        if (isNaN(coolRetrievalPercentage) || coolRetrievalPercentage < 0 || coolRetrievalPercentage > 100) {
            Toast.error('Cool data retrieval percentage must be between 0 and 100');
            return;
        }
        
        try {
            const encodedVolumeId = encodeURIComponent(this.volumeId);
            const response = await fetch(`${API_BASE_URL}/cool-assumptions/volume/${this.jobId}/${encodedVolumeId}`, {
                method: 'PUT',
                headers: {
                    'Content-Type': 'application/json',
                    ...(authManager.isSignedIn() ? {
                        'Authorization': `Bearer ${await authManager.getAccessToken()}`
                    } : {})
                },
                body: JSON.stringify({
                    coolDataPercentage,
                    coolDataRetrievalPercentage
                })
            });
            
            if (!response.ok) {
                const error = await response.json();
                throw new Error(error.error || 'Failed to save volume assumptions');
            }
            
            Toast.success('Volume cool assumptions saved and cost recalculated');
            
            // Reload volume to show updated cost
            await this.loadVolume();
        } catch (error) {
            console.error('Error saving volume assumptions:', error);
            Toast.error(error.message || 'Failed to save volume assumptions');
        }
    },
    
    async clearVolumeCoolAssumptions() {
        if (!confirm('Clear volume-specific cool assumptions? This will revert to job or global defaults and recalculate cost.')) {
            return;
        }
        
        try {
            const encodedVolumeId = encodeURIComponent(this.volumeId);
            const response = await fetch(`${API_BASE_URL}/cool-assumptions/volume/${this.jobId}/${encodedVolumeId}`, {
                method: 'DELETE',
                headers: authManager.isSignedIn() ? {
                    'Authorization': `Bearer ${await authManager.getAccessToken()}`
                } : {}
            });
            
            if (!response.ok) {
                const error = await response.json();
                throw new Error(error.error || 'Failed to clear volume assumptions');
            }
            
            Toast.success('Volume overrides cleared and cost recalculated');
            
            // Reload volume to show updated cost
            await this.loadVolume();
        } catch (error) {
            console.error('Error clearing volume assumptions:', error);
            Toast.error(error.message || 'Failed to clear volume assumptions');
        }
    },
    
    async recalculateHypothetical() {
        const coolEnabled = document.getElementById('volume-hypothetical-cool-enabled').checked;
        const coolPercentage = parseFloat(document.getElementById('volume-hypothetical-cool-percentage').value) || 80;
        const retrievalPercentage = parseFloat(document.getElementById('volume-hypothetical-retrieval-percentage').value) || 15;
        
        if (coolPercentage < 0 || coolPercentage > 100 || retrievalPercentage < 0 || retrievalPercentage > 100) {
            Toast.error('Percentages must be between 0 and 100');
            return;
        }
        
        try {
            Toast.info('Calculating hypothetical ANF cost...');
            
            const encodedVolumeId = encodeURIComponent(this.volumeId);
            const requestBody = {
                VolumeId: this.volumeId,
                CoolAccessEnabled: coolEnabled
            };
            
            if (coolEnabled) {
                requestBody.CoolDataPercentage = coolPercentage;
                requestBody.CoolDataRetrievalPercentage = retrievalPercentage;
            }
            
            const response = await fetch(`${API_BASE_URL}/hypothetical-cost/${this.jobId}/${encodedVolumeId}`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    ...(authManager.isSignedIn() ? {
                        'Authorization': `Bearer ${await authManager.getAccessToken()}`
                    } : {})
                },
                body: JSON.stringify(requestBody)
            });
            
            if (!response.ok) {
                const error = await response.json();
                throw new Error(error.error || 'Failed to calculate hypothetical cost');
            }
            
            const result = await response.json();
            
            // Update the hypothetical cost display
            const totalCostEl = document.getElementById('hypothetical-total-cost');
            const breakdownEl = document.getElementById('hypothetical-cost-breakdown');
            
            if (totalCostEl) {
                const totalCost = result.TotalMonthlyCost || result.totalMonthlyCost || 0;
                totalCostEl.textContent = `$${totalCost.toFixed(2)}`;
            }
            
            if (breakdownEl && result.CostBreakdown) {
                breakdownEl.innerHTML = Object.entries(result.CostBreakdown)
                    .map(([key, value]) => `<div>${this.escapeHtml(key)}: <strong>$${value.toFixed(2)}</strong></div>`)
                    .join('');
            }
            
            Toast.success('Hypothetical cost calculated successfully');
        } catch (error) {
            console.error('Error calculating hypothetical cost:', error);
            Toast.error(error.message || 'Failed to calculate hypothetical cost');
        }
    }
};

document.addEventListener('DOMContentLoaded', () => volumeDetailPage.init());
