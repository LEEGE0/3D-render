param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

$projectRoot = Join-Path $RepositoryRoot 'src\PvpGuide.Editor'
$domainRoot = Join-Path $RepositoryRoot 'src\PvpGuide.Domain'
$testRoot = Join-Path $RepositoryRoot 'tests\PvpGuide.Domain.Tests'
$requiredFiles = @(
    (Join-Path $projectRoot 'project.godot'),
    (Join-Path $projectRoot 'PvpGuide.Editor.csproj'),
    (Join-Path $projectRoot 'PvpGuide.Editor.sln'),
    (Join-Path $projectRoot 'Scenes\Main\Main.tscn'),
    (Join-Path $projectRoot 'Scenes\Main\Main.cs'),
    (Join-Path $domainRoot 'PvpGuide.Domain.csproj'),
    (Join-Path $domainRoot 'DomainAssembly.cs'),
    (Join-Path $testRoot 'PvpGuide.Domain.Tests.csproj'),
    (Join-Path $testRoot 'DomainAssemblyTests.cs')
)

foreach ($requiredFile in $requiredFiles) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "필수 프로젝트 파일이 없습니다: $requiredFile"
    }
}

function Assert-Contains {
    param(
        [string]$Path,
        [string]$Pattern,
        [string]$Description
    )

    $content = Get-Content -LiteralPath $Path -Raw
    if ($content -notmatch $Pattern) {
        throw "$Description 검증에 실패했습니다: $Path"
    }
}

$projectFile = Join-Path $projectRoot 'project.godot'
$csprojFile = Join-Path $projectRoot 'PvpGuide.Editor.csproj'
$sceneFile = Join-Path $projectRoot 'Scenes\Main\Main.tscn'
$testProjectFile = Join-Path $testRoot 'PvpGuide.Domain.Tests.csproj'

Assert-Contains $projectFile 'run/main_scene="res://Scenes/Main/Main\.tscn"' '메인 장면 설정'
Assert-Contains $projectFile '"C#"' 'C# 기능 설정'
Assert-Contains $projectFile '"Forward Plus"' 'Forward+ 렌더러 설정'
Assert-Contains $projectFile 'renderer/rendering_method="forward_plus"' 'Forward+ 렌더링 방식 설정'
Assert-Contains $csprojFile 'Godot\.NET\.Sdk/4\.7\.2' 'Godot .NET SDK 버전'
Assert-Contains $csprojFile '<TargetFramework>net8\.0</TargetFramework>' '.NET 대상 프레임워크'
Assert-Contains $csprojFile '<EnableDynamicLoading>true</EnableDynamicLoading>' 'Godot 동적 로딩 설정'
Assert-Contains $testProjectFile '\.\./\.\./src/PvpGuide\.Domain/PvpGuide\.Domain\.csproj' 'Domain 프로젝트 참조'

foreach ($nodeName in @('TopViewPanel', 'WorldViewPanel', 'TimelinePanel', 'InspectorPanel')) {
    Assert-Contains $sceneFile ([regex]::Escape('name="' + $nodeName + '"')) "장면 노드 $nodeName"
}

Write-Output 'PROJECT_SKELETON_VERIFICATION=PASS'
