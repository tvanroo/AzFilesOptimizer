// Simplified settings functionality for picking a single preferred AI model

let availableModels = [];
let selectedModel = '';

function toggleModelsList() {
    const container = document.getElementById('models-list-container');
    const btn = document.getElementById('toggle-models-btn');

    if (!container || !btn) {
        return;
    }

    if (container.style.display === 'none') {
        container.style.display = 'block';
        btn.textContent = 'Hide All';
    } else {
        container.style.display = 'none';
        btn.textContent = 'Show All';
    }
}

function copyApiKey() {
    const keyElement = document.getElementById('status-key');
    if (!keyElement) {
        return;
    }

    navigator.clipboard.writeText(keyElement.textContent)
        .then(() => Toast.success('API key copied to clipboard'))
        .catch(() => Toast.error('Failed to copy API key'));
}

function showUpdateForm() {
    document.getElementById('current-status').style.display = 'none';
    document.getElementById('configure-form').style.display = 'block';

    const status = window.currentApiStatus;
    if (status) {
        document.getElementById('provider').value = status.provider || 'OpenAI';
        if (status.endpoint) {
            document.getElementById('endpoint').value = status.endpoint;
        }
        toggleEndpointField();
    }
}

async function testApiKey(event) {
    const button = event?.target;
    const originalText = button ? button.textContent : '';

    if (button) {
        button.disabled = true;
        button.textContent = 'Testing...';
    }

    try {
        const API_BASE_URL = window.location.hostname === 'localhost' || window.location.hostname === '127.0.0.1'
            ? 'http://localhost:7071/api'
            : 'https://azfo-dev-func-xy76b.azurewebsites.net/api';

        const response = await fetch(`${API_BASE_URL}/settings/openai-key/test`, {
            method: 'POST',
            headers: authManager.isSignedIn() ? {
                'Authorization': `Bearer ${await authManager.getAccessToken()}`
            } : {}
        });

        const result = await response.json();

        if (!response.ok || !result.ok) {
            const message = result.error || 'API key test failed';
            Toast.error(message);
            return;
        }

        const latency = typeof result.latencyMs === 'number'
            ? Math.round(result.latencyMs)
            : null;

        const modelLabel = result.model || 'model';
        const latencyLabel = latency !== null ? ` in ${latency} ms` : '';

        const answer = (result.sampleResponse || '').trim();
        const answerPreview = answer ? ` Answer: "${answer.slice(0, 80)}${answer.length > 80 ? '…' : ''}"` : '';

        Toast.success(`API key and ${modelLabel} are responding${latencyLabel}.${answerPreview}`);
    } catch (error) {
        console.error('Error testing API key:', error);
        Toast.error(error.message || 'Failed to test API key');
    } finally {
        if (button) {
            button.disabled = false;
            button.textContent = originalText || 'Test API Key & Model';
        }
    }
}

async function loadPreferences() {
    try {
        const API_BASE_URL = window.location.hostname === 'localhost' || window.location.hostname === '127.0.0.1'
            ? 'http://localhost:7071/api'
            : 'https://azfo-dev-func-xy76b.azurewebsites.net/api';

        const response = await fetch(`${API_BASE_URL}/settings/model-preferences`, {
            headers: authManager.isSignedIn() ? {
                'Authorization': `Bearer ${await authManager.getAccessToken()}`
            } : {}
        });

        if (!response.ok) {
            throw new Error('Failed to load preferences');
        }

        const data = await response.json();
        availableModels = data.availableModels || data.AvailableModels || [];

        const prefs = data.preferences || data.Preferences || {};
        const preferredModels = prefs.preferredModels || prefs.PreferredModels || [];
        selectedModel = preferredModels.length > 0 ? preferredModels[0] : '';

        renderModelSelect();
    } catch (error) {
        console.error('Error loading preferences:', error);
        Toast.error(error.message || 'Failed to load model preferences');
    }
}

function renderModelSelect() {
    const selectElement = document.getElementById('preferred-model-select');
    const helper = document.getElementById('model-select-helper');

    if (!selectElement || !helper) {
        return;
    }

    selectElement.innerHTML = '';

    if (!availableModels || availableModels.length === 0) {
        const option = document.createElement('option');
        option.value = '';
        option.textContent = 'No models available';
        selectElement.appendChild(option);

        selectElement.disabled = true;
        helper.textContent = 'Add and validate an API key to load model deployments.';
        selectedModel = '';
        return;
    }

    selectElement.disabled = false;

    availableModels.forEach(model => {
        const option = document.createElement('option');
        option.value = model;
        option.textContent = model;
        selectElement.appendChild(option);
    });

    if (!selectedModel || !availableModels.includes(selectedModel)) {
        selectedModel = availableModels[0];
    }

    selectElement.value = selectedModel;
    helper.textContent = 'AzFilesOptimizer will always start with this model. Update your API key if the list changes.';
}

async function savePreferences() {
    if (!selectedModel) {
        Toast.error('Please select a model before saving.');
        return;
    }

    try {
        const API_BASE_URL = window.location.hostname === 'localhost' || window.location.hostname === '127.0.0.1'
            ? 'http://localhost:7071/api'
            : 'https://azfo-dev-func-xy76b.azurewebsites.net/api';

        const response = await fetch(`${API_BASE_URL}/settings/model-preferences`, {
            method: 'PUT',
            headers: {
                'Content-Type': 'application/json',
                ...(authManager.isSignedIn() ? {
                    'Authorization': `Bearer ${await authManager.getAccessToken()}`
                } : {})
            },
            body: JSON.stringify({
                preferredModels: [selectedModel],
                allowedModels: [],
                blockedModels: []
            })
        });

        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.error || 'Failed to save preferences');
        }

        Toast.success('Model preference saved');
        loadStatus();
    } catch (error) {
        console.error('Error saving preferences:', error);
        Toast.error(error.message || 'Failed to save preferences');
    }
}

document.addEventListener('DOMContentLoaded', () => {
    const selectElement = document.getElementById('preferred-model-select');
    if (selectElement) {
        selectElement.addEventListener('change', (event) => {
            selectedModel = event.target.value;
        });
    }
});
