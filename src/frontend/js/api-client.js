/**
 * API Client for AzFilesOptimizer Backend
 * Provides methods to interact with Azure Functions backend
 */

const API_BASE_URL = window.location.hostname === 'localhost' || window.location.hostname === '127.0.0.1'
    ? 'http://localhost:7071/api'
    : '/api';

class ApiClient {
    /**
     * Generic fetch wrapper with error handling
     */
    async fetchJson(endpoint, options = {}) {
        try {
            const response = await fetch(`${API_BASE_URL}${endpoint}`, {
                ...options,
                headers: {
                    'Content-Type': 'application/json',
                    ...options.headers
                }
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
        return this.fetchJson('/jobs');
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
}

// Export singleton instance
const apiClient = new ApiClient();
