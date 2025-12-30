# AzFilesOptimizer — Azure-Native Project Plan

## 1. Problem statement

AzFilesOptimizer should help customers and field teams quickly assess **Azure Files** environments and identify high-confidence **Azure NetApp Files (ANF)** modernization opportunities.

The original design assumed a **locally-run Windows application**. That approach is now deprecated. Going forward, AzFilesOptimizer is an **Azure-native solution** that is **deployed into each customer’s own subscription**, using Azure PaaS services and a "Deploy to Azure" experience.

The tool:
- Authenticates to an Azure tenant using Microsoft Entra ID.
- Discovers Azure Files shares (across storage accounts) and existing ANF accounts/pools/volumes.
- Optionally collects richer metrics where the customer chooses to enable them.
- Uses a customer-provided OpenAI/Azure OpenAI/OpenAI-compatible API key (stored in Key Vault) to:
  - Evaluate ANF migration candidacy.
  - Propose a target ANF architecture (service level, pooling/QoS, volume layout, snapshots/backup, security, resiliency).
- Produces reports and recommendations, but **does not perform data migrations**.

Each deployment is **single-tenant**: customers deploy this solution into their own Azure subscription and retain control of all data and identities.

## 2. Target users and UX

Primary users:
- Customer IT / storage engineers, deploying into and operating within their own Azure subscriptions.
- NetApp / Microsoft field teams and partners assisting customers with assessments.

UX principles:
- **Azure-native:** Deployed via "Deploy to Azure" and operated via a web UI.
- **Dashboard-first:** show a live view of discovered Azure Files and ANF resources.
- **Async jobs:** discovery and optimization jobs run asynchronously and can be queued.
- **Minimal friction:** only a few required inputs (scope, API key) to get value.

## 3. Azure-native architecture (summary)

Core building blocks:

- **Frontend**
  - Azure Static Web Apps hosting a simple web UI (static HTML+JavaScript or a light SPA).
  - Talks to backend APIs exposed by Azure Functions.

- **Backend**
  - Azure Functions (Consumption plan):
    - HTTP-triggered functions for API endpoints.
    - Queue-triggered functions for background discovery and optimization jobs.

- **Data & messaging**
  - Single general-purpose v2 Storage Account per deployment:
    - Table Storage for job definitions, status, configuration, and user preferences.
    - Queue Storage for job orchestration.
    - Blob Storage for logs, reports, and exported data.

- **Security & configuration**
  - Microsoft Entra ID app registration for user sign-in.
  - Managed Identity on the Functions app for access to Key Vault and Azure resources.
  - Azure Key Vault for API keys and sensitive configuration.

- **Observability**
  - Azure Application Insights attached to the Functions app.

This architecture is designed to be low-cost (serverless, scale-to-zero), simple, and friendly to an Azure Marketplace "solution template" or managed application offering.

## 4. MVP scope (Azure-native)

The MVP aims to deliver a functional, minimally polished solution that can be deployed to a test subscription and operated end-to-end.

### 4.1 MVP capabilities

1. **Deployment & configuration**
   - Deploy the core Azure resources via a Bicep/ARM template.
   - Configure an Entra ID app registration and connect it to the frontend.
   - Store OpenAI/Azure OpenAI credentials securely in Key Vault.

2. **Discovery (read-only)**
   - Enumerate Azure Files and ANF resources within a defined scope (tenant/subscription/resource group filters).
   - Persist a structured view of discovered resources into Table/Blob Storage.
   - Expose a simple web UI showing:
     - List of discovery jobs.
     - Drill-down into discovered shares and existing ANF footprint.

3. **AI-based recommendations**
   - For each share, generate a recommendation payload using a customer-provided LLM API key.
   - Present a candidate ANF design and a human-readable summary.
   - Persist recommendations, including inputs and outputs, for later review.

4. **Reporting**
   - Offer at least one export format (e.g., JSON/CSV) with key recommendations.

5. **Operations & troubleshooting**
   - Basic health check endpoint and UI indicator.
   - Application Insights traces and logs for key operations.

### 4.2 Explicit non-goals for MVP

- No data migration or write operations against Azure Files/ANF.
- No deep, automated metrics deployment workflow (may be added later).
- No multi-tenant service; all deployments are single-tenant in a customer’s subscription.

## 5. Target repository structure

Planned layout (may evolve as we implement):

- Root
  - `README.md` – high-level overview and quick links.
  - `docs/`
    - `PROJECT_PLAN.md` – this document; human- and agent-readable.
    - `ARCHITECTURE.md` – detailed architecture description.
    - `DEPLOYMENT.md` – deployment and local dev instructions.
  - `infra/`
    - `main.bicep` or `azuredeploy.json` – main deployment template.
    - `parameters/` – sample parameter files (e.g., `dev.json`, `test.json`).
  - `src/`
    - `backend/` – Azure Functions project.
    - `frontend/` – Static web assets for Azure Static Web Apps.
  - `spec/` (optional)
    - Structured specs for discovery schemas, prompts, etc.
  - `scripts/` (optional)
    - Helper scripts (e.g., deploy infra, run linters/tests).

## 6. Phased roadmap

This roadmap is designed so that both a human and an AI agent can pick up tasks and execute them in a predictable order.

### Phase 0 – Cleanup & documentation reboot

**Goals:**
- Fully move away from the old local-Windows-app architecture.
- Establish this project plan and new documentation as the source of truth.

**Agent tasks:**
1. Rewrite `README.md` to describe the Azure-native, single-tenant, per-subscription deployment model.
2. Replace the previous project plan with this Azure-native `PROJECT_PLAN.md`.
3. Create skeletons for `docs/ARCHITECTURE.md` and `docs/DEPLOYMENT.md` (headings only to start).

**Human tasks:**
1. Review updated documentation and adjust wording as needed.
2. Decide how to handle any legacy code (e.g., move to `legacy/` or remove).

**Exit criteria:**
- No remaining references in docs to the "locally-run Windows exe" as the main design.
- New documentation is committed and accepted.

### Phase 1 – Core infrastructure template

**Goals:**
- Define a single deployment template that provisions all core Azure resources.
- Make it suitable for a future "Deploy to Azure" button and eventually for Marketplace.

**Agent tasks:**
1. Create `infra/main.bicep` (or `infra/azuredeploy.json`) that defines:
   - Resource Group (parameterized name, if appropriate).
   - Storage Account (Tables/Queues/Blobs).
   - Function App + Consumption plan + managed identity.
   - Application Insights instance.
   - Azure Key Vault.
2. Add parameters for:
   - Location.
   - Base resource name prefix.
   - Optional tags.
3. Add `infra/parameters/dev.json` with sample values.
4. Document template parameters and outputs in `docs/DEPLOYMENT.md`.

**Human tasks:**
1. From a test subscription, deploy the template using Azure CLI or Portal.
2. Confirm that resources are created successfully and conform to naming and policy requirements.
3. Capture any required adjustments (e.g., naming constraints, mandatory tags) and feed back into the template and docs.

**Exit criteria:**
- A single template can deploy Storage, Functions, Key Vault, and App Insights with no manual portal steps.

### Phase 2 – Backend foundation (Azure Functions)

**Goals:**
- Stand up a running Functions app with basic health and placeholder endpoints.
- Set the pattern for storage access and configuration.

**Agent tasks:**
1. Scaffold an Azure Functions project under `src/backend` (recommended: .NET or Node.js; choose one and document it).
2. Add an HTTP-triggered `GET /api/health` function that returns:
   - Service name.
   - Version.
   - Basic environment info (e.g., whether Storage connection is configured).
3. Add a queue-triggered function stub (e.g., `ProcessJobFunction`) that:
   - Reads a simple job message (e.g., `{ jobId: string }`).
   - Logs the message.
4. Define initial data models for:
   - Discovery job.
   - Optimization job.
   - Job status.
5. Wire up configuration:
   - Use app settings / environment variables for Storage connection strings or identities.
   - Document expected configuration keys in `docs/DEPLOYMENT.md`.

**Human tasks:**
1. Configure local development settings (e.g., `local.settings.json`) for the Functions app.
2. Run the Functions project locally and verify `GET /api/health`.
3. Optionally, deploy the Functions code to the Azure Function App created in Phase 1 for end-to-end validation.

**Exit criteria:**
- Backend is deployable and responds to `/api/health`.
- Queue-triggered function can be invoked with a test message.

### Phase 3 – Frontend foundation (Static Web App)

**Goals:**
- Provide a minimal but functional UI hosted in Azure Static Web Apps.
- Integrate with the backend `/api/health` endpoint.

**Agent tasks:**
1. Scaffold a minimal frontend under `src/frontend` (e.g., static HTML + JavaScript or a very light SPA framework).
2. Implement pages:
   - **Home/Status page:** shows health check results from `/api/health`.
   - **Jobs list page:** placeholder list of discovery/optimization jobs (initially static or mocked).
   - **Job detail page:** placeholder view that will later show job status and results.
3. Create a small API client module to call backend endpoints (e.g., `GET /api/health`).
4. Add a basic build script (if needed) and document local dev steps.

**Human tasks:**
1. Run the frontend locally and confirm it can successfully call `/api/health` on the backend.
2. Provide feedback on layout and terminology.

**Exit criteria:**
- Static Web App assets build successfully.
- Health page correctly reflects backend status.

### Phase 4 – Identity & security

**Goals:**
- Secure the app with Entra ID sign-in.
- Ensure secrets and sensitive config live in Key Vault and are accessed via managed identity.

**Agent tasks:**
1. Update `docs/DEPLOYMENT.md` with a clear checklist for creating an Entra ID app registration:
   - App name, supported account types, redirect URIs, scopes.
2. Add configuration placeholders to the frontend and backend for:
   - Tenant ID.
   - Client ID (if necessary for SPA auth).
3. Configure backend to:
   - Use managed identity to access Key Vault.
   - Read the LLM API key and other secrets from Key Vault.
4. Document required Azure RBAC roles for the Functions app to:
   - Access Key Vault.
   - Read from customer Azure Files / ANF resources (as applicable).

**Human tasks:**
1. Create the Entra ID app registration following the documented steps.
2. Update app settings and/or Key Vault secrets with tenant IDs, client IDs, and any required endpoints.
3. Test login flow and confirm that unauthorized users cannot access the main app.

**Exit criteria:**
- Sign-in works end-to-end.
- No secrets are hard-coded in code or config files; secrets reside in Key Vault.

### Phase 5 – Observability

**Goals:**
- Ensure that operators can troubleshoot and understand app behavior.

**Agent tasks:**
1. Enable Application Insights for the Functions app (if not already done via template).
2. Add structured logging to key steps in the backend (e.g., job creation, job start, job completion, error paths).
3. Add correlation IDs to link logs across HTTP and queue-triggered functions.
4. Document common Kusto queries in `docs/DEPLOYMENT.md` for:
   - Request failures.
   - Job status analytics.

**Human tasks:**
1. Use the Azure Portal to validate that traces, logs, and exceptions appear as expected.
2. Refine or add log fields as needed.

**Exit criteria:**
- Basic troubleshooting flows are documented and verifiable.

### Phase 6 – Deploy to Azure button & CI/CD

**Goals:**
- Make it easy for others to deploy the solution into their own subscriptions.
- Automate build and deployment from the main branch.

**Agent tasks:**
1. Add a "Deploy to Azure" button in `README.md` that links to the public URL of `infra/main.bicep` or `infra/azuredeploy.json` in this repository.
2. Create a GitHub Actions workflow that:
   - Builds the frontend assets.
   - Builds the Functions app.
   - Deploys both to the Azure resources created by the template.
3. Document the required GitHub repository secrets (e.g., Azure service principal details) in `docs/DEPLOYMENT.md`.

**Human tasks:**
1. Configure the necessary GitHub secrets (service principal credentials, subscription ID, etc.).
2. Test an end-to-end flow:
   - Click the Deploy to Azure button to deploy infra.
   - Run the CI pipeline to deploy code.
   - Log in and run a sample discovery job (even if mocked).

**Exit criteria:**
- A new environment can be created and updated with minimal manual steps.

### Phase 7 – Marketplace readiness (future)

**Goals:**
- Prepare the solution for onboarding into the Azure Marketplace as a solution template or managed application.

**Agent tasks:**
1. Review current infrastructure template and compare against Marketplace guidance for managed applications.
2. Propose template and repo structure adjustments needed to meet requirements (e.g., parameterization, output formats, operation model).
3. Draft Marketplace-facing documentation sections (offer description, architecture summary, support model) based on existing docs.

**Human tasks:**
1. Register with the Azure Marketplace program (if not already done).
2. Create the Marketplace offer, attach the deployment template, and complete legal/support details.
3. Work through any review feedback from Microsoft.

**Exit criteria:**
- Marketplace offer is ready for submission or already published (depending on priorities).

## 7. Open questions and decisions

These should be revisited as implementation progresses:

1. **Frontend technology choice**
   - Default assumption: very simple static HTML/JavaScript or minimal framework to keep support overhead low.
   - Decision: adjust based on contributor familiarity and complexity of future UX.

2. **Scope configuration UX**
   - How users specify which subscriptions/resource groups to scan.

3. **Metrics depth**
   - How far to go in MVP with metrics collection vs. simply reading existing platform metrics.

4. **Cost modeling detail**
   - Whether the ANF recommendation engine remains qualitative or includes detailed cost modeling early on.

These questions do not block the initial phases (0–2) and can be resolved iteratively.
