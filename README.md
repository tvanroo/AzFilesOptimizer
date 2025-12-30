# AzFilesOptimizer

AzFilesOptimizer is an **Azure-native assessment tool** that customers deploy into **their own Azure subscription**. It discovers Azure Files shares, evaluates Azure NetApp Files (ANF) candidacy, and produces a recommended target ANF architecture (service levels, capacity pools, volumes, and protection strategy).

This project is designed to be:
- **Low cost** – serverless, scales to zero when idle.
- **Simple and supportable** – uses mainstream Azure PaaS services only.
- **Customer-owned** – each customer deploys into their own subscription via a "Deploy to Azure" experience.

## High-level architecture

AzFilesOptimizer is built on these Azure components:

- **Frontend**
  - Azure Static Web Apps hosting a simple web UI (static HTML/JavaScript or a light SPA).
  - Calls backend APIs exposed by Azure Functions.

- **Backend**
  - Azure Functions (Consumption plan) providing:
    - HTTP-triggered APIs for discovery and job management.
    - Queue-triggered functions for background optimization jobs.

- **Data & messaging**
  - Single general-purpose v2 **Storage Account** per deployment:
    - **Table Storage** – job definitions, job status, configuration, user preferences.
    - **Queue Storage** – async job orchestration (discovery and optimization workloads).
    - **Blob Storage** – reports, logs, and generated artifacts.

- **Security & configuration**
  - **Microsoft Entra ID (Azure AD)** app registration for user sign-in.
  - **Managed Identity** on the Functions app to access:
    - Azure Key Vault (for secrets/config).
    - Azure Resource Manager / Storage APIs (for discovery and metrics), as permitted by customer-assigned roles.
  - **Azure Key Vault** for OpenAI/Azure OpenAI keys and other sensitive settings.

- **Observability**
  - **Azure Application Insights** attached to the Functions app for telemetry and diagnostics.

Each deployment is **single-tenant**: all resources live inside the customer’s subscription and resource group. There is no shared, multi-tenant control plane.

## Current status

The repository is in an **early implementation** phase for this Azure-native architecture:

- Documentation is being rewritten to reflect the Azure-native design.
- Infrastructure templates (Bicep/ARM) will define the full deployment topology.
- Backend (Azure Functions) and frontend (Static Web Apps) projects are being planned.

See the project plan for the detailed roadmap.

## Project plan and documentation

- **Project plan:** `docs/PROJECT_PLAN.md`
- **Architecture details:** `docs/ARCHITECTURE.md` (planned)
- **Deployment instructions:** `docs/DEPLOYMENT.md` (planned)

These documents are written so both humans and AI agents can follow the steps to evolve the solution, implement features, and prepare for Azure Marketplace onboarding.
