# Azure Pricing Models Reference

This document defines the complete cost models for Azure Files (Storage Accounts) and Azure NetApp Files (ANF) used by AzFilesOptimizer for accurate cost estimation.

---

## Azure Files (Storage Accounts)

### Pay-As-You-Go Shares

#### Hot Tier
**Cost Components:**
- **Capacity**: $ per GiB/month (varies by redundancy)
- **Transactions**: $ per 10,000 transactions
  - Write operations
  - List/create container operations
  - Read operations
  - All other operations
- **Data Transfer**: 
  - Ingress: Free
  - Egress: $ per GB (varies by region/tier)
- **Snapshots**: $ per GiB/month (differential only)

**Redundancy Options:** LRS, ZRS, GRS, GZRS

---

#### Cool Tier
**Cost Components:**
- **Capacity**: $ per GiB/month (lower than Hot, varies by redundancy)
- **Transactions**: $ per 10,000 transactions (higher than Hot)
  - Write operations
  - List/create container operations
  - Read operations
  - All other operations
- **Data Retrieval**: $ per GB
- **Data Transfer**: 
  - Ingress: Free
  - Egress: $ per GB
- **Snapshots**: $ per GiB/month (differential only)

**Redundancy Options:** LRS, ZRS, GRS, GZRS

---

#### Transaction Optimized Tier
**Cost Components:**
- **Capacity**: $ per GiB/month (higher than Hot, varies by redundancy)
- **Transactions**: $ per 10,000 transactions (lower than Hot)
  - Write operations
  - List/create container operations
  - Read operations
  - All other operations
- **Data Transfer**: 
  - Ingress: Free
  - Egress: $ per GB
- **Snapshots**: $ per GiB/month (differential only)

**Redundancy Options:** LRS, ZRS, GRS, GZRS

---

### Provisioned Shares (Premium Files)

#### Premium LRS
**Cost Components:**
- **Provisioned Capacity**: $ per GiB/month
  - Minimum: 100 GiB
  - Includes performance guarantees:
    - Baseline IOPS: 400 + 1 IOPS per GiB (up to 100,000)
    - Baseline throughput: 0.04 MiB/s per provisioned GiB + 40 MiB/s (up to 10 GiB/s)
    - Burst IOPS: 4,000 or 3x baseline (whichever is higher)
    - Burst throughput: 3x baseline (up to 10 GiB/s)
- **Snapshots**: $ per GiB/month (differential only, billed separately from provisioned capacity)
- **Transactions**: Included (no per-transaction charges)
- **Data Transfer**:
  - Ingress: Free
  - Egress: $ per GB

---

#### Premium ZRS
**Cost Components:**
- **Provisioned Capacity**: $ per GiB/month (higher than LRS)
  - Minimum: 100 GiB
  - Same performance guarantees as Premium LRS
- **Snapshots**: $ per GiB/month (differential only)
- **Transactions**: Included
- **Data Transfer**:
  - Ingress: Free
  - Egress: $ per GB

---

#### Premium GRS (if available)
**Cost Components:**
- **Provisioned Capacity**: $ per GiB/month (higher than ZRS)
  - Minimum: 100 GiB
  - Same performance guarantees as Premium LRS
- **Snapshots**: $ per GiB/month (differential only)
- **Transactions**: Included
- **Data Transfer**:
  - Ingress: Free
  - Egress: $ per GB
  - Geo-replication transfer: $ per GB

---

#### Premium GZRS (if available)
**Cost Components:**
- **Provisioned Capacity**: $ per GiB/month (highest redundancy cost)
  - Minimum: 100 GiB
  - Same performance guarantees as Premium LRS
- **Snapshots**: $ per GiB/month (differential only)
- **Transactions**: Included
- **Data Transfer**:
  - Ingress: Free
  - Egress: $ per GB
  - Geo-replication transfer: $ per GB

---

## Azure NetApp Files (ANF)

### Volume Types Overview

ANF offers three volume types with different capacity and throughput limits:

1. **Regular Volumes**
   - Capacity: 50 GiB - 100 TiB
   - Max throughput: 4,500 MiB/s (empirical limit for regular volumes)

2. **Large Volumes**
   - Capacity: 50 TiB - 1,024 TiB (1 PiB)
   - With cool access enabled: Up to 7.2 PiB (by request, preview)
   - Max throughput: 12,800 MiB/s (throughput ceiling for Standard/Premium/Ultra)

3. **Large Volumes Breakthrough Mode** (Preview)
   - Capacity: 2,400 GiB (2.4 TiB) - 2,400 TiB (2 PiB)
   - Max throughput: Up to 50 GiB/s (50,000 MiB/s) depending on workload
   - Uses 6 storage endpoints per volume
   - Dedicated capacity (no noisy neighbors)
   - Cool access can be enabled after volume creation

**Important Notes:**
- Regular volumes cannot be converted to large volumes
- Large volumes cannot be resized below 50 TiB
- Large volumes can only grow by max 30% of lowest provisioned size (adjustable via support request)
- Cool access minimum size: 2,400 GiB when enabled

---

## Azure NetApp Files (ANF)

### Standard Tier
**Cost Components:**
- **Provisioned Capacity**: $ per GiB/month
- **Included Throughput**: 16 MiB/s per TiB provisioned
- **Snapshots**: Consume volume capacity (no separate cost)
- **Cross-region replication (CRR)**: 
  - Transfer cost: $ per GiB transferred (varies by replication frequency)
  - Destination volume: $ per GiB/month (can use lower service tier to reduce standby costs)
- **Cross-zone replication (CZR)**: 
  - Transfer cost: None
  - Destination volume: $ per GiB/month (can use lower service tier to reduce standby costs)

**Capacity Limits:**
- Regular volumes: 50 GiB - 100 TiB
- Large volumes: 50 TiB - 1,024 TiB
- Large volumes with cool access: Up to 7.2 PiB (preview, by request)
- Breakthrough mode: 2.4 TiB - 2 PiB

**Throughput Limits:**
- Regular volumes: Up to 4,500 MiB/s
- Large volumes: Up to 12,800 MiB/s
- Breakthrough mode: Up to 50,000 MiB/s (50 GiB/s)

**Performance Characteristics:**
- Latency: Sub-millisecond (same across all tiers)
- Maximum throughput per volume: Limited by capacity × 16 MiB/s per TiB (or volume type ceiling)

---

### Premium Tier
**Cost Components:**
- **Provisioned Capacity**: $ per GiB/month (higher than Standard)
- **Included Throughput**: 64 MiB/s per TiB provisioned
- **Snapshots**: Consume volume capacity (no separate cost)
- **Cross-region replication (CRR)**: 
  - Transfer cost: $ per GiB transferred (varies by replication frequency)
  - Destination volume: $ per GiB/month (can use lower service tier to reduce standby costs)
- **Cross-zone replication (CZR)**: 
  - Transfer cost: None
  - Destination volume: $ per GiB/month (can use lower service tier to reduce standby costs)

**Capacity Limits:**
- Regular volumes: 50 GiB - 100 TiB
- Large volumes: 50 TiB - 1,024 TiB
- Large volumes with cool access: Up to 7.2 PiB (preview, by request)
- Breakthrough mode: 2.4 TiB - 2 PiB

**Throughput Limits:**
- Regular volumes: Up to 4,500 MiB/s
- Large volumes: Up to 12,800 MiB/s
- Breakthrough mode: Up to 50,000 MiB/s (50 GiB/s)

**Performance Characteristics:**
- Latency: Sub-millisecond (same across all tiers)
- Maximum throughput per volume: Limited by capacity × 64 MiB/s per TiB (or volume type ceiling)

---

### Ultra Tier
**Cost Components:**
- **Provisioned Capacity**: $ per GiB/month (higher than Premium)
- **Included Throughput**: 128 MiB/s per TiB provisioned
- **Snapshots**: Consume volume capacity (no separate cost)
- **Cross-region replication (CRR)**: 
  - Transfer cost: $ per GiB transferred (varies by replication frequency)
  - Destination volume: $ per GiB/month (can use lower service tier to reduce standby costs)
- **Cross-zone replication (CZR)**: 
  - Transfer cost: None
  - Destination volume: $ per GiB/month (can use lower service tier to reduce standby costs)

**Capacity Limits:**
- Regular volumes: 50 GiB - 100 TiB
- Large volumes: 50 TiB - 1,024 TiB
- Large volumes with cool access: Up to 7.2 PiB (preview, by request)
- Breakthrough mode: 2.4 TiB - 2 PiB

**Throughput Limits:**
- Regular volumes: Up to 4,500 MiB/s
- Large volumes: Up to 12,800 MiB/s
- Breakthrough mode: Up to 50,000 MiB/s (50 GiB/s)

**Performance Characteristics:**
- Latency: Sub-millisecond (same across all tiers)
- Maximum throughput per volume: Limited by capacity × 128 MiB/s per TiB (or volume type ceiling)

---

### Standard with Cool Access
**Cost Components:**
- **Hot Capacity**: $ per GiB/month (Standard tier pricing)
- **Included Throughput**: 16 MiB/s per TiB provisioned (no reduction for cool access)
- **Cool Capacity**: $ per GiB/month (significantly lower than hot)
- **Cool Data Tiering (Hot → Cool)**: $ per GiB transferred
- **Cool Data Retrieval (Cool → Hot)**: $ per GiB transferred
- **Snapshots**: Consume volume capacity (no separate cost)
- **Cross-region replication (CRR)**: 
  - Transfer cost: $ per GiB transferred (varies by replication frequency)
  - Destination volume: $ per GiB/month (can use lower service tier to reduce standby costs)
- **Cross-zone replication (CZR)**: 
  - Transfer cost: None
  - Destination volume: $ per GiB/month (can use lower service tier to reduce standby costs)

**Capacity Limits:**
- Regular volumes with cool access: Minimum 2,400 GiB (when cool access enabled)
- Large volumes with cool access: Up to 7.2 PiB (preview, by request)
- Breakthrough mode with cool access: 2.4 TiB - 2 PiB (cool access enabled after creation)

**Throughput Limits:**
- Same as Standard tier without cool access

**Tiering Behavior:**
- Data blocks unused for 2-182 days (configurable) automatically moved to cool tier
- Transparent tiering - no application changes required

**Performance Characteristics:**
- Hot data: Same latency as Standard tier
- Cool data: Higher latency on first access (data retrieval penalty)
- Included throughput: 16 MiB/s per TiB (no reduction)

---

### Flexible Tier
**Cost Components:**
- **Capacity**: $ per GiB/month
- **Included Throughput**: 128 MiB/s (base, not per TiB)
- **Additional Throughput**: $ per MiB/s/month (purchased separately)
- **Snapshots**: Consume volume capacity (no separate cost)
- **Cross-region replication (CRR)**: 
  - Transfer cost: $ per GiB transferred (varies by replication frequency)
  - Destination volume: $ per GiB/month (can use lower service tier to reduce standby costs)
- **Cross-zone replication (CZR)**: 
  - Transfer cost: None
  - Destination volume: $ per GiB/month (can use lower service tier to reduce standby costs)

**Capacity Limits:**
- Regular volumes: 50 GiB - 100 TiB
- Large volumes: 50 TiB - 1,024 TiB
- Breakthrough mode: 2.4 TiB - 2 PiB

**Throughput Limits:**
- Minimum: 128 MiB/s (per capacity pool, regardless of pool quota)
- Maximum: 640 MiB/s per TiB of capacity pool size (5 × 128 MiB/s per TiB)
- Regular volumes: Up to 4,500 MiB/s
- Large volumes: Up to 12,800 MiB/s
- Breakthrough mode: Up to 50,000 MiB/s (50 GiB/s)

**Performance Characteristics:**
- Latency: Sub-millisecond (same across all tiers)
- Throughput: 128 MiB/s included + purchased throughput
- Capacity and throughput are **independent** (unlike other tiers)
- Requires manual QoS capacity pools

**Key Difference:**
- Other tiers: Throughput tied to capacity (X MiB/s per TiB)
- Flexible: Pay separately for capacity and throughput as needed

---

### Flexible with Cool Access
**Cost Components:**
- **Hot Capacity**: $ per GiB/month
- **Included Throughput**: 128 MiB/s (base, not per TiB)
- **Additional Throughput**: $ per MiB/s/month (purchased separately)
- **Cool Capacity**: $ per GiB/month (significantly lower than hot)
- **Cool Data Tiering (Hot → Cool)**: $ per GiB transferred
- **Cool Data Retrieval (Cool → Hot)**: $ per GiB transferred
- **Snapshots**: Consume volume capacity (no separate cost)
- **Cross-region replication (CRR)**: 
  - Transfer cost: $ per GiB transferred (varies by replication frequency)
  - Destination volume: $ per GiB/month (can use lower service tier to reduce standby costs)
- **Cross-zone replication (CZR)**: 
  - Transfer cost: None
  - Destination volume: $ per GiB/month (can use lower service tier to reduce standby costs)

**Tiering Behavior:**
- Data blocks unused for 2-182 days (configurable) automatically moved to cool tier
- Transparent tiering - no application changes required
- Cool tier for infrequently accessed data
- Hot tier for active data

**Capacity Limits:**
- Regular volumes with cool access: Minimum 2,400 GiB
- Large volumes with cool access: Up to 7.2 PiB (preview, by request)
- Breakthrough mode with cool access: 2.4 TiB - 2 PiB (cool access enabled after creation)

**Throughput Limits:**
- Same as Flexible tier without cool access

**Performance Characteristics:**
- Hot data: Sub-millisecond latency
- Cool data: Higher latency on first access (data retrieval penalty)
- Throughput: 128 MiB/s included + purchased throughput
- Capacity and throughput are **independent** (unlike other tiers)

**Key Difference:**
- Combines flexible throughput model with cool access capabilities

---

### Premium with Cool Access
**Cost Components:**
- **Hot Capacity**: $ per GiB/month (Premium tier pricing)
- **Included Throughput**: **36 MiB/s per TiB** (reduced from standard Premium 64 MiB/s)
- **Cool Capacity**: $ per GiB/month
- **Cool Data Tiering (Hot → Cool)**: $ per GiB transferred
- **Cool Data Retrieval (Cool → Hot)**: $ per GiB transferred
- **Snapshots**: Consume volume capacity (no separate cost)
- **Cross-region replication (CRR)**: 
  - Transfer cost: $ per GiB transferred (varies by replication frequency)
  - Destination volume: $ per GiB/month (can use lower service tier to reduce standby costs)
- **Cross-zone replication (CZR)**: 
  - Transfer cost: None
  - Destination volume: $ per GiB/month (can use lower service tier to reduce standby costs)

**Capacity Limits:**
- Regular volumes with cool access: Minimum 2,400 GiB
- Large volumes with cool access: Up to 7.2 PiB (preview, by request)
- Breakthrough mode with cool access: 2.4 TiB - 2 PiB (cool access enabled after creation)

**Throughput Limits:**
- Regular volumes: Up to 4,500 MiB/s
- Large volumes: Up to 12,800 MiB/s
- Breakthrough mode: Up to 50,000 MiB/s (50 GiB/s)

**Tiering Behavior:**
- Data blocks unused for 2-182 days (configurable) automatically moved to cool tier
- Transparent tiering - no application changes required

**Important Notes:**
- Enabling cool access **reduces the included throughput per TiB from 64 to 36 MiB/s**
- Must account for throughput reduction when calculating tier optimization

---

### Ultra with Cool Access
**Cost Components:**
- **Hot Capacity**: $ per GiB/month (Ultra tier pricing)
- **Included Throughput**: **68 MiB/s per TiB** (reduced from standard Ultra 128 MiB/s)
- **Cool Capacity**: $ per GiB/month
- **Cool Data Tiering (Hot → Cool)**: $ per GiB transferred
- **Cool Data Retrieval (Cool → Hot)**: $ per GiB transferred
- **Snapshots**: Consume volume capacity (no separate cost)
- **Cross-region replication (CRR)**: 
  - Transfer cost: $ per GiB transferred (varies by replication frequency)
  - Destination volume: $ per GiB/month (can use lower service tier to reduce standby costs)
- **Cross-zone replication (CZR)**: 
  - Transfer cost: None
  - Destination volume: $ per GiB/month (can use lower service tier to reduce standby costs)

**Capacity Limits:**
- Regular volumes with cool access: Minimum 2,400 GiB
- Large volumes with cool access: Up to 7.2 PiB (preview, by request)
- Breakthrough mode with cool access: 2.4 TiB - 2 PiB (cool access enabled after creation)

**Throughput Limits:**
- Regular volumes: Up to 4,500 MiB/s
- Large volumes: Up to 12,800 MiB/s
- Breakthrough mode: Up to 50,000 MiB/s (50 GiB/s)

**Tiering Behavior:**
- Data blocks unused for 2-182 days (configurable) automatically moved to cool tier
- Transparent tiering - no application changes required

**Important Notes:**
- Enabling cool access **reduces the included throughput per TiB from 128 to 68 MiB/s**
- Must account for throughput reduction when calculating tier optimization

---

## Managed Disks

### Standard HDD
**Cost Components:**
- **Disk Capacity**: $ per disk/month (based on provisioned size tier: S4, S6, S10, S15, S20, S30, S40, S50, S60, S70, S80)
- **Transactions**: $ per 10,000 transactions
- **Snapshots**: $ per GiB/month (differential only)

---

### Standard SSD
**Cost Components:**
- **Disk Capacity**: $ per disk/month (based on provisioned size tier: E1, E2, E3, E4, E6, E10, E15, E20, E30, E40, E50, E60, E70, E80)
- **Transactions**: $ per 10,000 transactions (typically lower than Standard HDD)
- **Snapshots**: $ per GiB/month (differential only)

---

### Premium SSD
**Cost Components:**
- **Disk Capacity**: $ per disk/month (based on provisioned size tier: P1, P2, P3, P4, P6, P10, P15, P20, P30, P40, P50, P60, P70, P80)
- **Transactions**: Included (no per-transaction charges)
- **Snapshots**: $ per GiB/month (differential only)

---

### Premium SSD v2
**Cost Components:**
- **Disk Capacity**: $ per GiB/month
- **Provisioned IOPS**: $ per IOPS/month (above baseline)
- **Provisioned Throughput**: $ per MiB/s/month (above baseline)
- **Transactions**: Included
- **Snapshots**: $ per GiB/month (differential only)

**Notes:** Highly flexible, pay only for capacity + performance you configure

---

### Ultra Disk
**Cost Components:**
- **Disk Capacity**: $ per GiB/month
- **Provisioned IOPS**: $ per IOPS/month
- **Provisioned Throughput**: $ per MiB/s/month
- **Transactions**: Included
- **Snapshots**: $ per GiB/month (differential only)

**Notes:** Highest performance, most expensive option

---

## Regional Pricing Variations

**All pricing varies by Azure region.** The Azure Retail Prices API must be queried with:
- `armRegionName` (e.g., "eastus", "westeurope")
- `serviceName` (e.g., "Storage", "Azure NetApp Files", "Storage")
- `productName` and `skuName` filters

---

## Optimization Considerations

### Azure Files
- **Provisioned vs. Pay-as-you-go**: Compare based on actual usage patterns
- **Tier selection**: Balance capacity cost vs. transaction cost vs. performance needs
- **Redundancy optimization**: Higher redundancy = higher cost but better availability

### ANF
- **Service level optimization**: Match throughput requirements to lowest-cost tier
- **Cool access**: Evaluate cool data % and access frequency
- **Throughput-to-capacity ratio**: Avoid over-provisioning capacity just to get throughput
- **Cool access throughput penalty**: Account for reduced throughput when cool is enabled

### Managed Disks
- **Tier selection**: Choose smallest tier that meets capacity + IOPS + throughput needs
- **Premium SSD v2 vs. Premium SSD**: v2 can be cheaper for workloads that don't need full provisioned performance
- **Standard SSD vs. HDD**: Balance cost vs. performance requirements

---

## Data Sources

### Retail Prices API Queries
Examples of API filters needed:

**Azure Files (Pay-as-you-go):**
```
serviceName eq 'Storage' 
and productName eq 'Files'
and skuName eq 'Cool LRS' (or Hot, Transaction Optimized, etc.)
and armRegionName eq 'eastus'
```

**Azure Files (Provisioned/Premium):**
```
serviceName eq 'Storage'
and productName eq 'Premium Files'
and skuName eq 'Premium LRS' (or ZRS, etc.)
and armRegionName eq 'eastus'
```

**ANF:**
```
serviceName eq 'Azure NetApp Files'
and productName eq 'Azure NetApp Files Standard' (or Premium, Ultra)
and armRegionName eq 'eastus'
```

**Managed Disks:**
```
serviceName eq 'Storage'
and productName eq 'Premium SSD Managed Disks' (or Standard HDD, Standard SSD, etc.)
and armRegionName eq 'eastus'
```

---

## Notes for Implementation

1. **All $ values above are placeholders** - actual pricing must be fetched from Azure Retail Prices API
2. **Regional variations are significant** - always query by region
3. **Pricing changes over time** - implement caching with 24-hour TTL
4. **Estimate accuracy depends on**:
   - Transaction count estimation (Azure Monitor metrics)
   - Snapshot differential size (Azure Monitor metrics)
   - Cool data distribution for ANF (Azure Monitor metrics)
   - Throughput requirements for ANF tier optimization

---

## Maintenance

This document should be updated when:
- Azure introduces new tiers or service levels
- Pricing model structure changes (new cost components)
- Redundancy options are added/removed
- Cool access becomes available on new tiers
