/**
 * Azure AD Authentication Configuration
 * 
 * IMPORTANT: Replace the placeholder values below with your actual Azure AD app registration details.
 * See docs/DEPLOYMENT.md section 5 for instructions on creating an app registration.
 */

const msalConfig = {
    auth: {
        clientId: "f2e00fd5-183e-4dc6-8269-50c5c5982d68", // AzFilesOptimizer app registration
        authority: "https://login.microsoftonline.com/890df2fb-c027-40fc-88ad-7dc5308deacc", // anfcsateam tenant
        redirectUri: window.location.origin, // Dynamically uses current origin (http://localhost:8080 or production URL)
        postLogoutRedirectUri: window.location.origin
    },
    cache: {
        cacheLocation: "localStorage", // Store tokens in localStorage for persistence across sessions
        storeAuthStateInCookie: false // Set to true if you need to support IE11 or Edge legacy
    },
    system: {
        loggerOptions: {
            loggerCallback: (level, message, containsPii) => {
                if (containsPii) return;
                
                switch (level) {
                    case 0: // Error
                        console.error(message);
                        break;
                    case 1: // Warning
                        console.warn(message);
                        break;
                    case 2: // Info
                        console.info(message);
                        break;
                    case 3: // Verbose
                        console.debug(message);
                        break;
                }
            },
            logLevel: 1 // Warning level
        }
    }
};

// Scopes for login request
const loginRequest = {
    scopes: [
        "https://management.azure.com/user_impersonation", // Azure Resource Manager API
        "User.Read" // Microsoft Graph - read user profile
    ]
};

// Scopes for API token requests
const apiTokenRequest = {
    scopes: ["https://management.azure.com/user_impersonation"]
};
