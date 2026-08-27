param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$GodotExecutable = 'D:\3D-render\tools\godot\4.7.2\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Join-Path $RepositoryRoot 'src\PvpGuide.Editor'
$projectFile = Join-Path $projectRoot 'PvpGuide.Editor.csproj'
$nugetPackages = 'D:\3D-render\tools\nuget-packages'

if (-not (Test-Path -LiteralPath $GodotExecutable -PathType Leaf)) {
    throw "Godot 실행 파일이 없습니다: $GodotExecutable"
}

$env:NUGET_PACKAGES = $nugetPackages

dotnet build $projectFile -c Debug --nologo
if ($LASTEXITCODE -ne 0) {
    throw "Godot C# 프로젝트 빌드에 실패했습니다. 종료 코드: $LASTEXITCODE"
}

function Invoke-GodotStep {
    param(
        [string]$Name,
        [string[]]$Arguments,
        [string[]]$RequiredOutput = @()
    )

    $output = (& $GodotExecutable @Arguments 2>&1 | Out-String)
    $exitCode = $LASTEXITCODE
    Write-Output "--- $Name ---"
    Write-Output $output.TrimEnd()

    if ($exitCode -ne 0) {
        throw "$Name 단계가 종료 코드 $exitCode`(으`)로 실패했습니다."
    }

    if ($output -match '(?im)^\s*(ERROR:|SCRIPT ERROR:)|Build FAILED|error CS\d+') {
        throw "$Name 단계 출력에서 오류를 발견했습니다."
    }

    foreach ($requiredMarker in $RequiredOutput) {
        if ($output -notmatch [regex]::Escape($requiredMarker)) {
            throw "$Name 단계 출력에 필수 표식 '$requiredMarker'이 없습니다."
        }
    }
}

Invoke-GodotStep -Name '리소스 가져오기' -Arguments @(
    '--headless',
    '--path', $projectRoot,
    '--import'
)

Invoke-GodotStep -Name 'Godot 솔루션 빌드' -Arguments @(
    '--headless',
    '--path', $projectRoot,
    '--build-solutions',
    '--quit'
)

Invoke-GodotStep -Name '메인 장면 실행' -Arguments @(
    '--headless',
    '--path', $projectRoot,
    '--scene', 'res://Scenes/Main/Main.tscn',
    '--quit-after', '2'
) -RequiredOutput @(
    'PROJECT_RUNTIME_READY',
    'PROJECTION_SYNC_READY revision=1 top=1 world=1'
)

Write-Output 'GODOT_RUNTIME_VERIFICATION=PASS'
