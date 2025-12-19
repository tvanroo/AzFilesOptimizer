# AzFilesOptimizer
Locally-run Windows app (with a basic Web UI) to inventory Azure Files shares, collect capacity/performance signals, and generate Azure NetApp Files (ANF) candidacy recommendations and a proposed target ANF architecture using a user-provided OpenAI/Azure OpenAI/OpenAI-compatible API key.

## Hackathon abstract / goal
AzFilesOptimizer is a locally-run Windows application with a simple Web UI that helps customers and field teams quickly assess Azure Files environments and identify high-confidence Azure NetApp Files (ANF) modernization opportunities. After an interactive sign-in to an Azure tenant, the app automatically discovers Azure Files shares across storage accounts (and any existing ANF accounts, capacity pools, and volumes), building a live, hierarchical dashboard as inventory is collected. It captures detailed configuration signals and enabled features, and can optionally deploy lightweight Azure monitoring to collect historical capacity and performance metrics over time—then cleanly remove those monitoring artifacts to return the environment to its original state.

Once discovery is complete, the user provides an OpenAI/Azure OpenAI (or OpenAI-compatible) API key and optional endpoint. The app uses that key to run an AI decision engine that analyzes each share’s footprint, performance patterns, resiliency posture, and feature requirements, then produces a ranked set of ANF candidacy recommendations. Outputs include a 0–100 “fit score,” a plain-language summary, and a proposed target ANF design: service level selection, capacity pool consolidation strategy with QoS, volume sizing based on throughput and capacity needs, snapshot/backup recommendations, security alignment (e.g., encryption/CMK), and replication guidance aligned to existing redundancy expectations. Where APIs cannot infer intent (workload type, latency sensitivity, data temperature), the tool prompts the user for minimal inputs and documents assumptions.

The result is a customer-facing report and architecture proposal that accelerates storage optimization conversations, highlights potential cost and risk reductions, and drives qualified ANF sales opportunities.

## Status
This repository currently contains planning documentation only. Implementation will be done on a Windows dev machine.

## Project plan
See `docs/PROJECT_PLAN.md`.
