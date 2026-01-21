/**
 * API Client for AzFilesOptimizer Backend
 * Provides methods to interact with Azure Functions backend
 */

const API_BASE_URL = window.location.hostname === 'localhost' || window.location.hostname === '127.0.0.1'
    ? 'http://localhost:7071/api'
    : 'https://azfo-dev-func-xy76b.azurewebsites.net/api';

class ApiClient {
    /**
     * Generic fetch wrapper with error handling
     */
    async fetchJson(endpoint, options = {}) {
        // Basic heuristic to avoid logging our own client-log calls and unrelated endpoints
        const shouldLogClientEvent = !endpoint.startsWith('/jobs/') || !endpoint.includes('/client-log');
        const method = (options.method || 'GET').toUpperCase();
        const jobIdFromPath = this.tryExtractJobIdFromEndpoint(endpoint);

        try {
            // Get access token if user is signed in
            let headers = {
                'Content-Type': 'application/json',
                ...options.headers
            };

            if (typeof authManager !== 'undefined' && authManager.isSignedIn()) {
                try {
                    const token = await authManager.getAccessToken();
                    headers['Authorization'] = `Bearer ${token}`;
                } catch (error) {
                    console.warn('Failed to get access token:', error);
                    // Continue without token for endpoints that don't require auth
                }
            }

            // Log outbound API call as a client event when we can tie it to a job
            if (shouldLogClientEvent && jobIdFromPath) {
                this.logClientJobEvent(jobIdFromPath, {
                    Message: `[API Request] ${method} ${endpoint}`,
                    Source: 'api-client'
                }).catch(() => { /* best-effort */ });
            }

            const response = await fetch(`${API_BASE_URL}${endpoint}`, {
                ...options,
                headers: headers
            });

            if (shouldLogClientEvent && jobIdFromPath) {
                const ok = response.ok;
                const status = response.status;
                const statusText = response.statusText;
                const msg = ok
                    ? `[API Response] ${method} ${endpoint} -> ${status} ${statusText}`
                    : `[API Error] ${method} ${endpoint} -> ${status} ${statusText}`;
                this.logClientJobEvent(jobIdFromPath, {
                    Message: msg,
                    Source: 'api-client'
                }).catch(() => { /* ignore logging failures */ });
            }

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}: ${response.statusText}`);
            }

            return await response.json();
        } catch (error) {
            console.error(`API Error [${endpoint}]:`, error);
            throw error;
        }
    }

    tryExtractJobIdFromEndpoint(endpoint) {
        // Handles patterns like /jobs/{jobId}/..., /discovery/{jobId}/...
        if (!endpoint || typeof endpoint !== 'string') return null;
        const parts = endpoint.split('?')[0].split('/').filter(p => p);
        for (let i = 0; i < parts.length - 1; i++) {
            const p = parts[i].toLowerCase();
            if (p === 'jobs' || p === 'discovery') {
                return parts[i + 1] || null;
            }
        }
        return null;
    }

    async logClientJobEvent(jobId, payload) {
        try {
            await fetch(`${API_BASE_URL}/jobs/${encodeURIComponent(jobId)}/client-log`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });
        } catch {
            // Swallow logging errors - this should never break primary flows
        }
    }

    /**
     * Get health status of the backend
     */
    async getHealth() {
        return this.fetchJson('/health');
    }

    /**
     * Get list of all jobs
     */
    async getJobs() {
        const response = await this.fetchJson('/jobs');
        // Azure Table Storage returns { value: [...], Count: N }
        return response.value || response;
    }

    /**
     * Get details of a specific job
     */
    async getJob(jobId) {
        return this.fetchJson(`/jobs/${jobId}`);
    }

    /**
     * Create a new discovery job
     */
    async createDiscoveryJob(config) {
        return this.fetchJson('/jobs/discovery', {
            method: 'POST',
            body: JSON.stringify(config)
        });
    }

    /**
     * Create a new optimization job
     */
    async createOptimizationJob(config) {
        return this.fetchJson('/jobs/optimization', {
            method: 'POST',
            body: JSON.stringify(config)
        });
    }

    /**
     * Manually trigger a job to run
     */
    async triggerJob(jobId) {
        return this.fetchJson(`/jobs/${jobId}/trigger`, {
            method: 'POST'
        });
    }

    /**
     * Get logs for a job
     */
    async getJobLogs(jobId) {
        const response = await this.fetchJson(`/jobs/${jobId}/logs`);
        // Azure Table Storage returns { value: [...], Count: N }
        return response.value || response;
    }

    /**
     * Re-run a discovery job
     */
    async rerunJob(jobId) {
        return this.fetchJson(`/jobs/${jobId}/rerun`, {
            method: 'POST'
        });
    }
    
    /**
     * Delete a job
     */
    async deleteJob(jobId) {
        return this.fetchJson(`/jobs/${jobId}`, {
            method: 'DELETE'
        });
    }

    /**
     * Get discovered shares and volumes for a job
     */
    async getJobShares(jobId) {
        return this.fetchJson(`/jobs/${jobId}/shares`);
    }

    /**
     * Get raw metrics JSON for a share (account-level for now)
     */
    async getShareMetricsRaw(jobId, resourceId, days = 30) {
        const encoded = encodeURIComponent(resourceId);
        return this.fetchJson(`/jobs/${jobId}/shares/metricsraw?resourceId=${encoded}&days=${days}`);
    }
    
    /**
     * Get list of accessible Azure subscriptions
     */
    async getSubscriptions() {
        return this.fetchJson('/subscriptions');
    }
    
    /**
     * Get list of resource groups in a subscription
     */
    async getResourceGroups(subscriptionId) {
        return this.fetchJson(`/subscriptions/${subscriptionId}/resourcegroups`);
    }
}

// Export singleton instance
const apiClient = new ApiClient();
