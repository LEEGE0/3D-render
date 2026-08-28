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
$readmeFile = Join-Path $RepositoryRoot 'README.md'
$editorArchitectureFile = Join-Path $RepositoryRoot 'docs\05-editor-architecture.md'
$roadmapFile = Join-Path $RepositoryRoot 'docs\13-roadmap.md'
$runtimeTestScript = Join-Path $RepositoryRoot 'scripts\Test-GodotRuntime.ps1'
$trajectoryPerformanceScript = Join-Path $RepositoryRoot 'scripts\Measure-TrajectoryPerformance.ps1'
$serializerFile = Join-Path $infrastructureRoot 'Serialization\SceneDocumentSerializer.cs'
$sceneRoundTripTestFile = Join-Path $infrastructureTestRoot 'SceneRoundTripTests.cs'
$lockOnTrajectoryPlanFile = Join-Path $RepositoryRoot 'docs\superpowers\plans\2026-08-28-lock-on-facing-trajectory.md'
$lockOnTrajectorySpecFile = Join-Path $RepositoryRoot 'docs\superpowers\specs\2026-08-28-lock-on-facing-trajectory-design.md'
$requiredFiles = @(
    $readmeFile,
    $editorArchitectureFile,
    $roadmapFile,
    $runtimeTestScript,
    $trajectoryPerformanceScript,
    $lockOnTrajectoryPlanFile,
    $lockOnTrajectorySpecFile,
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
    (Join-Path $domainRoot 'Timeline\FacingResolutionKind.cs'),
    (Join-Path $domainRoot 'Timeline\EvaluatedActorFacing.cs'),
    (Join-Path $domainRoot 'Timeline\LockOnFacingEvaluator.cs'),
    (Join-Path $domainRoot 'Timeline\TrajectoryAnchorKind.cs'),
    (Join-Path $domainRoot 'Timeline\TrajectorySamplingSettings.cs'),
    (Join-Path $domainRoot 'Timeline\TrajectorySamplePlan.cs'),
    (Join-Path $domainRoot 'Timeline\MovementTrajectorySample.cs'),
    (Join-Path $domainRoot 'Timeline\ActorMovementTrajectory.cs'),
    (Join-Path $domainRoot 'Timeline\MovementTrajectorySet.cs'),
    (Join-Path $domainRoot 'Timeline\MovementTrajectoryEvaluator.cs'),
    (Join-Path $domainRoot 'Timeline\TrajectoryEvaluationDiagnostics.cs'),
    (Join-Path $domainRoot 'ProjectionSourceMetadata.cs'),
    (Join-Path $domainRoot 'ISceneProjectionSource.cs'),
    (Join-Path $applicationRoot 'PvpGuide.Application.csproj'),
    (Join-Path $applicationRoot 'Properties\AssemblyInfo.cs'),
    (Join-Path $applicationRoot 'Sessions\DocumentSession.cs'),
    (Join-Path $applicationRoot 'Sessions\ActorDisplayInfo.cs'),
    (Join-Path $applicationRoot 'Sessions\SelectionChangedEventArgs.cs'),
    (Join-Path $applicationRoot 'Sessions\EditAvailabilityChangedEventArgs.cs'),
    (Join-Path $applicationRoot 'Playback\IPlaybackTimeSource.cs'),
    (Join-Path $applicationRoot 'Playback\PlaybackClock.cs'),
    (Join-Path $applicationRoot 'Playback\PlaybackChangedEventArgs.cs'),
    (Join-Path $applicationRoot 'Editing\TransformPreview.cs'),
    (Join-Path $applicationRoot 'Editing\TransformPreviewChangedEventArgs.cs'),
    (Join-Path $applicationRoot 'Editing\SceneEditResult.cs'),
    (Join-Path $applicationRoot 'Commands\ISceneEditCommand.cs'),
    (Join-Path $applicationRoot 'Commands\ReplaceTransformCommand.cs'),
    (Join-Path $applicationRoot 'Projection\ISceneProjectionConsumer.cs'),
    (Join-Path $applicationRoot 'Projection\SceneProjectionController.cs'),
    (Join-Path $applicationRoot 'Projection\ITransformPreviewConsumer.cs'),
    (Join-Path $applicationRoot 'Projection\TransformPreviewController.cs'),
    (Join-Path $applicationRoot 'Projection\SceneProjectionFrame.cs'),
    (Join-Path $applicationRoot 'Projection\TrajectorySamplingPolicy.cs'),
    (Join-Path $infrastructureRoot 'PvpGuide.Infrastructure.csproj'),
    (Join-Path $infrastructureRoot 'Serialization\SceneDocumentSerializer.cs'),
    (Join-Path $infrastructureRoot 'Import\TopviewGuideV1Importer.cs'),
    (Join-Path $projectRoot 'Features\TopView\TopViewCoordinateMapper.cs'),
    (Join-Path $projectRoot 'Features\TopView\TopViewCoordinateMapper.cs.uid'),
    (Join-Path $projectRoot 'Features\TopView\TopViewSurface.cs'),
    (Join-Path $projectRoot 'Features\TopView\TopViewSurface.cs.uid'),
    (Join-Path $projectRoot 'Features\TopView\TrajectoryOverlayLayout.cs'),
    (Join-Path $projectRoot 'Features\TopView\TrajectoryOverlayLayout.cs.uid'),
    (Join-Path $projectRoot 'Features\Trajectory\TrajectoryTickSelectionPolicy.cs'),
    (Join-Path $projectRoot 'Features\Trajectory\TrajectoryTickSelectionPolicy.cs.uid'),
    (Join-Path $projectRoot 'Features\ViewportSync\WorldViewProjectionAdapter.cs'),
    (Join-Path $projectRoot 'Features\ViewportSync\WorldViewProjectionAdapter.cs.uid'),
    (Join-Path $projectRoot 'Features\ViewportSync\WorldTransformMapper.cs'),
    (Join-Path $projectRoot 'Features\ViewportSync\WorldTransformMapper.cs.uid'),
    (Join-Path $projectRoot 'Features\ViewportSync\WorldTrajectoryGeometry.cs'),
    (Join-Path $projectRoot 'Features\ViewportSync\WorldTrajectoryGeometry.cs.uid'),
    (Join-Path $projectRoot 'Features\ViewportSync\WorldTrajectoryRenderState.cs'),
    (Join-Path $projectRoot 'Features\ViewportSync\WorldTrajectoryRenderState.cs.uid'),
    (Join-Path $projectRoot 'Features\ViewportSync\TrajectoryTimeFade.gdshader'),
    (Join-Path $projectRoot 'Features\ViewportSync\TrajectoryTimeFade.gdshader.uid'),
    (Join-Path $projectRoot 'Features\Inspector\TransformInspectorController.cs'),
    (Join-Path $projectRoot 'Features\Inspector\TransformInspectorController.cs.uid'),
    (Join-Path $projectRoot 'Features\Timeline\TimelineController.cs'),
    (Join-Path $projectRoot 'Features\Timeline\TimelineController.cs.uid'),
    (Join-Path $projectRoot 'Features\Timeline\TimelineTimeFormatter.cs'),
    (Join-Path $projectRoot 'Features\Timeline\TimelineTimeFormatter.cs.uid'),
    (Join-Path $projectRoot 'Features\Timeline\TransformTrackLayout.cs'),
    (Join-Path $projectRoot 'Features\Timeline\TransformTrackSurface.cs'),
    (Join-Path $projectRoot 'Features\Timeline\StepTrackLayout.cs'),
    (Join-Path $projectRoot 'Features\Timeline\StepTrackLayout.cs.uid'),
    (Join-Path $projectRoot 'Features\Timeline\ActionTrackSurface.cs'),
    (Join-Path $projectRoot 'Features\Timeline\ActionTrackSurface.cs.uid'),
    (Join-Path $projectRoot 'Features\Timeline\LockOnTrackSurface.cs'),
    (Join-Path $projectRoot 'Features\Timeline\LockOnTrackSurface.cs.uid'),
    (Join-Path $projectRoot 'Features\Timeline\SemanticTimelineController.cs'),
    (Join-Path $projectRoot 'Features\Timeline\SemanticTimelineController.cs.uid'),
    (Join-Path $projectRoot 'Features\Inspector\ActionLockOnInspectorController.cs'),
    (Join-Path $projectRoot 'Features\Inspector\ActionLockOnInspectorController.cs.uid'),
    (Join-Path $projectRoot 'Features\Rendering\RenderQueue.cs'),
    (Join-Path $projectRoot 'Features\Rendering\RenderQueue.cs.uid'),
    (Join-Path $sampleRoot 'synthetic-topview-v1.scene.json'),
    (Join-Path $testRoot 'PvpGuide.Domain.Tests.csproj'),
    (Join-Path $testRoot 'DomainAssemblyTests.cs'),
    (Join-Path $testRoot 'SceneDocumentTests.cs'),
    (Join-Path $testRoot 'LockOnFacingEvaluatorTests.cs'),
    (Join-Path $testRoot 'MovementTrajectoryEvaluatorTests.cs'),
    (Join-Path $testRoot 'TrajectoryPerformanceContractTests.cs'),
    (Join-Path $applicationTestRoot 'PvpGuide.Application.Tests.csproj'),
    (Join-Path $applicationTestRoot 'DocumentSessionTests.cs'),
    (Join-Path $applicationTestRoot 'PlaybackClockTests.cs'),
    (Join-Path $applicationTestRoot 'SceneProjectionControllerTests.cs'),
    (Join-Path $applicationTestRoot 'TransformPreviewControllerTests.cs'),
    (Join-Path $infrastructureTestRoot 'PvpGuide.Infrastructure.Tests.csproj'),
    (Join-Path $infrastructureTestRoot 'TopviewGuideV1ImporterTests.cs'),
    (Join-Path $infrastructureTestRoot 'SceneRoundTripTests.cs'),
    (Join-Path $editorTestRoot 'PvpGuide.Editor.Tests.csproj'),
    (Join-Path $editorTestRoot 'TopViewCoordinateMapperTests.cs'),
    (Join-Path $editorTestRoot 'WorldTransformMapperTests.cs'),
    (Join-Path $editorTestRoot 'RenderQueueTests.cs'),
    (Join-Path $editorTestRoot 'TimelineTimeFormatterTests.cs'),
    (Join-Path $editorTestRoot 'TransformTrackLayoutTests.cs'),
    (Join-Path $editorTestRoot 'StepTrackLayoutTests.cs'),
    (Join-Path $editorTestRoot 'SemanticOverlayLayoutTests.cs'),
    (Join-Path $editorTestRoot 'TrajectoryOverlayLayoutTests.cs'),
    (Join-Path $editorTestRoot 'WorldTrajectoryGeometryTests.cs')
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

function Assert-NotContains {
    param(
        [string]$Path,
        [string]$Pattern,
        [string]$Description
    )

    $content = Get-Content -LiteralPath $Path -Raw
    if ($content -match $Pattern) {
        throw "$Description 검증에 실패했습니다: $Path"
    }
}

function Assert-ScriptedSceneNode {
    param(
        [string]$Path,
        [string]$Name,
        [string]$Type,
        [string]$Parent,
        [string]$ScriptResourceId
    )

    $content = Get-Content -LiteralPath $Path -Raw
    $declaration = [regex]::Escape('[node name="' + $Name + '" type="' + $Type + '" parent="' + $Parent + '"]')
    $scriptAssignment = [regex]::Escape('script = ExtResource("' + $ScriptResourceId + '")')
    $nodeBlockPattern = '(?ms)^' + $declaration + '\r?\n(?:(?!^\[node ).)*?^' + $scriptAssignment + '\r?$'
    if ($content -notmatch $nodeBlockPattern) {
        throw "script 연결 장면 노드 검증에 실패했습니다: $Parent/$Name -> $ScriptResourceId"
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
$worldTransformMapperFile = Join-Path $projectRoot 'Features\ViewportSync\WorldTransformMapper.cs'
$playbackTimeSourceFile = Join-Path $applicationRoot 'Playback\IPlaybackTimeSource.cs'
$playbackClockFile = Join-Path $applicationRoot 'Playback\PlaybackClock.cs'
$timelineTimeFormatterFile = Join-Path $projectRoot 'Features\Timeline\TimelineTimeFormatter.cs'
$transformTrackLayoutFile = Join-Path $projectRoot 'Features\Timeline\TransformTrackLayout.cs'
$stepTrackLayoutFile = Join-Path $projectRoot 'Features\Timeline\StepTrackLayout.cs'
$runtimeExactMarker = 'LOCK_ON_MOTION_READY snap=1 continuous=1 keyframe_only=1 coincidence=1 missing_target=1 shared_frame=1 trajectories=1 cache_reuse=1 nodes_reused=1'
$runtimeRequiredOutputEntry = "'$runtimeExactMarker'"
$runtimeRequiredOutputEntryPattern = '(?ms)(?-i)^\)[\t ]+-RequiredOutput[\t ]+@\([\t ]*\r?\n(?:(?!^\)[\t ]*\r?$).)*?^[\t ]*' + [regex]::Escape($runtimeRequiredOutputEntry) + '[\t ]*,?[\t ]*\r?$(?:(?!^\)[\t ]*\r?$).)*?^\)[\t ]*\r?$'
$performanceResultContract = 'TRAJECTORY_PERFORMANCE_RESULT\s+fixture=(?<fixture>\S+)\s+build_p95_ms=(?<build>\d+(?:\.\d+)?)\s+snapshot_p95_ms=(?<snapshot>\d+(?:\.\d+)?)\s+actors=(?<actors>\d+)\s+samples=(?<samples>\d+)\s+keys=(?<keys>\d+)\s+segment_steps=(?<steps>\d+)'
$performanceResultParserLine = '$markerPattern = ' + "'$performanceResultContract'"
$performanceResultParserPattern = '(?m)(?-i)^[\t ]*' + [regex]::Escape($performanceResultParserLine) + '[\t ]*\r?$'
$performancePassOutputLine = 'Write-Output "TRAJECTORY_PERFORMANCE_GATE=PASS build_p95_ms=$($buildP95.ToString(''F6'', $invariantCulture)) limit_ms=$($BuildGateMilliseconds.ToString(''F2'', $invariantCulture))"'
$performancePassOutputPattern = '(?m)(?-i)^[\t ]*' + [regex]::Escape($performancePassOutputLine) + '[\t ]*\r?$'
$performanceDefaultGatePattern = '(?m)(?-i)^[\t ]*\[double\]\$BuildGateMilliseconds[\t ]*=[\t ]*8\.0[\t ]*\r?$'
$performanceGateComparisonPattern = '(?m)(?-i)^[\t ]*if[\t ]*\(\$buildP95[\t ]+-gt[\t ]+\$BuildGateMilliseconds\)[\t ]*\{[\t ]*\r?$'

Assert-Contains $projectFile 'run/main_scene="res://Scenes/Main/Main\.tscn"' '메인 장면 설정'
Assert-Contains $projectFile '"C#"' 'C# 기능 설정'
Assert-Contains $projectFile '"Forward Plus"' 'Forward+ 렌더러 설정'
Assert-Contains $projectFile 'renderer/rendering_method="forward_plus"' 'Forward+ 렌더링 방식 설정'
Assert-Contains $csprojFile 'Godot\.NET\.Sdk/4\.7\.2' 'Godot .NET SDK 버전'
Assert-Contains $csprojFile '<TargetFramework>net8\.0</TargetFramework>' '.NET 대상 프레임워크'
Assert-Contains $csprojFile '<EnableDynamicLoading>true</EnableDynamicLoading>' 'Godot 동적 로딩 설정'
Assert-Contains $csprojFile '\.\./PvpGuide\.Application/PvpGuide\.Application\.csproj' 'Editor에서 Application 프로젝트 참조'
Assert-Contains $testProjectFile '\.\./\.\./src/PvpGuide\.Domain/PvpGuide\.Domain\.csproj' 'Domain 프로젝트 참조'
Assert-Contains $applicationProjectFile '\.\./PvpGuide\.Domain/PvpGuide\.Domain\.csproj' 'Application에서 Domain 프로젝트 참조'
Assert-Contains $applicationTestProjectFile '\.\./\.\./src/PvpGuide\.Application/PvpGuide\.Application\.csproj' 'Application 테스트 프로젝트 참조'
Assert-Contains $infrastructureProjectFile '\.\./PvpGuide\.Domain/PvpGuide\.Domain\.csproj' 'Infrastructure에서 Domain 프로젝트 참조'
Assert-Contains $infrastructureTestProjectFile '\.\./\.\./src/PvpGuide\.Infrastructure/PvpGuide\.Infrastructure\.csproj' 'Infrastructure 테스트 프로젝트 참조'
Assert-Contains $infrastructureTestProjectFile 'synthetic-topview-v1\.scene\.json' '합성 가져오기 fixture 포함'
Assert-NotContains $worldTransformMapperFile 'Godot|Vector[234]' 'WorldTransformMapper의 Godot 독립성'
Assert-NotContains $playbackTimeSourceFile 'Godot|Node|Timer' 'IPlaybackTimeSource의 Godot 독립성'
Assert-NotContains $playbackClockFile 'Godot|Node|Timer' 'PlaybackClock의 Godot 독립성'
Assert-NotContains $timelineTimeFormatterFile 'Godot|Node|Control' 'TimelineTimeFormatter의 Godot 독립성'
Assert-NotContains $transformTrackLayoutFile 'Godot|Node|Control|Vector' 'TransformTrackLayout의 Godot 독립성'
Assert-NotContains $stepTrackLayoutFile 'Godot|Node|Control|Vector' 'StepTrackLayout의 Godot 독립성'
Assert-Contains $runtimeTestScript $runtimeRequiredOutputEntryPattern 'Lock-on 방향·궤적 runtime RequiredOutput exact entry'
Assert-Contains $serializerFile ([regex]::Escape('private const string CurrentSchemaV2 = "pvp-guide-scene/2";')) 'serializer schema /2 상수'
Assert-Contains $sceneRoundTripTestFile ([regex]::Escape('"schema": "pvp-guide-scene/2"')) 'scene round-trip schema /2 fixture'
Assert-Contains $trajectoryPerformanceScript $performanceResultParserPattern '궤적 성능 ordered result marker parser'
Assert-Contains $trajectoryPerformanceScript $performancePassOutputPattern '궤적 성능 exact gate 통과 marker'
Assert-Contains $trajectoryPerformanceScript $performanceDefaultGatePattern '궤적 성능 default 8ms gate'
Assert-Contains $trajectoryPerformanceScript $performanceGateComparisonPattern '궤적 성능 build p95 gate 비교'

foreach ($nodeName in @('TopViewPanel', 'WorldViewPanel', 'TimelinePanel', 'InspectorPanel')) {
    Assert-Contains $sceneFile ([regex]::Escape('name="' + $nodeName + '"')) "장면 노드 $nodeName"
}

foreach ($nodeName in @(
    'TopViewSurface',
    'WorldViewportContainer',
    'WorldViewport',
    'WorldRoot',
    'Camera3D',
    'DirectionalLight3D',
    'Ground',
    'Actors',
    'TransformInspector',
    'SelectedActorLabel',
    'XInput',
    'YInput',
    'ZInput',
    'YawInput',
    'ApplyButton',
    'UndoButton',
    'RedoButton',
    'ErrorLabel',
    'TimelineControls',
    'PlaybackButtons',
    'PlayPauseButton',
    'StopButton',
    'TimeSlider',
    'CurrentTimeLabel',
    'TimelineStatus',
    'TransformTrackSurface',
    'KeyframeToolbar',
    'AddKeyframeButton',
    'DeleteKeyframeButton',
    'SelectedKeyframeLabel',
    'TimeInput',
    'ActionTrackSurface',
    'LockOnTrackSurface',
    'ActionToolbar',
    'LockOnToolbar',
    'ActionKeyInput',
    'ActionTimeInput',
    'LockEnabledInput',
    'LockTargetInput',
    'LockModeInput',
    'LockYawOffsetInput',
    'LockTimeInput',
    'ActionApplyButton',
    'LockApplyButton'
)) {
    Assert-Contains $sceneFile ([regex]::Escape('name="' + $nodeName + '"')) "기본 편집 장면 노드 $nodeName"
}

$semanticSceneNodes = @(
    @{ Name = 'ActionTrackSurface'; Type = 'Control'; Parent = 'TimelinePanel/TimelineControls' },
    @{ Name = 'LockOnTrackSurface'; Type = 'Control'; Parent = 'TimelinePanel/TimelineControls' },
    @{ Name = 'ActionToolbar'; Type = 'HBoxContainer'; Parent = 'TimelinePanel/TimelineControls' },
    @{ Name = 'LockOnToolbar'; Type = 'HBoxContainer'; Parent = 'TimelinePanel/TimelineControls' },
    @{ Name = 'ActionAddButton'; Type = 'Button'; Parent = 'TimelinePanel/TimelineControls/ActionToolbar' },
    @{ Name = 'ActionDeleteButton'; Type = 'Button'; Parent = 'TimelinePanel/TimelineControls/ActionToolbar' },
    @{ Name = 'LockOnAddButton'; Type = 'Button'; Parent = 'TimelinePanel/TimelineControls/LockOnToolbar' },
    @{ Name = 'LockOnDeleteButton'; Type = 'Button'; Parent = 'TimelinePanel/TimelineControls/LockOnToolbar' },
    @{ Name = 'TransformInspector'; Type = 'VBoxContainer'; Parent = 'InspectorPanel' },
    @{ Name = 'HistoryToolbar'; Type = 'HBoxContainer'; Parent = 'InspectorPanel' },
    @{ Name = 'UndoButton'; Type = 'Button'; Parent = 'InspectorPanel/HistoryToolbar' },
    @{ Name = 'RedoButton'; Type = 'Button'; Parent = 'InspectorPanel/HistoryToolbar' },
    @{ Name = 'ActionInspector'; Type = 'VBoxContainer'; Parent = 'InspectorPanel' },
    @{ Name = 'ActionSelectedKeyframeLabel'; Type = 'Label'; Parent = 'InspectorPanel/ActionInspector' },
    @{ Name = 'ActionKeyInput'; Type = 'LineEdit'; Parent = 'InspectorPanel/ActionInspector' },
    @{ Name = 'ActionTimeInput'; Type = 'SpinBox'; Parent = 'InspectorPanel/ActionInspector' },
    @{ Name = 'ActionApplyButton'; Type = 'Button'; Parent = 'InspectorPanel/ActionInspector' },
    @{ Name = 'ActionErrorLabel'; Type = 'Label'; Parent = 'InspectorPanel/ActionInspector' },
    @{ Name = 'LockOnInspector'; Type = 'VBoxContainer'; Parent = 'InspectorPanel' },
    @{ Name = 'LockOnSelectedKeyframeLabel'; Type = 'Label'; Parent = 'InspectorPanel/LockOnInspector' },
    @{ Name = 'LockTimeInput'; Type = 'SpinBox'; Parent = 'InspectorPanel/LockOnInspector' },
    @{ Name = 'LockEnabledInput'; Type = 'CheckBox'; Parent = 'InspectorPanel/LockOnInspector' },
    @{ Name = 'LockTargetInput'; Type = 'OptionButton'; Parent = 'InspectorPanel/LockOnInspector' },
    @{ Name = 'LockModeInput'; Type = 'OptionButton'; Parent = 'InspectorPanel/LockOnInspector' },
    @{ Name = 'LockYawOffsetInput'; Type = 'SpinBox'; Parent = 'InspectorPanel/LockOnInspector' },
    @{ Name = 'LockApplyButton'; Type = 'Button'; Parent = 'InspectorPanel/LockOnInspector' },
    @{ Name = 'LockErrorLabel'; Type = 'Label'; Parent = 'InspectorPanel/LockOnInspector' },
    @{ Name = 'TimelineStatus'; Type = 'Label'; Parent = 'TimelinePanel/TimelineControls' }
)

foreach ($node in $semanticSceneNodes) {
    $declaration = '[node name="' + $node.Name + '" type="' + $node.Type + '" parent="' + $node.Parent + '"]'
    Assert-Contains $sceneFile ([regex]::Escape($declaration)) "semantic 장면 노드 $($node.Parent)/$($node.Name) 타입 $($node.Type)"
}

Assert-Contains $sceneFile '\[node name="ActionTrackSurface" type="Control" parent="TimelinePanel/TimelineControls"\]\r?\ncustom_minimum_size = Vector2\(0, 40\)' 'Action lane 최소 높이'
Assert-Contains $sceneFile '\[node name="LockOnTrackSurface" type="Control" parent="TimelinePanel/TimelineControls"\]\r?\ncustom_minimum_size = Vector2\(0, 40\)' 'Lock-on lane 최소 높이'
Assert-Contains $sceneFile ([regex]::Escape('[ext_resource type="Script" path="res://Features/Timeline/ActionTrackSurface.cs" id="4_action_track"]')) 'Action lane script resource'
Assert-Contains $sceneFile ([regex]::Escape('[ext_resource type="Script" path="res://Features/Timeline/LockOnTrackSurface.cs" id="5_lock_on_track"]')) 'Lock-on lane script resource'
Assert-ScriptedSceneNode $sceneFile 'ActionTrackSurface' 'Control' 'TimelinePanel/TimelineControls' '4_action_track'
Assert-ScriptedSceneNode $sceneFile 'LockOnTrackSurface' 'Control' 'TimelinePanel/TimelineControls' '5_lock_on_track'

Write-Output 'PROJECT_SKELETON_VERIFICATION=PASS'
