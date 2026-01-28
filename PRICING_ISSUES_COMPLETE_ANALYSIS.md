# Complete Pricing Flow Analysis - ALL ISSUES

## Flow Trace
1. `CostAnalysisFunction` calls `CostCollectionService.GetAnfVolumeCostAsync()` (line 630)
2. `GetAnfVolumeCostAsync` calls `RetailPricingService.GetAnfPricingAsync()` (line 630)
3. `GetAnfPricingAsync` checks cache for "anf-flexible-capacity" key (line 197-198)
4. If null or $0, calls `RefreshAnfPricingAsync()` (line 205)
5. `RefreshAnfPricingAsync` calls `BuildAnfQuery()` then `QueryRetailApiAsync()` (lines 446-449)
6. Query returns 2 meters from API (confirmed via curl)
7. `CacheMetersAsync` is called with a lambda to create cache keys (line 458)
8. **PROBLEM**: Lambda at lines 458-475 creates keys, but if meterType is EMPTY, creates invalid key

## CRITICAL ISSUES FOUND:

### Issue #1: Empty meterType creates invalid cache keys
**File**: `RetailPricingService.cs` lines 458-475
**Problem**: When a meter doesn't match "capacity" or "throughput", meterType stays empty
**Result**: Cache key becomes `"anf-flexible-"` instead of `"anf-flexible-capacity"`
**Impact**: Meters are cached with wrong keys and can never be retrieved

### Issue #2: Cool tier meters in wrong query
**File**: `RetailPricingService.cs` lines 469-472
**Problem**: Checking for "cool" in the Flexible SKU query, but cool meters have different SKU
**Result**: Cool meters from "Flexible Service Level" SKU (which don't exist) create empty keys
**Impact**: Adds noise to cache with unusable entries

### Issue #3: Missing validation that meters were actually cached
**File**: `RetailPricingService.cs` line 749-751  
**Problem**: If meterKey is empty/whitespace, silently continues without caching
**Result**: No error is logged, meters are silently dropped
**Impact**: Looks like caching succeeded but actually failed

### Issue #4: No logging of final cached keys
**File**: `RetailPricingService.cs` line 774
**Problem**: We cache to Table Storage but don't log what key was used
**Result**: Can't debug what keys are actually in cache
**Impact**: Impossible to trace why lookups fail

### Issue #5: Memory cache key differs from lookup key
**File**: `RetailPricingService.cs` line 777 vs 796
**Problem**: Memory cache uses `"{region}:{meterKey}"` but we normalized region differently
**Result**: Potential mismatch if region casing differs
**Impact**: Cache misses even when data exists

## ROOT CAUSE:
The meter name matching logic at lines 465-472 is TOO STRICT. It only matches if meter name contains EXACT strings "capacity" or "throughput". But what if API returns meters with slightly different names?

Let me check what the API actually returns...

According to our curl test:
- "Flexible Service Level Capacity" - matches "capacity" ✓
- "Flexible Service Level Throughput MiBps" - matches "throughput" ✓

So the matching SHOULD work, but the empty meterType case needs to be handled!

## THE REAL PROBLEM:

When `meterType` is empty, line 474 returns a cache key with empty meterType:
```csharp
return RetailPriceCache.CreateAnfMeterKey(serviceLevelStr, meterType);
// becomes: "anf-flexible-" when meterType is ""
```

Then line 750 checks `if (string.IsNullOrWhiteSpace(meterKey))` but `"anf-flexible-"` is NOT whitespace!
So it tries to cache with this broken key.

Later, when we look up "anf-flexible-capacity", it's not found because it was cached as "anf-flexible-".

## SOLUTION:
1. Add logging to show what cache keys are being created
2. Skip caching if meterType is empty (add check BEFORE creating key)
3. Log warning when meter doesn't match any pattern
4. After refresh, log what keys are now in cache
