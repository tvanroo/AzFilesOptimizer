# Storage Tier Cost Permutations and Calculations

This document defines all possible storage tier permutations across Azure Files, Azure NetApp Files (ANF), and Managed Disks, along with their cost calculation formulas.

**Important Notes:**
- All cost calculations are for a 720-hour billing period (30 days)
- The Azure Retail Prices API returns pricing in different units depending on the service:
  - **ANF Capacity**: Returned as $/GiB/hour (requires × 720 for 30-day cost)
  - **ANF Throughput (Flexible)**: Returned as $/MiB/s/hour (requires × 720 for 30-day cost)
  - **Azure Files**: Returned as $/GiB/month for storage, $ per 10K operations for transactions
  - **Managed Disks**: Returned as $/month for disks, $ per 10K operations for transactions
- All API URLs include `armRegionName` parameter set to a specific region (e.g., 'eastus2')
- Replace the region in API URLs with the target `{{region}}` variable during discovery

---

## Azure NetApp Files (ANF) - 11 Permutations

### 1. ANF Standard (Regular)
**Configuration:**
- Service Level: Standard
- Cool Access: Disabled
- Double Encryption: Disabled

**Cost Calculation:**
```
Total 720-Hour Cost = 
  Provisioned Capacity (GiB) × Standard Capacity Price ($/GiB/hour) × 720

Where:
  region = {{region}}  # Target Azure region (e.g., 'eastus2', 'westus', 'northeurope')
  Standard Capacity Price = API retailPrice for region ($/GiB/hour)

Notes:
- Minimum capacity: 50 GiB
- Included throughput: 16 MiB/s per TiB
- Snapshots consume volume capacity (no separate charge)
- API returns hourly pricing; multiply by 720 for 30-day cost
```

**Azure Retail Prices API URLs:**
```bash
# Capacity pricing - returns $/GiB/hour
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceName%20eq%20%27Azure%20NetApp%20Files%27%20and%20skuName%20eq%20%27Standard%27%20and%20meterName%20eq%20%27Standard%20Capacity%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

### 2. ANF Standard with Double Encryption
**Configuration:**
- Service Level: Standard
- Cool Access: Disabled
- Double Encryption: Enabled

**Cost Calculation:**
```
Total 720-Hour Cost = 
  Provisioned Capacity (GiB) × Standard Double Encrypted Capacity Price ($/GiB/hour) × 720

Where:
  region = {{region}}  # Target Azure region
  Standard Double Encrypted Capacity Price = API retailPrice for region ($/GiB/hour)

Notes:
- Minimum capacity: 50 GiB
- Included throughput: 16 MiB/s per TiB
- Double encryption cannot be combined with Cool Access
- Double encryption pricing is all-in-one (not base + premium)
- Snapshots consume volume capacity (no separate charge)
- API returns hourly pricing; multiply by 720 for 30-day cost
```

**Azure Retail Prices API URLs:**
```bash
# Double encryption capacity - returns $/GiB/hour
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Azure%20NetApp%20Files%27%20and%20productName%20eq%20%27Azure%20NetApp%20Files%27%20and%20skuName%20eq%20%27Standard%20Double%20Encrypted%27%20and%20meterName%20eq%20%27Standard%20Double%20Encrypted%20Capacity%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

### 3. ANF Standard with Cool Access
**Configuration:**
- Service Level: Standard
- Cool Access: Enabled
- Double Encryption: Disabled (incompatible with cool access)

**Cost Calculation:**
```
Total 720-Hour Cost = 
  (Hot Capacity (GiB) × Standard Hot Capacity Price ($/GiB/hour) × 720)
  + (Cool Capacity (GiB) × Standard Cool Capacity Price ($/GiB/hour) × 720)
  + (Data Tiered to Cool (GiB) × Tiering Price ($/GiB))
  + (Data Retrieved from Cool (GiB) × Retrieval Price ($/GiB))

Where:
  region = {{region}}  # Target Azure region
  Standard Hot Capacity Price = API retailPrice for Standard Capacity ($/GiB/hour)
  Standard Cool Capacity Price = API retailPrice for Cool Access Capacity ($/GiB/hour)
  Tiering Price = API retailPrice for Data Transfer ($/GiB, one-time)
  Retrieval Price = API retailPrice for Data Transfer ($/GiB, one-time)

Notes:
- Minimum capacity: 50 GiB (same as all ANF volumes)
- Included throughput: 16 MiB/s per TiB (no reduction)
- Hot capacity = Provisioned capacity - Cool capacity
- Tiering/retrieval charges apply only to data movement (one-time, not hourly)
- Capacity API returns hourly pricing; multiply by 720 for 30-day cost
```

**Azure Retail Prices API URLs:**
```bash
# Hot capacity pricing - returns $/GiB/hour
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceName%20eq%20%27Azure%20NetApp%20Files%27%20and%20skuName%20eq%20%27Standard%27%20and%20meterName%20eq%20%27Standard%20Capacity%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Cool capacity pricing - returns $/GiB/hour
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Azure%20NetApp%20Files%27%20and%20productName%20eq%20%27Azure%20NetApp%20Files%27%20and%20skuName%20eq%20%27Standard%20Storage%20with%20Cool%20Access%27%20and%20meterName%20eq%20%27Standard%20Storage%20with%20Cool%20Access%20Capacity%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Data transfer (tiering and retrieval) - returns $/GiB (one-time charge)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Azure%20NetApp%20Files%27%20and%20productName%20eq%20%27Azure%20NetApp%20Files%27%20and%20skuName%20eq%20%27Standard%20Storage%20with%20Cool%20Access%27%20and%20meterName%20eq%20%27Standard%20Storage%20with%20Cool%20Access%20Data%20Transfer%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

### 4. ANF Premium (Regular)
**Configuration:**
- Service Level: Premium
- Cool Access: Disabled
- Double Encryption: Disabled

**Cost Calculation:**
```
Total 720-Hour Cost = 
  Provisioned Capacity (GiB) × Premium Capacity Price ($/GiB/hour) × 720

Where:
  region = {{region}}  # Target Azure region
  Premium Capacity Price = API retailPrice for region ($/GiB/hour)

Notes:
- Minimum capacity: 50 GiB
- Included throughput: 64 MiB/s per TiB
- Snapshots consume volume capacity (no separate charge)
- API returns hourly pricing; multiply by 720 for 30-day cost
```

**Azure Retail Prices API URLs:**
```bash
# Capacity pricing - returns $/GiB/hour
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceName%20eq%20%27Azure%20NetApp%20Files%27%20and%20skuName%20eq%20%27Premium%27%20and%20meterName%20eq%20%27Premium%20Capacity%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

### 5. ANF Premium with Double Encryption
**Configuration:**
- Service Level: Premium
- Cool Access: Disabled
- Double Encryption: Enabled

**Cost Calculation:**
```
Total 720-Hour Cost = 
  Provisioned Capacity (GiB) × Premium Double Encrypted Capacity Price ($/GiB/hour) × 720

Where:
  region = {{region}}  # Target Azure region
  Premium Double Encrypted Capacity Price = API retailPrice for region ($/GiB/hour)

Notes:
- Minimum capacity: 50 GiB
- Included throughput: 64 MiB/s per TiB
- Double encryption cannot be combined with Cool Access
- Double encryption pricing is all-in-one (not base + premium)
- Snapshots consume volume capacity (no separate charge)
- API returns hourly pricing; multiply by 720 for 30-day cost
```

**Azure Retail Prices API URLs:**
```bash
# Capacity pricing - returns $/GiB/hour
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Azure%20NetApp%20Files%27%20and%20productName%20eq%20%27Azure%20NetApp%20Files%27%20and%20skuName%20eq%20%27Premium%20Double%20Encrypted%27%20and%20meterName%20eq%20%27Premium%20Double%20Encrypted%20Capacity%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

### 6. ANF Premium with Cool Access
**Configuration:**
- Service Level: Premium
- Cool Access: Enabled
- Double Encryption: Disabled (incompatible with cool access)

**Cost Calculation:**
```
Total 720-Hour Cost = 
  (Hot Capacity (GiB) × Premium Hot Capacity Price ($/GiB/hour) × 720)
  + (Cool Capacity (GiB) × Premium Cool Capacity Price ($/GiB/hour) × 720)
  + (Data Tiered to Cool (GiB) × Tiering Price ($/GiB))
  + (Data Retrieved from Cool (GiB) × Retrieval Price ($/GiB))

Where:
  region = {{region}}  # Target Azure region
  Premium Hot Capacity Price = API retailPrice for Premium Capacity ($/GiB/hour)
  Premium Cool Capacity Price = API retailPrice for Cool Access Capacity ($/GiB/hour)
  Tiering Price = API retailPrice for Data Transfer ($/GiB, one-time)
  Retrieval Price = API retailPrice for Data Transfer ($/GiB, one-time)

Notes:
- Minimum capacity: 50 GiB (same as all ANF volumes)
- Included throughput: 36 MiB/s per TiB (reduced from 64 MiB/s)
- Hot capacity = Provisioned capacity - Cool capacity
- Capacity API returns hourly pricing; multiply by 720 for 30-day cost
```

**Azure Retail Prices API URLs:**
```bash
# Hot capacity pricing - returns $/GiB/hour
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceName%20eq%20%27Azure%20NetApp%20Files%27%20and%20skuName%20eq%20%27Premium%27%20and%20meterName%20eq%20%27Premium%20Capacity%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Cool capacity pricing - returns $/GiB/hour
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Azure%20NetApp%20Files%27%20and%20productName%20eq%20%27Azure%20NetApp%20Files%27%20and%20skuName%20eq%20%27Standard%20Storage%20with%20Cool%20Access%27%20and%20meterName%20eq%20%27Standard%20Storage%20with%20Cool%20Access%20Capacity%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Data transfer (tiering and retrieval) - returns $/GiB (one-time charge)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Azure%20NetApp%20Files%27%20and%20productName%20eq%20%27Azure%20NetApp%20Files%27%20and%20skuName%20eq%20%27Standard%20Storage%20with%20Cool%20Access%27%20and%20meterName%20eq%20%27Standard%20Storage%20with%20Cool%20Access%20Data%20Transfer%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

### 7. ANF Ultra (Regular)
**Configuration:**
- Service Level: Ultra
- Cool Access: Disabled
- Double Encryption: Disabled

**Cost Calculation:**
```
Total 720-Hour Cost = 
  Provisioned Capacity (GiB) × Ultra Capacity Price ($/GiB/hour) × 720

Where:
  region = {{region}}  # Target Azure region
  Ultra Capacity Price = API retailPrice for region ($/GiB/hour)

Notes:
- Minimum capacity: 50 GiB
- Included throughput: 128 MiB/s per TiB
- Snapshots consume volume capacity (no separate charge)
- API returns hourly pricing; multiply by 720 for 30-day cost
```

**Azure Retail Prices API URLs:**
```bash
# Capacity pricing - returns $/GiB/hour
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceName%20eq%20%27Azure%20NetApp%20Files%27%20and%20skuName%20eq%20%27Ultra%27%20and%20meterName%20eq%20%27Ultra%20Capacity%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

### 8. ANF Ultra with Double Encryption
**Configuration:**
- Service Level: Ultra
- Cool Access: Disabled
- Double Encryption: Enabled

**Cost Calculation:**
```
Total 720-Hour Cost = 
  Provisioned Capacity (GiB) × Ultra Double Encrypted Capacity Price ($/GiB/hour) × 720

Where:
  region = {{region}}  # Target Azure region
  Ultra Double Encrypted Capacity Price = API retailPrice for region ($/GiB/hour)

Notes:
- Minimum capacity: 50 GiB
- Included throughput: 128 MiB/s per TiB
- Double encryption cannot be combined with Cool Access
- Double encryption pricing is all-in-one (not base + premium)
- Snapshots consume volume capacity (no separate charge)
- API returns hourly pricing; multiply by 720 for 30-day cost
```

**Azure Retail Prices API URLs:**
```bash
# Capacity pricing - returns $/GiB/hour
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Azure%20NetApp%20Files%27%20and%20productName%20eq%20%27Azure%20NetApp%20Files%27%20and%20skuName%20eq%20%27Ultra%20Double%20Encrypted%27%20and%20meterName%20eq%20%27Ultra%20Double%20Encrypted%20Capacity%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

### 9. ANF Ultra with Cool Access
**Configuration:**
- Service Level: Ultra
- Cool Access: Enabled
- Double Encryption: Disabled (incompatible with cool access)

**Cost Calculation:**
```
Total 720-Hour Cost = 
  (Hot Capacity (GiB) × Ultra Hot Capacity Price ($/GiB/hour) × 720)
  + (Cool Capacity (GiB) × Ultra Cool Capacity Price ($/GiB/hour) × 720)
  + (Data Tiered to Cool (GiB) × Tiering Price ($/GiB))
  + (Data Retrieved from Cool (GiB) × Retrieval Price ($/GiB))

Where:
  region = {{region}}  # Target Azure region
  Ultra Hot Capacity Price = API retailPrice for Ultra Capacity ($/GiB/hour)
  Ultra Cool Capacity Price = API retailPrice for Cool Access Capacity ($/GiB/hour)
  Tiering Price = API retailPrice for Data Transfer ($/GiB, one-time)
  Retrieval Price = API retailPrice for Data Transfer ($/GiB, one-time)

Notes:
- Minimum capacity: 50 GiB (same as all ANF volumes)
- Included throughput: 68 MiB/s per TiB (reduced from 128 MiB/s)
- Hot capacity = Provisioned capacity - Cool capacity
- Capacity API returns hourly pricing; multiply by 720 for 30-day cost
```

**Azure Retail Prices API URLs:**
```bash
# Hot capacity pricing - returns $/GiB/hour
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceName%20eq%20%27Azure%20NetApp%20Files%27%20and%20skuName%20eq%20%27Ultra%27%20and%20meterName%20eq%20%27Ultra%20Capacity%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Cool capacity pricing - returns $/GiB/hour
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Azure%20NetApp%20Files%27%20and%20productName%20eq%20%27Azure%20NetApp%20Files%27%20and%20skuName%20eq%20%27Standard%20Storage%20with%20Cool%20Access%27%20and%20meterName%20eq%20%27Standard%20Storage%20with%20Cool%20Access%20Capacity%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Data transfer (tiering and retrieval) - returns $/GiB (one-time charge)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Azure%20NetApp%20Files%27%20and%20productName%20eq%20%27Azure%20NetApp%20Files%27%20and%20skuName%20eq%20%27Standard%20Storage%20with%20Cool%20Access%27%20and%20meterName%20eq%20%27Standard%20Storage%20with%20Cool%20Access%20Data%20Transfer%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

### 10. ANF Flexible (Regular)
**Configuration:**
- Service Level: Flexible
- Cool Access: Disabled
- Double Encryption: Disabled

**Cost Calculation:**
```
Total 720-Hour Cost = 
  (Provisioned Capacity (GiB) × Flexible Capacity Price ($/GiB/hour) × 720)
  + (Throughput Above Base (MiB/s) × Flexible Throughput Price ($/MiB/s/hour) × 720)

Where:
  region = {{region}}  # Target Azure region
  Flexible Capacity Price = API retailPrice for Capacity ($/GiB/hour)
  Flexible Throughput Price = API retailPrice for Throughput ($/MiB/s/hour)

Notes:
- Minimum capacity: 50 GiB
- Base included throughput: 128 MiB/s (flat, not per TiB)
- Maximum throughput: 640 MiB/s per TiB of capacity pool
- Throughput Above Base = max(0, Required Throughput - 128 MiB/s)
- Capacity and throughput are independent
- API returns hourly pricing; multiply by 720 for 30-day cost
```

**Azure Retail Prices API URLs:**
```bash
# Capacity pricing - returns $/GiB/hour
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Azure%20NetApp%20Files%27%20and%20productName%20eq%20%27Azure%20NetApp%20Files%27%20and%20skuName%20eq%20%27Flexible%20Service%20Level%27%20and%20meterName%20eq%20%27Flexible%20Service%20Level%20Capacity%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Throughput pricing - returns $/MiB/s/hour
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Azure%20NetApp%20Files%27%20and%20productName%20eq%20%27Azure%20NetApp%20Files%27%20and%20skuName%20eq%20%27Flexible%20Service%20Level%27%20and%20meterName%20eq%20%27Flexible%20Service%20Level%20Throughput%20MiBps%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

### 11. ANF Flexible with Cool Access
**Configuration:**
- Service Level: Flexible
- Cool Access: Enabled
- Double Encryption: Disabled (incompatible with cool access)

**Cost Calculation:**
```
Total 720-Hour Cost = 
  (Hot Capacity (GiB) × Flexible Hot Capacity Price ($/GiB/hour) × 720)
  + (Cool Capacity (GiB) × Flexible Cool Capacity Price ($/GiB/hour) × 720)
  + (Data Tiered to Cool (GiB) × Tiering Price ($/GiB))
  + (Data Retrieved from Cool (GiB) × Retrieval Price ($/GiB))
  + (Throughput Above Base (MiB/s) × Flexible Throughput Price ($/MiB/s/hour) × 720)

Where:
  region = {{region}}  # Target Azure region
  Flexible Hot Capacity Price = API retailPrice for Flexible Capacity ($/GiB/hour)
  Flexible Cool Capacity Price = API retailPrice for Cool Access Capacity ($/GiB/hour)
  Flexible Throughput Price = API retailPrice for Flexible Throughput ($/MiB/s/hour)
  Tiering Price = API retailPrice for Data Transfer ($/GiB, one-time)
  Retrieval Price = API retailPrice for Data Transfer ($/GiB, one-time)

Notes:
- Minimum capacity: 50 GiB (same as all ANF volumes)
- Base included throughput: 128 MiB/s (flat)
- Hot capacity = Provisioned capacity - Cool capacity
- Throughput pricing is independent of cool access
- Capacity and Throughput API returns hourly pricing; multiply by 720 for 30-day cost
```

**Azure Retail Prices API URLs:**
```bash
# Hot capacity pricing - returns $/GiB/hour
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Azure%20NetApp%20Files%27%20and%20productName%20eq%20%27Azure%20NetApp%20Files%27%20and%20skuName%20eq%20%27Flexible%20Service%20Level%27%20and%20meterName%20eq%20%27Flexible%20Service%20Level%20Capacity%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Throughput pricing - returns $/MiB/s/hour
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Azure%20NetApp%20Files%27%20and%20productName%20eq%20%27Azure%20NetApp%20Files%27%20and%20skuName%20eq%20%27Flexible%20Service%20Level%27%20and%20meterName%20eq%20%27Flexible%20Service%20Level%20Throughput%20MiBps%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Cool capacity pricing - returns $/GiB/hour
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Azure%20NetApp%20Files%27%20and%20productName%20eq%20%27Azure%20NetApp%20Files%27%20and%20skuName%20eq%20%27Standard%20Storage%20with%20Cool%20Access%27%20and%20meterName%20eq%20%27Standard%20Storage%20with%20Cool%20Access%20Capacity%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Data transfer (tiering and retrieval) - returns $/GiB (one-time charge)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Azure%20NetApp%20Files%27%20and%20productName%20eq%20%27Azure%20NetApp%20Files%27%20and%20skuName%20eq%20%27Standard%20Storage%20with%20Cool%20Access%27%20and%20meterName%20eq%20%27Standard%20Storage%20with%20Cool%20Access%20Data%20Transfer%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Azure%20NetApp%20Files%27%20and%20productName%20eq%20%27Azure%20NetApp%20Files%27%20and%20skuName%20eq%20%27Standard%20Storage%20with%20Cool%20Access%27%20and%20meterName%20eq%20%27Standard%20Storage%20with%20Cool%20Access%20Data%20Transfer%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

---

## Azure Files - 16 Permutations

### Pay-as-you-go Tiers (12 permutations)

#### Hot Tier (4 redundancy options × 1 tier)
**Redundancy Options:** LRS, ZRS, GRS, GZRS

**Cost Calculation (same for all redundancy levels):**
```
Total 720-Hour Cost = 
  Used Capacity (GiB) × Hot Storage Price ($/GiB/month)
  + (Write Operations / 10,000) × Write Operation Price ($/10K)
  + (Read Operations / 10,000) × Read Operation Price ($/10K)
  + (List Operations / 10,000) × List Operation Price ($/10K)
  + (Other Operations / 10,000) × Other Operation Price ($/10K)
  + Snapshot Capacity (GiB) × Snapshot Price ($/GiB/month)
  + Egress Data (GiB) × Egress Price ($/GiB)

Where:
  region = {{region}}  # Target Azure region
  Hot Storage Price = API retailPrice for Data Stored ($/GiB/month, varies by redundancy)
  Write/Read/List/Other Operation Prices = API retailPrice ($ per 10K operations)
  Snapshot Price = API retailPrice for snapshots ($/GiB/month, varies by redundancy)

Notes:
- Ingress is free
- Storage pricing varies by redundancy: LRS < ZRS < GRS < GZRS
- Snapshots are differential only
- Transaction prices vary by operation type and redundancy
- API returns monthly pricing for storage (already covers 720 hours/30 days)
- API returns cost per 10K operations for transactions
```

**Azure Retail Prices API URLs (Hot Tier):**

**Hot LRS:**
```bash
# Data Stored - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Hot%20LRS%27%20and%20meterName%20eq%20%27Hot%20LRS%20Data%20Stored%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Write Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Hot%20LRS%27%20and%20meterName%20eq%20%27Hot%20LRS%20Write%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Read Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Hot%20LRS%27%20and%20meterName%20eq%20%27Hot%20Read%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# List Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Hot%20LRS%27%20and%20meterName%20eq%20%27Hot%20LRS%20List%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Other Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Hot%20LRS%27%20and%20meterName%20eq%20%27Hot%20Other%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Metadata - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Hot%20LRS%27%20and%20meterName%20eq%20%27LRS%20Metadata%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

**Hot ZRS:**
```bash
# Data Stored - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Hot%20ZRS%27%20and%20meterName%20eq%20%27Hot%20ZRS%20Data%20Stored%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Write Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Hot%20ZRS%27%20and%20meterName%20eq%20%27Hot%20ZRS%20Write%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Read Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Hot%20ZRS%27%20and%20meterName%20eq%20%27Hot%20Read%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# List Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Hot%20ZRS%27%20and%20meterName%20eq%20%27Hot%20ZRS%20List%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Other Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Hot%20ZRS%27%20and%20meterName%20eq%20%27Hot%20Other%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Metadata - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Hot%20ZRS%27%20and%20meterName%20eq%20%27ZRS%20Metadata%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

**Hot GRS:**
```bash
# Data Stored - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Hot%20GRS%27%20and%20meterName%20eq%20%27Hot%20GRS%20Data%20Stored%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Write Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Hot%20GRS%27%20and%20meterName%20eq%20%27Hot%20GRS%20Write%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Read Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Hot%20GRS%27%20and%20meterName%20eq%20%27Hot%20Read%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# List Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Hot%20GRS%27%20and%20meterName%20eq%20%27Hot%20GRS%20List%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Other Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Hot%20GRS%27%20and%20meterName%20eq%20%27Hot%20Other%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Metadata - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Hot%20GRS%27%20and%20meterName%20eq%20%27GRS%20Metadata%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

**Hot GZRS:**
```bash
# Data Stored - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Hot%20GZRS%27%20and%20meterName%20eq%20%27Hot%20GZRS%20Data%20Stored%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Write Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Hot%20GZRS%27%20and%20meterName%20eq%20%27Hot%20GZRS%20Write%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Read Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Hot%20GZRS%27%20and%20meterName%20eq%20%27Hot%20Read%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# List Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Hot%20GZRS%27%20and%20meterName%20eq%20%27Hot%20GZRS%20List%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Other Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Hot%20GZRS%27%20and%20meterName%20eq%20%27Hot%20GZRS%20Other%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Metadata - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Hot%20GZRS%27%20and%20meterName%20eq%20%27GZRS%20Metadata%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

#### Cool Tier (4 redundancy options × 1 tier)
**Redundancy Options:** LRS, ZRS, GRS, GZRS

**Cost Calculation:**
```
Total 720-Hour Cost = 
  Used Capacity (GiB) × Cool Storage Price ($/GiB/month)
  + (Write Operations / 10,000) × Write Operation Price ($/10K)
  + (Read Operations / 10,000) × Read Operation Price ($/10K)
  + (List Operations / 10,000) × List Operation Price ($/10K)
  + (Other Operations / 10,000) × Other Operation Price ($/10K)
  + Data Retrieval (GiB) × Data Retrieval Price ($/GiB)
  + Snapshot Capacity (GiB) × Snapshot Price ($/GiB/month)
  + Egress Data (GiB) × Egress Price ($/GiB)

Where:
  region = {{region}}  # Target Azure region
  Cool Storage Price = API retailPrice for Data Stored ($/GiB/month, varies by redundancy)
  Write/Read/List/Other Operation Prices = API retailPrice ($ per 10K operations)
  Data Retrieval Price = API retailPrice for Data Retrieval ($/GiB, one-time charge)
  Snapshot Price = API retailPrice for snapshots ($/GiB/month, varies by redundancy)

Notes:
- Storage price is lower than Hot, but transaction prices are higher
- Additional data retrieval charge applies
- Data retrieval = amount of data read from cool tier
- API returns monthly pricing for storage (already covers 720 hours/30 days)
- API returns cost per 10K operations for transactions
- API returns $/GiB for data retrieval (one-time charge)
```

**Azure Retail Prices API URLs (Cool Tier):**

**Cool LRS:**
```bash
# Data Stored - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Cool%20LRS%27%20and%20meterName%20eq%20%27Cool%20LRS%20Data%20Stored%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Write Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Cool%20LRS%27%20and%20meterName%20eq%20%27Cool%20LRS%20Write%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Read Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Cool%20LRS%27%20and%20meterName%20eq%20%27Cool%20Read%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# List Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Cool%20LRS%27%20and%20meterName%20eq%20%27Cool%20LRS%20List%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Other Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Cool%20LRS%27%20and%20meterName%20eq%20%27Cool%20Other%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Data Retrieval - returns $/GiB (one-time charge)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Cool%20LRS%27%20and%20meterName%20eq%20%27Cool%20Data%20Retrieval%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Metadata - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Cool%20LRS%27%20and%20meterName%20eq%20%27LRS%20Metadata%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

**Cool ZRS:**
```bash
# Data Stored - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Cool%20ZRS%27%20and%20meterName%20eq%20%27Cool%20ZRS%20Data%20Stored%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Write Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Cool%20ZRS%27%20and%20meterName%20eq%20%27Cool%20ZRS%20Write%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Read Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Cool%20ZRS%27%20and%20meterName%20eq%20%27Cool%20Read%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# List Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Cool%20ZRS%27%20and%20meterName%20eq%20%27Cool%20ZRS%20List%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Other Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Cool%20ZRS%27%20and%20meterName%20eq%20%27Cool%20Other%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Data Retrieval - returns $/GiB (one-time charge)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Cool%20ZRS%27%20and%20meterName%20eq%20%27Cool%20Data%20Retrieval%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Metadata - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Cool%20ZRS%27%20and%20meterName%20eq%20%27ZRS%20Metadata%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

**Cool GRS:**
```bash
# Data Stored - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Cool%20GRS%27%20and%20meterName%20eq%20%27Cool%20GRS%20Data%20Stored%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Write Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Cool%20GRS%27%20and%20meterName%20eq%20%27Cool%20GRS%20Write%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Read Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Cool%20GRS%27%20and%20meterName%20eq%20%27Cool%20Read%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# List Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Cool%20GRS%27%20and%20meterName%20eq%20%27Cool%20GRS%20List%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Other Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Cool%20GRS%27%20and%20meterName%20eq%20%27Cool%20Other%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Data Retrieval - returns $/GiB (one-time charge)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Cool%20GRS%27%20and%20meterName%20eq%20%27Cool%20Data%20Retrieval%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Metadata - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Cool%20GRS%27%20and%20meterName%20eq%20%27GRS%20Metadata%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

**Cool GZRS:**
```bash
# Data Stored - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Cool%20GZRS%27%20and%20meterName%20eq%20%27Cool%20GZRS%20Data%20Stored%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Write Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Cool%20GZRS%27%20and%20meterName%20eq%20%27Cool%20GZRS%20Write%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Read Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Cool%20GZRS%27%20and%20meterName%20eq%20%27Cool%20Read%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# List Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Cool%20GZRS%27%20and%20meterName%20eq%20%27Cool%20GZRS%20List%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Other Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Cool%20GZRS%27%20and%20meterName%20eq%20%27Cool%20GZRS%20Other%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Data Retrieval - returns $/GiB (one-time charge)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Cool%20GZRS%27%20and%20meterName%20eq%20%27Cool%20GZRS%20Data%20Retrieval%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Metadata - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Cool%20GZRS%27%20and%20meterName%20eq%20%27GZRS%20Metadata%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

#### Transaction Optimized Tier (4 redundancy options × 1 tier)
**Redundancy Options:** LRS, ZRS, GRS, GZRS

**Cost Calculation:**
```
Total 720-Hour Cost = 
  Used Capacity (GiB) × TransactionOpt Storage Price ($/GiB/month) [varies by redundancy]
  + (Write Operations / 10,000) × Write Operation Price ($/10K)
  + (Read Operations / 10,000) × Read Operation Price ($/10K)
  + (List Operations / 10,000) × List Operation Price ($/10K)
  + (Other Operations / 10,000) × Other Operation Price ($/10K)
  + Snapshot Capacity (GiB) × Snapshot Price ($/GiB/month)
  + Egress Data (GiB) × Egress Price ($/GiB)

Notes:
- Storage price is higher than Hot
- Transaction prices are lower than Hot (optimized for high transaction workloads)
- Best for workloads with high transaction counts
- This tier is also known as "Standard" in the Azure Retail Prices API
```

**Azure Retail Prices API URLs (Transaction Optimized/Standard Tier):**

**Standard LRS:**
```bash
# Data Stored - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Standard%20LRS%27%20and%20meterName%20eq%20%27LRS%20Data%20Stored%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Write Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Standard%20LRS%27%20and%20meterName%20eq%20%27LRS%20Write%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Read Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Standard%20LRS%27%20and%20meterName%20eq%20%27Read%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# List Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Standard%20LRS%27%20and%20meterName%20eq%20%27List%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Delete Operations (Other)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Standard%20LRS%27%20and%20meterName%20eq%20%27Delete%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Protocol Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Standard%20LRS%27%20and%20meterName%20eq%20%27Protocol%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

**Standard ZRS:**
```bash
# Data Stored - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Standard%20ZRS%27%20and%20meterName%20eq%20%27ZRS%20Data%20Stored%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Write Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Standard%20ZRS%27%20and%20meterName%20eq%20%27ZRS%20Write%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Read Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Standard%20ZRS%27%20and%20meterName%20eq%20%27ZRS%20Read%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# List Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Standard%20ZRS%27%20and%20meterName%20eq%20%27ZRS%20List%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Delete Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Standard%20ZRS%27%20and%20meterName%20eq%20%27Delete%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Protocol Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Standard%20ZRS%27%20and%20meterName%20eq%20%27ZRS%20Protocol%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

**Standard GRS:**
```bash
# Data Stored - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Standard%20GRS%27%20and%20meterName%20eq%20%27GRS%20Data%20Stored%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Write Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Standard%20GRS%27%20and%20meterName%20eq%20%27GRS%20Write%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Read Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Standard%20GRS%27%20and%20meterName%20eq%20%27Read%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# List Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Standard%20GRS%27%20and%20meterName%20eq%20%27List%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Delete Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Standard%20GRS%27%20and%20meterName%20eq%20%27Delete%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Protocol Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Standard%20GRS%27%20and%20meterName%20eq%20%27Protocol%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

**Standard GZRS:**
```bash
# Data Stored - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Standard%20GZRS%27%20and%20meterName%20eq%20%27GZRS%20Data%20Stored%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Write Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Standard%20GZRS%27%20and%20meterName%20eq%20%27GZRS%20Write%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Read Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Standard%20GZRS%27%20and%20meterName%20eq%20%27GZRS%20Read%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# List Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Standard%20GZRS%27%20and%20meterName%20eq%20%27GZRS%20List%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Delete Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Standard%20GZRS%27%20and%20meterName%20eq%20%27Delete%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Protocol Operations - returns $ per 10K operations
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Files%20v2%27%20and%20skuName%20eq%20%27Standard%20GZRS%27%20and%20meterName%20eq%20%27GZRS%20Protocol%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

### Provisioned (Premium Files) Tiers (4 permutations)

#### Premium LRS
**Cost Calculation:**
```
Total 720-Hour Cost = 
  Provisioned Capacity (GiB) × Premium LRS Price ($/GiB/month)
  + Snapshot Capacity (GiB) × Premium Snapshot Price ($/GiB/month)
  + Egress Data (GiB) × Egress Price ($/GiB)

Notes:
- Minimum provisioned capacity: 100 GiB
- Transactions are included (no per-transaction charges)
- Performance included:
  - Baseline IOPS: 400 + 1 IOPS per GiB (up to 100,000)
  - Baseline throughput: 0.04 MiB/s per GiB + 40 MiB/s (up to 10 GiB/s)
  - Burst IOPS: 4,000 or 3× baseline (whichever is higher)
  - Burst throughput: 3× baseline (up to 10 GiB/s)
```

**Azure Retail Prices API URLs (Premium LRS):**
```bash
# Provisioned Storage - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Premium%20Files%27%20and%20skuName%20eq%20%27Premium%20LRS%27%20and%20meterName%20eq%20%27Premium%20LRS%20Provisioned%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Snapshots - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Premium%20Files%27%20and%20skuName%20eq%20%27Premium%20LRS%27%20and%20meterName%20eq%20%27Premium%20LRS%20Snapshots%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Burst Bandwidth - returns $/unit/month (optional if bursting beyond baseline)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Premium%20Files%27%20and%20skuName%20eq%20%27Premium%20LRS%27%20and%20meterName%20eq%20%27Premium%20LRS%20Burst%20Bandwidth%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Burst Transactions - returns $/unit/month (optional if bursting beyond baseline)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Premium%20Files%27%20and%20skuName%20eq%20%27Premium%20LRS%27%20and%20meterName%20eq%20%27Premium%20LRS%20Burst%20Transactions%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

#### Premium ZRS
**Cost Calculation:**
```
Total 720-Hour Cost = 
  Provisioned Capacity (GiB) × Premium ZRS Price ($/GiB/month)
  + Snapshot Capacity (GiB) × Premium Snapshot Price ($/GiB/month)
  + Egress Data (GiB) × Egress Price ($/GiB)

Notes:
- Same as Premium LRS but with higher storage price for zone redundancy
- Same minimum capacity and performance characteristics as LRS
```

**Azure Retail Prices API URLs (Premium ZRS):**
```bash
# Provisioned Storage - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Premium%20Files%27%20and%20skuName%20eq%20%27Premium%20ZRS%27%20and%20meterName%20eq%20%27Premium%20ZRS%20Provisioned%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Snapshots - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Premium%20Files%27%20and%20skuName%20eq%20%27Premium%20ZRS%27%20and%20meterName%20eq%20%27Premium%20ZRS%20Snapshots%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Burst Bandwidth - returns $/unit/month (optional if bursting beyond baseline)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Premium%20Files%27%20and%20skuName%20eq%20%27Premium%20ZRS%27%20and%20meterName%20eq%20%27Premium%20ZRS%20Burst%20Bandwidth%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Burst Transactions - returns $/unit/month (optional if bursting beyond baseline)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Premium%20Files%27%20and%20skuName%20eq%20%27Premium%20ZRS%27%20and%20meterName%20eq%20%27Premium%20ZRS%20Burst%20Transactions%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

#### Premium GRS and Premium GZRS
**Note:** Premium GRS and Premium GZRS redundancy options are **not available** for Azure Files. Premium Files only supports LRS and ZRS redundancy options.

### Azure Files Provisioned v2 (6 permutations)

Azure Files Provisioned v2 offers flexible, pay-for-what-you-use provisioning with independent capacity, IOPS, and throughput configuration.

#### SSD LRS Provisioned v2
**Cost Calculation:**
```
Total 720-Hour Cost = 
  Provisioned Capacity (GiB) × SSD LRS Storage Price ($/GiB/month)
  + Provisioned IOPS (above free tier) × IOPS Price ($/IOPS/month)
  + Provisioned Throughput (above free tier) × Throughput Price ($/MiB/s/month)
  + Overflow Snapshot Usage (GiB) × Snapshot Price ($/GiB/month)
  + Soft-Deleted Usage (GiB) × Soft-Delete Price ($/GiB/month)

Notes:
- Includes free baseline IOPS and throughput per share
- Pay only for what you provision
- Flexible performance scaling
```

**Azure Retail Prices API URLs (SSD LRS Provisioned v2):**
```bash
# Provisioned Storage - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Azure%20Files%20Provisioned%20v2%27%20and%20skuName%20eq%20%27SSD%20LRS%27%20and%20meterName%20eq%20%27SSD%20LRS%20Provisioned%20Storage%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Provisioned IOPS (above free tier)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Azure%20Files%20Provisioned%20v2%27%20and%20skuName%20eq%20%27SSD%20LRS%27%20and%20meterName%20eq%20%27SSD%20LRS%20Provisioned%20IOPS%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Provisioned IOPS Free (baseline)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Azure%20Files%20Provisioned%20v2%27%20and%20skuName%20eq%20%27SSD%20LRS%27%20and%20meterName%20eq%20%27SSD%20LRS%20Provisioned%20IOPS%20Free%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Provisioned Throughput (above free tier)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Azure%20Files%20Provisioned%20v2%27%20and%20skuName%20eq%20%27SSD%20LRS%27%20and%20meterName%20eq%20%27SSD%20LRS%20Provisioned%20Throughput%20MiBPS%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Provisioned Throughput Free (baseline)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Azure%20Files%20Provisioned%20v2%27%20and%20skuName%20eq%20%27SSD%20LRS%27%20and%20meterName%20eq%20%27SSD%20LRS%20Provisioned%20Throughput%20MiBPS%20Free%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Overflow Snapshot - returns $/GiB/months
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Azure%20Files%20Provisioned%20v2%27%20and%20skuName%20eq%20%27SSD%20LRS%27%20and%20meterName%20eq%20%27SSD%20LRS%20Overflow%20Snapshot%20Usage%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Soft-Deleted Usage - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Azure%20Files%20Provisioned%20v2%27%20and%20skuName%20eq%20%27SSD%20LRS%27%20and%20meterName%20eq%20%27SSD%20LRS%20Soft-Deleted%20Usage%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

#### SSD ZRS Provisioned v2
**Azure Retail Prices API URLs (SSD ZRS Provisioned v2):**
```bash
# Provisioned Storage - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Azure%20Files%20Provisioned%20v2%27%20and%20skuName%20eq%20%27SSD%20ZRS%27%20and%20meterName%20eq%20%27SSD%20ZRS%20Provisioned%20Storage%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Provisioned IOPS - returns $/IOPS/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Azure%20Files%20Provisioned%20v2%27%20and%20skuName%20eq%20%27SSD%20ZRS%27%20and%20meterName%20eq%20%27SSD%20ZRS%20Provisioned%20IOPS%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Provisioned Throughput
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Azure%20Files%20Provisioned%20v2%27%20and%20skuName%20eq%20%27SSD%20ZRS%27%20and%20meterName%20eq%20%27SSD%20ZRS%20Provisioned%20Throughput%20MiBPS%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Overflow Snapshot - returns $/GiB/months
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Azure%20Files%20Provisioned%20v2%27%20and%20skuName%20eq%20%27SSD%20ZRS%27%20and%20meterName%20eq%20%27SSD%20ZRS%20Overflow%20Snapshot%20Usage%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

#### HDD LRS Provisioned v2
**Azure Retail Prices API URLs (HDD LRS Provisioned v2):**
```bash
# Provisioned Storage - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Azure%20Files%20Provisioned%20v2%27%20and%20skuName%20eq%20%27HDD%20LRS%27%20and%20meterName%20eq%20%27HDD%20LRS%20Provisioned%20Storage%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Provisioned IOPS - returns $/IOPS/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Azure%20Files%20Provisioned%20v2%27%20and%20skuName%20eq%20%27HDD%20LRS%27%20and%20meterName%20eq%20%27HDD%20LRS%20Provisioned%20IOPS%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Provisioned Throughput
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Azure%20Files%20Provisioned%20v2%27%20and%20skuName%20eq%20%27HDD%20LRS%27%20and%20meterName%20eq%20%27HDD%20LRS%20Provisioned%20Throughput%20MiBPS%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Overflow Snapshot - returns $/GiB/months
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Azure%20Files%20Provisioned%20v2%27%20and%20skuName%20eq%20%27HDD%20LRS%27%20and%20meterName%20eq%20%27HDD%20LRS%20Overflow%20Snapshot%20Usage%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

#### HDD ZRS Provisioned v2
**Azure Retail Prices API URLs (HDD ZRS Provisioned v2):**
```bash
# Provisioned Storage - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Azure%20Files%20Provisioned%20v2%27%20and%20skuName%20eq%20%27HDD%20ZRS%27%20and%20meterName%20eq%20%27HDD%20ZRS%20Provisioned%20Storage%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Provisioned IOPS - returns $/IOPS/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Azure%20Files%20Provisioned%20v2%27%20and%20skuName%20eq%20%27HDD%20ZRS%27%20and%20meterName%20eq%20%27HDD%20ZRS%20Provisioned%20IOPS%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Provisioned Throughput
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Azure%20Files%20Provisioned%20v2%27%20and%20skuName%20eq%20%27HDD%20ZRS%27%20and%20meterName%20eq%20%27HDD%20ZRS%20Provisioned%20Throughput%20MiBPS%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Overflow Snapshot - returns $/GiB/months
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Azure%20Files%20Provisioned%20v2%27%20and%20skuName%20eq%20%27HDD%20ZRS%27%20and%20meterName%20eq%20%27HDD%20ZRS%20Overflow%20Snapshot%20Usage%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

#### HDD GRS Provisioned v2
**Azure Retail Prices API URLs (HDD GRS Provisioned v2):**
```bash
# Provisioned Storage - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Azure%20Files%20Provisioned%20v2%27%20and%20skuName%20eq%20%27HDD%20GRS%27%20and%20meterName%20eq%20%27HDD%20GRS%20Provisioned%20Storage%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Provisioned IOPS - returns $/IOPS/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Azure%20Files%20Provisioned%20v2%27%20and%20skuName%20eq%20%27HDD%20GRS%27%20and%20meterName%20eq%20%27HDD%20GRS%20Provisioned%20IOPS%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Provisioned Throughput
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Azure%20Files%20Provisioned%20v2%27%20and%20skuName%20eq%20%27HDD%20GRS%27%20and%20meterName%20eq%20%27HDD%20GRS%20Provisioned%20Throughput%20MiBPS%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Overflow Snapshot - returns $/GiB/months
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Azure%20Files%20Provisioned%20v2%27%20and%20skuName%20eq%20%27HDD%20GRS%27%20and%20meterName%20eq%20%27HDD%20GRS%20Overflow%20Snapshot%20Usage%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

#### HDD GZRS Provisioned v2
**Azure Retail Prices API URLs (HDD GZRS Provisioned v2):**
```bash
# Provisioned Storage - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Azure%20Files%20Provisioned%20v2%27%20and%20skuName%20eq%20%27HDD%20GZRS%27%20and%20meterName%20eq%20%27HDD%20GZRS%20Provisioned%20Storage%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Provisioned IOPS - returns $/IOPS/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Azure%20Files%20Provisioned%20v2%27%20and%20skuName%20eq%20%27HDD%20GZRS%27%20and%20meterName%20eq%20%27HDD%20GZRS%20Provisioned%20IOPS%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Provisioned Throughput
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Azure%20Files%20Provisioned%20v2%27%20and%20skuName%20eq%20%27HDD%20GZRS%27%20and%20meterName%20eq%20%27HDD%20GZRS%20Provisioned%20Throughput%20MiBPS%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Overflow Snapshot - returns $/GiB/months
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Azure%20Files%20Provisioned%20v2%27%20and%20skuName%20eq%20%27HDD%20GZRS%27%20and%20meterName%20eq%20%27HDD%20GZRS%20Overflow%20Snapshot%20Usage%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

---

## Managed Disks - 15+ Permutations

### Standard HDD (11 tiers)
**Tiers:** S4 (32 GB), S6 (64 GB), S10 (128 GB), S15 (256 GB), S20 (512 GB), S30 (1 TiB), S40 (2 TiB), S50 (4 TiB), S60 (8 TiB), S70 (16 TiB), S80 (32 TiB)

**Cost Calculation (per tier):**
```
Total 720-Hour Cost = 
  Fixed Disk Price for Tier ($/month)
  + (Transaction Count / 10,000) × Transaction Price ($/10K)
  + Snapshot Capacity (GiB) × Snapshot Price ($/GiB/month)

Notes:
- Each tier has a fixed monthly price regardless of actual usage within that tier
- Transaction charges apply (unlike Premium)
- Snapshots are differential only
- Lowest cost, lowest performance
```

**Azure Retail Prices API URLs (Standard HDD):**

**Example tiers (S4, S10, S30, S80):**
```bash
# S4 LRS Disk (32 GB)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Standard%20HDD%20Managed%20Disks%27%20and%20skuName%20eq%20%27S4%20LRS%27%20and%20meterName%20eq%20%27S4%20LRS%20Disk%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# S10 LRS Disk (128 GB)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Standard%20HDD%20Managed%20Disks%27%20and%20skuName%20eq%20%27S10%20LRS%27%20and%20meterName%20eq%20%27S10%20LRS%20Disk%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# S30 LRS Disk (1 TiB)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Standard%20HDD%20Managed%20Disks%27%20and%20skuName%20eq%20%27S30%20LRS%27%20and%20meterName%20eq%20%27S30%20LRS%20Disk%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# S80 LRS Disk (32 TiB)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Standard%20HDD%20Managed%20Disks%27%20and%20skuName%20eq%20%27S80%20LRS%27%20and%20meterName%20eq%20%27S80%20LRS%20Disk%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Disk Transactions - returns $ per 10K operations (applies to all tiers)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Standard%20HDD%20Managed%20Disks%27%20and%20skuName%20eq%20%27Disk%20Transactions%20LRS%27%20and%20meterName%20eq%20%27Disk%20Transactions%20LRS%20Disk%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Snapshots LRS - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Standard%20HDD%20Managed%20Disks%27%20and%20skuName%20eq%20%27Snapshots%20LRS%27%20and%20meterName%20eq%20%27Snapshots%20LRS%20Snapshots%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Snapshots ZRS - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Standard%20HDD%20Managed%20Disks%27%20and%20skuName%20eq%20%27Snapshots%20ZRS%27%20and%20meterName%20eq%20%27Snapshots%20ZRS%20Snapshots%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

**Note:** Replace tier names (S4, S6, S10, S15, S20, S30, S40, S50, S60, S70, S80) in the SKU name to get pricing for other tiers.

### Standard SSD (14 tiers)
**Tiers:** E1 (4 GB), E2 (8 GB), E3 (16 GB), E4 (32 GB), E6 (64 GB), E10 (128 GB), E15 (256 GB), E20 (512 GB), E30 (1 TiB), E40 (2 TiB), E50 (4 TiB), E60 (8 TiB), E70 (16 TiB), E80 (32 TiB)

**Cost Calculation (per tier):**
```
Total 720-Hour Cost = 
  Fixed Disk Price for Tier ($/month)
  + (Transaction Count / 10,000) × Transaction Price ($/10K)
  + Snapshot Capacity (GiB) × Snapshot Price ($/GiB/month)

Notes:
- Each tier has a fixed monthly price
- Transaction charges apply (lower than Standard HDD)
- Snapshots are differential only
- Better performance than Standard HDD
```

**Azure Retail Prices API URLs (Standard SSD):**

**Example tiers (E4, E10, E30, E80) - LRS:**
```bash
# E4 LRS Disk (32 GB)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Standard%20SSD%20Managed%20Disks%27%20and%20skuName%20eq%20%27E4%20LRS%27%20and%20meterName%20eq%20%27E4%20LRS%20Disk%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# E10 LRS Disk (128 GB)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Standard%20SSD%20Managed%20Disks%27%20and%20skuName%20eq%20%27E10%20LRS%27%20and%20meterName%20eq%20%27E10%20LRS%20Disk%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# E30 LRS Disk (1 TiB)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Standard%20SSD%20Managed%20Disks%27%20and%20skuName%20eq%20%27E30%20LRS%27%20and%20meterName%20eq%20%27E30%20LRS%20Disk%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# E80 LRS Disk (32 TiB)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Standard%20SSD%20Managed%20Disks%27%20and%20skuName%20eq%20%27E80%20LRS%27%20and%20meterName%20eq%20%27E80%20LRS%20Disk%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

**Example tiers (E4, E30) - ZRS:**
```bash
# E4 ZRS Disk (32 GB)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Standard%20SSD%20Managed%20Disks%27%20and%20skuName%20eq%20%27E4%20ZRS%27%20and%20meterName%20eq%20%27E4%20ZRS%20Disk%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# E30 ZRS Disk (1 TiB)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Standard%20SSD%20Managed%20Disks%27%20and%20skuName%20eq%20%27E30%20ZRS%27%20and%20meterName%20eq%20%27E30%20ZRS%20Disk%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

**Transactions and Snapshots:**
```bash
# Disk Transactions - returns $ per 10K operations (applies to all tiers)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Standard%20SSD%20Managed%20Disks%27%20and%20skuName%20eq%20%27Disk%20Transactions%20LRS%27%20and%20meterName%20eq%20%27Disk%20Transactions%20LRS%20Disk%20Operations%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Snapshots LRS - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Standard%20SSD%20Managed%20Disks%27%20and%20skuName%20eq%20%27Snapshots%20LRS%27%20and%20meterName%20eq%20%27Snapshots%20LRS%20Snapshots%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Snapshots ZRS - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Standard%20SSD%20Managed%20Disks%27%20and%20skuName%20eq%20%27Snapshots%20ZRS%27%20and%20meterName%20eq%20%27Snapshots%20ZRS%20Snapshots%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

**Note:** Replace tier names (E1-E80) in the SKU name to get pricing for other tiers. Both LRS and ZRS are available for all tiers.

### Premium SSD (13 tiers)
**Tiers:** P1 (4 GB), P2 (8 GB), P3 (16 GB), P4 (32 GB), P6 (64 GB), P10 (128 GB), P15 (256 GB), P20 (512 GB), P30 (1 TiB), P40 (2 TiB), P50 (4 TiB), P60 (8 TiB), P70 (16 TiB), P80 (32 TiB)

**Cost Calculation (per tier):**
```
Total 720-Hour Cost = 
  Fixed Disk Price for Tier ($/month)
  + Snapshot Capacity (GiB) × Snapshot Price ($/GiB/month)

Notes:
- Each tier has a fixed monthly price
- No transaction charges
- Snapshots are differential only
- Best performance for Standard/Premium
- Higher base IOPS and throughput per tier
```

**Azure Retail Prices API URLs (Premium SSD):**

**Example tiers (P4, P10, P30, P80) - LRS:**
```bash
# P4 LRS Disk (32 GB)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Premium%20SSD%20Managed%20Disks%27%20and%20skuName%20eq%20%27P4%20LRS%27%20and%20meterName%20eq%20%27P4%20LRS%20Disk%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# P10 LRS Disk (128 GB)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Premium%20SSD%20Managed%20Disks%27%20and%20skuName%20eq%20%27P10%20LRS%27%20and%20meterName%20eq%20%27P10%20LRS%20Disk%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# P30 LRS Disk (1 TiB)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Premium%20SSD%20Managed%20Disks%27%20and%20skuName%20eq%20%27P30%20LRS%27%20and%20meterName%20eq%20%27P30%20LRS%20Disk%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# P80 LRS Disk (32 TiB)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Premium%20SSD%20Managed%20Disks%27%20and%20skuName%20eq%20%27P80%20LRS%27%20and%20meterName%20eq%20%27P80%20LRS%20Disk%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

**Example tiers (P4, P30) - ZRS:**
```bash
# P4 ZRS Disk (32 GB)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Premium%20SSD%20Managed%20Disks%27%20and%20skuName%20eq%20%27P4%20ZRS%27%20and%20meterName%20eq%20%27P4%20ZRS%20Disk%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# P30 ZRS Disk (1 TiB)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Premium%20SSD%20Managed%20Disks%27%20and%20skuName%20eq%20%27P30%20ZRS%27%20and%20meterName%20eq%20%27P30%20ZRS%20Disk%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

**Snapshots:**
```bash
# Snapshots LRS - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Premium%20SSD%20Managed%20Disks%27%20and%20skuName%20eq%20%27Snapshots%20LRS%27%20and%20meterName%20eq%20%27Snapshots%20LRS%20Snapshots%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Snapshots ZRS - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Premium%20SSD%20Managed%20Disks%27%20and%20skuName%20eq%20%27Snapshots%20ZRS%27%20and%20meterName%20eq%20%27Snapshots%20ZRS%20Snapshots%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

**Note:** Replace tier names (P1-P80) in the SKU name to get pricing for other tiers. Both LRS and ZRS are available for all tiers.

### Premium SSD v2
**Cost Calculation:**
```
Total 720-Hour Cost = 
  Disk Capacity (GiB) × Capacity Price ($/GiB/month)
  + Provisioned IOPS (above baseline) × IOPS Price ($/IOPS/month)
  + Provisioned Throughput (above baseline) × Throughput Price ($/MiB/s/month)
  + Snapshot Capacity (GiB) × Snapshot Price ($/GiB/month)

Notes:
- Pay only for what you configure (capacity, IOPS, throughput)
- No fixed tier pricing
- Transactions are included
- Baseline IOPS and throughput included with capacity
- Additional IOPS/throughput can be added independently
- More flexible and potentially cheaper than Premium SSD for right-sized workloads
```

**Azure Retail Prices API URLs (Premium SSD v2):**

**Provisioned Resources:**
```bash
# Premium LRS Provisioned Capacity (per GiB/month)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Azure%20Premium%20SSD%20v2%27%20and%20skuName%20eq%20%27Premium%20LRS%27%20and%20meterName%20eq%20%27Premium%20LRS%20Provisioned%20Capacity%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Premium LRS Provisioned IOPS (per IOPS/month)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Azure%20Premium%20SSD%20v2%27%20and%20skuName%20eq%20%27Premium%20LRS%27%20and%20meterName%20eq%20%27Premium%20LRS%20Provisioned%20IOPS%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Premium LRS Provisioned Throughput - returns $/MBps/month (per MB/s/month)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Azure%20Premium%20SSD%20v2%27%20and%20skuName%20eq%20%27Premium%20LRS%27%20and%20meterName%20eq%20%27Premium%20LRS%20Provisioned%20Throughput%20(MBps)%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

**Confidential Compute Encryption:**
```bash
# Confidential Compute Encryption LRS Provisioned Capacity - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Azure%20Premium%20SSD%20v2%27%20and%20skuName%20eq%20%27Confidential%20Compute%20Encryption%20LRS%27%20and%20meterName%20eq%20%27Confidential%20Compute%20Encryption%20LRS%20Provisioned%20Capacity%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

**Instant Access Snapshots:**
```bash
# Instant Access Snapshots - returns $/GiB/month LRS (storage)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Azure%20Premium%20SSD%20v2%27%20and%20skuName%20eq%20%27Instant%20Access%20Snapshots%20LRS%27%20and%20meterName%20eq%20%27Instant%20Access%20Snapshots%20LRS%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Instant Access Snapshots - returns $/GiB/month LRS Disk Capacity Restored
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Azure%20Premium%20SSD%20v2%27%20and%20skuName%20eq%20%27Instant%20Access%20Snapshots%20LRS%27%20and%20meterName%20eq%20%27Instant%20Access%20Snapshots%20LRS%20Disk%20Capacity%20Restored%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

### Ultra Disk
**Cost Calculation:**
```
Total 720-Hour Cost = 
  Disk Capacity (GiB) × Capacity Price ($/GiB/month)
  + Provisioned IOPS × IOPS Price ($/IOPS/month)
  + Provisioned Throughput (MiB/s) × Throughput Price ($/MiB/s/month)
  + Snapshot Capacity (GiB) × Snapshot Price ($/GiB/month)

Notes:
- Pay for capacity, IOPS, and throughput independently
- No baseline - everything is provisioned and charged
- Transactions are included
- Highest performance option
- Most expensive option
- No tiers - fully configurable
```

**Azure Retail Prices API URLs (Ultra Disk):**

**Provisioned Resources:**
```bash
# Ultra LRS Provisioned Capacity (per GiB/month)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Ultra%20Disks%27%20and%20skuName%20eq%20%27Ultra%20LRS%27%20and%20meterName%20eq%20%27Ultra%20LRS%20Provisioned%20Capacity%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Ultra LRS Provisioned IOPS (per IOPS/month)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Ultra%20Disks%27%20and%20skuName%20eq%20%27Ultra%20LRS%27%20and%20meterName%20eq%20%27Ultra%20LRS%20Provisioned%20IOPS%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Ultra LRS Provisioned Throughput - returns $/MBps/month (per MB/s/month)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Ultra%20Disks%27%20and%20skuName%20eq%20%27Ultra%20LRS%27%20and%20meterName%20eq%20%27Ultra%20LRS%20Provisioned%20Throughput%20(MBps)%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Ultra LRS Reservation - returns $/vCPU/month per vCPU Provisioned
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Ultra%20Disks%27%20and%20skuName%20eq%20%27Ultra%20LRS%27%20and%20meterName%20eq%20%27Ultra%20LRS%20Reservation%20per%20vCPU%20Provisioned%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

**Instant Access Snapshots:**
```bash
# Instant Access Snapshots - returns $/GiB/month LRS (storage)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Ultra%20Disks%27%20and%20skuName%20eq%20%27Instant%20Access%20Snapshots%20LRS%27%20and%20meterName%20eq%20%27Instant%20Access%20Snapshots%20LRS%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Instant Access Snapshots - returns $/GiB/month LRS Disk Capacity Restored
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Storage%27%20and%20productName%20eq%20%27Ultra%20Disks%27%20and%20skuName%20eq%20%27Instant%20Access%20Snapshots%20LRS%27%20and%20meterName%20eq%20%27Instant%20Access%20Snapshots%20LRS%20Disk%20Capacity%20Restored%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

---

## Azure Files with Azure Backup

Each Azure Files tier can have backup enabled, adding additional cost:

**Backup Cost Calculation (applies to all Azure Files tiers):**
```
Additional Backup Cost = 
  Protected Instance Cost ($/month per instance)
  + Backup Storage (GiB) × Backup Storage Price ($/GiB/month)

Notes:
- Protected instance charge is per file share
- Backup storage is charged separately from live storage
- Backup storage includes all recovery points
- Retention policy affects backup storage size
```

**Azure Retail Prices API URLs (Azure Files Backup):**

**Protected Instances:**
```bash
# Azure Files Protected Instances (per instance/month)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceName%20eq%20%27Backup%27%20and%20productName%20eq%20%27Backup%27%20and%20skuName%20eq%20%27Azure%20Files%27%20and%20meterName%20eq%20%27Azure%20Files%20Protected%20Instances%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Azure Files Vaulted Protected Instances (per instance/month)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceName%20eq%20%27Backup%27%20and%20productName%20eq%20%27Backup%27%20and%20skuName%20eq%20%27Azure%20Files%20Vaulted%27%20and%20meterName%20eq%20%27Azure%20Files%20Vaulted%20Protected%20Instances%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

**Backup Storage (Vaulted):**
```bash
# LRS Data Stored - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceName%20eq%20%27Backup%27%20and%20productName%20eq%20%27Backup%27%20and%20skuName%20eq%20%27Azure%20Files%20Vaulted%27%20and%20meterName%20eq%20%27LRS%20Data%20Stored%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# ZRS Data Stored - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceName%20eq%20%27Backup%27%20and%20productName%20eq%20%27Backup%27%20and%20skuName%20eq%20%27Azure%20Files%20Vaulted%27%20and%20meterName%20eq%20%27ZRS%20Data%20Stored%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# GRS Data Stored - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceName%20eq%20%27Backup%27%20and%20productName%20eq%20%27Backup%27%20and%20skuName%20eq%20%27Azure%20Files%20Vaulted%27%20and%20meterName%20eq%20%27GRS%20Data%20Stored%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# RA-GRS Data Stored - returns $/GiB/month
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceName%20eq%20%27Backup%27%20and%20productName%20eq%20%27Backup%27%20and%20skuName%20eq%20%27Azure%20Files%20Vaulted%27%20and%20meterName%20eq%20%27RA-GRS%20Data%20Stored%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

---

## Managed Disks with Azure Backup

Each Managed Disk tier can have backup enabled:

**Backup Cost Calculation (applies to all Managed Disk types):**
```
Additional Backup Cost = 
  Protected Instance Cost ($/month per disk)
  + Backup Storage (GiB) × Backup Storage Price ($/GiB/month)

Notes:
- Protected instance charge is per disk
- Backup storage is charged separately from disk storage
- Backup storage pricing may differ from disk storage pricing
- Retention policy affects backup storage size
```

**Azure Retail Prices API URLs (Managed Disks Backup):**

**Protected Instances:**
```bash
# Azure VM Protected Instances (per VM/disk/month)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceName%20eq%20%27Backup%27%20and%20productName%20eq%20%27Backup%27%20and%20skuName%20eq%20%27Azure%20VM%27%20and%20meterName%20eq%20%27Azure%20VM%20Protected%20Instances%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

**Backup Storage:**
```bash
# LRS Data Stored (backup vault storage)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceName%20eq%20%27Backup%27%20and%20productName%20eq%20%27Backup%27%20and%20meterName%20eq%20%27LRS%20Data%20Stored%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# GRS Data Stored (backup vault storage)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceName%20eq%20%27Backup%27%20and%20productName%20eq%20%27Backup%27%20and%20meterName%20eq%20%27GRS%20Data%20Stored%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# RA-GRS Data Stored (backup vault storage)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceName%20eq%20%27Backup%27%20and%20productName%20eq%20%27Backup%27%20and%20meterName%20eq%20%27RA-GRS%20Data%20Stored%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

---

## ANF with Backup

Azure NetApp Files volumes can have backup enabled:

**Backup Cost Calculation (applies to all ANF service levels):**
```
Additional Backup Cost = 
  Backup Capacity (GiB) × Backup Storage Price ($/GiB/month)

Notes:
- No protected instance charge for ANF backups
- Backup storage is charged separately from volume storage
- Backup storage includes all recovery points
- Retention policy affects backup storage size
- Backups are stored in Azure Blob storage
```

**Azure Retail Prices API URLs (ANF Backup):**

```bash
# ANF Backup Capacity (per GiB/month)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Azure%20NetApp%20Files%27%20and%20productName%20eq%20%27Azure%20NetApp%20Files%27%20and%20skuName%20eq%20%27Backup%27%20and%20meterName%20eq%20%27Backup%20Capacity%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

---

## ANF with Replication

ANF volumes can have Cross-Region Replication (CRR) or Cross-Zone Replication (CZR):

### Cross-Region Replication (CRR)
**Additional Cost for Source Volume:**
```
Additional CRR Cost = 
  Replicated Data Transfer (GiB) × CRR Transfer Price ($/GiB)

Notes:
- Transfer price varies by replication frequency (Hours, Days, or Minutes)
- Pricing varies by source and destination region pair
- Destination volume is charged at full capacity rate (can use lower tier)
- Data transfer is from source to destination region
- Hours = most frequent, Days = less frequent, Minutes = least frequent
```

**Destination Volume Cost:**
```
Destination Volume Cost = 
  Same as source volume tier calculation
  (but can use a lower service level to reduce standby costs)

Notes:
- Full volume capacity cost applies even if destination is standby
- Can use Standard tier for destination even if source is Premium/Ultra
- Flexible service level with minimum 128 MiB included throughput is another low-cost option for standby destinations
- Destination volume also supports Cool Access, Double Encryption, etc.
```

**Azure Retail Prices API URLs (CRR):**

**Generic Cross Region Replication (by frequency):**
```bash
# Cross Region Replication Hours (most frequent, highest cost)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Azure%20NetApp%20Files%27%20and%20productName%20eq%20%27Azure%20NetApp%20Files%27%20and%20skuName%20eq%20%27Cross%20Region%20Replication%27%20and%20meterName%20eq%20%27Cross%20Region%20Replication%20Hours%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Cross Region Replication Days (daily, medium cost)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Azure%20NetApp%20Files%27%20and%20productName%20eq%20%27Azure%20NetApp%20Files%27%20and%20skuName%20eq%20%27Cross%20Region%20Replication%27%20and%20meterName%20eq%20%27Cross%20Region%20Replication%20Days%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'

# Cross Region Replication Minutes (least frequent, lowest cost - if available)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Azure%20NetApp%20Files%27%20and%20productName%20eq%20%27Azure%20NetApp%20Files%27%20and%20skuName%20eq%20%27Cross%20Region%20Replication%27%20and%20meterName%20eq%20%27Cross%20Region%20Replication%20Minutes%27%20and%20armRegionName%20eq%20%27{{region}}%27' | jq -r '.Items[0].retailPrice'
```

**Region-specific CRR pricing (examples):**
```bash
# US Central to US East - Days
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Azure%20NetApp%20Files%27%20and%20productName%20eq%20%27Azure%20NetApp%20Files%27%20and%20skuName%20eq%20%27Cross%20Region%20Replication%20-%20US%20Central%20to%20US%20East%27%20and%20meterName%20eq%20%27Cross%20Region%20Replication%20-%20US%20Central%20to%20US%20East%20Days%27' | jq -r '.Items[0].retailPrice'

# US East 2 to US West 2 - Hours
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Azure%20NetApp%20Files%27%20and%20productName%20eq%20%27Azure%20NetApp%20Files%27%20and%20skuName%20eq%20%27Cross%20Region%20Replication%20-%20US%20East%202%20to%20US%20West%202%27%20and%20meterName%20eq%20%27Cross%20Region%20Replication%20-%20US%20East%202%20to%20US%20West%202%20Hours%27' | jq -r '.Items[0].retailPrice'

# EU West to EU North - Days
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Azure%20NetApp%20Files%27%20and%20productName%20eq%20%27Azure%20NetApp%20Files%27%20and%20skuName%20eq%20%27Cross%20Region%20Replication%20-%20EU%20West%20to%20EU%20North%27%20and%20meterName%20eq%20%27Cross%20Region%20Replication%20-%20EU%20West%20to%20EU%20North%20Hours%27' | jq -r '.Items[0].retailPrice'

# CRR shorthand format (IL Central to SE Central - Minutes)
curl -s 'https://prices.azure.com/api/retail/prices?$filter=serviceFamily%20eq%20%27Storage%27%20and%20serviceName%20eq%20%27Azure%20NetApp%20Files%27%20and%20productName%20eq%20%27Azure%20NetApp%20Files%27%20and%20skuName%20eq%20%27CRR%20-%20IL%20Central%20to%20SE%20Central%27%20and%20meterName%20eq%20%27CRR%20-%20IL%20Central%20to%20SE%20Central%20Minutes%27' | jq -r '.Items[0].retailPrice'
```

**Note:** CRR pricing is region-pair specific. To get pricing for your specific source→destination pair:
1. Use generic "Cross Region Replication" SKU for baseline pricing in source region
2. Or query for specific region pair SKU: "Cross Region Replication - [Source] to [Destination]"
3. Or use shorthand "CRR - [Source] to [Destination]"
4. Meter names include frequency: "Hours", "Days", or "Minutes"
5. Unit of measure is "1 GiB/Hour" - multiply by data replicated

### Cross-Zone Replication (CZR)
**Additional Cost for Source Volume:**
```
Additional CZR Cost = 
  $0 (no data transfer charges for CZR)

Notes:
- No transfer charges for zone-to-zone replication within same region
- Destination volume is charged at full capacity rate
- CZR provides availability zone redundancy
```

**Destination Volume Cost:**
```
Destination Volume Cost = 
  Same as source volume tier calculation
  (but can use a lower service level to reduce standby costs)
```

**Azure Retail Prices API URLs (CZR):**

```bash
# CZR has no data transfer charges - only destination volume capacity is charged
# Use standard ANF capacity pricing for the destination volume (see ANF sections above)
```

---

## Summary of Total Permutations

| Service | Base Tiers | Cool Access | Double Encryption | Redundancy | Backup | Replication | Total Permutations |
|---------|------------|-------------|-------------------|------------|--------|-------------|-------------------|
| ANF | 4 (Std, Prem, Ultra, Flex) | +4 (w/ Cool) | +3 (w/ Dbl Enc - not Flex) | N/A | Manual | +CRR/CZR | 11 base + replication variants |
| Azure Files Pay-as-you-go | 3 (Hot, Cool, TransOpt) | Tier native | N/A | 4 (LRS, ZRS, GRS, GZRS) | Optional | N/A | 12 base + backup variants |
| Azure Files Premium | 1 (Premium) | N/A | N/A | 4 (LRS, ZRS, GRS, GZRS) | Optional | N/A | 4 base + backup variants |
| Managed Disks Std HDD | 11 tiers | N/A | N/A | N/A | Optional | N/A | 11 + backup variants |
| Managed Disks Std SSD | 14 tiers | N/A | N/A | N/A | Optional | N/A | 14 + backup variants |
| Managed Disks Premium SSD | 14 tiers | N/A | N/A | N/A | Optional | N/A | 14 + backup variants |
| Managed Disks Premium v2 | Flexible | N/A | N/A | N/A | Optional | N/A | 1 + backup variants |
| Managed Disks Ultra | Flexible | N/A | N/A | N/A | Optional | N/A | 1 + backup variants |

**Total Base Permutations:** 82
**With Optional Features (Backup, Replication):** 159+

---

## Implementation Notes

### Pricing Data Sources
All pricing must be fetched from the Azure Retail Prices API using appropriate filters:
- `serviceFamily`, `serviceName`, `productName`, `skuName`, `armRegionName`
- Cache pricing for 24 hours to reduce API calls
- Handle missing pricing gracefully with warnings

### Confidence Scoring
- High (80-100%): All metrics available, pricing current
- Medium (50-79%): Some estimates or missing data
- Low (<50%): Significant data gaps or assumptions

### Key Variables Needed for Calculations
- **Capacity:** Used (for pay-as-you-go) or provisioned (for provisioned tiers)
- **Transaction Counts:** Write, read, list, other operations
- **Cool Data Distribution:** Hot GiB vs. cool GiB (for ANF cool access)
- **Data Movement:** Tiering and retrieval volumes (for cool access)
- **Snapshot Size:** Differential snapshot capacity
- **Egress Data:** Data transfer out of Azure
- **IOPS/Throughput:** For flexible disk types (Premium v2, Ultra)

### Incompatibilities
- ANF: Cool Access and Double Encryption are mutually exclusive
- Azure Files: Cool tier has data retrieval charges that Hot/TransOpt do not
- Managed Disks: Transaction charges only apply to Standard HDD and Standard SSD

---

## Revision History

| Date | Change | Author |
|------|--------|--------|
| 2026-01-28 | Initial creation | Warp AI |

---

## References
- [PRICING_API_URLS.md](./PRICING_API_URLS.md) - Complete Azure Retail Prices API URLs for validation
- [AZURE_PRICING_MODELS.md](./AZURE_PRICING_MODELS.md)
- [PRICING_SYSTEM.md](./PRICING_SYSTEM.md)
- [COST_ESTIMATION.md](./COST_ESTIMATION.md)
