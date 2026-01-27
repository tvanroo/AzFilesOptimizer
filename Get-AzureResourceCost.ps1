# Get-AzureResourceCost.ps1
# Query actual costs for a specific Azure resource using Cost Management API

param(
    [Parameter(Mandatory=$true)]
    [string]$ResourceId,
    
    [Parameter(Mandatory=$false)]
    [ValidateSet("TheLastMonth", "MonthToDate", "TheLastBillingMonth", "BillingMonthToDate", "TheLastYear", "Custom")]
    [string]$Timeframe = "MonthToDate",
    
    [Parameter(Mandatory=$false)]
    [datetime]$CustomStartDate,
    
    [Parameter(Mandatory=$false)]
    [datetime]$CustomEndDate,
    
    [Parameter(Mandatory=$false)]
    [ValidateSet("Daily", "Monthly", "None")]
    [string]$Granularity = "Daily"
)

# Extract subscription ID from the resource ID
if ($ResourceId -match "/subscriptions/([^/]+)") {
    $subscriptionId = $Matches[1]
    Write-Host "Subscription ID: $subscriptionId" -ForegroundColor Cyan
} else {
    Write-Error "Could not extract subscription ID from ResourceId"
    exit 1
}

# Set the scope for the Cost Management query
$scope = "subscriptions/$subscriptionId"

# Build the query body
$queryBody = @{
    type = "ActualCost"
    timeframe = $Timeframe
    dataset = @{
        granularity = $Granularity
        aggregation = @{
            totalCost = @{
                name = "Cost"
                function = "Sum"
            }
            totalCostUSD = @{
                name = "CostUSD"
                function = "Sum"
            }
        }
        grouping = @(
            @{
                type = "Dimension"
                name = "ResourceId"
            },
            @{
                type = "Dimension"
                name = "MeterSubcategory"
            },
            @{
                type = "Dimension"
                name = "Meter"
            }
        )
        filter = @{
            dimensions = @{
                name = "ResourceId"
                operator = "In"
                values = @($ResourceId)
            }
        }
    }
}

# If custom timeframe, add timePeriod
if ($Timeframe -eq "Custom") {
    if (-not $CustomStartDate -or -not $CustomEndDate) {
        Write-Error "CustomStartDate and CustomEndDate are required when using Custom timeframe"
        exit 1
    }
    $queryBody["timePeriod"] = @{
        from = $CustomStartDate.ToString("yyyy-MM-ddT00:00:00Z")
        to = $CustomEndDate.ToString("yyyy-MM-ddT23:59:59Z")
    }
}

# Convert to JSON
$jsonBody = $queryBody | ConvertTo-Json -Depth 10

Write-Host "`nQuery Body:" -ForegroundColor Yellow
Write-Host $jsonBody

# Make the API call using ARM REST (token from az CLI)
Write-Host "`nQuerying Cost Management API..." -ForegroundColor Yellow

try {
    $apiVersion = "2023-11-01"
    $uri = "https://management.azure.com/$scope/providers/Microsoft.CostManagement/query?api-version=$apiVersion"

    # Get an access token via az CLI (reuses your current az login/session)
    $accessToken = az account get-access-token --resource https://management.azure.com/ --query accessToken -o tsv
    if (-not $accessToken) {
        throw "Failed to acquire access token from az CLI."
    }

    $headers = @{
        Authorization = "Bearer $accessToken"
        "Content-Type" = "application/json"
    }

    $response = Invoke-RestMethod -Method Post -Uri $uri -Headers $headers -Body $jsonBody

    Write-Host "`n=== COST DATA RESULTS ===" -ForegroundColor Green
    Write-Host "Resource: $ResourceId" -ForegroundColor Cyan
    Write-Host "Timeframe: $Timeframe" -ForegroundColor Cyan
    Write-Host "Granularity: $Granularity`n" -ForegroundColor Cyan
    
    if ($response.properties.rows.Count -eq 0) {
        Write-Host "No cost data found for this resource in the specified timeframe." -ForegroundColor Yellow
        Write-Host "This could mean:"
        Write-Host "  - The resource has no costs in this period"
        Write-Host "  - Cost data is not yet available (can take 24-48 hours)"
        Write-Host "  - The ResourceId might be incorrect"
    } else {
        # Parse columns to understand the data structure
        $columns = $response.properties.columns
        $columnNames = $columns | ForEach-Object { $_.name }
        
        Write-Host "Columns: $($columnNames -join ', ')" -ForegroundColor Gray
        Write-Host "`n"
        
        # Display each row
        $totalCost = 0
        foreach ($row in $response.properties.rows) {
            $rowData = @{}
            for ($i = 0; $i -lt $columns.Count; $i++) {
                $rowData[$columns[$i].name] = $row[$i]
            }
            
            # Display the row data
            Write-Host "--- Cost Entry ---" -ForegroundColor Magenta
            foreach ($key in $rowData.Keys) {
                Write-Host "  ${key}: $($rowData[$key])"
            }
            Write-Host ""
            
            # Accumulate total cost
            if ($rowData.ContainsKey("Cost")) {
                $totalCost += [double]$rowData["Cost"]
            }
        }
        
        Write-Host "=== TOTAL COST: $totalCost ===" -ForegroundColor Green
        
        # Return the full response for further processing
        Write-Host "`nFull Response Object (for inspection):" -ForegroundColor Yellow
        return $response
    }
    
} catch {
    Write-Error "Failed to query Cost Management API: $_"
    Write-Host "`nTroubleshooting tips:" -ForegroundColor Yellow
    Write-Host "  1. Ensure you're logged in: az login"
    Write-Host "  2. Ensure you have the correct subscription: az account set --subscription $subscriptionId"
    Write-Host "  3. Ensure you have Cost Management Reader permissions"
    Write-Host "  4. Check that the ResourceId is correct"
    exit 1
}
