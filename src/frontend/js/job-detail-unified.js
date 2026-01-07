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
        } else if (tabName === 'resources') {
            this.loadResources();
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
    
    async loadResources() {
        try {
            const response = await fetch(`${API_BASE_URL}/jobs/${this.jobId}/shares`);
            if (!response.ok) throw new Error('Failed to load resources');
            
            const data = await response.json();
            const container = document.getElementById('resources-container');
            
            container.innerHTML = `
                <h3>Azure Files Shares: ${data.totalShares || 0}</h3>
                <p>Use the Volume Analysis tab to view and analyze discovered shares.</p>
            `;
        } catch (error) {
            console.error('Error loading resources:', error);
        }
    },
    
    // Volume Analysis Functions
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
        
        try {
            let url = `${API_BASE_URL}/discovery/${this.jobId}/volumes?page=${this.currentPage}&pageSize=${this.pageSize}`;
            if (workloadFilter) url += `&workloadFilter=${workloadFilter}`;
            if (statusFilter) url += `&statusFilter=${statusFilter}`;
            if (confidenceMin > 0) url += `&confidenceMin=${confidenceMin}`;
            
            const response = await fetch(url);
            if (!response.ok) throw new Error('Failed to load volumes');
            
            const data = await response.json();
            this.volumes = data.Volumes || [];
            this.totalCount = data.TotalCount || 0;
            
            this.renderVolumeGrid();
        } catch (error) {
            console.error('Error loading volumes:', error);
            document.getElementById('volumesGrid').innerHTML = '<div style="padding: 20px; text-align: center;">Error loading volumes</div>';
        }
    },
    
    renderVolumeGrid() {
        const grid = document.getElementById('volumesGrid');
        
        if (this.volumes.length === 0) {
            grid.innerHTML = '<div style="padding: 40px; text-align: center; color: #999;">No volumes found</div>';
            return;
        }
        
        grid.innerHTML = this.volumes.map(v => {
            const isSelected = this.selectedVolumes.has(v.VolumeId);
            const confidence = (v.AiAnalysis?.ConfidenceScore || 0) * 100;
            
            return `
                <div class="grid-row ${isSelected ? 'selected' : ''}" data-id="${v.VolumeId}">
                    <div><input type="checkbox" ${isSelected ? 'checked' : ''} onchange="jobDetail.toggleSelect('${v.VolumeId}')"></div>
                    <div onclick="jobDetail.showVolumeDetail('${v.VolumeId}')">${v.VolumeData.ShareName || 'Unknown'}</div>
                    <div onclick="jobDetail.showVolumeDetail('${v.VolumeId}')">${v.VolumeData.StorageAccountName || '-'}</div>
                    <div onclick="jobDetail.showVolumeDetail('${v.VolumeId}')">${v.VolumeData.ShareQuotaGiB || 0}</div>
                    <div onclick="jobDetail.showVolumeDetail('${v.VolumeId}')">${v.AiAnalysis?.SuggestedWorkloadName || '-'}</div>
                    <div onclick="jobDetail.showVolumeDetail('${v.VolumeId}')">${v.UserAnnotations?.ConfirmedWorkloadName || '-'}</div>
                    <div onclick="jobDetail.showVolumeDetail('${v.VolumeId}')">
                        <div class="confidence-bar">
                            <div class="confidence-fill" style="width: ${confidence}%"></div>
                        </div>
                        <small>${confidence.toFixed(0)}%</small>
                    </div>
                    <div>${v.UserAnnotations?.MigrationStatus || 'Candidate'}</div>
                </div>
            `;
        }).join('');
    },
    
    toggleSelect(volumeId) {
        if (this.selectedVolumes.has(volumeId)) {
            this.selectedVolumes.delete(volumeId);
        } else {
            this.selectedVolumes.add(volumeId);
        }
        this.updateBulkToolbar();
        this.renderVolumeGrid();
    },
    
    toggleSelectAll() {
        const checked = document.getElementById('selectAll').checked;
        this.selectedVolumes.clear();
        if (checked) {
            this.volumes.forEach(v => this.selectedVolumes.add(v.VolumeId));
        }
        this.updateBulkToolbar();
        this.renderVolumeGrid();
    },
    
    clearSelection() {
        this.selectedVolumes.clear();
        document.getElementById('selectAll').checked = false;
        this.updateBulkToolbar();
        this.renderVolumeGrid();
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
    
    showVolumeDetail(volumeId) {
        const volume = this.volumes.find(v => v.VolumeId === volumeId);
        if (!volume) return;
        
        document.getElementById('detailTitle').textContent = volume.VolumeData.ShareName;
        document.getElementById('detailContent').innerHTML = `
            <h3>Volume Properties</h3>
            <div class="property-grid">
                <div class="property-label">Name</div>
                <div>${volume.VolumeData.ShareName}</div>
                <div class="property-label">Storage Account</div>
                <div>${volume.VolumeData.StorageAccountName}</div>
                <div class="property-label">Size</div>
                <div>${volume.VolumeData.ShareQuotaGiB} GiB</div>
                <div class="property-label">AI Workload</div>
                <div>${volume.AiAnalysis?.SuggestedWorkloadName || 'Not analyzed'}</div>
                <div class="property-label">Confidence</div>
                <div>${((volume.AiAnalysis?.ConfidenceScore || 0) * 100).toFixed(0)}%</div>
            </div>
        `;
        
        document.getElementById('detailPanel').classList.add('show');
    },
    
    closeDetail() {
        document.getElementById('detailPanel').classList.remove('show');
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
