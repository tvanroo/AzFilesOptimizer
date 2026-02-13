// Get job ID from URL
const urlParams = new URLSearchParams(window.location.search);
const JOB_ID = urlParams.get('id');

const jobDetail = {
    // Shared state
    jobId: JOB_ID,
    currentTab: 'analysis',
    jobData: null,
    discoveryLogPollInterval: null,
    isLogConsoleCollapsed: false,
    analysisLogPollInterval: null,
    isAnalysisConsoleCollapsed: false,
    
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
        { key: 'VolumeType', label: 'Type', sortable: true },
        { key: 'VolumeName', label: 'Volume Name', sortable: true },
        { key: 'StorageAccountName', label: 'Storage Account / VM', sortable: true },
        { key: 'ResourceGroup', label: 'Resource Group', sortable: true },
        { key: 'SubscriptionId', label: 'Subscription', sortable: true },
        { key: 'Location', label: 'Location', sortable: true },
        { key: 'CapacityGiB', label: 'Capacity', sortable: true },
        { key: 'UsedCapacity', label: 'Used Capacity', sortable: true },
        { key: 'RequiredCapacityGiB', label: 'Req Capacity', sortable: true },
        { key: 'RequiredThroughputMiBps', label: 'Req Throughput (MiB/s)', sortable: true },
        { key: 'CurrentThroughputMiBps', label: 'Current Throughput (MiB/s)', sortable: true },
        { key: 'CurrentIops', label: 'Current IOPS', sortable: true },
        { key: 'AccessTier', label: 'Tier / Service Level', sortable: true },
        { key: 'Protocols', label: 'Protocols', sortable: true },
        { key: 'StorageAccountSku', label: 'Redundancy / Replication', sortable: true },
        { key: 'Cost30Days', label: '30-Day Cost', sortable: true },
        { key: 'CostPerDay', label: 'Daily Cost', sortable: true },
        { key: 'CostSource', label: 'Cost Source', sortable: true },
        { key: 'HypotheticalAnfFlexible', label: 'Hypothetical ANF Flexible', sortable: true },
        { key: 'MigrationStatus', label: 'Migration Status', sortable: true }
    ],
    defaultVisibleVolumeColumns: ['select','VolumeType','VolumeName','StorageAccountName','ResourceGroup','CapacityGiB','UsedCapacity','RequiredCapacityGiB','RequiredThroughputMiBps','AccessTier','Cost30Days','HypotheticalAnfFlexible','MigrationStatus'],
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
        
        // Determine initial tab from URL (e.g., ?tab=analysis or #analysis)
        const urlParams = new URLSearchParams(window.location.search);
        const tabFromQuery = urlParams.get('tab');
        const hash = window.location.hash ? window.location.hash.substring(1) : '';
        if (tabFromQuery) {
            this.currentTab = tabFromQuery;
        } else if (hash) {
            this.currentTab = hash;
        }
        
        await this.loadJob();
        this.loadVolumeViewState();
        this.setupEventListeners();


        // Apply initial tab selection
        const initialTab = this.currentTab || 'analysis';
        const tabButton = document.querySelector(`.tab-btn[data-tab="${initialTab}"]`);
        if (tabButton) {
            this.switchTab(initialTab, tabButton);
        }
    },
    
    setupEventListeners() {
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
    switchTab(tabName, sourceElement) {
        this.currentTab = tabName;
        
        // Update tab buttons
        document.querySelectorAll('.tab-btn').forEach(btn => btn.classList.remove('active'));
        const target = sourceElement || event.target;
        if (target) {
            target.classList.add('active');
        }
        
        // Update tab content
        document.querySelectorAll('.tab-content').forEach(content => content.classList.remove('active'));
        document.getElementById(`tab-${tabName}`).classList.add('active');
        
        // Load tab-specific data
        if (tabName === 'analysis') {
            if (this.volumes.length === 0) {
                this.loadVolumes();
            }
            // Always check for analysis logs when switching to analysis tab
            this.checkForAnalysisLogs();
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
            
            // Update button states based on job status
            this.updateButtonStates();
            
            // Display overview
            this.renderOverview();
        } catch (error) {
            Toast.error('Failed to load job: ' + error.message);
        }
    },
    
    updateButtonStates() {
        const rerunBtn = document.getElementById('rerun-btn');
        if (rerunBtn) {
            // Disable rerun button if job is running or pending
            const isRunning = this.jobData.Status === 0 || this.jobData.Status === 1;
            rerunBtn.disabled = isRunning;
            if (isRunning) {
                rerunBtn.style.opacity = '0.5';
                rerunBtn.style.cursor = 'not-allowed';
            } else {
                rerunBtn.style.opacity = '1';
                rerunBtn.style.cursor = 'pointer';
            }
        }
    },
    
    async rerunJob() {
        if (!confirm('Re-run discovery for this job? This will update all volumes and discover any new ones in the same scope.')) {
            return;
        }
        
        try {
            await apiClient.rerunJob(this.jobId);
            Toast.success('Discovery job re-started! Refreshing...');
            
            // Reload job to show updated status
            setTimeout(() => {
                this.loadJob();
            }, 1000);
        } catch (error) {
            Toast.error('Failed to re-run job: ' + error.message);
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
                <div class="info-item">
                    <label>Managed Disks</label>
                    <strong>${job.ManagedDisksFound || 0}</strong>
                </div>
            </div>
        `;
        
        // Start discovery log polling if job is running or pending
        if (job.Status === 0 || job.Status === 1) { // Pending or Running
            this.startDiscoveryLogPolling();
        } else {
            this.stopDiscoveryLogPolling();
            // Show logs one final time for completed jobs
            if (job.Status === 2 || job.Status === 3) { // Completed or Failed
                this.fetchDiscoveryLogs();
            }
        }
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
            const searchTerm = parsed.searchTerm || '';
            const searchInput = document.getElementById('volumeSearchInput');
            if (searchInput) searchInput.value = searchTerm;
        } catch {
            this.visibleVolumeColumns = [...this.defaultVisibleVolumeColumns];
        }
    },

    saveVolumeViewState() {
        const searchTerm = document.getElementById('volumeSearchInput')?.value || '';

        const payload = {
            visibleColumns: this.visibleVolumeColumns || this.defaultVisibleVolumeColumns,
            sortColumn: this.volumeSortColumn,
            sortDirection: this.volumeSortDirection,
            searchTerm
        };
        try {
            localStorage.setItem(this.volumeViewStorageKey, JSON.stringify(payload));
        } catch {
            // ignore storage errors
        }
    },

    async loadVolumes() {
        await this.applyFilters();
    },
    
    async applyFilters() {

        // Persist filter portion of view state immediately
        this.saveVolumeViewState();
        
        try {
            let url = `/discovery/${this.jobId}/volumes?page=${this.currentPage}&pageSize=${this.pageSize}`;
            
            const data = await apiClient.fetchJson(url);
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
        const validKeys = new Set(this.volumeColumns.map(c => c.key));
        this.visibleVolumeColumns = this.visibleVolumeColumns.filter(k => validKeys.has(k));
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
        const vType = v.VolumeType || 'AzureFiles';
        const vData = v.VolumeData || {};
        
        switch (columnKey) {
            case 'VolumeType': {
                let iconHtml = '';
                let label = '';
                if (vType === 'ManagedDisk') {
                    iconHtml = '<img src="images/icon-managed-disk.png" class="vol-type-icon" alt="Managed Disk">';
                    label = 'Disk';
                } else if (vType === 'ANF') {
                    iconHtml = '<img src="images/icon-anf.png" class="vol-type-icon" alt="ANF">';
                    label = 'ANF';
                } else {
                    iconHtml = '<img src="images/icon-storage-account.png" class="vol-type-icon" alt="Azure Files">';
                    label = 'Files';
                }
                return `<span class="vol-type">${iconHtml}<span>${label}</span></span>`;
            }
            case 'VolumeName':
                if (vType === 'ManagedDisk') return vData.DiskName || 'Unknown';
                if (vType === 'ANF') {
                    const full = vData.VolumeName || 'Unknown';
                    const parts = full.split('/').filter(Boolean);
                    return parts.length ? parts[parts.length - 1] : full;
                }
                return vData.ShareName || 'Unknown';
            case 'StorageAccountName':
                if (vType === 'ManagedDisk') return vData.AttachedVmName || 'Unattached';
                if (vType === 'ANF') return vData.NetAppAccountName || '-';
                return vData.StorageAccountName || '-';
            case 'ResourceGroup':
                return vData.ResourceGroup || '-';
            case 'SubscriptionId':
                return vData.SubscriptionId || '-';
            case 'Location':
                return vData.Location || '-';
            case 'CapacityGiB': {
                let bytes = 0;
                let rawValue = 0;
                if (vType === 'ManagedDisk') {
                    rawValue = vData.DiskSizeGB || 0;
                    bytes = rawValue * 1024 ** 3;
                } else if (vType === 'ANF') {
                    bytes = vData.ProvisionedSizeBytes || 0;
                    rawValue = bytes / (1024 ** 3);
                } else {
                    rawValue = vData.ShareQuotaGiB || 0;
                    bytes = rawValue * 1024 ** 3;
                }
                const displayValue = this.formatBytes(bytes);
                return `<span title="Full precision: ${rawValue.toFixed(8)} GiB (${bytes} bytes)">${displayValue}</span>`;
            }
            case 'UsedCapacity': {
                let bytes = 0;
                if (vType === 'ManagedDisk') {
                    bytes = vData.UsedBytes || 0;
                } else {
                    bytes = vData.ShareUsageBytes ?? 0;
                }
                if (!bytes) return `<span title="Full precision: 0.00000000 GiB (0 bytes)">0 B</span>`;
                const gib = bytes / (1024 ** 3);
                const displayValue = this.formatBytes(bytes);
                return `<span title="Full precision: ${gib.toFixed(8)} GiB (${bytes} bytes)">${displayValue}</span>`;
            }
            case 'AccessTier':
                if (vType === 'ManagedDisk') {
                    return vData.DiskTier || vData.DiskSku || 'N/A';
                }
                if (vType === 'ANF') {
                    // Handle ANF service levels including cool variants
                    const serviceLevel = vData.ServiceLevel || 'N/A';
                    if (vData.CoolAccessEnabled) {
                        return serviceLevel + ' Cool';
                    }
                    return serviceLevel;
                }
                // Azure Files - comprehensive deployment type detection
                const accessTier = vData.AccessTier || '';
                const accountKind = vData.StorageAccountKind || '';
                const provisionedTier = vData.ProvisionedTier || '';
                const isProvisioned = vData.IsProvisioned;
                
                // Premium FileStorage accounts
                if (accountKind === 'FileStorage' || accessTier === 'Premium') {
                    return 'Premium';
                }
                
                // Provisioned v1/v2 variations
                if (isProvisioned || provisionedTier) {
                    if (provisionedTier === 'ProvisionedV2SSD') return 'Provisioned v2 SSD';
                    if (provisionedTier === 'ProvisionedV2HDD') return 'Provisioned v2 HDD';
                    if (provisionedTier === 'ProvisionedV1') return 'Provisioned v1';
                    if (isProvisioned) return 'Provisioned v1'; // fallback
                }
                
                // Standard deployment types
                if (accessTier === 'TransactionOptimized') return 'Transaction Optimized';
                if (accessTier === 'Hot') return 'Hot';
                if (accessTier === 'Cool') return 'Cool';
                
                return accessTier || 'N/A';
            case 'Protocols':
                if (vType === 'ManagedDisk') return 'Block';
                if (vType === 'ANF') return vData.ProtocolTypes?.join(', ') || '-';
                return vData.EnabledProtocols?.join(', ') || 'SMB';
            case 'StorageAccountSku':
                if (vType === 'ManagedDisk') {
                    // Extract redundancy from DiskSku (e.g., Premium_LRS -> LRS)
                    const redundancy = vData.DiskSku?.split('_').pop() || 'N/A';
                    return redundancy;
                }
                if (vType === 'ANF') {
                    // ANF Replication options: None, CRR (Cross-Region), CZR (Cross-Zone), CZRR (Cross-Zone+Region)
                    // TODO: Enhance discovery service to collect ANF replication configuration
                    // Properties needed: VolumeReplicationEnabled, ReplicationEndpoints, ReplicationSchedule
                    // For now, return 'None' as most volumes don't have replication configured
                    return 'None';
                }
                // Azure Files redundancy from StorageAccountSku (e.g., Standard_LRS -> LRS, Standard_GRS -> GRS)
                const sku = vData.StorageAccountSku || '';
                if (sku.includes('_')) {
                    const redundancy = sku.split('_').pop();
                    // Map specific redundancy types
                    switch (redundancy) {
                        case 'LRS': return 'LRS';
                        case 'ZRS': return 'ZRS';
                        case 'GRS': return 'GRS';
                        case 'GZRS': return 'GZRS';
                        case 'RAGRS': return 'RA-GRS';
                        case 'RAGZRS': return 'RA-GZRS';
                        default: return redundancy || 'N/A';
                    }
                }
                return 'N/A';
            case 'RequiredCapacityGiB': {
                const val = typeof v.RequiredCapacityGiB === 'number' ? v.RequiredCapacityGiB : null;
                if (val == null) return 'N/A';
                const bytes = val * 1024 ** 3;
                const displayValue = this.formatBytes(bytes);
                return `<span title="Full precision: ${val.toFixed(8)} GiB (${bytes} bytes)">${displayValue}</span>`;
            }
            case 'RequiredThroughputMiBps': {
                const val = typeof v.RequiredThroughputMiBps === 'number' ? v.RequiredThroughputMiBps : null;
                if (val == null) return 'N/A';
                const displayValue = val.toFixed(1);
                const fullPrecision = val.toFixed(8);
                return `<span title="Full precision: ${fullPrecision} MiB/s">${displayValue}</span>`;
            }
            case 'CurrentThroughputMiBps': {
                const val = typeof v.CurrentThroughputMiBps === 'number' ? v.CurrentThroughputMiBps : null;
                if (val == null) return 'N/A';
                const displayValue = val.toFixed(1);
                const fullPrecision = val.toFixed(8);
                return `<span title="Full precision: ${fullPrecision} MiB/s">${displayValue}</span>`;
            }
            case 'CurrentIops': {
                const val = typeof v.CurrentIops === 'number' ? v.CurrentIops : null;
                if (val == null) return 'N/A';
                if (val < 0) return '<span title="IOPS are unmetered for this tier">Unmetered</span>';
                const displayValue = val.toFixed(0);
                return `<span title="Full precision: ${val.toFixed(8)} IOPS">${displayValue}</span>`;
            }
            case 'Cost30Days': {
                const cs = v.CostSummary;
                if (!cs || typeof cs.TotalCost30Days !== 'number') return v.CostStatus === 'Pending' ? 'Pending' : '-';
                const rawValue = cs.TotalCost30Days;
                const displayValue = `$${rawValue.toFixed(2)}`;
                const fullPrecision = `$${rawValue.toFixed(8)}`;
                const costType = cs.IsActual ? 'actual billed' : 'estimated retail';
                const tooltip = `Full precision: ${fullPrecision} (${costType})`;
                return cs.IsActual ? `<span title="${tooltip}">${displayValue}</span>` : `<span style="color: #999;" title="${tooltip}">${displayValue}</span>`;
            }
            case 'CostPerDay': {
                const cs = v.CostSummary;
                if (!cs || typeof cs.DailyAverage !== 'number') return v.CostStatus === 'Pending' ? 'Pending' : '-';
                const rawValue = cs.DailyAverage;
                const displayValue = `$${rawValue.toFixed(2)}`;
                const fullPrecision = `$${rawValue.toFixed(8)}`;
                const costType = cs.IsActual ? 'actual billed' : 'estimated retail';
                const tooltip = `Full precision: ${fullPrecision} (${costType})`;
                return cs.IsActual ? `<span title="${tooltip}">${displayValue}</span>` : `<span style="color: #999;" title="${tooltip}">${displayValue}</span>`;
            }
            case 'CostSource': {
                const cs = v.CostSummary;
                if (!cs) return v.CostStatus || 'Pending';
                return cs.IsActual ? 'Actual' : 'Estimate';
            }
            case 'HypotheticalAnfFlexible': {
                const hc = v.HypotheticalCost;
                if (!hc || typeof hc.TotalMonthlyCost !== 'number') return '-';
                const rawValue = hc.TotalMonthlyCost;
                const displayValue = `$${rawValue.toFixed(2)}`;
                const fullPrecision = `$${rawValue.toFixed(8)}`;
                const coolIndicator = hc.CoolAccessEnabled ? ' <span style="color: #4a9eff; font-size: 0.8em;" title="Cool access enabled">❄️</span>' : '';
                const tooltip = `Full precision: ${fullPrecision} (Hypothetical ANF Flexible)`;
                return `<span style="color: #666; font-style: italic;" title="${tooltip}">${displayValue}${coolIndicator}</span>`;
            }
            case 'MigrationStatus':
                return v.UserAnnotations?.MigrationStatus || 'Candidate';
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
                const vData = v.VolumeData || {};
                const fields = [
                    v.VolumeType,
                    vData.ShareName || vData.DiskName || vData.VolumeName,
                    vData.StorageAccountName || vData.AttachedVmName || vData.NetAppAccountName,
                    vData.ResourceGroup,
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
                if (key === 'RequiredCapacityGiB' || key === 'RequiredThroughputMiBps' || key === 'CurrentThroughputMiBps' || key === 'CurrentIops') {
                    const av = typeof a[key] === 'number' ? a[key] : 0;
                    const bv = typeof b[key] === 'number' ? b[key] : 0;
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

    formatBytes(bytes) {
        const k = 1024;
        const sizes = ['B', 'KB', 'MB', 'GB', 'TB', 'PB'];
        if (!bytes || bytes <= 0) return '0 B';
        const i = Math.floor(Math.log(bytes) / Math.log(k));
        const val = parseFloat((bytes / Math.pow(k, i)).toFixed(2));
        return `${val} ${sizes[i]}`;
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
    async checkForAnalysisLogs() {
        // Check if there's an analysis job for this discovery job so we can include its logs in the unified console
        try {
            const data = await apiClient.fetchJson(`/discovery/${this.jobId}/analysis-status`);
            if (data && data.AnalysisJobId) {
                this.currentAnalysisJobId = data.AnalysisJobId;
            }
        } catch (error) {
            console.error('Error checking for analysis logs:', error);
        }
    },
    
    async runAnalysis() {
        if (!confirm('Run AI analysis on all volumes in this job?')) return;
        
        try {
            const result = await apiClient.fetchJson(`/discovery/${this.jobId}/analyze`, {
                method: 'POST'
            });
            this.currentAnalysisJobId = result.AnalysisJobId;
            Toast.success('Analysis started. Logs will appear in the Job Activity Log.');
        } catch (error) {
            Toast.error('Failed to start analysis: ' + error.message);
        }
    },
    
    // Discovery Log Functions
    startDiscoveryLogPolling() {
        this.stopDiscoveryLogPolling();
        this.discoveryLogPollInterval = setInterval(() => this.fetchDiscoveryLogs(), 2000);
        this.fetchDiscoveryLogs();
    },
    
    stopDiscoveryLogPolling() {
        if (this.discoveryLogPollInterval) {
            clearInterval(this.discoveryLogPollInterval);
            this.discoveryLogPollInterval = null;
        }
    },
    
    async fetchDiscoveryLogs() {
        try {
            const jobLogs = await apiClient.getJobLogs(this.jobId);
            
            if (!jobLogs || jobLogs.length === 0) {
                return;
            }

            let combinedLogs = jobLogs.slice();


            combinedLogs.sort((a, b) => new Date(a.Timestamp) - new Date(b.Timestamp));
            
            // Show the unified job activity log if we have logs
            const logConsole = document.getElementById('job-activity-log');
            if (logConsole && logConsole.style.display === 'none') {
                logConsole.style.display = 'block';
            }
            
            // Update status indicator
            const indicator = document.getElementById('log-status-indicator');
            if (indicator && this.jobData) {
                indicator.className = 'log-status-indicator';
                if (this.jobData.Status === 0 || this.jobData.Status === 1) {
                    indicator.classList.add('running');
                } else if (this.jobData.Status === 2) {
                    indicator.classList.add('completed');
                } else if (this.jobData.Status === 3) {
                    indicator.classList.add('failed');
                }
            }
            
            // Render logs
            const logBody = document.getElementById('discovery-log-body');
            if (!logBody) return;
            
            logBody.innerHTML = combinedLogs.map(log => {
                const timestamp = new Date(log.Timestamp).toLocaleTimeString();
                let cssClass = 'discovery-log-entry';
                
                // Determine log level styling
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
            
            // Auto-scroll to bottom
            logBody.scrollTop = logBody.scrollHeight;
        } catch (error) {
            console.error('Error fetching discovery logs:', error);
        }
    },
    
    toggleLogConsole() {
        const logBody = document.getElementById('discovery-log-body');
        const toggleBtn = event.target;

        if (this.isLogConsoleCollapsed) {
            logBody.style.display = 'block';
            toggleBtn.textContent = 'Collapse';
            this.isLogConsoleCollapsed = false;
        } else {
            logBody.style.display = 'none';
            toggleBtn.textContent = 'Expand';
            this.isLogConsoleCollapsed = true;
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

    escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    },
    
    // Hypothetical ANF Flexible Cost Functions
    updateHypotheticalSettings() {
        const coolEnabled = document.getElementById('hypothetical-cool-enabled').checked;
        const coolInputs = document.getElementById('hypothetical-cool-inputs');
        
        if (coolEnabled) {
            coolInputs.style.display = 'flex';
        } else {
            coolInputs.style.display = 'none';
        }
    },
    
    async recalculateHypotheticalCosts() {
        const coolEnabled = document.getElementById('hypothetical-cool-enabled').checked;
        const coolPercentage = parseFloat(document.getElementById('hypothetical-cool-percentage').value) || 80;
        const retrievalPercentage = parseFloat(document.getElementById('hypothetical-retrieval-percentage').value) || 15;
        
        if (coolPercentage < 0 || coolPercentage > 100 || retrievalPercentage < 0 || retrievalPercentage > 100) {
            Toast.error('Percentages must be between 0 and 100');
            return;
        }
        
        try {
            Toast.info('Calculating hypothetical costs...');
            
            // Prepare batch request with all volumes
            const volumeRequests = this.volumes.map(v => ({
                VolumeId: v.VolumeId,
                RequiredCapacityGiB: v.RequiredCapacityGiB,
                RequiredThroughputMiBps: v.RequiredThroughputMiBps
            }));
            
            const assumptions = coolEnabled ? {
                CoolDataPercentage: coolPercentage,
                CoolDataRetrievalPercentage: retrievalPercentage
            } : null;
            
            const response = await apiClient.fetchJson('/hypothetical-cost/batch', {
                method: 'POST',
                body: JSON.stringify({
                    Volumes: volumeRequests,
                    Assumptions: assumptions
                })
            });
            
            // Update volumes with hypothetical costs
            // Response is a dictionary with volumeId as key
            if (response) {
                Object.keys(response).forEach(volumeId => {
                    const volume = this.volumes.find(v => v.VolumeId === volumeId);
                    if (volume) {
                        volume.HypotheticalCost = response[volumeId];
                    }
                });
            }
            
            // Re-render the table
            this.renderVolumeTable();
            Toast.success('Hypothetical costs calculated successfully');
        } catch (error) {
            console.error('Error calculating hypothetical costs:', error);
            Toast.error('Failed to calculate hypothetical costs: ' + error.message);
        }
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
    },
    
    // Job Settings Functions
    async openJobSettings() {
        const modal = document.getElementById('jobSettingsModal');
        modal.style.display = 'flex';
        
        // Load current job assumptions
        try {
            const response = await fetch(`${API_BASE_URL}/cool-assumptions/job/${this.jobId}`, {
                headers: authManager.isSignedIn() ? {
                    'Authorization': `Bearer ${await authManager.getAccessToken()}`
                } : {}
            });
            
            if (response.ok) {
                const data = await response.json();
                document.getElementById('job-cool-data-percentage').value = data.coolDataPercentage || data.CoolDataPercentage || '';
                document.getElementById('job-cool-retrieval-percentage').value = data.coolDataRetrievalPercentage || data.CoolDataRetrievalPercentage || '';
                
                // Show status
                const statusDiv = document.getElementById('job-assumptions-status');
                const source = data.source || data.Source || 'Global';
                if (source === 'Job') {
                    statusDiv.innerHTML = '✓ Job-wide overrides are active';
                    statusDiv.style.background = '#e8f5e9';
                    statusDiv.style.color = '#2e7d32';
                    statusDiv.style.display = 'block';
                } else {
                    statusDiv.innerHTML = `Using ${source.toLowerCase()} defaults`;
                    statusDiv.style.background = '#f5f5f5';
                    statusDiv.style.color = '#666';
                    statusDiv.style.display = 'block';
                }
            }
        } catch (error) {
            console.error('Error loading job assumptions:', error);
            // Leave fields empty if load fails
        }
    },
    
    closeJobSettings() {
        const modal = document.getElementById('jobSettingsModal');
        modal.style.display = 'none';
    },
    
    async saveJobCoolAssumptions() {
        const coolDataPercentage = parseFloat(document.getElementById('job-cool-data-percentage').value);
        const coolRetrievalPercentage = parseFloat(document.getElementById('job-cool-retrieval-percentage').value);
        
        if (isNaN(coolDataPercentage) || coolDataPercentage < 0 || coolDataPercentage > 100) {
            Toast.error('Cool data percentage must be between 0 and 100');
            return;
        }
        
        if (isNaN(coolRetrievalPercentage) || coolRetrievalPercentage < 0 || coolRetrievalPercentage > 100) {
            Toast.error('Cool data retrieval percentage must be between 0 and 100');
            return;
        }
        
        try {
            const response = await fetch(`${API_BASE_URL}/cool-assumptions/job/${this.jobId}`, {
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
                throw new Error(error.error || 'Failed to save job assumptions');
            }
            
            Toast.success('Job cool assumptions saved and costs recalculated');
            this.closeJobSettings();
            
            // Reload volumes to show updated costs
            if (this.currentTab === 'analysis') {
                this.loadVolumes();
            }
        } catch (error) {
            console.error('Error saving job assumptions:', error);
            Toast.error(error.message || 'Failed to save job assumptions');
        }
    },
    
    async clearJobCoolAssumptions() {
        if (!confirm('Clear job-wide cool assumptions? This will revert to global defaults and recalculate costs.')) {
            return;
        }
        
        try {
            const response = await fetch(`${API_BASE_URL}/cool-assumptions/job/${this.jobId}`, {
                method: 'DELETE',
                headers: authManager.isSignedIn() ? {
                    'Authorization': `Bearer ${await authManager.getAccessToken()}`
                } : {}
            });
            
            if (!response.ok) {
                const error = await response.json();
                throw new Error(error.error || 'Failed to clear job assumptions');
            }
            
            Toast.success('Job overrides cleared and costs recalculated');
            this.closeJobSettings();
            
            // Reload volumes to show updated costs
            if (this.currentTab === 'analysis') {
                this.loadVolumes();
            }
        } catch (error) {
            console.error('Error clearing job assumptions:', error);
            Toast.error(error.message || 'Failed to clear job assumptions');
        }
    }
};

// Initialize on page load
document.addEventListener('DOMContentLoaded', () => jobDetail.init());
