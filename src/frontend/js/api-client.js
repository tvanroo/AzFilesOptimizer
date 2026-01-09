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

            const response = await fetch(`${API_BASE_URL}${endpoint}`, {
                ...options,
                headers: headers
            });

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}: ${response.statusText}`);
            }

            return await response.json();
        } catch (error) {
            console.error(`API Error [${endpoint}]:`, error);
            throw error;
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
