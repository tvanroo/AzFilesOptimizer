// Toast notification system
const Toast = {
    container: null,
    
    init() {
        if (!this.container) {
            this.container = document.createElement('div');
            this.container.className = 'toast-container';
            document.body.appendChild(this.container);
        }
    },
    
    show(message, type = 'info', duration = 4000) {
        this.init();
        
        const toast = document.createElement('div');
        toast.className = `toast ${type}`;
        
        const icons = {
            success: '✓',
            error: '✕',
            warning: '⚠',
            info: 'ℹ'
        };
        
        toast.innerHTML = `
            <span class="toast-icon">${icons[type] || icons.info}</span>
            <span class="toast-message">${message}</span>
            <button class="toast-close" onclick="Toast.dismiss(this.parentElement)">×</button>
        `;
        
        this.container.appendChild(toast);
        
        // Auto-dismiss after duration
        if (duration > 0) {
            setTimeout(() => {
                this.dismiss(toast);
            }, duration);
        }
        
        return toast;
    },
    
    success(message, duration = 4000) {
        return this.show(message, 'success', duration);
    },
    
    error(message, duration = 5000) {
        return this.show(message, 'error', duration);
    },
    
    warning(message, duration = 4000) {
        return this.show(message, 'warning', duration);
    },
    
    info(message, duration = 4000) {
        return this.show(message, 'info', duration);
    },
    
    dismiss(toast) {
        if (!toast || !toast.parentElement) return;
        
        toast.classList.add('removing');
        setTimeout(() => {
            if (toast.parentElement) {
                toast.remove();
            }
        }, 300);
    },
    
    // Confirmation modal for destructive actions
    confirm(message, title = 'Confirm Action', confirmText = 'Confirm', cancelText = 'Cancel') {
        return new Promise((resolve) => {
            const modal = document.createElement('div');
            modal.className = 'confirm-modal show';
            modal.innerHTML = `
                <div class="confirm-content">
                    <h3>${title}</h3>
                    <p>${message}</p>
                    <div class="confirm-buttons">
                        <button class="btn" style="background-color: var(--surface); color: var(--text);" onclick="this.closest('.confirm-modal').dispatchEvent(new CustomEvent('cancel'))">${cancelText}</button>
                        <button class="btn" style="background-color: var(--error-color);" onclick="this.closest('.confirm-modal').dispatchEvent(new CustomEvent('confirm'))">${confirmText}</button>
                    </div>
                </div>
            `;
            
            modal.addEventListener('confirm', () => {
                modal.remove();
                resolve(true);
            });
            
            modal.addEventListener('cancel', () => {
                modal.remove();
                resolve(false);
            });
            
            // Close on background click
            modal.addEventListener('click', (e) => {
                if (e.target === modal) {
                    modal.dispatchEvent(new CustomEvent('cancel'));
                }
            });
            
            document.body.appendChild(modal);
        });
    }
};

// Make Toast available globally
window.Toast = Toast;
