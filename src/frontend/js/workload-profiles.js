const workloadProfiles = {
    profiles: [],
    selectedProfile: null,

    init() {
        this.loadProfiles();
    },

    async loadProfiles() {
        try {
            const response = await fetch(`${API_BASE_URL}/workload-profiles`);
            if (!response.ok) throw new Error('Failed to load profiles');
            this.profiles = await response.json();
            this.renderProfilesList();
        } catch (error) {
            console.error('Error loading profiles:', error);
            alert('Error loading workload profiles: ' + error.message);
        }
    },

    renderProfilesList() {
        const listEl = document.getElementById('profilesList');
        if (this.profiles.length === 0) {
            listEl.innerHTML = '<p style="color: #999; padding: 20px; text-align: center;">No profiles found</p>';
            return;
        }

        listEl.innerHTML = this.profiles.map(profile => `
            <div class="profile-item ${profile.IsSystemProfile ? 'system' : ''} ${profile.IsExclusionProfile ? 'exclusion' : ''} ${this.selectedProfile?.ProfileId === profile.ProfileId ? 'selected' : ''}" 
                 onclick="workloadProfiles.selectProfile('${profile.ProfileId}')">
                <div style="font-weight: 600; margin-bottom: 5px;">${this.escapeHtml(profile.Name)}</div>
                <div style="font-size: 12px; color: #666;">
                    ${profile.IsSystemProfile ? '<span class="badge badge-system">System</span> ' : ''}
                    ${profile.IsExclusionProfile ? '<span class="badge badge-exclusion">Exclusion</span>' : ''}
                </div>
            </div>
        `).join('');
    },

    selectProfile(profileId) {
        this.selectedProfile = this.profiles.find(p => p.ProfileId === profileId);
        this.renderProfilesList();
        this.renderEditor();
    },

    createNew() {
        this.selectedProfile = {
            ProfileId: null,
            Name: '',
            Description: '',
            IsSystemProfile: false,
            IsExclusionProfile: false,
            PerformanceRequirementsJson: JSON.stringify({
                MinSizeGB: null,
                MaxSizeGB: null,
                MinIops: null,
                MaxIops: null,
                LatencySensitivity: 'Medium',
                MinThroughputMBps: null,
                MaxThroughputMBps: null,
                IoPattern: null
            }),
            AnfSuitabilityJson: JSON.stringify({
                Compatible: true,
                RecommendedServiceLevel: 'Standard',
                Notes: '',
                Caveats: []
            }),
            DetectionHintsJson: JSON.stringify({
                NamingPatterns: [],
                CommonTags: [],
                FileTypeIndicators: [],
                PathPatterns: []
            })
        };
        this.renderProfilesList();
        this.renderEditor();
    },

    renderEditor() {
        const editorEl = document.getElementById('editorContent');
        
        if (!this.selectedProfile) {
            editorEl.innerHTML = '<div class="empty-state"><p>Select a profile to edit or create a new one</p></div>';
            return;
        }

        const perf = this.selectedProfile.PerformanceRequirementsJson ? JSON.parse(this.selectedProfile.PerformanceRequirementsJson) : {};
        const anf = this.selectedProfile.AnfSuitabilityJson ? JSON.parse(this.selectedProfile.AnfSuitabilityJson) : {};
        const hints = this.selectedProfile.DetectionHintsJson ? JSON.parse(this.selectedProfile.DetectionHintsJson) : {};

        const isReadOnly = this.selectedProfile.IsSystemProfile;

        editorEl.innerHTML = `
            <h2>${this.selectedProfile.ProfileId ? 'Edit' : 'New'} Workload Profile</h2>
            
            <div class="form-group">
                <label>Name *</label>
                <input type="text" id="profileName" value="${this.escapeHtml(this.selectedProfile.Name || '')}" ${isReadOnly ? 'readonly' : ''}>
            </div>

            <div class="form-group">
                <label>Description *</label>
                <textarea id="profileDescription" rows="4" ${isReadOnly ? 'readonly' : ''}>${this.escapeHtml(this.selectedProfile.Description || '')}</textarea>
            </div>

            <h3>Performance Requirements</h3>
            <div class="form-row">
                <div class="form-group">
                    <label>Min Size (GB)</label>
                    <input type="number" id="perfMinSize" value="${perf.MinSizeGB || ''}" ${isReadOnly ? 'readonly' : ''}>
                </div>
                <div class="form-group">
                    <label>Max Size (GB)</label>
                    <input type="number" id="perfMaxSize" value="${perf.MaxSizeGB || ''}" ${isReadOnly ? 'readonly' : ''}>
                </div>
            </div>

            <div class="form-row">
                <div class="form-group">
                    <label>Min IOPS</label>
                    <input type="number" id="perfMinIops" value="${perf.MinIops || ''}" ${isReadOnly ? 'readonly' : ''}>
                </div>
                <div class="form-group">
                    <label>Max IOPS</label>
                    <input type="number" id="perfMaxIops" value="${perf.MaxIops || ''}" ${isReadOnly ? 'readonly' : ''}>
                </div>
            </div>

            <div class="form-row">
                <div class="form-group">
                    <label>Latency Sensitivity</label>
                    <select id="perfLatency" ${isReadOnly ? 'disabled' : ''}>
                        <option value="Low" ${perf.LatencySensitivity === 'Low' ? 'selected' : ''}>Low</option>
                        <option value="Medium" ${perf.LatencySensitivity === 'Medium' ? 'selected' : ''}>Medium</option>
                        <option value="High" ${perf.LatencySensitivity === 'High' ? 'selected' : ''}>High</option>
                        <option value="VeryHigh" ${perf.LatencySensitivity === 'VeryHigh' ? 'selected' : ''}>Very High</option>
                        <option value="Ultra" ${perf.LatencySensitivity === 'Ultra' ? 'selected' : ''}>Ultra</option>
                    </select>
                </div>
                <div class="form-group">
                    <label>I/O Pattern</label>
                    <select id="perfIoPattern" ${isReadOnly ? 'disabled' : ''}>
                        <option value="">Not specified</option>
                        <option value="Sequential" ${perf.IoPattern === 'Sequential' ? 'selected' : ''}>Sequential</option>
                        <option value="Random" ${perf.IoPattern === 'Random' ? 'selected' : ''}>Random</option>
                        <option value="Mixed" ${perf.IoPattern === 'Mixed' ? 'selected' : ''}>Mixed</option>
                    </select>
                </div>
            </div>

            <h3>ANF Suitability</h3>
            <div class="form-group">
                <label><input type="checkbox" id="anfCompatible" ${anf.Compatible ? 'checked' : ''} ${isReadOnly ? 'disabled' : ''}> Compatible with ANF</label>
            </div>

            <div class="form-row">
                <div class="form-group">
                    <label>Recommended Service Level</label>
                    <select id="anfServiceLevel" ${isReadOnly ? 'disabled' : ''}>
                        <option value="">Not specified</option>
                        <option value="Standard" ${anf.RecommendedServiceLevel === 'Standard' ? 'selected' : ''}>Standard</option>
                        <option value="Premium" ${anf.RecommendedServiceLevel === 'Premium' ? 'selected' : ''}>Premium</option>
                        <option value="Ultra" ${anf.RecommendedServiceLevel === 'Ultra' ? 'selected' : ''}>Ultra</option>
                    </select>
                </div>
                <div class="form-group">
                    <label><input type="checkbox" id="isExclusion" ${this.selectedProfile.IsExclusionProfile ? 'checked' : ''} ${isReadOnly ? 'disabled' : ''}> Exclusion Profile</label>
                </div>
            </div>

            <div class="form-group">
                <label>ANF Notes</label>
                <textarea id="anfNotes" rows="3" ${isReadOnly ? 'readonly' : ''}>${this.escapeHtml(anf.Notes || '')}</textarea>
            </div>

            <h3>Detection Hints</h3>
            <div class="form-group">
                <label>Naming Patterns (comma-separated)</label>
                <input type="text" id="hintNaming" value="${(hints.NamingPatterns || []).join(', ')}" ${isReadOnly ? 'readonly' : ''}>
            </div>

            <div class="form-group">
                <label>Common Tags (comma-separated)</label>
                <input type="text" id="hintTags" value="${(hints.CommonTags || []).join(', ')}" ${isReadOnly ? 'readonly' : ''}>
            </div>

            <div class="button-group">
                <button class="btn btn-primary" onclick="workloadProfiles.save()" ${isReadOnly ? 'disabled' : ''}>Save Profile</button>
                <button class="btn btn-danger" onclick="workloadProfiles.deleteProfile()" ${isReadOnly || !this.selectedProfile.ProfileId ? 'disabled' : ''}>Delete</button>
                <button class="btn" onclick="workloadProfiles.cancel()">Cancel</button>
            </div>
        `;
    },

    async save() {
        const profile = {
            ProfileId: this.selectedProfile.ProfileId,
            Name: document.getElementById('profileName').value,
            Description: document.getElementById('profileDescription').value,
            IsExclusionProfile: document.getElementById('isExclusion').checked,
            PerformanceRequirementsJson: JSON.stringify({
                MinSizeGB: this.parseNumber('perfMinSize'),
                MaxSizeGB: this.parseNumber('perfMaxSize'),
                MinIops: this.parseNumber('perfMinIops'),
                MaxIops: this.parseNumber('perfMaxIops'),
                LatencySensitivity: document.getElementById('perfLatency').value,
                IoPattern: document.getElementById('perfIoPattern').value || null
            }),
            AnfSuitabilityJson: JSON.stringify({
                Compatible: document.getElementById('anfCompatible').checked,
                RecommendedServiceLevel: document.getElementById('anfServiceLevel').value || null,
                Notes: document.getElementById('anfNotes').value,
                Caveats: []
            }),
            DetectionHintsJson: JSON.stringify({
                NamingPatterns: this.parseArray('hintNaming'),
                CommonTags: this.parseArray('hintTags'),
                FileTypeIndicators: [],
                PathPatterns: []
            })
        };

        try {
            const url = profile.ProfileId ? `${API_BASE_URL}/workload-profiles/${profile.ProfileId}` : `${API_BASE_URL}/workload-profiles`;
            const method = profile.ProfileId ? 'PUT' : 'POST';
            
            const response = await fetch(url, {
                method: method,
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(profile)
            });

            if (!response.ok) throw new Error('Failed to save profile');

            alert('Profile saved successfully');
            this.selectedProfile = null;
            await this.loadProfiles();
        } catch (error) {
            console.error('Error saving profile:', error);
            alert('Error saving profile');
        }
    },

    async deleteProfile() {
        if (!confirm('Are you sure you want to delete this profile?')) return;

        try {
            const response = await fetch(`${API_BASE_URL}/workload-profiles/${this.selectedProfile.ProfileId}`, {
                method: 'DELETE'
            });

            if (!response.ok) throw new Error('Failed to delete profile');

            alert('Profile deleted successfully');
            this.selectedProfile = null;
            await this.loadProfiles();
        } catch (error) {
            console.error('Error deleting profile:', error);
            alert('Error deleting profile');
        }
    },

    async seedProfiles() {
        if (!confirm('This will create default system profiles. Continue?')) return;

        try {
            const response = await fetch(`${API_BASE_URL}/workload-profiles/seed`, { method: 'POST' });
            if (!response.ok) throw new Error('Failed to seed profiles');

            alert('Default profiles created successfully');
            await this.loadProfiles();
        } catch (error) {
            console.error('Error seeding profiles:', error);
            alert('Error seeding profiles');
        }
    },

    cancel() {
        this.selectedProfile = null;
        this.renderProfilesList();
        this.renderEditor();
    },

    parseNumber(id) {
        const value = document.getElementById(id).value;
        return value ? parseInt(value) : null;
    },

    parseArray(id) {
        const value = document.getElementById(id).value;
        return value ? value.split(',').map(s => s.trim()).filter(s => s) : [];
    },

    escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }
};

// Initialize on page load
document.addEventListener('DOMContentLoaded', () => {
    workloadProfiles.init();
});
