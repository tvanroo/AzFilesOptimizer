const volumeAnalysis = {
    volumes: [],
    selectedVolumes: new Set(),
    currentPage: 1,
    pageSize: 50,
    totalCount: 0,
    currentJobId: null,
    currentAnalysisJobId: null,
    logPollInterval: null,

    init() {
        this.loadJobs();
        this.loadWorkloadProfiles();
        document.getElementById('confidenceSlider').addEventListener('input', (e) => {
            document.getElementById('confidenceValue').textContent = e.target.value + '%';
        });
    },

    async loadJobs() {
    try {
        const response = await fetch(`${API_BASE_URL}/jobs`);
        if (!response.ok) throw new Error('Failed to load jobs');
            const jobs = await response.json();
            
            const selector = document.getElementById('jobSelector');
            selector.innerHTML = '<option value="">Select a job...</option>' +
                jobs.map(j => `<option value="${j.JobId}">${j.JobId} - ${new Date(j.CreatedAt).toLocaleDateString()}</option>`).join('');
        } catch (error) {
            console.error('Error loading jobs:', error);
        }
    },

    async loadWorkloadProfiles() {
        try {
            const response = await fetch(`${API_BASE_URL}/workload-profiles`);
            if (!response.ok) throw new Error('Failed to load workload profiles');
            const profiles = await response.json();
            
            const filterSelect = document.getElementById('workloadFilter');
            const bulkSelect = document.getElementById('bulkWorkload');
            
            const options = profiles.map(p => `<option value="${p.ProfileId}">${this.escapeHtml(p.Name)}</option>`).join('');
            filterSelect.innerHTML += options;
            bulkSelect.innerHTML += options;
        } catch (error) {
            console.error('Error loading profiles:', error);
        }
    },

    async loadVolumes() {
        const jobId = document.getElementById('jobSelector').value;
        if (!jobId) return;

        this.currentJobId = jobId;
        await this.applyFilters();
    },

    async applyFilters() {
        if (!this.currentJobId) return;

        const workloadFilter = document.getElementById('workloadFilter').value;
        const statusFilter = document.getElementById('statusFilter').value;
        const confidenceMin = parseInt(document.getElementById('confidenceSlider').value) / 100;

        try {
            let url = `${API_BASE_URL}/discovery/${this.currentJobId}/volumes?page=${this.currentPage}&pageSize=${this.pageSize}`;
            if (workloadFilter) url += `&workloadFilter=${workloadFilter}`;
            if (statusFilter) url += `&statusFilter=${statusFilter}`;
            if (confidenceMin > 0) url += `&confidenceMin=${confidenceMin}`;

            const response = await fetch(url);
            if (!response.ok) throw new Error('Failed to load volumes');
            const data = await response.json();
            
            this.volumes = data.Volumes;
            this.totalCount = data.TotalCount;
            this.renderGrid();
            this.renderPagination();
        } catch (error) {
            console.error('Error loading volumes:', error);
            alert('Error loading volumes');
        }
    },

    renderGrid() {
        const grid = document.getElementById('volumesGrid');
        
        if (this.volumes.length === 0) {
            grid.innerHTML = '<div style="padding: 60px; text-align: center; color: #999;">No volumes found</div>';
            return;
        }

        grid.innerHTML = this.volumes.map(v => {
            const isSelected = this.selectedVolumes.has(v.VolumeId);
            const statusIcon = this.getStatusIcon(v.UserAnnotations?.MigrationStatus);
            const confidence = (v.AiAnalysis?.ConfidenceScore || 0) * 100;
            
            // Get volume name based on type
            const volumeName = this.getVolumeName(v);
            const storageAccount = this.getStorageAccountName(v);
            const quota = this.getQuota(v);

            return `
                <div class="grid-row ${isSelected ? 'selected' : ''}" data-id="${v.VolumeId}">
                    <div><input type="checkbox" ${isSelected ? 'checked' : ''} onchange="volumeAnalysis.toggleSelect('${v.VolumeId}')"></div>
                    <div onclick="volumeAnalysis.showDetail('${v.VolumeId}')">${this.escapeHtml(volumeName)}</div>
                    <div onclick="volumeAnalysis.showDetail('${v.VolumeId}')">${this.escapeHtml(storageAccount)}</div>
                    <div onclick="volumeAnalysis.showDetail('${v.VolumeId}')">${quota}</div>
                    <div onclick="volumeAnalysis.showDetail('${v.VolumeId}')">
                        ${v.AiAnalysis?.SuggestedWorkloadName || '-'}
                    </div>
                    <div onclick="volumeAnalysis.showDetail('${v.VolumeId}')">
                        ${v.UserAnnotations?.ConfirmedWorkloadName || '-'}
                    </div>
                    <div onclick="volumeAnalysis.showDetail('${v.VolumeId}')">
                        <div class="confidence-bar">
                            <div class="confidence-fill" style="width: ${confidence}%"></div>
                        </div>
                        <small>${confidence.toFixed(0)}%</small>
                    </div>
                    <div onclick="volumeAnalysis.showDetail('${v.VolumeId}')" class="status-icon">${statusIcon}</div>
                </div>
            `;
        }).join('');
    },
    
    getVolumeName(volume) {
        switch (volume.VolumeType) {
            case 'AzureFiles':
                return volume.VolumeData?.ShareName || 'Unknown';
            case 'ANF':
                return volume.VolumeData?.VolumeName || 'Unknown';
            case 'ManagedDisk':
                return volume.VolumeData?.DiskName || 'Unknown';
            default:
                return 'Unknown';
        }
    },
    
    getStorageAccountName(volume) {
        switch (volume.VolumeType) {
            case 'AzureFiles':
                return volume.VolumeData?.StorageAccountName || 'N/A';
            case 'ANF':
                return volume.VolumeData?.NetAppAccountName || 'N/A';
            case 'ManagedDisk':
                return volume.VolumeData?.ResourceGroup || 'N/A';
            default:
                return 'N/A';
        }
    },
    
    getQuota(volume) {
        switch (volume.VolumeType) {
            case 'AzureFiles':
                return volume.VolumeData?.ShareQuotaGiB || 0;
            case 'ANF':
                return volume.VolumeData?.UsedCapacityGiB || 0;
            case 'ManagedDisk':
                return volume.VolumeData?.DiskSizeGb || 0;
            default:
                return 0;
        }
    },

    getStatusIcon(status) {
        const icons = {
            'Candidate': '📋',
            'Excluded': '⊘',
            'UnderReview': '🔄',
            'Approved': '✓'
        };
        return icons[status] || '⚠';
    },

    renderPagination() {
        const totalPages = Math.ceil(this.totalCount / this.pageSize);
        const pagination = document.getElementById('pagination');
        
        if (totalPages <= 1) {
            pagination.innerHTML = '';
            return;
        }

        let html = '';
        if (this.currentPage > 1) {
            html += `<button class="page-btn" onclick="volumeAnalysis.goToPage(${this.currentPage - 1})">Previous</button>`;
        }

        for (let i = Math.max(1, this.currentPage - 2); i <= Math.min(totalPages, this.currentPage + 2); i++) {
            html += `<button class="page-btn ${i === this.currentPage ? 'active' : ''}" onclick="volumeAnalysis.goToPage(${i})">${i}</button>`;
        }

        if (this.currentPage < totalPages) {
            html += `<button class="page-btn" onclick="volumeAnalysis.goToPage(${this.currentPage + 1})">Next</button>`;
        }

        pagination.innerHTML = html;
    },

    goToPage(page) {
        this.currentPage = page;
        this.applyFilters();
    },

    toggleSelect(volumeId) {
        if (this.selectedVolumes.has(volumeId)) {
            this.selectedVolumes.delete(volumeId);
        } else {
            this.selectedVolumes.add(volumeId);
        }
        this.updateBulkToolbar();
        this.renderGrid();
    },

    toggleSelectAll() {
        const checked = document.getElementById('selectAll').checked;
        this.selectedVolumes.clear();
        if (checked) {
            this.volumes.forEach(v => this.selectedVolumes.add(v.VolumeId));
        }
        this.updateBulkToolbar();
        this.renderGrid();
    },

    clearSelection() {
        this.selectedVolumes.clear();
        document.getElementById('selectAll').checked = false;
        this.updateBulkToolbar();
        this.renderGrid();
    },

    updateBulkToolbar() {
        const toolbar = document.getElementById('bulkToolbar');
        const count = this.selectedVolumes.size;
        
        if (count > 0) {
            toolbar.classList.add('show');
            document.getElementById('selectedCount').textContent = `${count} selected`;
        } else {
            toolbar.classList.remove('show');
        }
    },

    async showDetail(volumeId) {
        const volume = this.volumes.find(v => v.VolumeId === volumeId);
        if (!volume) return;

        const panel = document.getElementById('detailPanel');
        const content = document.getElementById('detailContent');
        
        const volumeName = this.getVolumeName(volume);
        document.getElementById('detailTitle').textContent = volumeName;

        // Generate type-specific properties
        const propertiesHtml = this.generateVolumeProperties(volume);

        content.innerHTML = `
            <div class="tabs">
                <div class="tab active" onclick="volumeAnalysis.switchTab(0)">Overview</div>
                <div class="tab" onclick="volumeAnalysis.switchTab(1)">AI Analysis</div>
                <div class="tab" onclick="volumeAnalysis.switchTab(2)">User Annotations</div>
            </div>

            <div class="tab-content active">
                <h3>Volume Properties</h3>
                <div class="property-grid">
                    ${propertiesHtml}
                </div>
            </div>

            <div class="tab-content">
                <h3>AI Analysis Results</h3>
                ${volume.AiAnalysis ? `
                    <div style="margin-bottom: 20px;">
                        <strong>Suggested Workload:</strong> ${volume.AiAnalysis.SuggestedWorkloadName || 'None'}<br>
                        <strong>Confidence:</strong> ${(volume.AiAnalysis.ConfidenceScore * 100).toFixed(0)}%<br>
                        <strong>Analyzed:</strong> ${new Date(volume.AiAnalysis.LastAnalyzed).toLocaleString()}
                    </div>
                    
                    ${volume.AiAnalysis.AppliedPrompts?.length > 0 ? `
                        <h4>Applied Prompts</h4>
                        ${volume.AiAnalysis.AppliedPrompts.map(p => `
                            <div style="padding: 10px; border: 1px solid #ddd; border-radius: 4px; margin-bottom: 10px;">
                                <strong>${this.escapeHtml(p.PromptName)}</strong>
                                ${p.StoppedProcessing ? '<span class="badge badge-warning">Stopped Here</span>' : ''}
                                <div style="margin-top: 5px; font-size: 14px;">${this.escapeHtml(p.Result)}</div>
                                ${p.Evidence?.length > 0 ? `
                                    <div style="margin-top: 10px;">
                                        <strong>Evidence:</strong>
                                        <ul style="margin: 5px 0;">
                                            ${p.Evidence.map(e => `<li>${this.escapeHtml(e)}</li>`).join('')}
                                        </ul>
                                    </div>
                                ` : ''}
                            </div>
                        `).join('')}
                    ` : ''}
                ` : '<p>No AI analysis performed yet</p>'}
            </div>

            <div class="tab-content">
                <h3>User Annotations</h3>
                <div class="form-group">
                    <label>Confirmed Workload</label>
                    <select id="userWorkload">
                        <option value="">Not confirmed</option>
                    </select>
                </div>
                
                <div class="form-group">
                    <label>Migration Status</label>
                    <select id="userStatus">
                        <option value="Candidate">Candidate</option>
                        <option value="Excluded">Excluded</option>
                        <option value="UnderReview">Under Review</option>
                        <option value="Approved">Approved</option>
                    </select>
                </div>
                
                <div class="form-group">
                    <label>Custom Tags (comma-separated)</label>
                    <input type="text" id="userTags" value="${volume.UserAnnotations?.CustomTags?.join(', ') || ''}">
                </div>
                
                <div class="form-group">
                    <label>Notes</label>
                    <textarea id="userNotes" rows="4">${volume.UserAnnotations?.Notes || ''}</textarea>
                </div>
                
                <button class="btn btn-primary" onclick="volumeAnalysis.saveAnnotations('${volumeId}')">Save Annotations</button>
            </div>
        `;

        // Load workloads and set current value
        const response = await fetch(`${API_BASE_URL}/workload-profiles`);
        if (!response.ok) throw new Error('Failed to load workload profiles');
        const profiles = await response.json();
        const workloadSelect = document.getElementById('userWorkload');
        workloadSelect.innerHTML = '<option value="">Not confirmed</option>' +
            profiles.map(p => `<option value="${p.ProfileId}" ${volume.UserAnnotations?.ConfirmedWorkloadId === p.ProfileId ? 'selected' : ''}>${this.escapeHtml(p.Name)}</option>`).join('');
        
        // Set status
        if (volume.UserAnnotations?.MigrationStatus) {
            document.getElementById('userStatus').value = volume.UserAnnotations.MigrationStatus;
        }

        panel.classList.add('show');
    },
    
    generateVolumeProperties(volume) {
        switch (volume.VolumeType) {
            case 'AzureFiles':
                return this.generateAzureFilesProperties(volume.VolumeData);
            case 'ANF':
                return this.generateAnfProperties(volume.VolumeData);
            case 'ManagedDisk':
                return this.generateManagedDiskProperties(volume.VolumeData);
            default:
                return '<div>Unknown volume type</div>';
        }
    },
    
    generateAzureFilesProperties(data) {
        return `
            <div class="property-label">Name</div>
            <div>${this.escapeHtml(data?.ShareName || 'N/A')}</div>
            
            <div class="property-label">Storage Account</div>
            <div>${this.escapeHtml(data?.StorageAccountName || 'N/A')}</div>
            
            <div class="property-label">Resource Group</div>
            <div>${this.escapeHtml(data?.ResourceGroup || 'N/A')}</div>
            
            <div class="property-label">Size (Quota)</div>
            <div>${data?.ShareQuotaGiB || 0} GiB</div>
            
            <div class="property-label">Used Capacity</div>
            <div>${this.formatBytes(data?.ShareUsageBytes || 0)}</div>
            
            <div class="property-label">Access Tier</div>
            <div>${data?.AccessTier || 'N/A'}</div>
            
            <div class="property-label">SKU</div>
            <div>${data?.StorageAccountSku || 'N/A'}</div>
            
            <div class="property-label">Location</div>
            <div>${data?.Location || 'N/A'}</div>
            
            <div class="property-label">Protocols</div>
            <div>${data?.EnabledProtocols?.join(', ') || 'N/A'}</div>
            
            <div class="property-label">Tags</div>
            <div>${this.formatTags(data?.Tags) || 'None'}</div>
        `;
    },
    
    generateAnfProperties(data) {
        return `
            <div class="property-label">Volume Name</div>
            <div>${this.escapeHtml(data?.VolumeName || 'N/A')}</div>
            
            <div class="property-label">NetApp Account</div>
            <div>${this.escapeHtml(data?.NetAppAccountName || 'N/A')}</div>
            
            <div class="property-label">Capacity Pool</div>
            <div>${this.escapeHtml(data?.CapacityPoolName || 'N/A')}</div>
            
            <div class="property-label">Resource Group</div>
            <div>${this.escapeHtml(data?.ResourceGroup || 'N/A')}</div>
            
            <div class="property-label">Provisioned Size</div>
            <div>${data?.ProvisionedThroughputMiBps || 0} GiB</div>
            
            <div class="property-label">Used Capacity</div>
            <div>${data?.UsedCapacityGiB || 0} GiB</div>
            
            <div class="property-label">Service Level</div>
            <div>${data?.ServiceLevel || 'N/A'}</div>
            
            <div class="property-label">Location</div>
            <div>${data?.Location || 'N/A'}</div>
            
            <div class="property-label">Protocols</div>
            <div>${data?.EnabledProtocols?.join(', ') || 'N/A'}</div>
            
            <div class="property-label">Resource ID</div>
            <div style="word-break: break-all; font-size: 12px;">${this.escapeHtml(data?.ResourceId || 'N/A')}</div>
        `;
    },
    
    generateManagedDiskProperties(data) {
        return `
            <div class="property-label">Disk Name</div>
            <div>${this.escapeHtml(data?.DiskName || 'N/A')}</div>
            
            <div class="property-label">Resource Group</div>
            <div>${this.escapeHtml(data?.ResourceGroup || 'N/A')}</div>
            
            <div class="property-label">Size</div>
            <div>${data?.DiskSizeGb || 0} GiB</div>
            
            <div class="property-label">Type</div>
            <div>${data?.ManagedBy ? 'Data Disk (Attached)' : 'Data Disk (Unattached)'}</div>
            
            <div class="property-label">SKU</div>
            <div>${data?.Sku || 'N/A'}</div>
            
            <div class="property-label">Location</div>
            <div>${data?.Location || 'N/A'}</div>
            
            <div class="property-label">Attached VM</div>
            <div>${data?.ManagedBy ? this.escapeHtml(data.ManagedBy.split('/').pop() || 'N/A') : 'Not attached'}</div>
            
            <div class="property-label">Creation Time</div>
            <div>${data?.TimeCreated ? new Date(data.TimeCreated).toLocaleString() : 'N/A'}</div>
            
            <div class="property-label">Encryption</div>
            <div>${data?.EncryptionType || 'N/A'}</div>
            
            <div class="property-label">Resource ID</div>
            <div style="word-break: break-all; font-size: 12px;">${this.escapeHtml(data?.ResourceId || 'N/A')}</div>
        `;
    },

    closeDetail() {
        document.getElementById('detailPanel').classList.remove('show');
    },

    switchTab(index) {
        const tabs = document.querySelectorAll('.tab');
        const contents = document.querySelectorAll('.tab-content');
        
        tabs.forEach((tab, i) => {
            tab.classList.toggle('active', i === index);
        });
        
        contents.forEach((content, i) => {
            content.classList.toggle('active', i === index);
        });
    },

    async saveAnnotations(volumeId) {
        const annotations = {
            ConfirmedWorkloadId: document.getElementById('userWorkload').value || null,
            MigrationStatus: document.getElementById('userStatus').value,
            CustomTags: document.getElementById('userTags').value.split(',').map(t => t.trim()).filter(t => t),
            Notes: document.getElementById('userNotes').value
        };

        try {
            const response = await fetch(`${API_BASE_URL}/discovery/${this.currentJobId}/volumes/${volumeId}/annotations`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(annotations)
            });

            if (!response.ok) throw new Error('Failed to save annotations');
            
            alert('Annotations saved successfully');
            await this.applyFilters();
        } catch (error) {
            console.error('Error saving annotations:', error);
            alert('Error saving annotations');
        }
    },

    async runAnalysis() {
        if (!this.currentJobId) {
            alert('Please select a discovery job first');
            return;
        }

        if (!confirm('This will analyze all volumes in the selected job. Continue?')) return;

        try {
            const response = await fetch(`${API_BASE_URL}/discovery/${this.currentJobId}/analyze`, {
                method: 'POST'
            });

            if (!response.ok) throw new Error('Failed to start analysis');
            
            const result = await response.json();
            this.currentAnalysisJobId = result.AnalysisJobId;
            
            // Show log modal and start polling
            this.showLogModal();
            this.pollAnalysisStatus(result.AnalysisJobId);
        } catch (error) {
            console.error('Error starting analysis:', error);
            alert('Error starting analysis');
        }
    },

    showLogModal() {
        document.getElementById('logModal').classList.add('show');
        document.getElementById('logBody').innerHTML = '<div style="color: #888;">Starting analysis...</div>';
        document.getElementById('logProgress').style.width = '0%';
        document.getElementById('logStatus').textContent = 'Starting analysis...';
        
        // Start polling for logs
        this.startLogPolling();
    },

    closeLogModal() {
        document.getElementById('logModal').classList.remove('show');
        this.stopLogPolling();
    },

    startLogPolling() {
        this.stopLogPolling(); // Clear any existing interval
        this.logPollInterval = setInterval(() => this.fetchLogs(), 2000);
        this.fetchLogs(); // Fetch immediately
    },

    stopLogPolling() {
        if (this.logPollInterval) {
            clearInterval(this.logPollInterval);
            this.logPollInterval = null;
        }
    },

    async fetchLogs() {
        if (!this.currentAnalysisJobId) return;

        try {
            const response = await fetch(`${API_BASE_URL}/analysis/${this.currentAnalysisJobId}/logs`);
            if (!response.ok) return;
            
            const logs = await response.json();
            this.renderLogs(logs);
        } catch (error) {
            console.error('Error fetching logs:', error);
        }
    },

    renderLogs(logs) {
        if (!logs || logs.length === 0) return;

        const logBody = document.getElementById('logBody');
        logBody.innerHTML = logs.map(log => {
            const timestamp = new Date(log.Timestamp).toLocaleTimeString();
            return `<div class="log-entry ${log.Level}">
                <span class="log-timestamp">${timestamp}</span>
                <span>${this.escapeHtml(log.Message)}</span>
            </div>`;
        }).join('');
        
        // Auto-scroll to bottom
        logBody.scrollTop = logBody.scrollHeight;
    },

    async pollAnalysisStatus(analysisJobId) {
        const checkStatus = async () => {
            try {
                const response = await fetch(`${API_BASE_URL}/analysis/${analysisJobId}/status`);
                if (!response.ok) throw new Error('Failed to check status');
                const status = await response.json();
                
                // Update progress
                const progress = status.ProgressPercentage || 0;
                document.getElementById('logProgress').style.width = progress + '%';
                document.getElementById('logStatus').textContent = 
                    `${status.Status} - ${status.ProcessedVolumes}/${status.TotalVolumes} volumes (${progress}%)`;
                
                if (status.Status === 'Completed') {
                    this.stopLogPolling();
                    setTimeout(() => {
                        alert('Analysis completed successfully!');
                        this.closeLogModal();
                        this.applyFilters();
                    }, 2000); // Give time to see final logs
                } else if (status.Status === 'Failed') {
                    this.stopLogPolling();
                    alert('Analysis failed: ' + status.ErrorMessage);
                    this.closeLogModal();
                } else {
                    setTimeout(checkStatus, 3000); // Check again in 3 seconds
                }
            } catch (error) {
                console.error('Error checking status:', error);
            }
        };
        
        setTimeout(checkStatus, 3000);
    },

    async exportData(format) {
        if (!this.currentJobId) return;

        try {
            const response = await fetch(`${API_BASE_URL}/discovery/${this.currentJobId}/export?format=${format}`);
            if (!response.ok) throw new Error('Failed to export data');
            const data = await response.text();
            
            const blob = new Blob([data], { type: format === 'csv' ? 'text/csv' : 'application/json' });
            const url = window.URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = `volumes-${this.currentJobId}.${format}`;
            a.click();
        } catch (error) {
            console.error('Error exporting data:', error);
            alert('Error exporting data');
        }
    },

    async bulkApprove() {
        // Bulk approve AI suggestions
        // Implementation similar to bulkExclude
    },

    async bulkExclude() {
        if (this.selectedVolumes.size === 0) return;

        const volumeIds = Array.from(this.selectedVolumes);
        try {
            const response = await fetch(`${API_BASE_URL}/discovery/${this.currentJobId}/volumes/bulk-annotations`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    VolumeIds: volumeIds,
                    Annotations: { MigrationStatus: 'Excluded' }
                })
            });

            if (!response.ok) throw new Error('Failed to bulk update');
            
            alert('Volumes excluded successfully');
            this.clearSelection();
            await this.applyFilters();
        } catch (error) {
            console.error('Error bulk updating:', error);
            alert('Error bulk updating volumes');
        }
    },

    formatBytes(bytes) {
        if (!bytes) return '0 B';
        const k = 1024;
        const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
        const i = Math.floor(Math.log(bytes) / Math.log(k));
        return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
    },

    formatTags(tags) {
        if (!tags || Object.keys(tags).length === 0) return 'None';
        return Object.entries(tags).map(([k, v]) => `${k}: ${v}`).join(', ');
    },

    escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text || '';
        return div.innerHTML;
    }
};

document.addEventListener('DOMContentLoaded', () => {
    volumeAnalysis.init();
});
