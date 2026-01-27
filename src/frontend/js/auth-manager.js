/**
 * Authentication Manager
 * Handles Azure AD authentication using MSAL.js
 */

class AuthManager {
    constructor() {
        this.msalInstance = null;
        this.currentAccount = null;
    }

    /**
     * Initialize MSAL
     */
    async initialize() {
        console.log('Starting MSAL initialization...');
        try {
            // Check if MSAL library is loaded
            if (typeof msal === 'undefined') {
                console.error('MSAL library is undefined');
                throw new Error('MSAL library not loaded. Make sure msal-browser.min.js is included before auth-manager.js');
            }
            console.log('MSAL library loaded successfully');
            
            // Create MSAL instance
            console.log('Creating PublicClientApplication with config:', msalConfig);
            this.msalInstance = new msal.PublicClientApplication(msalConfig);
            console.log('MSAL instance created successfully');
            
            // Handle redirect response (after login redirect)
            console.log('Handling redirect promise...');
            const response = await this.msalInstance.handleRedirectPromise();
            console.log('Redirect promise handled:', response);
            
            if (response) {
                this.currentAccount = response.account;
                console.log('User signed in from redirect:', this.currentAccount);
            } else {
                // Check if user is already logged in
                const accounts = this.msalInstance.getAllAccounts();
                console.log('Existing accounts found:', accounts.length);
                if (accounts.length > 0) {
                    this.currentAccount = accounts[0];
                    console.log('Using existing account:', this.currentAccount);
                }
            }
            
            console.log('MSAL initialization complete');
            return this.currentAccount;
        } catch (error) {
            console.error('Failed to initialize MSAL:', error);
            throw error;
        }
    }

    /**
     * Sign in using redirect
     */
    async signIn() {
        try {
            await this.msalInstance.loginRedirect(loginRequest);
        } catch (error) {
            console.error('Sign-in failed:', error);
            throw error;
        }
    }

    /**
     * Sign in using popup (alternative to redirect)
     */
    async signInPopup() {
        try {
            const response = await this.msalInstance.loginPopup(loginRequest);
            this.currentAccount = response.account;
            return this.currentAccount;
        } catch (error) {
            console.error('Sign-in popup failed:', error);
            throw error;
        }
    }

    /**
     * Sign out
     */
    async signOut() {
        const logoutRequest = {
            account: this.currentAccount,
            postLogoutRedirectUri: msalConfig.auth.postLogoutRedirectUri
        };
        
        try {
            await this.msalInstance.logoutRedirect(logoutRequest);
        } catch (error) {
            console.error('Sign-out failed:', error);
            throw error;
        }
    }

    /**
     * Get access token for calling APIs
     */
    async getAccessToken(scopes = apiTokenRequest.scopes) {
        if (!this.currentAccount) {
            throw new Error('No user signed in');
        }

        const request = {
            scopes: scopes,
            account: this.currentAccount
        };

        try {
            // Try to acquire token silently
            const response = await this.msalInstance.acquireTokenSilent(request);
            return response.accessToken;
        } catch (error) {
            // If silent token acquisition fails, fall back to interactive
            if (error instanceof msal.InteractionRequiredAuthError) {
                try {
                    const response = await this.msalInstance.acquireTokenRedirect(request);
                    return response.accessToken;
                } catch (interactiveError) {
                    console.error('Interactive token acquisition failed:', interactiveError);
                    throw interactiveError;
                }
            } else {
                console.error('Token acquisition failed:', error);
                throw error;
            }
        }
    }

    /**
     * Check if user is signed in
     */
    isSignedIn() {
        return this.currentAccount !== null;
    }

    /**
     * Get current user account
     */
    getCurrentAccount() {
        return this.currentAccount;
    }

    /**
     * Get user display name
     */
    getUserDisplayName() {
        if (!this.currentAccount) return null;
        return this.currentAccount.name || this.currentAccount.username;
    }

    /**
     * Get user email
     */
    getUserEmail() {
        if (!this.currentAccount) return null;
        return this.currentAccount.username;
    }
    
    /**
     * Get user object ID (for user-scoped queries)
     */
    getUserObjectId() {
        if (!this.currentAccount) return null;
        return this.currentAccount.localAccountId || this.currentAccount.homeAccountId;
    }
    
    /**
     * Require authentication - redirect to home if not signed in
     * Call this on page load for protected pages
     */
    async requireAuth(redirectUrl = 'index.html') {
        // Wait for initialization to complete
        if (!this.msalInstance) {
            await this.initialize();
        }
        
        if (!this.isSignedIn()) {
            console.log('User not signed in, redirecting to:', redirectUrl);
            window.location.href = redirectUrl;
            return false;
        }
        
        return true;
    }
}

// Export singleton instance
const authManager = new AuthManager();
