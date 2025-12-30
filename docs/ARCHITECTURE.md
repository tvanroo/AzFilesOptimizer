# AzFilesOptimizer Architecture (Azure-Native)

> Status: initial skeleton for the Azure-native design. This document will be expanded as implementation proceeds.

## 1. Overview

AzFilesOptimizer is an **Azure-native assessment tool** deployed into an individual customer’s Azure subscription. It discovers Azure Files and ANF resources, runs optimization logic, and presents recommendations via a web UI.

This document describes the architecture for the new Azure-based design and supersedes any previous descriptions of a locally-run Windows application.

## 2. High-level architecture

- **Frontend**
  - Hosted on **Azure Static Web Apps**.
  - Provides a browser-based UI for running discovery and optimization jobs, and for reviewing results.

- **Backend**
  - Implemented as **Azure Functions** (Consumption plan).
  - Exposes HTTP APIs for the UI.
  - Uses queue-triggered functions for background work.

- **Data & messaging**
  - Single **Storage Account** per deployment with:
    - Table Storage for job metadata and configuration.
    - Queue Storage for job orchestration.
    - Blob Storage for reports and logs.

- **Security & configuration**
  - **Microsoft Entra ID** for user authentication.
  - **Managed Identity** on the Functions app to access Key Vault and Azure resources.
  - **Azure Key Vault** for API keys and other secrets.

- **Observability**
  - **Application Insights** connected to the Functions app.

Each deployment is **single-tenant**: all resources live entirely inside the customer’s own subscription.

## 3. Component responsibilities (to be detailed)

- Frontend UI
- API surface (Functions HTTP endpoints)
- Background job processing
- Storage schemas (tables, queues, blobs)
- Integration with Azure Files and ANF APIs
- LLM-based recommendation engine

## 4. Deployment topology (to be detailed)

- Resource group layout
- Naming conventions
- Network considerations (if any)

## 5. Future enhancements

As the implementation evolves, this document will be expanded with:

- Sequence diagrams for key flows (discovery, recommendation generation).
- Detailed API contracts.
- Error handling and retry strategies.
- Notes for Azure Marketplace packaging.
