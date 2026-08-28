[CmdletBinding()]
param(
    [double]$BuildGateMilliseconds = 8.0
)

$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw 'Measure-TrajectoryPerformance.ps1 requires PowerShell 7 or later.'
}

if (-not [double]::IsFinite($BuildGateMilliseconds) -or $BuildGateMilliseconds -le 0) {
    throw 'BuildGateMilliseconds must be finite and positive.'
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $repositoryRoot 'tests\PvpGuide.Domain.Tests\PvpGuide.Domain.Tests.csproj'
$previousNugetPackages = $env:NUGET_PACKAGES
$previousProbe = $env:PVP_GUIDE_TRAJECTORY_PERF_PROBE

try {
    Set-Location -LiteralPath $repositoryRoot
    $env:NUGET_PACKAGES = 'D:\3D-render\tools\nuget-packages'

    & dotnet build $testProject -c Debug --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Domain performance probe build failed with exit code $LASTEXITCODE."
    }

    $env:PVP_GUIDE_TRAJECTORY_PERF_PROBE = '1'
    $probeOutput = @(& dotnet test $testProject `
        -c Debug `
        --nologo `
        --no-build `
        --no-restore `
        --filter FullyQualifiedName~TrajectoryPerformanceContractTests.Performance_probe `
        --logger 'console;verbosity=detailed' 2>&1)
    $probeExitCode = $LASTEXITCODE
    $probeOutput | ForEach-Object { Write-Output $_ }
    if ($probeExitCode -ne 0) {
        throw "Domain performance probe failed with exit code $probeExitCode."
    }

    $joinedOutput = $probeOutput -join "`n"
    $markerPattern = 'TRAJECTORY_PERFORMANCE_RESULT\s+fixture=(?<fixture>\S+)\s+build_p95_ms=(?<build>\d+(?:\.\d+)?)\s+snapshot_p95_ms=(?<snapshot>\d+(?:\.\d+)?)\s+actors=(?<actors>\d+)\s+samples=(?<samples>\d+)\s+keys=(?<keys>\d+)\s+segment_steps=(?<steps>\d+)'
    $markers = [regex]::Matches($joinedOutput, $markerPattern)
    if ($markers.Count -lt 2) {
        throw 'Expected both 4x100 and 16x1000 TRAJECTORY_PERFORMANCE_RESULT markers.'
    }

    $representative = $markers |
        Where-Object { $_.Groups['fixture'].Value -eq '4x100' } |
        Select-Object -First 1
    if ($null -eq $representative) {
        throw 'The 4x100 representative performance marker was not emitted.'
    }

    $invariantCulture = [System.Globalization.CultureInfo]::InvariantCulture
    $buildP95 = [double]::Parse(
        $representative.Groups['build'].Value,
        $invariantCulture)
    if ($buildP95 -gt $BuildGateMilliseconds) {
        Write-Output "TRAJECTORY_PERFORMANCE_GATE=FAIL build_p95_ms=$($buildP95.ToString('F6', $invariantCulture)) limit_ms=$($BuildGateMilliseconds.ToString('F2', $invariantCulture))"
        throw 'The representative trajectory build p95 exceeded the 8ms completion gate.'
    }

    Write-Output "TRAJECTORY_PERFORMANCE_GATE=PASS build_p95_ms=$($buildP95.ToString('F6', $invariantCulture)) limit_ms=$($BuildGateMilliseconds.ToString('F2', $invariantCulture))"
}
finally {
    if ($null -eq $previousNugetPackages) {
        Remove-Item Env:NUGET_PACKAGES -ErrorAction SilentlyContinue
    }
    else {
        $env:NUGET_PACKAGES = $previousNugetPackages
    }

    if ($null -eq $previousProbe) {
        Remove-Item Env:PVP_GUIDE_TRAJECTORY_PERF_PROBE -ErrorAction SilentlyContinue
    }
    else {
        $env:PVP_GUIDE_TRAJECTORY_PERF_PROBE = $previousProbe
    }

    Set-Location -LiteralPath $PSScriptRoot
}
