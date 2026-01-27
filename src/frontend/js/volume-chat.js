const volumeChat = {
    selectedJobId: null,
    conversationHistory: [],
    apiBaseUrl: window.location.hostname === 'localhost' || window.location.hostname === '127.0.0.1' 
        ? 'http://localhost:7071/api' 
        : '/api',

    init: async function() {
        await this.loadJobs();
        this.setupEventListeners();
        
        // Check for job ID in URL
        const params = new URLSearchParams(window.location.search);
        const jobId = params.get('jobId');
        if (jobId) {
            document.getElementById('jobSelector').value = jobId;
            await this.loadJobContext();
        }
    },

    setupEventListeners: function() {
        const chatInput = document.getElementById('chatInput');
        
        // Auto-resize textarea
        chatInput.addEventListener('input', function() {
            this.style.height = 'auto';
            this.style.height = Math.min(this.scrollHeight, 120) + 'px';
            volumeChat.updateSendButton();
        });

        // Enable send button only when there's text and a job is selected
        chatInput.addEventListener('input', () => this.updateSendButton());
    },

    updateSendButton: function() {
        const input = document.getElementById('chatInput');
        const sendBtn = document.getElementById('sendBtn');
        sendBtn.disabled = !input.value.trim() || !this.selectedJobId;
    },

    loadJobs: async function() {
        try {
            const response = await fetch(`${this.apiBaseUrl}/discovery/jobs`);
            if (!response.ok) throw new Error('Failed to load jobs');
            
            const jobs = await response.json();
            const jobSelector = document.getElementById('jobSelector');
            
            jobs.forEach(job => {
                const option = document.createElement('option');
                option.value = job.JobId;
                option.textContent = `${job.JobName || job.JobId} (${new Date(job.StartTime).toLocaleDateString()})`;
                jobSelector.appendChild(option);
            });
        } catch (error) {
            console.error('Error loading jobs:', error);
            this.showError('Failed to load discovery jobs');
        }
    },

    loadJobContext: async function() {
        const jobId = document.getElementById('jobSelector').value;
        
        if (!jobId) {
            this.selectedJobId = null;
            document.getElementById('contextStats').style.display = 'none';
            this.updateSendButton();
            return;
        }

        this.selectedJobId = jobId;
        this.conversationHistory = [];
        this.clearMessages();
        this.updateSendButton();

        try {
            // Load volume data to show context
            const response = await fetch(`${this.apiBaseUrl}/discovery/${jobId}/volumes`);
            if (!response.ok) throw new Error('Failed to load job context');
            
            const data = await response.json();
            
            // Update stats
            document.getElementById('statVolumes').textContent = data.TotalCount || 0;
            const totalSizeGB = Math.round((data.TotalSize || 0) / (1024 * 1024 * 1024));
            document.getElementById('statSize').textContent = totalSizeGB.toLocaleString() + ' GB';
            
            document.getElementById('contextStats').style.display = 'block';

            // Show welcome message
            this.addWelcomeMessage();
        } catch (error) {
            console.error('Error loading job context:', error);
            this.showError('Failed to load job context');
        }
    },

    addWelcomeMessage: function() {
        const messagesContainer = document.getElementById('chatMessages');
        messagesContainer.innerHTML = '';
        
        const welcomeMsg = this.createMessage('assistant', 
            `Hello! I'm your AI assistant for analyzing Azure Files volumes. I have context about this discovery job and can help you understand your volumes, identify migration candidates, and answer questions about workload classifications.\n\nWhat would you like to know?`
        );
        
        messagesContainer.appendChild(welcomeMsg);
    },

    clearMessages: function() {
        const messagesContainer = document.getElementById('chatMessages');
        messagesContainer.innerHTML = `
            <div class="empty-state">
                <div class="empty-state-icon">💬</div>
                <h3>Start a conversation</h3>
                <p>Select a discovery job and ask me anything about your Azure Files volumes</p>
            </div>
        `;
    },

    handleKeyPress: function(event) {
        // Send on Enter (without Shift)
        if (event.key === 'Enter' && !event.shiftKey) {
            event.preventDefault();
            this.sendMessage();
        }
    },

    askExample: function(question) {
        if (!this.selectedJobId) {
            alert('Please select a discovery job first');
            return;
        }
        document.getElementById('chatInput').value = question;
        this.updateSendButton();
        this.sendMessage();
    },

    sendMessage: async function() {
        const chatInput = document.getElementById('chatInput');
        const userMessage = chatInput.value.trim();
        
        if (!userMessage || !this.selectedJobId) return;

        // Clear input
        chatInput.value = '';
        chatInput.style.height = 'auto';
        this.updateSendButton();

        // Add user message to UI
        const messagesContainer = document.getElementById('chatMessages');
        
        // Remove empty state if present
        const emptyState = messagesContainer.querySelector('.empty-state');
        if (emptyState) {
            emptyState.remove();
        }

        const userMsgElement = this.createMessage('user', userMessage);
        messagesContainer.appendChild(userMsgElement);

        // Add to conversation history
        this.conversationHistory.push({
            role: 'user',
            content: userMessage
        });

        // Show typing indicator
        const typingIndicator = this.createTypingIndicator();
        messagesContainer.appendChild(typingIndicator);
        
        // Scroll to bottom
        this.scrollToBottom();

        try {
            // Call chat API
            const response = await fetch(`${this.apiBaseUrl}/discovery/${this.selectedJobId}/chat`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({
                    Message: userMessage,
                    ConversationHistory: this.conversationHistory.slice(0, -1) // Exclude the message we just added
                })
            });

            // Remove typing indicator
            typingIndicator.remove();

            if (!response.ok) {
                const errorData = await response.json().catch(() => ({}));
                throw new Error(errorData.error || 'Failed to get response from AI');
            }

            const result = await response.json();
            const assistantMessage = result.Response;

            // Add assistant message to UI
            const assistantMsgElement = this.createMessage('assistant', assistantMessage);
            messagesContainer.appendChild(assistantMsgElement);

            // Add to conversation history
            this.conversationHistory.push({
                role: 'assistant',
                content: assistantMessage
            });

            this.scrollToBottom();

        } catch (error) {
            console.error('Error sending message:', error);
            typingIndicator.remove();
            
            const errorMsgElement = this.createMessage('assistant', 
                `❌ Sorry, I encountered an error: ${error.message}`
            );
            messagesContainer.appendChild(errorMsgElement);
            
            this.scrollToBottom();
        }
    },

    createMessage: function(role, content) {
        const messageDiv = document.createElement('div');
        messageDiv.className = `message ${role}`;

        const avatar = document.createElement('div');
        avatar.className = 'message-avatar';
        avatar.textContent = role === 'user' ? '👤' : '🤖';

        const messageContent = document.createElement('div');
        messageContent.style.flex = '1';

        const contentDiv = document.createElement('div');
        contentDiv.className = 'message-content';
        
        // Format content with basic markdown-like styling
        contentDiv.innerHTML = this.formatContent(content);

        const timeDiv = document.createElement('div');
        timeDiv.className = 'message-time';
        timeDiv.textContent = new Date().toLocaleTimeString();

        messageContent.appendChild(contentDiv);
        messageContent.appendChild(timeDiv);

        messageDiv.appendChild(avatar);
        messageDiv.appendChild(messageContent);

        return messageDiv;
    },

    formatContent: function(text) {
        // Basic formatting: convert **bold**, *italic*, `code`, and preserve line breaks
        let formatted = text
            .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
            .replace(/\*(.+?)\*/g, '<em>$1</em>')
            .replace(/`([^`]+)`/g, '<code>$1</code>')
            .replace(/\n/g, '<br>');

        // Convert bullet points
        formatted = formatted.replace(/^- (.+)/gm, '• $1');

        return formatted;
    },

    createTypingIndicator: function() {
        const indicator = document.createElement('div');
        indicator.className = 'message assistant';
        
        const avatar = document.createElement('div');
        avatar.className = 'message-avatar';
        avatar.textContent = '🤖';

        const typingDiv = document.createElement('div');
        typingDiv.className = 'typing-indicator show';
        
        const dotsDiv = document.createElement('div');
        dotsDiv.className = 'typing-dots';
        dotsDiv.innerHTML = '<div class="typing-dot"></div><div class="typing-dot"></div><div class="typing-dot"></div>';
        
        typingDiv.appendChild(dotsDiv);
        indicator.appendChild(avatar);
        indicator.appendChild(typingDiv);

        return indicator;
    },

    scrollToBottom: function() {
        const messagesContainer = document.getElementById('chatMessages');
        messagesContainer.scrollTop = messagesContainer.scrollHeight;
    },

    showError: function(message) {
        const messagesContainer = document.getElementById('chatMessages');
        const errorMsg = this.createMessage('assistant', `❌ ${message}`);
        messagesContainer.appendChild(errorMsg);
        this.scrollToBottom();
    }
};

// Initialize on page load
document.addEventListener('DOMContentLoaded', () => volumeChat.init());
