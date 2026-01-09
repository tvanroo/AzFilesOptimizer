// Get job ID from URL
const urlParams = new URLSearchParams(window.location.search);
const JOB_ID = urlParams.get('id');

const jobDetail = {
    // Shared state
    jobId: JOB_ID,
    currentTab: 'overview',
    jobData: null,
    
    // Volume Analysis state
    volumes: [],
    selectedVolumes: new Set(),
    currentPage: 1,
    pageSize: 50,
    totalCount: 0,
    currentAnalysisJobId: null,
    logPollInterval: null,

    // Volume table view state (persisted across jobs)
    volumeViewStorageKey: 'azfo-volume-analysis-view',
    volumeColumns: [
        { key: 'select', label: '', sortable: false },
        { key: 'VolumeName', label: 'Volume Name', sortable: true },
        { key: 'StorageAccountName', label: 'Storage Account', sortable: true },
        { key: 'ResourceGroup', label: 'Resource Group', sortable: true },
        { key: 'SubscriptionId', label: 'Subscription', sortable: true },
        { key: 'Location', label: 'Location', sortable: true },
        { key: 'CapacityGiB', label: 'Capacity (GiB)', sortable: true },
        { key: 'UsedCapacity', label: 'Used Capacity', sortable: true },
        { key: 'AccessTier', label: 'Access Tier', sortable: true },
        { key: 'Protocols', label: 'Protocols', sortable: false },
        { key: 'StorageAccountSku', label: 'SKU', sortable: true },
        { key: 'AiWorkload', label: 'AI Workload', sortable: true },
        { key: 'AiConfidence', label: 'AI Confidence', sortable: true },
        { key: 'AiLastAnalyzed', label: 'AI Last Analyzed', sortable: true },
        { key: 'UserWorkload', label: 'User Workload', sortable: true },
        { key: 'MigrationStatus', label: 'Migration Status', sortable: true },
        { key: 'ReviewedBy', label: 'Reviewed By', sortable: true },
        { key: 'ReviewedAt', label: 'Reviewed At', sortable: true }
    ],
    defaultVisibleVolumeColumns: ['select','VolumeName','StorageAccountName','ResourceGroup','CapacityGiB','AiWorkload','UserWorkload','AiConfidence','MigrationStatus'],
    visibleVolumeColumns: null,
    volumeSortColumn: null,
    volumeSortDirection: 'asc',

    // Chat state
    conversationHistory: [],
    
    // Initialize
    async init() {
        if (!this.jobId) {
            alert('No job ID provided');
            window.location.href = 'jobs.html';
            return;
        }
        
        await authManager.initialize();
        const isAuthenticated = await authManager.requireAuth();
        if (!isAuthenticated) return;
        
        await this.loadJob();
        this.loadVolumeViewState();
        this.setupEventListeners();
    },
    
    setupEventListeners() {
        // Confidence slider
        const slider = document.getElementById('confidenceSlider');
        if (slider) {
            slider.addEventListener('input', (e) => {
                document.getElementById('confidenceValue').textContent = e.target.value + '%';
                this.applyFilters();
            });
        }
        
        // Chat input
        const chatInput = document.getElementById('chatInput');
        if (chatInput) {
            chatInput.addEventListener('input', () => {
                chatInput.style.height = 'auto';
                chatInput.style.height = Math.min(chatInput.scrollHeight, 120) + 'px';
            });
            chatInput.addEventListener('keypress', (e) => {
                if (e.key === 'Enter' && !e.shiftKey) {
                    e.preventDefault();
                    this.sendMessage();
                }
            });
        }
    },
    
    // Tab Management
    switchTab(tabName) {
        this.currentTab = tabName;
        
        // Update tab buttons
        document.querySelectorAll('.tab-btn').forEach(btn => btn.classList.remove('active'));
        event.target.classList.add('active');
        
        // Update tab content
        document.querySelectorAll('.tab-content').forEach(content => content.classList.remove('active'));
        document.getElementById(`tab-${tabName}`).classList.add('active');
        
        // Load tab-specific data
        if (tabName === 'analysis' && this.volumes.length === 0) {
            this.loadVolumes();
            this.loadWorkloadProfiles();
        } else if (tabName === 'chat') {
            this.initChat();
        }
    },
    
    // Job Loading
    async loadJob() {
        try {
            this.jobData = await apiClient.getJob(this.jobId);
            const displayJobId = this.jobData.RowKey || this.jobData.JobId;
            
            document.getElementById('job-id').textContent = `Job: ${displayJobId}`;
            document.getElementById('job-status-badge').innerHTML = this.getStatusBadge(this.jobData.Status);
            
            // Display overview
            this.renderOverview();
        } catch (error) {
            Toast.error('Failed to load job: ' + error.message);
        }
    },
    
    renderOverview() {
        const container = document.getElementById('job-details-container');
        const job = this.jobData;
        
        let duration = '-';
        if (job.StartedAt && job.CompletedAt) {
            const start = new Date(job.StartedAt);
            const end = new Date(job.CompletedAt);
            const minutes = Math.floor((end - start) / 60000);
            duration = `${minutes}m`;
        } else if (job.StartedAt) {
            duration = 'In progress';
        }
        
        container.innerHTML = `
            <div class="info-grid">
                <div class="info-item">
                    <label>Job ID</label>
                    <strong>${job.RowKey || job.JobId}</strong>
                </div>
                <div class="info-item">
                    <label>Status</label>
                    <strong>${this.getStatusText(job.Status)}</strong>
                </div>
                <div class="info-item">
                    <label>Created</label>
                    <strong>${new Date(job.CreatedAt).toLocaleString()}</strong>
                </div>
                <div class="info-item">
                    <label>Duration</label>
                    <strong>${duration}</strong>
                </div>
            </div>
            
            <h3 style="margin-top: 1.5rem;">Scope</h3>
            <div style="background: var(--surface); padding: 1rem; border-radius: 4px;">
                <div><strong>Subscription:</strong> ${job.SubscriptionId || '-'}</div>
                ${job.ResourceGroupNames && job.ResourceGroupNames.length > 0 ? 
                    `<div><strong>Resource Groups:</strong> ${job.ResourceGroupNames.join(', ')}</div>` : ''}
            </div>
            
            <h3 style="margin-top: 1.5rem;">Results</h3>
            <div class="info-grid">
                <div class="info-item">
                    <label>Azure Files Shares</label>
                    <strong>${job.AzureFilesSharesFound || 0}</strong>
                </div>
                <div class="info-item">
                    <label>ANF Volumes</label>
                    <strong>${job.AnfVolumesFound || 0}</strong>
                </div>
            </div>
        `;
    },
    
    // Volume Analysis Functions
    loadVolumeViewState() {
        try {
            const raw = localStorage.getItem(this.volumeViewStorageKey);
            if (!raw) {
                this.visibleVolumeColumns = [...this.defaultVisibleVolumeColumns];
                return;
            }
            const parsed = JSON.parse(raw);
            this.visibleVolumeColumns = parsed.visibleColumns || [...this.defaultVisibleVolumeColumns];
            this.volumeSortColumn = parsed.sortColumn || null;
            this.volumeSortDirection = parsed.sortDirection || 'asc';
            const workloadFilter = parsed.workloadFilter || '';
            const statusFilter = parsed.statusFilter || '';
            const confidenceMin = typeof parsed.confidenceMin === 'number' ? parsed.confidenceMin : 0;
            const searchTerm = parsed.searchTerm || '';

            const workloadSelect = document.getElementById('workloadFilter');
            const statusSelect = document.getElementById('statusFilter');
            const confidenceSlider = document.getElementById('confidenceSlider');
            const searchInput = document.getElementById('volumeSearchInput');

            if (workloadSelect) workloadSelect.value = workloadFilter;
            if (statusSelect) statusSelect.value = statusFilter;
            if (confidenceSlider) {
                confidenceSlider.value = Math.round(confidenceMin * 100);
                const label = document.getElementById('confidenceValue');
                if (label) label.textContent = `${confidenceSlider.value}%`;
            }
            if (searchInput) searchInput.value = searchTerm;
        } catch {
            this.visibleVolumeColumns = [...this.defaultVisibleVolumeColumns];
        }
    },

    saveVolumeViewState() {
        const workloadFilter = document.getElementById('workloadFilter')?.value || '';
        const statusFilter = document.getElementById('statusFilter')?.value || '';
        const confidenceMin = parseInt(document.getElementById('confidenceSlider')?.value || 0) / 100;
        const searchTerm = document.getElementById('volumeSearchInput')?.value || '';

        const payload = {
            visibleColumns: this.visibleVolumeColumns || this.defaultVisibleVolumeColumns,
            sortColumn: this.volumeSortColumn,
            sortDirection: this.volumeSortDirection,
            workloadFilter,
            statusFilter,
            confidenceMin,
            searchTerm
        };
        try {
            localStorage.setItem(this.volumeViewStorageKey, JSON.stringify(payload));
        } catch {
            // ignore storage errors
        }
    },

    async loadWorkloadProfiles() {
        try {
            const response = await fetch(`${API_BASE_URL}/workload-profiles`);
            if (!response.ok) return;
            
            const profiles = await response.json();
            const filterSelect = document.getElementById('workloadFilter');
            
            profiles.forEach(p => {
                const option = document.createElement('option');
                option.value = p.ProfileId;
                option.textContent = p.Name;
                filterSelect.appendChild(option);
            });
        } catch (error) {
            console.error('Error loading profiles:', error);
        }
    },
    
    async loadVolumes() {
        await this.applyFilters();
    },
    
    async applyFilters() {
        const workloadFilter = document.getElementById('workloadFilter')?.value || '';
        const statusFilter = document.getElementById('statusFilter')?.value || '';
        const confidenceMin = parseInt(document.getElementById('confidenceSlider')?.value || 0) / 100;

        // Persist filter portion of view state immediately
        this.saveVolumeViewState();
        
        try {
            let url = `${API_BASE_URL}/discovery/${this.jobId}/volumes?page=${this.currentPage}&pageSize=${this.pageSize}`;
            if (workloadFilter) url += `&workloadFilter=${encodeURIComponent(workloadFilter)}`;
            if (statusFilter) url += `&statusFilter=${encodeURIComponent(statusFilter)}`;
            if (confidenceMin > 0) url += `&confidenceMin=${confidenceMin}`;
            
            const response = await fetch(url);
            if (!response.ok) throw new Error('Failed to load volumes');
            
            const data = await response.json();
            this.volumes = data.Volumes || [];
            this.totalCount = data.TotalCount || 0;
            
            this.renderVolumeTable();
            this.renderVolumePagination();
        } catch (error) {
            console.error('Error loading volumes:', error);
            const tbody = document.getElementById('volumesTableBody');
            if (tbody) {
                tbody.innerHTML = '<tr><td colspan="10" style="padding: 20px; text-align: center;">Error loading volumes</td></tr>';
            }
        }
    },

    getVisibleVolumeColumns() {
        if (!this.visibleVolumeColumns || !Array.isArray(this.visibleVolumeColumns)) {
            this.visibleVolumeColumns = [...this.defaultVisibleVolumeColumns];
        }
        return this.visibleVolumeColumns;
    },

    toggleVolumeColumnSelector() {
        const list = document.getElementById('volumeColumnList');
        const header = document.querySelector('#volumeColumnSelector .column-selector-header strong');
        if (!list || !header) return;
        const isVisible = list.style.display !== 'none';
        list.style.display = isVisible ? 'none' : 'block';
        header.textContent = isVisible ? '▶ Show/Hide Columns' : '▼ Show/Hide Columns';
    },

    renderVolumeColumnSelector() {
        const list = document.getElementById('volumeColumnList');
        if (!list) return;
        const visible = new Set(this.getVisibleVolumeColumns());
        list.innerHTML = this.volumeColumns
            .filter(c => c.key !== 'select')
            .map(col => `
                <div class="column-checkbox">
                    <input type="checkbox" id="vol-col-${col.key}" value="${col.key}" ${visible.has(col.key) ? 'checked' : ''}
                        onchange="jobDetail.onToggleVolumeColumn('${col.key}')">
                    <label for="vol-col-${col.key}">${col.label}</label>
                </div>
            `).join('');
    },

    onToggleVolumeColumn(key) {
        const current = new Set(this.getVisibleVolumeColumns());
        if (current.has(key)) {
            current.delete(key);
        } else {
            current.add(key);
        }
        this.visibleVolumeColumns = Array.from(current);
        this.saveVolumeViewState();
        this.renderVolumeTable();
    },

    sortByVolumeColumn(key) {
        const colDef = this.volumeColumns.find(c => c.key === key);
        if (!colDef || !colDef.sortable) return;
        if (this.volumeSortColumn === key) {
            this.volumeSortDirection = this.volumeSortDirection === 'asc' ? 'desc' : 'asc';
        } else {
            this.volumeSortColumn = key;
            this.volumeSortDirection = 'asc';
        }
        this.saveVolumeViewState();
        this.renderVolumeTable();
    },

    getVolumeCellValue(columnKey, v) {
        switch (columnKey) {
            case 'VolumeName':
                return v.VolumeData?.ShareName || 'Unknown';
            case 'StorageAccountName':
                return v.VolumeData?.StorageAccountName || '-';
            case 'ResourceGroup':
                return v.VolumeData?.ResourceGroup || '-';
            case 'SubscriptionId':
                return v.VolumeData?.SubscriptionId || '-';
            case 'Location':
                return v.VolumeData?.Location || '-';
            case 'CapacityGiB':
                return v.VolumeData?.ShareQuotaGiB ?? 0;
            case 'UsedCapacity': {
                const bytes = v.VolumeData?.ShareUsageBytes ?? 0;
                if (!bytes) return '0 B';
                const k = 1024;
                const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
                const i = Math.floor(Math.log(bytes) / Math.log(k));
                const val = parseFloat((bytes / Math.pow(k, i)).toFixed(2));
                return `${val} ${sizes[i]}`;
            }
            case 'AccessTier':
                return v.VolumeData?.AccessTier || 'N/A';
            case 'Protocols':
                return v.VolumeData?.EnabledProtocols?.join(', ') || 'SMB';
            case 'StorageAccountSku':
                return v.VolumeData?.StorageAccountSku || 'N/A';
            case 'AiWorkload':
                return v.AiAnalysis?.SuggestedWorkloadName || '-';
            case 'AiConfidence':
                return ((v.AiAnalysis?.ConfidenceScore || 0) * 100).toFixed(0) + '%';
            case 'AiLastAnalyzed':
                return v.AiAnalysis?.LastAnalyzed ? new Date(v.AiAnalysis.LastAnalyzed).toLocaleString() : '-';
            case 'UserWorkload':
                return v.UserAnnotations?.ConfirmedWorkloadName || '-';
            case 'MigrationStatus':
                return v.UserAnnotations?.MigrationStatus || 'Candidate';
            case 'ReviewedBy':
                return v.UserAnnotations?.ReviewedBy || '-';
            case 'ReviewedAt':
                return v.UserAnnotations?.ReviewedAt ? new Date(v.UserAnnotations.ReviewedAt).toLocaleString() : '-';
            default:
                return '';
        }
    },

    renderVolumeTable() {
        const table = document.getElementById('volumesTable');
        const thead = document.getElementById('volumesTableHeader');
        const tbody = document.getElementById('volumesTableBody');
        if (!table || !thead || !tbody) return;

        // Column selector UI
        this.renderVolumeColumnSelector();

        if (!this.volumes || this.volumes.length === 0) {
            thead.innerHTML = '';
            tbody.innerHTML = '<tr><td colspan="10" style="padding: 40px; text-align: center; color: #999;">No volumes found</td></tr>';
            return;
        }

        const searchTerm = (document.getElementById('volumeSearchInput')?.value || '').toLowerCase();
        const visibleCols = this.getVisibleVolumeColumns();

        // Filter client-side by search term
        let rows = this.volumes.slice();
        if (searchTerm) {
            rows = rows.filter(v => {
                const fields = [
                    v.VolumeData?.ShareName,
                    v.VolumeData?.StorageAccountName,
                    v.VolumeData?.ResourceGroup,
                    v.AiAnalysis?.SuggestedWorkloadName,
                    v.UserAnnotations?.ConfirmedWorkloadName,
                    v.UserAnnotations?.MigrationStatus?.toString() || ''
                ].map(x => (x || '').toString().toLowerCase());
                return fields.some(f => f.includes(searchTerm));
            });
        }

        // Sort client-side by selected column (within current page)
        if (this.volumeSortColumn) {
            const key = this.volumeSortColumn;
            const dir = this.volumeSortDirection === 'asc' ? 1 : -1;
            rows.sort((a, b) => {
                if (key === 'CapacityGiB') {
                    const av = a.VolumeData?.ShareQuotaGiB ?? 0;
                    const bv = b.VolumeData?.ShareQuotaGiB ?? 0;
                    return (av - bv) * dir;
                }
                if (key === 'AiConfidence') {
                    const av = a.AiAnalysis?.ConfidenceScore ?? 0;
                    const bv = b.AiAnalysis?.ConfidenceScore ?? 0;
                    return (av - bv) * dir;
                }
                const avRaw = this.getVolumeCellValue(key, a) || '';
                const bvRaw = this.getVolumeCellValue(key, b) || '';
                const av = avRaw.toString().toLowerCase();
                const bv = bvRaw.toString().toLowerCase();
                if (av < bv) return -1 * dir;
                if (av > bv) return 1 * dir;
                return 0;
            });
        }

        // Render header
        const headerCols = this.volumeColumns.filter(c => visibleCols.includes(c.key));
        const sortCol = this.volumeSortColumn;
        const sortDir = this.volumeSortDirection;
        thead.innerHTML = '<tr>' + headerCols.map(col => {
            if (col.key === 'select') {
                return `<th><input type="checkbox" id="selectAll" onchange="jobDetail.toggleSelectAll()"></th>`;
            }
            const sortClass = sortCol === col.key ? (sortDir === 'asc' ? 'sorted-asc' : 'sorted-desc') : '';
            return `<th class="${sortClass}" onclick="jobDetail.sortByVolumeColumn('${col.key}')">${col.label}<span class="sort-indicator"></span></th>`;
        }).join('') + '</tr>';

        // Render body
        tbody.innerHTML = rows.map(v => {
            const isSelected = this.selectedVolumes.has(v.VolumeId);
            const cells = headerCols.map(col => {
                if (col.key === 'select') {
                    return `<td><input type="checkbox" ${isSelected ? 'checked' : ''} onchange="jobDetail.toggleSelect('${v.VolumeId}')"></td>`;
                }
                if (col.key === 'VolumeName') {
                    const name = this.getVolumeCellValue('VolumeName', v);
                    const url = `volume-detail.html?jobId=${encodeURIComponent(this.jobId)}&volumeId=${encodeURIComponent(v.VolumeId)}`;
                    return `<td><a href="${url}">${name}</a></td>`;
                }
                const value = this.getVolumeCellValue(col.key, v);
                return `<td>${value}</td>`;
            }).join('');
            return `<tr>${cells}</tr>`;
        }).join('');
    },

    renderVolumePagination() {
        const pagination = document.getElementById('pagination');
        if (!pagination) return;
        const totalPages = Math.ceil(this.totalCount / this.pageSize);
        if (!totalPages || totalPages <= 1) {
            pagination.innerHTML = '';
            return;
        }

        let html = '';
        if (this.currentPage > 1) {
            html += `<button class="page-btn" onclick="jobDetail.goToPage(${this.currentPage - 1})">Previous</button>`;
        }

        const start = Math.max(1, this.currentPage - 2);
        const end = Math.min(totalPages, this.currentPage + 2);
        for (let i = start; i <= end; i++) {
            html += `<button class="page-btn ${i === this.currentPage ? 'active' : ''}" onclick="jobDetail.goToPage(${i})">${i}</button>`;
        }

        if (this.currentPage < totalPages) {
            html += `<button class="page-btn" onclick="jobDetail.goToPage(${this.currentPage + 1})">Next</button>`;
        }

        pagination.innerHTML = html;
    },

    goToPage(page) {
        if (page < 1) return;
        const totalPages = Math.ceil(this.totalCount / this.pageSize);
        if (page > totalPages) return;
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
        this.renderVolumeTable();
    },
    
    toggleSelectAll() {
        const checked = document.getElementById('selectAll')?.checked;
        this.selectedVolumes.clear();
        if (checked) {
            this.volumes.forEach(v => this.selectedVolumes.add(v.VolumeId));
        }
        this.updateBulkToolbar();
        this.renderVolumeTable();
    },
    
    clearSelection() {
        this.selectedVolumes.clear();
        const selectAll = document.getElementById('selectAll');
        if (selectAll) selectAll.checked = false;
        this.updateBulkToolbar();
        this.renderVolumeTable();
    },
    
    updateBulkToolbar() {
        const toolbar = document.getElementById('bulkToolbar');
        if (!toolbar) return;
        const count = this.selectedVolumes.size;
        
        if (count > 0) {
            toolbar.classList.add('show');
            const label = document.getElementById('selectedCount');
            if (label) label.textContent = `${count} selected`;
        } else {
            toolbar.classList.remove('show');
        }
    },
    
    showVolumeDetail(volumeId) {
        // Deprecated: detail panel replaced by dedicated volume-detail page.
        const url = `volume-detail.html?jobId=${encodeURIComponent(this.jobId)}&volumeId=${encodeURIComponent(volumeId)}`;
        window.location.href = url;
    },
    
    closeDetail() {
        const panel = document.getElementById('detailPanel');
        if (panel) {
            panel.classList.remove('show');
        }
    },
    
    async bulkExclude() {
        if (this.selectedVolumes.size === 0) return;
        
        const volumeIds = Array.from(this.selectedVolumes);
        try {
            const response = await fetch(`${API_BASE_URL}/discovery/${this.jobId}/volumes/bulk-annotations`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    VolumeIds: volumeIds,
                    Annotations: { MigrationStatus: 'Excluded' }
                })
            });
            
            if (!response.ok) throw new Error('Failed to bulk update');
            
            Toast.success('Volumes excluded successfully');
            this.clearSelection();
            await this.applyFilters();
        } catch (error) {
            Toast.error('Error excluding volumes: ' + error.message);
        }
    },
    
    // Analysis Functions
    async runAnalysis() {
        if (!confirm('Run AI analysis on all volumes in this job?')) return;
        
        try {
            const response = await fetch(`${API_BASE_URL}/discovery/${this.jobId}/analyze`, {
                method: 'POST'
            });
            
            if (!response.ok) throw new Error('Failed to start analysis');
            
            const result = await response.json();
            this.currentAnalysisJobId = result.AnalysisJobId;
            
            document.getElementById('view-logs-btn').style.display = 'inline-block';
            this.showLogModal();
            this.pollAnalysisStatus(result.AnalysisJobId);
        } catch (error) {
            Toast.error('Failed to start analysis: ' + error.message);
        }
    },
    
    showLogModal() {
        document.getElementById('logModal').classList.add('show');
        document.getElementById('logBody').innerHTML = '<div style="color: #888;">Starting analysis...</div>';
        this.startLogPolling();
    },
    
    closeLogModal() {
        document.getElementById('logModal').classList.remove('show');
        this.stopLogPolling();
    },
    
    startLogPolling() {
        this.stopLogPolling();
        this.logPollInterval = setInterval(() => this.fetchLogs(), 2000);
        this.fetchLogs();
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
            const logBody = document.getElementById('logBody');

            if (!logs || logs.length === 0) {
                // Preserve any existing placeholder text until real logs arrive
                if (!logBody.innerHTML || logBody.innerHTML.trim() === '') {
                    logBody.innerHTML = '<div style="color: #888;">Waiting for logs...</div>';
                }
                return;
            }
            
            logBody.innerHTML = logs.map(log => {
                const time = new Date(log.Timestamp).toLocaleTimeString();
                return `<div class="log-entry ${log.Level}"><span style="color: #888;">${time}</span> ${log.Message}</div>`;
            }).join('');
            
            logBody.scrollTop = logBody.scrollHeight;
        } catch (error) {
            console.error('Error fetching logs:', error);
        }
    },
    
    async pollAnalysisStatus(analysisJobId) {
        const checkStatus = async () => {
            try {
                const response = await fetch(`${API_BASE_URL}/analysis/${analysisJobId}/status`);
                if (!response.ok) return;
                
                const status = await response.json();
                const progress = status.ProgressPercentage || 0;
                
                document.getElementById('logProgress').style.width = progress + '%';
                document.getElementById('logStatus').textContent = 
                    `${status.Status} - ${status.ProcessedVolumes}/${status.TotalVolumes} volumes`;
                
                if (status.Status === 'Completed') {
                    this.stopLogPolling();
                    setTimeout(() => {
                        Toast.success('Analysis completed!');
                        this.closeLogModal();
                        this.applyFilters();
                    }, 2000);
                } else if (status.Status === 'Failed') {
                    this.stopLogPolling();
                    Toast.error('Analysis failed');
                    this.closeLogModal();
                } else {
                    setTimeout(checkStatus, 3000);
                }
            } catch (error) {
                console.error('Error checking status:', error);
            }
        };
        
        setTimeout(checkStatus, 3000);
    },
    
    // Chat Functions
    async initChat() {
        // Load context stats
        try {
            const response = await fetch(`${API_BASE_URL}/discovery/${this.jobId}/volumes?pageSize=1`);
            if (response.ok) {
                const data = await response.json();
                document.getElementById('chatStatVolumes').textContent = data.TotalCount || 0;
                document.getElementById('chatStatSize').textContent = '0 GB'; // Would need calculation
            }
        } catch (error) {
            console.error('Error loading chat context:', error);
        }
        
        // Show welcome message if empty
        if (this.conversationHistory.length === 0) {
            this.addWelcomeMessage();
        }
    },
    
    addWelcomeMessage() {
        const container = document.getElementById('chatMessages');
        container.innerHTML = '';
        
        const welcome = this.createMessage('assistant', 
            `Hello! I'm your AI assistant. I have context about this discovery job and can help analyze your volumes. What would you like to know?`
        );
        container.appendChild(welcome);
    },
    
    askExample(question) {
        document.getElementById('chatInput').value = question;
        this.sendMessage();
    },
    
    async sendMessage() {
        const input = document.getElementById('chatInput');
        const message = input.value.trim();
        
        if (!message) return;
        
        input.value = '';
        input.style.height = 'auto';
        
        const container = document.getElementById('chatMessages');
        
        // Clear empty state
        if (container.querySelector('[style*="text-align: center"]')) {
            container.innerHTML = '';
        }
        
        // Add user message
        const userMsg = this.createMessage('user', message);
        container.appendChild(userMsg);
        this.conversationHistory.push({ role: 'user', content: message });
        
        // Scroll to bottom
        container.scrollTop = container.scrollHeight;
        
        try {
            const response = await fetch(`${API_BASE_URL}/discovery/${this.jobId}/chat`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    Message: message,
                    ConversationHistory: this.conversationHistory.slice(0, -1)
                })
            });
            
            if (!response.ok) throw new Error('Failed to get response');
            
            const result = await response.json();
            const assistantMsg = this.createMessage('assistant', result.Response);
            container.appendChild(assistantMsg);
            this.conversationHistory.push({ role: 'assistant', content: result.Response });
            
            container.scrollTop = container.scrollHeight;
        } catch (error) {
            const errorMsg = this.createMessage('assistant', `❌ Error: ${error.message}`);
            container.appendChild(errorMsg);
            container.scrollTop = container.scrollHeight;
        }
    },
    
    createMessage(role, content) {
        const div = document.createElement('div');
        div.className = `message ${role}`;
        
        const avatar = document.createElement('div');
        avatar.className = 'message-avatar';
        avatar.textContent = role === 'user' ? '👤' : '🤖';
        
        const msgContent = document.createElement('div');
        const contentDiv = document.createElement('div');
        contentDiv.className = 'message-content';
        contentDiv.textContent = content;
        
        const timeDiv = document.createElement('div');
        timeDiv.style.fontSize = '11px';
        timeDiv.style.color = '#999';
        timeDiv.style.marginTop = '4px';
        timeDiv.textContent = new Date().toLocaleTimeString();
        
        msgContent.appendChild(contentDiv);
        msgContent.appendChild(timeDiv);
        
        div.appendChild(avatar);
        div.appendChild(msgContent);
        
        return div;
    },
    
    // Utility Functions
    getStatusBadge(status) {
        const statusMap = { 0: 'Pending', 1: 'Running', 2: 'Completed', 3: 'Failed', 4: 'Cancelled' };
        const text = typeof status === 'number' ? statusMap[status] : status;
        return `<span class="badge ${text.toLowerCase()}">${text}</span>`;
    },
    
    getStatusText(status) {
        const statusMap = { 0: 'Pending', 1: 'Running', 2: 'Completed', 3: 'Failed', 4: 'Cancelled' };
        return typeof status === 'number' ? statusMap[status] : status;
    },
    
    async deleteJob() {
        if (!confirm('Delete this job? This cannot be undone.')) return;
        
        try {
            await apiClient.deleteJob(this.jobId);
            Toast.success('Job deleted');
            setTimeout(() => window.location.href = 'jobs.html', 500);
        } catch (error) {
            Toast.error('Failed to delete job: ' + error.message);
        }
    }
};

// Initialize on page load
document.addEventListener('DOMContentLoaded', () => jobDetail.init());
