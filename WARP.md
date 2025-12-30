# WARP.md

This file provides guidance to WARP (warp.dev) when working with code in this repository.

## Repository status
- As of the current README, this repo contains planning documentation only; no application code has been committed yet.
- The canonical product and architecture description lives in `docs/PROJECT_PLAN.md`. Treat that document as the source of truth when evolving the implementation.

## Project overview
AzFilesOptimizer is intended to be a locally-run Windows application with a simple Web UI that:
- Authenticates interactively to an Azure tenant.
- Discovers Azure Files shares (across storage accounts) plus existing ANF accounts, capacity pools, and volumes.
- Optionally deploys and later removes Azure monitoring artifacts to collect rich historical metrics.
- Uses a user-provided OpenAI/Azure OpenAI/OpenAI-compatible API key (and optional endpoint) to score each share (0–100 fit score) and generate ANF migration-candidacy recommendations and a proposed ANF target architecture.

The tool is explicitly **advisory only**: it generates recommendations and reports, but does not perform data migrations.

## Planned runtime and tech stack
From `docs/PROJECT_PLAN.md`:
- Target platform: Windows (local-first, runs on the user’s device).
- Recommended implementation: **.NET 8 (or newer)** published as a **self-contained single-file executable**.
- Hosting model:
  - Local web server (ASP.NET Core).
  - Web UI (either Blazor Server or a static SPA served by ASP.NET Core).
- Strong Azure SDK integration is expected (for discovery, metrics, and auth).

When implementing or modifying code, prefer conventional .NET layout (e.g., `src/` for production projects, `tests/` for test projects), but defer to any explicit structure that is eventually adopted in this repo.

## Planned high-level architecture
The big-picture architecture implied by the docs spans several cooperating subsystems:

### 1. Local host application & persistence
Responsibilities:
- Start the ASP.NET Core web host and serve the UI.
- Manage application configuration (Azure cloud selection, tenant/subscription filters, LLM endpoint/API key, feature flags).
- Provide a persistence abstraction that stores all runtime state **locally within the app directory**, including:
  - Discovery snapshots.
  - Metric summaries and computed aggregates.
  - User questionnaire responses.
  - Prompt templates and user overrides.
  - AI inputs/outputs and scoring results (for auditability).
  - Deployment manifests for any monitoring artifacts the tool creates.

Planned layout for machine-readable state (per `PROJECT_PLAN.md`):
- `state/` — local runtime outputs (snapshots, metrics rollups, logs, AI audit trail, deployment manifests).

### 2. Azure auth and multi-cloud support
Responsibilities:
- Interactive sign-in to a single Azure tenant per run.
- Allow explicit selection of Azure cloud/authority endpoints (public vs sovereign clouds) and apply those consistently across the Azure SDK clients.
- Discover required permissions dynamically and surface missing permissions clearly in the UI.
- Enable/disable optional capabilities (e.g., metrics deployment) based on effective permissions.

Future code should centralize:
- A small, testable auth service that encapsulates Azure Identity and cloud-environment selection.
- A permissions/capabilities module that inspects the current principal and advertises which app features are available.

### 3. Discovery pipeline (inventory)
Responsibilities:
- Enumerate the Azure resource hierarchy:
  - Tenant (single selected tenant).
  - Subscriptions (discover all, allow filtering).
  - Resource groups (where a single subscription is selected).
  - Storage accounts → Azure Files shares (capture as many properties and flags as available).
  - NetApp accounts → capacity pools → ANF volumes.
- Maintain an in-memory and persisted model of this hierarchy that supports:
  - A dashboard-style tree view in the UI that fills in as discovery progresses.
  - Drill-down views at each level.
  - Per-share JSON exports of all known facts.

Design guidance:
- Keep discovery logic separate from UI and from AI decision-making.
- Model discovery results in a way that is stable enough to serialize to `spec/discovery.schema.json` (see below) and to persist under `state/`.

### 4. Metrics acquisition and summarization
Responsibilities:
- Query historical metrics for Azure Files shares where available.
- Default reporting window: 7 days, with options for 30 and 90 days.
- Compute aggregates such as average, p95, max, and peak windows.
- Represent gaps explicitly (treat missing metrics as "unknown" and surface that to the AI layer).

Optional metrics deployment flow:
- Assisted workflow to enable richer monitoring quickly.
- Deterministic rollback/cleanup that removes any artifacts created by the tool and returns resources to their prior state.
- Local deployment manifest that records all created/modified resources for later cleanup.

These behaviors should be driven by machine-readable configuration in `spec/metrics.yaml` and `spec/metrics-deployment.yaml` once those files are created.

### 5. Feature-parity catalog (Azure Files → ANF)
Responsibilities:
- Maintain an explicit, extensible mapping of Azure Files configurations and features to their closest ANF analogs or alternative patterns.
- Classify each mapping as **compatible**, **partial**, or **blocker**.
- Define follow-up questions for cases where parity cannot be inferred (e.g., workload type, latency sensitivity, data temperature).
- Allow the user to mark certain source features as "not required" to unblock ANF proposal generation.

Planned machine-readable sources (from `PROJECT_PLAN.md`):
- `spec/feature-parity.yaml` — core mappings and classifications.
- `spec/anf-capabilities.yaml` and `spec/anf-pricing.yaml` — capabilities and pricing data used for proposal sizing.

The application code should treat these `spec/` files as authoritative data rather than hard-coding mappings.

### 6. AI decision engine
Responsibilities:
- For each discovered share, produce:
  - A 0–100 candidacy score.
  - A human-readable narrative summary.
  - Recommended ANF service level (e.g., Standard/Premium/Ultra/Flexible).
  - Initial sizing recommendations (capacity and throughput with rationale).
  - Capacity-pool strategy (consolidation, QoS allocations).
  - Snapshot and backup guidance.
  - Security guidance (e.g., double encryption, CMK) based on source configuration.
  - Resiliency guidance (zone/cross-region replication) aligned to existing redundancy.
  - A list of unknowns and questions to validate with the user.

Prompting model:
- Default prompt templates kept under version control.
- Persistent user overrides that can be edited without recompiling.
- An audit trail that records which template+override produced each output, stored under `state/`.

Planned machine-readable prompt configuration:
- `spec/prompts.yaml` — system and user prompt templates, plus tunable parameters per scenario.

Connectivity:
- Built-in support for classic OpenAI, Azure OpenAI, and arbitrary OpenAI-compatible endpoints/base URLs.
- User-supplied API key and (optionally) endpoint are stored locally and used only for outbound calls from the local app.

### 7. Reporting and exports
Responsibilities:
- Generate a customer-facing report that summarizes:
  - Discovered environment.
  - Candidate ANF migrations and target architecture.
  - Potential cost/risk reductions and next steps.
- Produce per-share JSON exports that contain the full fact set plus AI outputs.

Implementation guidance:
- Keep report generation logic separated from the decision engine so reports can be regenerated from persisted state without re-querying Azure or the LLM.

## Machine-readable specs and configuration
The project plan calls out a set of machine-readable specs to be added next:
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
- `state/` (runtime outputs)

Guidance for future changes:
- When evolving behavior in any of the major subsystems (discovery, metrics, feature parity, AI decisions), prefer to update or extend the relevant `spec/` file first, then adjust code to consume it.
- Keep schemas (e.g., `spec/discovery.schema.json`) synchronized with the in-memory models used by the app so persisted state remains compatible.

## Commands and workflows (once code exists)
This section documents the expected CLI workflows for a .NET 8–based implementation. Adjust paths and project names once the concrete solution and projects are created.

### Prerequisites
- Windows machine (target environment for both development and runtime).
- [.NET 8 SDK or newer](https://dotnet.microsoft.com/) installed and on `PATH`.

Verify installation:
- `dotnet --info`

### Build
From the repository root (once a solution exists):
- Build the entire solution:
  - `dotnet build`
- Build in Release mode:
  - `dotnet build -c Release`

If multiple solutions are added, prefer a single top-level `.sln` and run `dotnet build AzFilesOptimizer.sln` from the repo root.

### Tests
Once test projects exist (e.g., under a `tests/` directory):
- Run all tests:
  - `dotnet test`
- Run tests for a specific test project:
  - `dotnet test path\to\Some.Tests.csproj`
- Run a single test (by name or fully qualified name):
  - `dotnet test --filter "Name=SomeTestMethod"`
  - or `dotnet test --filter "FullyQualifiedName~Namespace.ClassName.SomeTestMethod"`

### Local development run
For a web-hosted app project (e.g., an ASP.NET Core host project):
- Run from the project directory:
  - `dotnet run`

During development, you can set the environment to `Development` (if the app uses ASP.NET Core conventions):
- PowerShell:
  - `$env:ASPNETCORE_ENVIRONMENT = "Development"`
  - `dotnet run`

### Single-file, self-contained publish (Windows)
To produce a self-contained, single-file Windows executable for distribution (aligning with the project plan):
- From the host project directory:
  - `dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true`

This will emit a single `.exe` under `bin/Release/net8.0/win-x64/publish/` (exact path depends on the chosen target framework and runtime identifier).

## How to keep this file up to date
- When you introduce a real solution structure (projects, tests, scripts), update the **Commands and workflows** section with the concrete paths and any custom tooling.
- When you add or change machine-readable specs under `spec/`, update the **Machine-readable specs and configuration** section to reflect the new shapes and contracts.
- When the high-level architecture diverges from `docs/PROJECT_PLAN.md`, update both the plan and this WARP file so future agents have an accurate mental model of the system.
