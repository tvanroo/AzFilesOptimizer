const analysisPrompts = {
    prompts: [],
    draggedItem: null,
    variables: ['{VolumeName}', '{Size}', '{SizeGB}', '{UsedCapacity}', '{Tags}', '{Metadata}', 
                '{StorageAccount}', '{ResourceGroup}', '{PerformanceTier}', '{StorageAccountSku}',
                '{ProvisionedIOPS}', '{ProvisionedBandwidth}', '{Protocols}', '{Location}', '{AccessTier}'],

    init() {
        this.loadPrompts();
    },

    async loadPrompts() {
        try {
            const response = await fetch('/api/analysis-prompts');
            this.prompts = await response.json();
            this.renderPrompts();
        } catch (error) {
            console.error('Error loading prompts:', error);
            alert('Error loading analysis prompts');
        }
    },

    renderPrompts() {
        const listEl = document.getElementById('promptsList');
        if (this.prompts.length === 0) {
            listEl.innerHTML = '<div class="empty-state"><p>No prompts configured. Create your first prompt to get started.</p></div>';
            return;
        }

        listEl.innerHTML = this.prompts.map((prompt, index) => {
            const categoryClass = {
                'Exclusion': 'category-exclusion',
                'WorkloadDetection': 'category-detection',
                'MigrationAssessment': 'category-assessment'
            }[prompt.Category] || 'category-detection';

            return `
                <div class="prompt-card ${!prompt.Enabled ? 'disabled' : ''}" 
                     draggable="true" 
                     data-id="${prompt.PromptId}"
                     ondragstart="analysisPrompts.handleDragStart(event, ${index})"
                     ondragover="analysisPrompts.handleDragOver(event)"
                     ondrop="analysisPrompts.handleDrop(event, ${index})">
                    <div class="prompt-header">
                        <span class="prompt-priority">Priority ${prompt.Priority}</span>
                        <div class="prompt-actions">
                            <div class="toggle-switch ${prompt.Enabled ? 'on' : ''}" 
                                 onclick="analysisPrompts.toggleEnabled('${prompt.PromptId}')">
                                <div class="toggle-slider"></div>
                            </div>
                            <button class="btn btn-primary btn-sm" onclick="analysisPrompts.edit('${prompt.PromptId}')">Edit</button>
                            <button class="btn btn-danger btn-sm" onclick="analysisPrompts.deletePrompt('${prompt.PromptId}')">Delete</button>
                        </div>
                    </div>
                    <div class="prompt-name">${this.escapeHtml(prompt.Name)}</div>
                    <span class="prompt-category ${categoryClass}">${prompt.Category}</span>
                    <div style="margin-top: 10px; font-size: 14px; color: #666;">
                        ${this.truncate(prompt.PromptTemplate, 150)}
                    </div>
                </div>
            `;
        }).join('');
    },

    handleDragStart(event, index) {
        this.draggedItem = index;
        event.dataTransfer.effectAllowed = 'move';
    },

    handleDragOver(event) {
        event.preventDefault();
        event.dataTransfer.dropEffect = 'move';
    },

    handleDrop(event, dropIndex) {
        event.preventDefault();
        if (this.draggedItem === null || this.draggedItem === dropIndex) return;

        const draggedPrompt = this.prompts[this.draggedItem];
        this.prompts.splice(this.draggedItem, 1);
        this.prompts.splice(dropIndex, 0, draggedPrompt);
        
        // Update priorities
        this.prompts.forEach((prompt, idx) => {
            prompt.Priority = (idx + 1) * 10;
        });

        this.draggedItem = null;
        this.renderPrompts();
    },

    async saveOrder() {
        try {
            const priorities = {};
            this.prompts.forEach(prompt => {
                priorities[prompt.PromptId] = prompt.Priority;
            });

            const response = await fetch('/api/analysis-prompts/reorder', {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ Priorities: priorities })
            });

            if (!response.ok) throw new Error('Failed to save order');
            alert('Prompt order saved successfully');
        } catch (error) {
            console.error('Error saving order:', error);
            alert('Error saving prompt order');
        }
    },

    async toggleEnabled(promptId) {
        const prompt = this.prompts.find(p => p.PromptId === promptId);
        if (!prompt) return;

        prompt.Enabled = !prompt.Enabled;
        
        try {
            await this.savePrompt(prompt);
            this.renderPrompts();
        } catch (error) {
            console.error('Error toggling prompt:', error);
            prompt.Enabled = !prompt.Enabled; // Revert
        }
    },

    createNew() {
        this.showModal({
            PromptId: null,
            Name: '',
            Priority: (this.prompts.length + 1) * 10,
            Category: 'WorkloadDetection',
            PromptTemplate: '',
            Enabled: true,
            StopConditionsJson: JSON.stringify({
                StopOnMatch: false,
                ActionOnMatch: 'None',
                TargetWorkloadId: null
            })
        });
    },

    edit(promptId) {
        const prompt = this.prompts.find(p => p.PromptId === promptId);
        if (prompt) this.showModal(prompt);
    },

    showModal(prompt) {
        const modal = document.getElementById('promptModal');
        const title = document.getElementById('modalTitle');
        const body = document.getElementById('modalBody');

        title.textContent = prompt.PromptId ? 'Edit Prompt' : 'New Prompt';

        const stopConditions = prompt.StopConditionsJson ? JSON.parse(prompt.StopConditionsJson) : {};

        body.innerHTML = `
            <div class="form-group">
                <label>Prompt Name *</label>
                <input type="text" id="promptName" value="${this.escapeHtml(prompt.Name)}" placeholder="e.g., CloudShell Detection">
            </div>

            <div class="form-row">
                <div class="form-group">
                    <label>Category *</label>
                    <select id="promptCategory">
                        <option value="Exclusion" ${prompt.Category === 'Exclusion' ? 'selected' : ''}>Exclusion</option>
                        <option value="WorkloadDetection" ${prompt.Category === 'WorkloadDetection' ? 'selected' : ''}>Workload Detection</option>
                        <option value="MigrationAssessment" ${prompt.Category === 'MigrationAssessment' ? 'selected' : ''}>Migration Assessment</option>
                    </select>
                </div>
                <div class="form-group">
                    <label>Priority</label>
                    <input type="number" id="promptPriority" value="${prompt.Priority}" min="1">
                </div>
            </div>

            <div class="form-group">
                <label>Prompt Template *</label>
                <div class="variable-picker">
                    <small style="width: 100%; margin-bottom: 5px; display: block;">Click to insert:</small>
                    ${this.variables.map(v => `<span class="variable-tag" onclick="analysisPrompts.insertVariable('${v}')">${v}</span>`).join('')}
                </div>
                <textarea id="promptTemplate" rows="8" placeholder="Enter your prompt template here...">${this.escapeHtml(prompt.PromptTemplate)}</textarea>
            </div>

            <div class="form-group">
                <div class="stop-conditions">
                    <h4 style="margin-top: 0;">Stop Conditions</h4>
                    <label><input type="checkbox" id="stopOnMatch" ${stopConditions.StopOnMatch ? 'checked' : ''}> Stop processing if condition met</label>
                    
                    <div style="margin-top: 15px;">
                        <label>Action on Match</label>
                        <select id="stopAction" ${!stopConditions.StopOnMatch ? 'disabled' : ''}>
                            <option value="None" ${stopConditions.ActionOnMatch === 'None' ? 'selected' : ''}>None</option>
                            <option value="ExcludeVolume" ${stopConditions.ActionOnMatch === 'ExcludeVolume' ? 'selected' : ''}>Exclude Volume</option>
                            <option value="SetWorkload" ${stopConditions.ActionOnMatch === 'SetWorkload' ? 'selected' : ''}>Set Workload</option>
                            <option value="SkipRemaining" ${stopConditions.ActionOnMatch === 'SkipRemaining' ? 'selected' : ''}>Skip Remaining Prompts</option>
                        </select>
                    </div>

                    <div id="workloadPicker" style="margin-top: 15px; display: ${stopConditions.ActionOnMatch === 'SetWorkload' ? 'block' : 'none'};">
                        <label>Target Workload</label>
                        <select id="targetWorkload">
                            <option value="">Select workload...</option>
                        </select>
                    </div>
                </div>
            </div>

            <div style="display: flex; gap: 10px; margin-top: 30px;">
                <button class="btn btn-primary" onclick="analysisPrompts.save('${prompt.PromptId || ''}')">Save Prompt</button>
                <button class="btn" onclick="analysisPrompts.closeModal()">Cancel</button>
            </div>
        `;

        // Setup event listeners
        document.getElementById('stopOnMatch').addEventListener('change', (e) => {
            document.getElementById('stopAction').disabled = !e.target.checked;
        });

        document.getElementById('stopAction').addEventListener('change', (e) => {
            document.getElementById('workloadPicker').style.display = e.target.value === 'SetWorkload' ? 'block' : 'none';
        });

        // Load workload profiles for picker
        this.loadWorkloadProfiles();

        modal.classList.add('show');
    },

    async loadWorkloadProfiles() {
        try {
            const response = await fetch('/api/workload-profiles');
            const profiles = await response.json();
            
            const select = document.getElementById('targetWorkload');
            if (select) {
                select.innerHTML = '<option value="">Select workload...</option>' + 
                    profiles.map(p => `<option value="${p.ProfileId}">${this.escapeHtml(p.Name)}</option>`).join('');
            }
        } catch (error) {
            console.error('Error loading workload profiles:', error);
        }
    },

    insertVariable(variable) {
        const textarea = document.getElementById('promptTemplate');
        const pos = textarea.selectionStart;
        const text = textarea.value;
        textarea.value = text.substring(0, pos) + variable + text.substring(pos);
        textarea.focus();
        textarea.setSelectionRange(pos + variable.length, pos + variable.length);
    },

    closeModal() {
        document.getElementById('promptModal').classList.remove('show');
    },

    async save(promptId) {
        const prompt = {
            PromptId: promptId || null,
            Name: document.getElementById('promptName').value,
            Category: document.getElementById('promptCategory').value,
            Priority: parseInt(document.getElementById('promptPriority').value),
            PromptTemplate: document.getElementById('promptTemplate').value,
            Enabled: true,
            StopConditions: {
                StopOnMatch: document.getElementById('stopOnMatch').checked,
                ActionOnMatch: document.getElementById('stopAction').value,
                TargetWorkloadId: document.getElementById('targetWorkload').value || null
            }
        };

        try {
            await this.savePrompt(prompt);
            alert('Prompt saved successfully');
            this.closeModal();
            await this.loadPrompts();
        } catch (error) {
            console.error('Error saving prompt:', error);
            alert('Error saving prompt');
        }
    },

    async savePrompt(prompt) {
        const url = prompt.PromptId ? `/api/analysis-prompts/${prompt.PromptId}` : '/api/analysis-prompts';
        const method = prompt.PromptId ? 'PUT' : 'POST';

        const response = await fetch(url, {
            method: method,
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(prompt)
        });

        if (!response.ok) throw new Error('Failed to save prompt');
    },

    async deletePrompt(promptId) {
        if (!confirm('Are you sure you want to delete this prompt?')) return;

        try {
            const response = await fetch(`/api/analysis-prompts/${promptId}`, { method: 'DELETE' });
            if (!response.ok) throw new Error('Failed to delete prompt');

            alert('Prompt deleted successfully');
            await this.loadPrompts();
        } catch (error) {
            console.error('Error deleting prompt:', error);
            alert('Error deleting prompt');
        }
    },

    truncate(text, length) {
        if (!text) return '';
        return text.length > length ? text.substring(0, length) + '...' : text;
    },

    escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }
};

document.addEventListener('DOMContentLoaded', () => {
    analysisPrompts.init();
});
