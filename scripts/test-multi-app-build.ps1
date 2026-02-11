<#
.SYNOPSIS
    Tests the multi-application Docker build configuration.

.DESCRIPTION
    This script validates that the Docker build works correctly for each application
    by building container images with different APPLICATION build arguments.

.PARAMETER Application
    The application to build (Transfers, Lsrp). If not specified, tests all applications.

.PARAMETER SkipBuild
    If specified, only validates configuration files without building Docker images.

.EXAMPLE
    .\test-multi-app-build.ps1
    Tests all applications.

.EXAMPLE
    .\test-multi-app-build.ps1 -Application Transfers
    Tests only the Transfers application.

.EXAMPLE
    .\test-multi-app-build.ps1 -SkipBuild
    Validates configuration files without building Docker images.
#>

param(
    [Parameter(Mandatory = $false)]
    [ValidateSet("Transfers", "Lsrp")]
    [string]$Application,

    [Parameter(Mandatory = $false)]
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

# Get the repository root directory
$scriptDir = $PSScriptRoot
if (-not $scriptDir) {
    $scriptDir = Get-Location
}
$repoRoot = Split-Path -Parent $scriptDir

Write-Host "Repository root: $repoRoot" -ForegroundColor Cyan

# Define applications to test
$applications = if ($Application) { @($Application) } else { @("Transfers", "Lsrp") }

# Validate configuration folders exist
Write-Host "`n=== Validating Configuration Folders ===" -ForegroundColor Yellow

$allValid = $true
foreach ($app in $applications) {
    $configPath = Join-Path $repoRoot "configurations" $app
    Write-Host "`nChecking $app configuration..." -ForegroundColor Cyan
    
    if (-not (Test-Path $configPath)) {
        Write-Host "  ERROR: Configuration folder not found: $configPath" -ForegroundColor Red
        $allValid = $false
        continue
    }
    
    Write-Host "  Configuration folder exists: $configPath" -ForegroundColor Green
    
    # Check for required files
    $requiredFiles = @(
        "appsettings.json",
        "appsettings.Development.json",
        "appsettings.Test.json",
        "appsettings.Production.json"
    )
    
    foreach ($file in $requiredFiles) {
        $filePath = Join-Path $configPath $file
        if (Test-Path $filePath) {
            Write-Host "  Found: $file" -ForegroundColor Green
            
            # Validate JSON syntax
            try {
                $content = Get-Content $filePath -Raw
                $null = $content | ConvertFrom-Json
                Write-Host "    JSON syntax: Valid" -ForegroundColor Green
            }
            catch {
                Write-Host "    JSON syntax: INVALID - $_" -ForegroundColor Red
                $allValid = $false
            }
        }
        else {
            Write-Host "  MISSING: $file" -ForegroundColor Red
            $allValid = $false
        }
    }
}

if (-not $allValid) {
    Write-Host "`nConfiguration validation failed!" -ForegroundColor Red
    exit 1
}

Write-Host "`nConfiguration validation passed!" -ForegroundColor Green

# Validate Dockerfile has APPLICATION build arg
Write-Host "`n=== Validating Dockerfile ===" -ForegroundColor Yellow

$dockerfilePath = Join-Path $repoRoot "Dockerfile"
if (-not (Test-Path $dockerfilePath)) {
    Write-Host "ERROR: Dockerfile not found at $dockerfilePath" -ForegroundColor Red
    exit 1
}

$dockerfileContent = Get-Content $dockerfilePath -Raw

if ($dockerfileContent -match "ARG APPLICATION") {
    Write-Host "Dockerfile contains APPLICATION build arg" -ForegroundColor Green
}
else {
    Write-Host "ERROR: Dockerfile does not contain APPLICATION build arg" -ForegroundColor Red
    exit 1
}

if ($dockerfileContent -match "cp.*configurations/\`${APPLICATION}") {
    Write-Host "Dockerfile copies application-specific configuration" -ForegroundColor Green
}
else {
    Write-Host "ERROR: Dockerfile does not copy application-specific configuration" -ForegroundColor Red
    exit 1
}

Write-Host "`nDockerfile validation passed!" -ForegroundColor Green

# Skip Docker build if requested
if ($SkipBuild) {
    Write-Host "`nSkipping Docker build (use without -SkipBuild to test full build)" -ForegroundColor Yellow
    Write-Host "`nAll validations passed!" -ForegroundColor Green
    exit 0
}

# Test Docker builds
Write-Host "`n=== Testing Docker Builds ===" -ForegroundColor Yellow

foreach ($app in $applications) {
    $imageName = "extapp-$($app.ToLower()):test"
    Write-Host "`nBuilding $app ($imageName)..." -ForegroundColor Cyan
    
    Push-Location $repoRoot
    try {
        # Build the Docker image
        $buildArgs = "--build-arg", "APPLICATION=$app", "-t", $imageName, "."
        
        Write-Host "Running: docker build $($buildArgs -join ' ')" -ForegroundColor Gray
        
        $process = Start-Process -FilePath "docker" -ArgumentList @("build") + $buildArgs -Wait -PassThru -NoNewWindow
        
        if ($process.ExitCode -eq 0) {
            Write-Host "Build successful: $imageName" -ForegroundColor Green
            
            # Verify the configuration was copied correctly
            Write-Host "Verifying configuration in container..." -ForegroundColor Cyan
            
            $verifyScript = "if [ -f /app/appsettings.json ]; then echo 'appsettings.json: FOUND'; cat /app/appsettings.json | head -20; else echo 'appsettings.json: MISSING'; fi"
            docker run --rm $imageName sh -c $verifyScript
            
            # Clean up test image
            Write-Host "Cleaning up test image..." -ForegroundColor Gray
            docker rmi $imageName 2>$null
        }
        else {
            Write-Host "Build FAILED for $app" -ForegroundColor Red
            $allValid = $false
        }
    }
    finally {
        Pop-Location
    }
}

if ($allValid) {
    Write-Host "`n=== All tests passed! ===" -ForegroundColor Green
    exit 0
}
else {
    Write-Host "`n=== Some tests failed ===" -ForegroundColor Red
    exit 1
}
