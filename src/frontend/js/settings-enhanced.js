// Enhanced settings functionality for model preferences and improved display

let currentModels = [];
let currentPreferences = { preferredModels: [], allowedModels: [], blockedModels: [] };

function toggleModelsList() {
    const container = document.getElementById('models-list-container');
    const btn = document.getElementById('toggle-models-btn');
    
    if (container.style.display === 'none') {
        container.style.display = 'block';
        btn.textContent = 'Hide All';
    } else {
        container.style.display = 'none';
        btn.textContent = 'Show All';
    }
}

function copyApiKey() {
    const keyText = document.getElementById('status-key').textContent;
    navigator.clipboard.writeText(keyText).then(() => {
        Toast.success('API key copied to clipboard');
    }).catch(() => {
        Toast.error('Failed to copy API key');
    });
}

function showUpdateForm() {
    document.getElementById('current-status').style.display = 'none';
    document.getElementById('configure-form').style.display = 'block';
    
    // Pre-populate form with current values
    const status = window.currentApiStatus;
    if (status) {
        document.getElementById('provider').value = status.provider || 'OpenAI';
        if (status.endpoint) {
            document.getElementById('endpoint').value = status.endpoint;
        }
        toggleEndpointField();
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
        currentModels = data.availableModels || [];
        currentPreferences = data.preferences || { preferredModels: [], allowedModels: [], blockedModels: [] };
        
        renderPreferences();
    } catch (error) {
        console.error('Error loading preferences:', error);
    }
}

function renderPreferences() {
    const preferredContainer = document.getElementById('preferred-models');
    const allowedContainer = document.getElementById('allowed-models');
    const blockedContainer = document.getElementById('blocked-models');
    const uncategorizedContainer = document.getElementById('uncategorized-models');
    
    // Clear existing content
    preferredContainer.innerHTML = '';
    allowedContainer.innerHTML = '';
    blockedContainer.innerHTML = '';
    uncategorizedContainer.innerHTML = '';
    
    // Get search filter
    const searchTerm = document.getElementById('model-search')?.value.toLowerCase() || '';
    
    // Categorize models
    const categorized = new Set([
        ...currentPreferences.preferredModels,
        ...currentPreferences.allowedModels,
        ...currentPreferences.blockedModels
    ]);
    
    const filteredModels = currentModels.filter(m => m.toLowerCase().includes(searchTerm));
    
    // Render preferred models
    currentPreferences.preferredModels
        .filter(m => m.toLowerCase().includes(searchTerm))
        .forEach(model => {
            preferredContainer.appendChild(createModelChip(model, 'preferred'));
        });
    
    // Render allowed models
    currentPreferences.allowedModels
        .filter(m => m.toLowerCase().includes(searchTerm))
        .forEach(model => {
            allowedContainer.appendChild(createModelChip(model, 'allowed'));
        });
    
    // Render blocked models
    currentPreferences.blockedModels
        .filter(m => m.toLowerCase().includes(searchTerm))
        .forEach(model => {
            blockedContainer.appendChild(createModelChip(model, 'blocked'));
        });
    
    // Render uncategorized models
    filteredModels
        .filter(m => !categorized.has(m))
        .forEach(model => {
            uncategorizedContainer.appendChild(createModelChip(model, 'uncategorized'));
        });
}

function createModelChip(model, category) {
    const chip = document.createElement('div');
    chip.className = 'model-chip';
    chip.draggable = true;
    chip.dataset.model = model;
    chip.dataset.category = category;
    
    const colors = {
        preferred: { bg: '#e8f5e9', border: 'var(--success-color)', text: '#2e7d32' },
        allowed: { bg: '#e3f2fd', border: '#2196F3', text: '#1565c0' },
        blocked: { bg: '#ffebee', border: 'var(--error-color)', text: '#c62828' },
        uncategorized: { bg: '#f5f5f5', border: '#ccc', text: '#666' }
    };
    
    const color = colors[category];
    
    chip.style.cssText = `
        display: inline-block;
        padding: 0.4rem 0.75rem;
        margin: 0.25rem;
        background: ${color.bg};
        border: 1px solid ${color.border};
        border-radius: 16px;
        font-size: 0.75rem;
        color: ${color.text};
        cursor: move;
        user-select: none;
    `;
    
    chip.textContent = model;
    
    // Drag and drop events
    chip.addEventListener('dragstart', handleDragStart);
    chip.addEventListener('dragend', handleDragEnd);
    chip.addEventListener('click', () => handleModelClick(model, category));
    
    return chip;
}

function handleDragStart(e) {
    e.dataTransfer.effectAllowed = 'move';
    e.dataTransfer.setData('text/plain', e.target.dataset.model);
    e.target.style.opacity = '0.5';
}

function handleDragEnd(e) {
    e.target.style.opacity = '1';
}

function handleModelClick(model, currentCategory) {
    // Show context menu to move model
    const categories = ['preferred', 'allowed', 'blocked', 'uncategorized'];
    const nextCategory = categories[(categories.indexOf(currentCategory) + 1) % categories.length];
    moveModel(model, nextCategory);
}

function moveModel(model, toCategory) {
    // Remove from all categories
    currentPreferences.preferredModels = currentPreferences.preferredModels.filter(m => m !== model);
    currentPreferences.allowedModels = currentPreferences.allowedModels.filter(m => m !== model);
    currentPreferences.blockedModels = currentPreferences.blockedModels.filter(m => m !== model);
    
    // Add to new category
    if (toCategory === 'preferred') {
        currentPreferences.preferredModels.push(model);
    } else if (toCategory === 'allowed') {
        currentPreferences.allowedModels.push(model);
    } else if (toCategory === 'blocked') {
        currentPreferences.blockedModels.push(model);
    }
    
    renderPreferences();
}

// Setup drag and drop on category containers
function setupDragAndDrop() {
    const containers = ['preferred-models', 'allowed-models', 'blocked-models', 'uncategorized-models'];
    
    containers.forEach(containerId => {
        const container = document.getElementById(containerId);
        if (!container) return;
        
        container.addEventListener('dragover', (e) => {
            e.preventDefault();
            e.dataTransfer.dropEffect = 'move';
            container.style.opacity = '0.8';
        });
        
        container.addEventListener('dragleave', (e) => {
            container.style.opacity = '1';
        });
        
        container.addEventListener('drop', (e) => {
            e.preventDefault();
            container.style.opacity = '1';
            
            const model = e.dataTransfer.getData('text/plain');
            const category = containerId.replace('-models', '');
            moveModel(model, category);
        });
    });
}

async function savePreferences() {
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
            body: JSON.stringify(currentPreferences)
        });
        
        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.error || 'Failed to save preferences');
        }
        
        Toast.success('Model preferences saved successfully');
        loadStatus(); // Reload to update badges in models list
    } catch (error) {
        console.error('Error saving preferences:', error);
        Toast.error(error.message || 'Failed to save preferences');
    }
}

// Enhanced loadStatus to populate model list and preferences
function enhanceLoadStatus(originalLoadStatus) {
    return async function() {
        await originalLoadStatus();
        
        const status = window.currentApiStatus;
        if (status && status.configured && status.availableModels?.length > 0) {
            const modelsCount = status.availableModels.length;
            document.getElementById('status-models-count').textContent = `${modelsCount} models available`;
            document.getElementById('toggle-models-btn').style.display = 'inline-block';
            
            // Populate models list with preference badges
            const modelsList = document.getElementById('available-models-list');
            modelsList.innerHTML = status.availableModels
                .map(model => {
                    const prefs = status.preferences || {};
                    let badge = '';
                    let color = '#666';
                    
                    if (prefs.preferredModels?.includes(model)) {
                        badge = ' ✓';
                        color = 'var(--success-color)';
                    } else if (prefs.allowedModels?.includes(model)) {
                        badge = ' ○';
                        color = '#2196F3';
                    } else if (prefs.blockedModels?.includes(model)) {
                        badge = ' ✕';
                        color = 'var(--error-color)';
                    }
                    
                    return `<li style="color: ${color}; margin-bottom: 0.25rem;">${model}${badge}</li>`;
                })
                .join('');
            
            // Show preferences card and load preferences
            document.getElementById('preferences-card').style.display = 'block';
            loadPreferences();
        }
    };
}

// Initialize when DOM is ready
document.addEventListener('DOMContentLoaded', () => {
    setupDragAndDrop();
    
    // Add search filter handler
    const searchInput = document.getElementById('model-search');
    if (searchInput) {
        searchInput.addEventListener('input', renderPreferences);
    }
});
