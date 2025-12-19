# AzFilesOptimizer — MVP + Roadmap + Machine-Readable Spec (Draft)

## Problem statement
Build a locally-run Windows app with a basic Web UI that:
- Authenticates interactively to an Azure tenant.
- Discovers all Azure Files shares (across storage accounts) plus existing ANF accounts/pools/volumes.
- Optionally deploys and later removes monitoring to collect richer historical metrics.
- Uses a user-provided OpenAI/Azure OpenAI/OpenAI-compatible API key (and optional custom endpoint) to produce an ANF migration-candidacy report and proposed ANF target architecture (service levels, pooling/QoS, snapshots/backup, security, replication), including a 0–100 score and human-readable summary.

The tool does not perform migrations; it generates recommendations and next-step guidance for a deeper human engagement.

## Target users and UX
Primary users:
- Customer IT / storage engineers (running in their environment with their permissions)
- Optional assistance from NetApp/Microsoft sellers/SA teams

UX:
- Dashboard-first.
- One "Link to Azure" button launches a short wizard for tenant selection and scope narrowing.
- Discovery runs async; the dashboard fills in hierarchy live as resources are discovered.
- Users can queue additional scopes while discovery jobs are still running.

## Proposed MVP scope
### 1) Local-first deployment and persistence
- Runs only on the user’s Windows device.
- Persist locally (within the app directory) all:
  - discovered inventory snapshots
  - metric summaries
  - user questionnaire answers
  - AI prompts + responses + scoring outputs
  - prompt override settings

### 2) Azure auth and cloud support
- Interactive auth only.
- Support Azure public + sovereign clouds by allowing cloud selection (authority/resource endpoints).
- Required permissions are checked at runtime; missing permissions are surfaced in UI.
- Optional features (metrics deployment) are disabled/greyed out when write permissions are missing, and become enabled once permissions are corrected.

### 3) Discovery (read-only)
- Enumerate at minimum:
  - Tenants (force single selection per run)
  - Subscriptions (discover all, then allow filtering)
  - Resource Groups (if a single subscription selected, allow RG filtering)
  - Storage Accounts -> Azure Files shares (capture as many properties/features as available)
  - Existing ANF footprint: NetApp Accounts -> Capacity Pools -> Volumes (capture properties)
- Provide drill-down views and allow exporting a per-share JSON summary of all known facts.

### 4) Feature parity mapping (Azure Files -> ANF)
- Maintain an explicit, extensible catalog of:
  - Azure Files features/configs discovered
  - Closest ANF analog (or alternative pattern)
  - Blocker/partial/compatible classification
  - Required user validation questions when parity is unclear
- In the UI, allow marking a source feature as "not required" to unblock ANF proposal generation.

### 5) Metrics
- Default report window: 7 days; allow 30/90 days.
- Attempt to query up to 90 days of historical metrics where available.
- Compute and store avg + p95 + max, plus peak windows.
- Treat missing metrics as "unknown" and capture the gaps explicitly for AI.

### 6) Optional metrics deployment + cleanup
- Provide an assisted workflow to enable richer monitoring quickly.
- Provide an automated rollback workflow to remove anything created by the tool and return resources to prior state.
- Persist a deployment manifest locally describing every created/modified resource for deterministic cleanup.

### 7) AI decision engine
Produce per-share:
- 0–100 candidacy score
- human-readable summary
- recommended ANF service level (Standard/Premium/Ultra/Flexible)
- initial sizing recommendations (capacity + throughput rationale)
- capacity pool strategy (consolidation opportunities, QoS allocations)
- snapshot and backup recommendations
- security recommendations (double encryption, CMK, etc.) when source indicates analogous needs
- resiliency recommendations (zone/cross-region replication) aligned to source redundancy
- unknowns and questions to validate

Prompt system:
- default templates
- persistent user overrides
- audit log showing which template/override produced each output

LLM connectivity:
- OpenAI API key
- Azure OpenAI
- OpenAI-compatible endpoint/base URL override

## Proposed tech stack (recommendation)
Packaging constraint: runnable on Windows with minimal setup, downloaded via GitHub zip/clone.

Recommendation: .NET 8 (or newer) self-contained single-file executable hosting:
- Local web server (ASP.NET Core)
- Web UI (Blazor Server or static SPA assets)

Rationale:
- Easiest path to a single exe for Windows without requiring the user to install runtimes.
- Strong Azure SDK support and auth story.

## Machine-readable specs to add (next)
- `spec/product.yaml`
- `spec/azure-clouds.yaml`
- `spec/permissions.yaml`
- `spec/discovery.schema.json`
- `spec/metrics.yaml`
- `spec/metrics-deployment.yaml`
- `spec/feature-parity.yaml`
- `spec/anf-capabilities.yaml`
- `spec/anf-pricing.yaml`
- `spec/prompts.yaml`
- `state/` (local runtime outputs)

## Open questions
1) Metrics deployment design (platform metrics vs diagnostic settings/log analytics).
2) Cost modeling approach (quantitative vs qualitative first).
3) UI diagram approach (hierarchy-first vs full network diagram).
4) Sovereign cloud UX (manual selection vs auto-detect).
