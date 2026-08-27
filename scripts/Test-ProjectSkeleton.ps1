param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

$projectRoot = Join-Path $RepositoryRoot 'src\PvpGuide.Editor'
$domainRoot = Join-Path $RepositoryRoot 'src\PvpGuide.Domain'
$applicationRoot = Join-Path $RepositoryRoot 'src\PvpGuide.Application'
$infrastructureRoot = Join-Path $RepositoryRoot 'src\PvpGuide.Infrastructure'
$testRoot = Join-Path $RepositoryRoot 'tests\PvpGuide.Domain.Tests'
$applicationTestRoot = Join-Path $RepositoryRoot 'tests\PvpGuide.Application.Tests'
$infrastructureTestRoot = Join-Path $RepositoryRoot 'tests\PvpGuide.Infrastructure.Tests'
$editorTestRoot = Join-Path $RepositoryRoot 'tests\PvpGuide.Editor.Tests'
$sampleRoot = Join-Path $RepositoryRoot 'samples\guides'
$requiredFiles = @(
    (Join-Path $projectRoot 'project.godot'),
    (Join-Path $projectRoot 'PvpGuide.Editor.csproj'),
    (Join-Path $projectRoot 'PvpGuide.Editor.sln'),
    (Join-Path $projectRoot 'Scenes\Main\Main.tscn'),
    (Join-Path $projectRoot 'Scenes\Main\Main.cs'),
    (Join-Path $domainRoot 'PvpGuide.Domain.csproj'),
    (Join-Path $domainRoot 'DomainAssembly.cs'),
    (Join-Path $domainRoot 'SceneDocument.cs'),
    (Join-Path $domainRoot 'SceneSnapshot.cs'),
    (Join-Path $domainRoot 'Actors\ActorTrack.cs'),
    (Join-Path $domainRoot 'Timeline\TransformKeyframe.cs'),
    (Join-Path $domainRoot 'Timeline\ActionKeyframe.cs'),
    (Join-Path $domainRoot 'Timeline\LockOnKeyframe.cs'),
    (Join-Path $applicationRoot 'PvpGuide.Application.csproj'),
    (Join-Path $applicationRoot 'Properties\AssemblyInfo.cs'),
    (Join-Path $applicationRoot 'Sessions\DocumentSession.cs'),
    (Join-Path $applicationRoot 'Sessions\SelectionChangedEventArgs.cs'),
    (Join-Path $applicationRoot 'Editing\TransformPreview.cs'),
    (Join-Path $applicationRoot 'Editing\TransformPreviewChangedEventArgs.cs'),
    (Join-Path $applicationRoot 'Commands\ISceneEditCommand.cs'),
    (Join-Path $applicationRoot 'Commands\ReplaceTransformCommand.cs'),
    (Join-Path $infrastructureRoot 'PvpGuide.Infrastructure.csproj'),
    (Join-Path $infrastructureRoot 'Serialization\SceneDocumentSerializer.cs'),
    (Join-Path $infrastructureRoot 'Import\TopviewGuideV1Importer.cs'),
    (Join-Path $projectRoot 'Features\ViewportSync\SceneProjectionController.cs'),
    (Join-Path $projectRoot 'Features\Rendering\RenderQueue.cs'),
    (Join-Path $projectRoot 'Features\Rendering\RenderQueue.cs.uid'),
    (Join-Path $sampleRoot 'synthetic-topview-v1.scene.json'),
    (Join-Path $testRoot 'PvpGuide.Domain.Tests.csproj'),
    (Join-Path $testRoot 'DomainAssemblyTests.cs'),
    (Join-Path $testRoot 'SceneDocumentTests.cs'),
    (Join-Path $applicationTestRoot 'PvpGuide.Application.Tests.csproj'),
    (Join-Path $applicationTestRoot 'DocumentSessionTests.cs'),
    (Join-Path $infrastructureTestRoot 'PvpGuide.Infrastructure.Tests.csproj'),
    (Join-Path $infrastructureTestRoot 'TopviewGuideV1ImporterTests.cs'),
    (Join-Path $infrastructureTestRoot 'SceneRoundTripTests.cs'),
    (Join-Path $editorTestRoot 'PvpGuide.Editor.Tests.csproj'),
    (Join-Path $editorTestRoot 'SceneProjectionControllerTests.cs'),
    (Join-Path $editorTestRoot 'RenderQueueTests.cs')
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
$applicationProjectFile = Join-Path $applicationRoot 'PvpGuide.Application.csproj'
$applicationTestProjectFile = Join-Path $applicationTestRoot 'PvpGuide.Application.Tests.csproj'
$infrastructureProjectFile = Join-Path $infrastructureRoot 'PvpGuide.Infrastructure.csproj'
$infrastructureTestProjectFile = Join-Path $infrastructureTestRoot 'PvpGuide.Infrastructure.Tests.csproj'

Assert-Contains $projectFile 'run/main_scene="res://Scenes/Main/Main\.tscn"' '메인 장면 설정'
Assert-Contains $projectFile '"C#"' 'C# 기능 설정'
Assert-Contains $projectFile '"Forward Plus"' 'Forward+ 렌더러 설정'
Assert-Contains $projectFile 'renderer/rendering_method="forward_plus"' 'Forward+ 렌더링 방식 설정'
Assert-Contains $csprojFile 'Godot\.NET\.Sdk/4\.7\.2' 'Godot .NET SDK 버전'
Assert-Contains $csprojFile '<TargetFramework>net8\.0</TargetFramework>' '.NET 대상 프레임워크'
Assert-Contains $csprojFile '<EnableDynamicLoading>true</EnableDynamicLoading>' 'Godot 동적 로딩 설정'
Assert-Contains $csprojFile '\.\./PvpGuide\.Domain/PvpGuide\.Domain\.csproj' 'Editor에서 Domain 프로젝트 참조'
Assert-Contains $testProjectFile '\.\./\.\./src/PvpGuide\.Domain/PvpGuide\.Domain\.csproj' 'Domain 프로젝트 참조'
Assert-Contains $applicationProjectFile '\.\./PvpGuide\.Domain/PvpGuide\.Domain\.csproj' 'Application에서 Domain 프로젝트 참조'
Assert-Contains $applicationTestProjectFile '\.\./\.\./src/PvpGuide\.Application/PvpGuide\.Application\.csproj' 'Application 테스트 프로젝트 참조'
Assert-Contains $infrastructureProjectFile '\.\./PvpGuide\.Domain/PvpGuide\.Domain\.csproj' 'Infrastructure에서 Domain 프로젝트 참조'
Assert-Contains $infrastructureTestProjectFile '\.\./\.\./src/PvpGuide\.Infrastructure/PvpGuide\.Infrastructure\.csproj' 'Infrastructure 테스트 프로젝트 참조'
Assert-Contains $infrastructureTestProjectFile 'synthetic-topview-v1\.scene\.json' '합성 가져오기 fixture 포함'

foreach ($nodeName in @('TopViewPanel', 'WorldViewPanel', 'TimelinePanel', 'InspectorPanel')) {
    Assert-Contains $sceneFile ([regex]::Escape('name="' + $nodeName + '"')) "장면 노드 $nodeName"
}

Write-Output 'PROJECT_SKELETON_VERIFICATION=PASS'
