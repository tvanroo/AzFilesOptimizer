#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Quick start script for Cost Estimation System
    
.DESCRIPTION
    Builds and runs the AzFilesOptimizer backend with cost estimation features.
    Opens the test page in your default browser.
    
.EXAMPLE
    .\start-cost-estimation.ps1
    
.EXAMPLE
    .\start-cost-estimation.ps1 -SkipBuild
#>

param(
    [switch]$SkipBuild,
    [switch]$SkipFrontend,
    [int]$FrontendPort = 8080
)

$ErrorActionPreference = "Stop"

Write-Host "🚀 AzFilesOptimizer - Cost Estimation System" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host ""

# Get script directory
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$backendDir = Join-Path $scriptDir "src\backend"
$frontendDir = Join-Path $scriptDir "src\frontend"

# Step 1: Build Backend
if (-not $SkipBuild) {
    Write-Host "📦 Building backend..." -ForegroundColor Yellow
    Push-Location $backendDir
    try {
        dotnet build --configuration Release
        if ($LASTEXITCODE -ne 0) {
            throw "Build failed"
        }
        Write-Host "✅ Build successful!" -ForegroundColor Green
    }
    finally {
        Pop-Location
    }
}
else {
    Write-Host "⏭️  Skipping build (using existing build)" -ForegroundColor Gray
}

Write-Host ""

# Step 2: Start Frontend (if requested)
$frontendJob = $null
if (-not $SkipFrontend) {
    Write-Host "🌐 Starting frontend server on port $FrontendPort..." -ForegroundColor Yellow
    
    # Check if Python is available
    if (Get-Command python -ErrorAction SilentlyContinue) {
        $frontendJob = Start-Job -ScriptBlock {
            param($dir, $port)
            Set-Location $dir
            python -m http.server $port
        } -ArgumentList $frontendDir, $FrontendPort
        
        Write-Host "✅ Frontend server starting (Job ID: $($frontendJob.Id))" -ForegroundColor Green
        Write-Host "   Test page: http://localhost:$FrontendPort/cost-estimation-test.html" -ForegroundColor Cyan
    }
    else {
        Write-Host "⚠️  Python not found. Please start frontend manually:" -ForegroundColor Yellow
        Write-Host "   cd $frontendDir" -ForegroundColor Gray
        Write-Host "   python -m http.server $FrontendPort" -ForegroundColor Gray
    }
}

Write-Host ""

# Step 3: Start Backend
Write-Host "⚡ Starting Azure Functions backend..." -ForegroundColor Yellow
Write-Host "   API will be available at: http://localhost:7071" -ForegroundColor Cyan
Write-Host ""
Write-Host "📋 Available endpoints:" -ForegroundColor White
Write-Host "   GET  /api/pricing/test?region=eastus&service=files" -ForegroundColor Gray
Write-Host "   GET  /api/discovery/{jobId}/cost-estimates" -ForegroundColor Gray
Write-Host "   POST /api/cost-estimates/calculate" -ForegroundColor Gray
Write-Host ""
Write-Host "Press Ctrl+C to stop the backend..." -ForegroundColor Yellow
Write-Host ""

# Wait a moment for frontend to start
Start-Sleep -Seconds 2

# Open browser to test page
if (-not $SkipFrontend -and $frontendJob) {
    Write-Host "🌍 Opening test page in browser..." -ForegroundColor Cyan
    Start-Sleep -Seconds 1
    Start-Process "http://localhost:$FrontendPort/cost-estimation-test.html"
}

# Start backend
Push-Location $backendDir
try {
    # Run Azure Functions
    func start
}
finally {
    Pop-Location
    
    # Cleanup frontend job
    if ($frontendJob) {
        Write-Host ""
        Write-Host "🛑 Stopping frontend server..." -ForegroundColor Yellow
        Stop-Job -Job $frontendJob
        Remove-Job -Job $frontendJob
        Write-Host "✅ Frontend server stopped" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "✅ Shutdown complete" -ForegroundColor Green
