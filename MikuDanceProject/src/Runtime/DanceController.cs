using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Logging;
using MikuDanceProject.Core;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MikuDanceProject.Runtime;

internal sealed class DanceController : MonoBehaviour
{
    private const float GroundProbeStartHeight = 80f;
    private const float GroundProbeDistance = 200f;
    private const float PlayerGroundProbeStartHeight = 1.25f;
    private const float PlayerGroundProbeMaxRise = 0.6f;
    private const float PlayerGroundProbePreferredMaxDrop = 0.9f;
    private const float PlayerGroundProbeFallbackMaxDrop = 1.25f;
    private const float PlayerGroundProbeSearchRadius = 3f;
    private const float PlayerColliderFootPadding = 0.05f;
    private const float RuntimeRefreshInterval = 0.25f;
    private const float OfflinePauseTimeScaleThreshold = 0.001f;
    private const float DistancePauseThresholdMeters = 100f;
    private const float AudioMinDistanceMeters = 1f;
    private const float LegacyMotionTrimStartFrame = 0f;
    private const float LegacyMotionFrameRate = 30f;
    private const float AudioFadeDurationSeconds = 0.35f;
    private const float AudioDriftCorrectionThresholdSeconds = 0.35f;
    private const float AudioHardResyncThresholdSeconds = 1.25f;
    private const float LoopIntervalSeconds = 5f;
    private const float SavePlacementHoldDurationSeconds = 1f;
    private static readonly string[] NativeFacialClipTokens =
    {
        "face",
        "facial",
        "expression",
        "morph",
        "blend",
        "blendshape",
        "viseme",
        "mouth",
        "lip",
        "eye",
        "brow",
        "smile",
        "blink",
        "表情",
        "脸",
        "口",
        "目",
        "眉",
        "モーフ",
        "フェイス",
    };
    private static readonly string[] PreferredPivotBoneTokens =
    {
        "hips",
        "pelvis",
        "hip",
        "cf_j_hips",
        "j_bip_c_hips",
        "j_bip_c_pelvis",
        "腰",
        "下半身",
        "骨盤",
        "センター",
        "center",
        "centre",
    };
    private static readonly string[] LightingSensitiveShaderTokens =
    {
        "lit",
        "toon",
        "mmd",
        "standard",
        "outline",
        "cel",
    };
    private static readonly string[] LightingNeutralShaderTokens =
    {
        "unlit",
        "sprites/default",
        "sprite",
    };
    private static readonly string[] LightingSensitiveMaterialProperties =
    {
        "_Ambient",
        "_IndirectLightMinColor",
        "_CelShadeMidPoint",
        "_CelShadeSoftness",
        "_ReceiveShadowMappingAmount",
        "_ShadowMapColor",
        "_SpecularHighlights",
        "_EnvironmentReflections",
        "_Metallic",
        "_Smoothness",
        "_Glossiness",
        "_SpecColor",
    };
    private static readonly string[] RuntimeLightingExposureColorProperties =
    {
        "_BaseColor",
        "_Color",
        "_TintColor",
        "_MainColor",
        "_LitColor",
    };
    private const string DancerObjectName = "MikuDanceDisplay";
    private const string WrappedModelRootName = "MikuLobbyModel";
    private static readonly DancePlaybackMetadata LegacyPlaybackMetadata = DancePlaybackMetadata.CreateDefault();
    private static readonly Vector3 AirportDisplayPosition = new(-16.78f, 2.55f, 64.85f);
    private static readonly Vector3 AirportDisplayForward = Vector3.right;
    private static readonly Vector3[] PlayerGroundProbeSampleOffsets = BuildPlayerGroundSampleOffsets();

    private readonly UnityAssetBundleLoader _unityBundleLoader = new UnityAssetBundleLoader();

    private DancePluginConfig? _config;
    private ManualLogSource? _logger;
    private LoadedDancePrefab? _loadedPrefab;
    private GameObject? _activeDancer;
    private Animator? _activeAnimator;
    private Animator[] _activeAnimators = Array.Empty<Animator>();
    private Animation? _activeAnimation;
    private AudioSource? _activeAudioSource;
    private AnimationClip? _activePrimaryAnimatorClip;
    private readonly List<Material> _runtimeLightingStabilizedMaterials = new();
    private readonly Dictionary<Material, RuntimeLightingMaterialSnapshot> _runtimeLightingMaterialSnapshots = new();
    // 记录头发/服装匹配材质的原始颜色，用于在颜色开关关闭时恢复原色。
    // 仅在首次遇到材质时记录一次（此时材质应处于原色或仅经光照稳定后的状态）。
    private readonly Dictionary<Material, Color> _originalMaterialColors = new();
    private PlacementPose _activePlacement;
    private bool _hasActivePlacement;
    private bool _isLoading;
    private float _nextRefreshTime;
    private string? _lastSceneName;
    private bool _loggedSceneHint;
    private bool _lastAudioEnabledState;
    private bool _audioRetrySuppressed;
    private bool _isPausedForOfflineMenu;
    private bool _isPausedForDistance;
    private bool _audioPausedForPlaybackPause;
    private AudioSource? _fadingAudioSource;
    private float _audioFadeStartTime;
    private float _audioFadeDuration;
    private float _audioFadeTargetVolume;
    private int _animatorSegmentLoopIndex;
    private int _lastAnimatorLoopIndex = -1;
    private int _lastMorphLoopIndex = -1;
    private bool _isInLoopInterval;
    private float _loopIntervalEndTime = -1f;
    private GameObject? _activeShadowBlob;
    private bool _hasAppliedPlacementConfiguration;
    private float _appliedModelScale;
    private bool _hasResolvedPlacementPivot;
    private Vector3 _resolvedPlacementPivotLocalPosition;
    private bool _hasCachedRuntimeComponents;
    private GameObject? _cachedRuntimeComponentRoot;
    private RuntimeAnimatorController? _cachedAnimatorController;
    private bool _hasNativeFacialAnimationControl;
    private float _placementHotkeyPressedAt = -1f;
    private bool _placementHotkeyHoldConsumed;
    private KeyCode _lastObservedSpawnModelKey = KeyCode.None;
    private bool _pendingRuntimeLightingRefresh;
    private bool _lastAppliedStabilizeModelLighting;
    private float _lastAppliedLightingExposureCompensation = -1f;

    public void Initialize(DancePluginConfig config, ManualLogSource logger)
    {
        _config = config;
        _logger = logger;
        _lastAudioEnabledState = config.EnableAudio.Value;
        _lastAppliedStabilizeModelLighting = config.StabilizeModelLighting.Value;
        _lastAppliedLightingExposureCompensation = config.ResolvedLightingExposureCompensation;
        config.StabilizeModelLighting.SettingChanged += HandleRuntimeLightingConfigChanged;
        config.LightingExposureCompensation.SettingChanged += HandleRuntimeLightingConfigChanged;

        // 颜色配置变更时立即重新应用到当前模型
        config.HairColorEnabled.SettingChanged += HandleColorConfigChanged;
        config.HairColorR.SettingChanged += HandleColorConfigChanged;
        config.HairColorG.SettingChanged += HandleColorConfigChanged;
        config.HairColorB.SettingChanged += HandleColorConfigChanged;
        config.ClothColorEnabled.SettingChanged += HandleColorConfigChanged;
        config.ClothColorR.SettingChanged += HandleColorConfigChanged;
        config.ClothColorG.SettingChanged += HandleColorConfigChanged;
        config.ClothColorB.SettingChanged += HandleColorConfigChanged;
        // 随机颜色开关切换时，重新生成随机色并应用
        config.RandomizeColors.SettingChanged += HandleColorConfigChanged;
    }

    private void Start()
    {
        StartCoroutine(LoadAssetsRoutine());
    }

    private void OnDestroy()
    {
        if (_config != null)
        {
            _config.StabilizeModelLighting.SettingChanged -= HandleRuntimeLightingConfigChanged;
            _config.LightingExposureCompensation.SettingChanged -= HandleRuntimeLightingConfigChanged;
            _config.HairColorEnabled.SettingChanged -= HandleColorConfigChanged;
            _config.HairColorR.SettingChanged -= HandleColorConfigChanged;
            _config.HairColorG.SettingChanged -= HandleColorConfigChanged;
            _config.HairColorB.SettingChanged -= HandleColorConfigChanged;
            _config.ClothColorEnabled.SettingChanged -= HandleColorConfigChanged;
            _config.ClothColorR.SettingChanged -= HandleColorConfigChanged;
            _config.ClothColorG.SettingChanged -= HandleColorConfigChanged;
            _config.ClothColorB.SettingChanged -= HandleColorConfigChanged;
            _config.RandomizeColors.SettingChanged -= HandleColorConfigChanged;
        }

        DestroyActiveDancer("Dance controller destroyed.");
        DestroyRuntimeLightingStabilizedMaterials();
        _unityBundleLoader.UnloadRetainedBundle();
    }

    private void Update()
    {
        if (_config == null || _logger == null)
        {
            return;
        }

        TrackSceneTransitions();

        if (!_config.ModEnabled.Value)
        {
            if (_activeDancer != null)
            {
                DestroyActiveDancer("Model hidden because ModEnabled=False.");
            }

            _hasActivePlacement = false;
            ResetPlacementHotkeyState();
            return;
        }

        EnsureAirportLobbyDisplay();
        ProcessSpawnHotkey();
        MaintainSynchronizedLoopPlayback();
        UpdateActiveAudioFade();
        RefreshRuntimeLightingIfConfigChanged();

        if (Time.unscaledTime < _nextRefreshTime)
        {
            return;
        }

        _nextRefreshTime = Time.unscaledTime + RuntimeRefreshInterval;
        if (_activeDancer == null)
        {
            return;
        }

        if (_hasActivePlacement && HasPlacementConfigurationChanged())
        {
            ApplyPlacement(_activeDancer.transform, _activePlacement, _config, _loadedPrefab);
            UpdateShadowPresentation(_activeDancer);
            RecordAppliedPlacementConfiguration();
        }

        RefreshLivePlayback(_activeDancer);
    }

    private void LateUpdate()
    {
        if (_config == null || _logger == null)
        {
            return;
        }

        MaintainMorphPlaybackAfterAnimator();
    }

    private void HandleRuntimeLightingConfigChanged(object? sender, EventArgs eventArgs)
    {
        _pendingRuntimeLightingRefresh = true;
    }

    private void HandleColorConfigChanged(object? sender, EventArgs eventArgs)
    {
        if (_activeDancer == null || _config == null)
        {
            return;
        }

        // 切换随机颜色开关时，重新生成一组随机色
        if (sender == _config.RandomizeColors)
        {
            _config.RefreshRandomColors();
        }

        ReapplyAllMaterialColors();
    }

    private void ReapplyAllMaterialColors()
    {
        if (_activeDancer == null || _config == null)
        {
            return;
        }

        var renderers = _activeDancer.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
        {
            return;
        }

        LogVerbose(
            $"Reapplying material colors. randomize={_config.RandomizeColors.Value} " +
            $"hairEnabled={_config.HairColorEnabled.Value} " +
            $"hair=({_config.HairColorR.Value:0.###},{_config.HairColorG.Value:0.###},{_config.HairColorB.Value:0.###}) " +
            $"clothEnabled={_config.ClothColorEnabled.Value} " +
            $"cloth=({_config.ClothColorR.Value:0.###},{_config.ClothColorG.Value:0.###},{_config.ClothColorB.Value:0.###}) " +
            $"renderers={renderers.Length}.");

        foreach (var renderer in renderers)
        {
            ApplyLocalizedMaterialColors(renderer);
        }
    }

    private IEnumerator LoadAssetsRoutine()
    {
        if (_config == null || _logger == null || _isLoading || _loadedPrefab != null)
        {
            yield break;
        }

        _isLoading = true;
        var resolvedUnityBundlePath = _config.ResolveUnityBundlePath();
        var loadStartedAt = Time.realtimeSinceStartup;
        LogVerbose($"Starting Unity asset bundle load. bundlePath='{resolvedUnityBundlePath}'.");
        try
        {
            if (!File.Exists(resolvedUnityBundlePath))
            {
                _logger.LogError($"Required Unity asset bundle file was not found: '{resolvedUnityBundlePath}'.");
                yield break;
            }

            _loadedPrefab = _unityBundleLoader.Load(resolvedUnityBundlePath, _logger);
            DontDestroyOnLoad(_loadedPrefab.Template);
            LogVerbose(
                $"Using Unity asset bundle dancer source '{resolvedUnityBundlePath}'. loadDuration={(Time.realtimeSinceStartup - loadStartedAt):0.###}s.");
        }
        catch (Exception exception)
        {
            _logger.LogError($"Failed to load Unity asset bundle dancer source '{resolvedUnityBundlePath}': {exception}");
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void TrackSceneTransitions()
    {
        var sceneName = SceneManager.GetActiveScene().name;
        if (string.Equals(sceneName, _lastSceneName, StringComparison.Ordinal))
        {
            return;
        }

        _lastSceneName = sceneName;
        _loggedSceneHint = false;
        _nextRefreshTime = 0f;

        if (_activeDancer != null)
        {
            DestroyActiveDancer($"Scene changed to '{sceneName}'. The model must be placed again in the new scene.");
        }

        _hasActivePlacement = false;
        _isPausedForOfflineMenu = false;
        _isPausedForDistance = false;
        _audioPausedForPlaybackPause = false;
        ResetPlacementHotkeyState();
    }

    private void EnsureAirportLobbyDisplay()
    {
        if (_loadedPrefab == null || _activeDancer != null || _hasActivePlacement)
        {
            return;
        }

        if (!string.Equals(SceneManager.GetActiveScene().name, "Airport", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _activePlacement = ResolveAirportLobbyPlacement();
        _hasActivePlacement = true;

        if (!EnsureDancerInstance() || _activeDancer == null)
        {
            return;
        }

        ApplyPlacement(_activeDancer.transform, _activePlacement, _config!, _loadedPrefab);
        UpdateShadowPresentation(_activeDancer);
        RecordAppliedPlacementConfiguration();
        ConfigurePlayback(_activeDancer);
        LogVerbose(
            $"Spawned default Airport display model at {_activeDancer.transform.position}. " +
            $"rotation={_activeDancer.transform.rotation.eulerAngles}, scale={_activeDancer.transform.localScale}.");
    }

    private void ProcessSpawnHotkey()
    {
        if (_config == null || _logger == null)
        {
            return;
        }

        if (!_loggedSceneHint)
        {
            _loggedSceneHint = true;
            LogVerbose(
                $"Placement hotkey ready in scene '{SceneManager.GetActiveScene().name}'. " +
                $"Tap {_config.SpawnModelKey.Value} to spawn or move the model to the local player's ground position. " +
                $"Hold it for {SavePlacementHoldDurationSeconds:0.#} seconds in Airport to move the model and save the lobby showcase position into the config file for future launches.");
        }

        var shortcut = _config.SpawnModelKey.Value;
        if (shortcut != _lastObservedSpawnModelKey)
        {
            ResetPlacementHotkeyState();
            _lastObservedSpawnModelKey = shortcut;
            LogVerbose($"Updated placement hotkey binding to '{shortcut}'. Tap/hold behavior now follows this key.");
        }

        if (shortcut == KeyCode.None)
        {
            ResetPlacementHotkeyState();
            return;
        }

        if (Input.GetKeyDown(shortcut))
        {
            _placementHotkeyPressedAt = Time.unscaledTime;
            _placementHotkeyHoldConsumed = false;
        }

        if (Input.GetKey(shortcut))
        {
            if (!_placementHotkeyHoldConsumed
                && _placementHotkeyPressedAt >= 0f
                && Time.unscaledTime - _placementHotkeyPressedAt >= SavePlacementHoldDurationSeconds)
            {
                _placementHotkeyHoldConsumed = true;
                // 长按：移动到玩家脚下并保存（与轻按同一路径，额外写 cfg）。
                SpawnOrMoveToCurrentPlayer(savePlacement: true);
            }

            return;
        }

        if (!Input.GetKeyUp(shortcut))
        {
            return;
        }

        var shouldSpawnOrMove = !_placementHotkeyHoldConsumed;
        ResetPlacementHotkeyState();
        if (shouldSpawnOrMove)
        {
            SpawnOrMoveToCurrentPlayer();
        }
    }

    private void SpawnOrMoveToCurrentPlayer(bool savePlacement = false)
    {
        if (_config == null || _logger == null)
        {
            return;
        }

        if (savePlacement && !string.Equals(SceneManager.GetActiveScene().name, "Airport", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                $"Hold {_config.SpawnModelKey.Value} for {SavePlacementHoldDurationSeconds:0.#} seconds only in the Airport lobby to save the lobby showcase position.");
            return;
        }

        if (_loadedPrefab == null)
        {
            if (!_isLoading)
            {
                StartCoroutine(LoadAssetsRoutine());
            }

            _logger.LogWarning(_isLoading
                ? "The Unity asset bundle is still loading. Try the placement hotkey again in a moment."
                : "The model resources are loading now. Press the placement hotkey again in a moment.");
            return;
        }

        if (!TryResolveCurrentLocalPlayerPose(out var playerPosition, out var playerForward, out var ignoredRoot, out var source))
        {
            _logger.LogWarning("The local player transform is not available yet, so the model cannot be moved right now.");
            return;
        }

        var facingDirection = ResolveFacingDirection(playerForward);
        var placementSource = savePlacement ? $"{source}.SavedAirportDisplay" : source;
        _activePlacement = CreatePlayerGroundLockedPlacement(playerPosition, facingDirection, placementSource, ignoredRoot);
        _hasActivePlacement = true;

        if (savePlacement)
        {
            _config.SaveAirportPlacement(_activePlacement.Position, _activePlacement.Forward);
        }

        var createdNow = EnsureDancerInstance();
        if (_activeDancer == null)
        {
            return;
        }

        ApplyPlacement(_activeDancer.transform, _activePlacement, _config, _loadedPrefab);
        UpdateShadowPresentation(_activeDancer);
        RecordAppliedPlacementConfiguration();
        if (createdNow)
        {
            ConfigurePlayback(_activeDancer);
        }
        else
        {
            RefreshLivePlayback(_activeDancer);
            // 已存在实例：按生成键视为重新生成，刷新随机颜色并重新应用材质颜色。
            // 新建实例路径已在 EnsureDancerInstance -> NormalizeRendererMaterials 中刷新过，无需重复。
            if (_config != null && _config.RandomizeColors.Value)
            {
                _config.RefreshRandomColors();
                ReapplyAllMaterialColors();
            }
        }

        if (savePlacement)
        {
            _logger.LogInfo(
                $"Moved and saved Airport showcase placement to config. source={source}, groundedPosition={_activePlacement.Position}, " +
                $"forward={_activePlacement.Forward}, createdNow={createdNow}.");
        }

        LogVerbose(
            $"{(createdNow ? "Spawned" : "Moved")} display model in scene '{SceneManager.GetActiveScene().name}' (savePlacement={savePlacement}). " +
            $"source={source}, playerPosition={playerPosition}, groundedPosition={_activePlacement.Position}, " +
            $"modelPosition={_activeDancer.transform.position}, modelRotation={_activeDancer.transform.rotation.eulerAngles}, " +
            $"modelScale={_activeDancer.transform.localScale}.");
    }

    private bool EnsureDancerInstance()
    {
        if (_activeDancer != null || _loadedPrefab == null)
        {
            return false;
        }

        _activeDancer = Instantiate(_loadedPrefab.Template);
        _activeDancer.name = DancerObjectName;
        RemoveAllColliders(_activeDancer);
        ResetAudioPlaybackState();
        ClearResolvedPlacementPivot();
        CacheActiveRuntimeComponents(_activeDancer);
        LogVerbose(
            $"Using embedded Animator playback for '{_activeDancer.name}'. " +
            $"preBakedMorphClipAvailable={_loadedPrefab.MorphClip != null}.");

        NormalizeRendererMaterials(_activeDancer);
        ConfigureFacialExpressions(_activeDancer);
        _activeDancer.SetActive(true);
        return true;
    }

    private void DestroyActiveDancer(string reason)
    {
        if (_activeDancer == null)
        {
            return;
        }

        var audioSource = _activeAudioSource;
        if (audioSource != null && audioSource.isPlaying)
        {
            audioSource.Stop();
        }

        ClearActiveAudioFade(audioSource);
        Destroy(_activeDancer);
        _activeDancer = null;
        _activeShadowBlob = null;
        DestroyRuntimeLightingStabilizedMaterials();
        _originalMaterialColors.Clear();
        ClearAppliedPlacementConfiguration();
        ClearResolvedPlacementPivot();
        ClearActiveRuntimeComponents();
        ResetAudioPlaybackState();
        LogVerbose(reason);
    }

    private void ApplyPlacement(Transform dancerTransform, PlacementPose placement, DancePluginConfig config, LoadedDancePrefab? prefab)
    {
        var scale = config.ResolvedModelScale;
        var rotation = Quaternion.LookRotation(ResolveFacingDirection(placement.Forward), Vector3.up);
        var pivotLocalPosition = ResolvePlacementPivotLocalPosition(dancerTransform, prefab) * scale;

        dancerTransform.localScale = Vector3.one * scale;
        dancerTransform.rotation = rotation;
        dancerTransform.position = placement.Position - (rotation * pivotLocalPosition);
    }

    private Vector3 ResolvePlacementPivotLocalPosition(Transform dancerTransform, LoadedDancePrefab? prefab)
    {
        if (_hasResolvedPlacementPivot)
        {
            return _resolvedPlacementPivotLocalPosition;
        }

        var pivotY = prefab?.LocalMinY ?? 0f;
        if (TryResolvePlacementPivotFromAnimator(dancerTransform, pivotY, out var animatorPivot))
        {
            _resolvedPlacementPivotLocalPosition = animatorPivot;
            _hasResolvedPlacementPivot = true;
            return animatorPivot;
        }

        if (TryResolvePlacementPivotFromNamedBone(dancerTransform, pivotY, out var namedBonePivot))
        {
            _resolvedPlacementPivotLocalPosition = namedBonePivot;
            _hasResolvedPlacementPivot = true;
            return namedBonePivot;
        }

        var fallbackPivot = prefab == null
            ? new Vector3(0f, pivotY, 0f)
            : new Vector3(
            prefab.LocalBoundsCenter.x,
            pivotY,
            prefab.LocalBoundsCenter.z);
        _resolvedPlacementPivotLocalPosition = fallbackPivot;
        _hasResolvedPlacementPivot = true;
        return fallbackPivot;
    }

    private static PlacementPose CreateGroundLockedPlacement(Vector3 desiredPosition, Vector3 forward, string source)
    {
        var groundedPosition = desiredPosition;
        groundedPosition.y = ResolveGroundYExact(
            desiredPosition,
            desiredPosition.y + GroundProbeStartHeight,
            GroundProbeDistance,
            float.PositiveInfinity);
        return PlacementPose.Create(groundedPosition, forward, source);
    }

    private static PlacementPose CreatePlayerGroundLockedPlacement(Vector3 desiredPosition, Vector3 forward, string source, Transform? ignoredRoot)
    {
        var groundedPosition = desiredPosition;
        groundedPosition.y = ResolveGroundYNearPlayer(
            desiredPosition,
            desiredPosition.y + PlayerGroundProbeStartHeight,
            GroundProbeDistance,
            desiredPosition.y + PlayerGroundProbeMaxRise,
            ignoredRoot);
        return PlacementPose.Create(groundedPosition, forward, source);
    }

    private PlacementPose ResolveAirportLobbyPlacement()
    {
        if (_config != null && _config.TryGetSavedAirportPlacement(out var savedPosition, out var savedForward))
        {
            return CreateGroundLockedPlacement(savedPosition, savedForward, "AirportLobbyDisplay.Saved");
        }

        return CreateGroundLockedPlacement(AirportDisplayPosition, AirportDisplayForward, "AirportLobbyDisplay.Default");
    }

    private bool HasPlacementConfigurationChanged()
    {
        if (_config == null)
        {
            return false;
        }

        if (!_hasAppliedPlacementConfiguration)
        {
            return true;
        }

        return Mathf.Abs(_appliedModelScale - _config.ResolvedModelScale) > 0.0001f;
    }

    private void RecordAppliedPlacementConfiguration()
    {
        if (_config == null)
        {
            ClearAppliedPlacementConfiguration();
            return;
        }

        _hasAppliedPlacementConfiguration = true;
        _appliedModelScale = _config.ResolvedModelScale;
    }

    private void ClearAppliedPlacementConfiguration()
    {
        _hasAppliedPlacementConfiguration = false;
        _appliedModelScale = 0f;
    }

    private void ClearResolvedPlacementPivot()
    {
        _hasResolvedPlacementPivot = false;
        _resolvedPlacementPivotLocalPosition = Vector3.zero;
    }

    private void ResetPlacementHotkeyState()
    {
        _placementHotkeyPressedAt = -1f;
        _placementHotkeyHoldConsumed = false;
    }

    private bool TryResolvePlacementPivotFromAnimator(Transform dancerTransform, float pivotY, out Vector3 pivotLocalPosition)
    {
        var animator = dancerTransform.GetComponentInChildren<Animator>(includeInactive: true);
        if (animator != null && animator.isHuman)
        {
            try
            {
                var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                if (TryBuildPlacementPivotFromBone(dancerTransform, hips, pivotY, out pivotLocalPosition))
                {
                    LogVerbose($"Resolved placement pivot from humanoid hips bone '{hips!.name}' at local {pivotLocalPosition}.");
                    return true;
                }
            }
            catch (Exception exception)
            {
                LogVerbose($"Failed to resolve humanoid hips pivot: {exception.Message}");
            }
        }

        pivotLocalPosition = default;
        return false;
    }

    private bool TryResolvePlacementPivotFromNamedBone(Transform dancerTransform, float pivotY, out Vector3 pivotLocalPosition)
    {
        var transforms = dancerTransform.GetComponentsInChildren<Transform>(includeInactive: true);
        Transform? bestTransform = null;
        var bestScore = int.MinValue;

        foreach (var candidate in transforms)
        {
            if (candidate == null || ReferenceEquals(candidate, dancerTransform))
            {
                continue;
            }

            var score = ScorePlacementPivotCandidate(candidate.name);
            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            bestTransform = candidate;
        }

        if (bestTransform != null && TryBuildPlacementPivotFromBone(dancerTransform, bestTransform, pivotY, out pivotLocalPosition))
        {
            LogVerbose($"Resolved placement pivot from named bone '{bestTransform.name}' at local {pivotLocalPosition}.");
            return true;
        }

        pivotLocalPosition = default;
        return false;
    }

    private static bool TryBuildPlacementPivotFromBone(Transform dancerTransform, Transform? pivotBone, float pivotY, out Vector3 pivotLocalPosition)
    {
        if (pivotBone == null)
        {
            pivotLocalPosition = default;
            return false;
        }

        var localBonePosition = dancerTransform.InverseTransformPoint(pivotBone.position);
        pivotLocalPosition = new Vector3(localBonePosition.x, pivotY, localBonePosition.z);
        return true;
    }

    private static int ScorePlacementPivotCandidate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return int.MinValue;
        }

        var normalized = (name ?? string.Empty).Trim().ToLowerInvariant();
        var score = int.MinValue;
        for (var index = 0; index < PreferredPivotBoneTokens.Length; index++)
        {
            var token = PreferredPivotBoneTokens[index];
            if (string.Equals(normalized, token, StringComparison.Ordinal))
            {
                return 1000 - index;
            }

            if (normalized.IndexOf(token, StringComparison.Ordinal) >= 0)
            {
                score = Mathf.Max(score, 500 - index);
            }
        }

        return score;
    }

    private static Vector3 ResolveFacingDirection(Vector3 forward)
    {
        var planarForward = Vector3.ProjectOnPlane(forward, Vector3.up);
        return planarForward.sqrMagnitude > 0.01f
            ? planarForward.normalized
            : Vector3.forward;
    }

    private static bool TryResolveCurrentLocalPlayerPose(out Vector3 position, out Vector3 forward, out Transform? ignoredRoot, out string source)
    {
        if (TryGetLocalCharacterObject(out var localCharacter)
            && TryResolveCharacterPose(localCharacter!, "Character.localCharacter", out position, out forward, out ignoredRoot, out source))
        {
            return true;
        }

        if (TryGetLocalPlayerCharacterObject(out var playerCharacter)
            && TryResolveCharacterPose(playerCharacter!, "Player.localPlayer.character", out position, out forward, out ignoredRoot, out source))
        {
            return true;
        }

        position = default;
        forward = Vector3.forward;
        ignoredRoot = null;
        source = string.Empty;
        return false;
    }

    private static float ResolveGroundYExact(Vector3 desiredPosition, float startY, float maxDistance, float maxAcceptedY, Transform? ignoredRoot = null)
    {
        var probeOrigin = new Vector3(desiredPosition.x, startY, desiredPosition.z);
        var hits = Physics.RaycastAll(probeOrigin, Vector3.down, maxDistance, ~0, QueryTriggerInteraction.Ignore);
        if (hits.Length == 0)
        {
            return desiredPosition.y;
        }

        var validHits = hits
            .Where(hit => hit.collider != null)
            .Where(hit => hit.normal.y > 0.35f)
            .Where(hit => !ShouldIgnoreGroundHit(hit, ignoredRoot))
            .ToArray();

        if (validHits.Length == 0)
        {
            return desiredPosition.y;
        }

        if (!float.IsPositiveInfinity(maxAcceptedY))
        {
            var preferredHit = validHits
                .Where(hit => hit.point.y <= maxAcceptedY)
                .OrderByDescending(hit => hit.point.y)
                .ThenBy(hit => hit.distance)
                .FirstOrDefault();
            if (preferredHit.collider != null)
            {
                return preferredHit.point.y;
            }

            return desiredPosition.y;
        }

        var bestHit = validHits
            .OrderByDescending(hit => hit.point.y)
            .ThenBy(hit => hit.distance)
            .FirstOrDefault();
        return bestHit.collider == null ? desiredPosition.y : bestHit.point.y;
    }

    private static float ResolveGroundYNearPlayer(Vector3 desiredPosition, float startY, float maxDistance, float maxAcceptedY, Transform? ignoredRoot)
    {
        GroundSampleCandidate? bestPreferredCandidate = null;
        GroundSampleCandidate? bestAnyCandidate = null;

        for (var index = 0; index < PlayerGroundProbeSampleOffsets.Length; index++)
        {
            var samplePosition = desiredPosition + PlayerGroundProbeSampleOffsets[index];
            var candidate = TryResolveGroundCandidate(samplePosition, startY, maxDistance, maxAcceptedY, ignoredRoot);
            if (candidate == null)
            {
                continue;
            }

            if (bestAnyCandidate == null || candidate.Value.IsBetterThan(bestAnyCandidate.Value))
            {
                bestAnyCandidate = candidate;
            }

            if (desiredPosition.y - candidate.Value.Y > PlayerGroundProbePreferredMaxDrop)
            {
                continue;
            }

            if (bestPreferredCandidate == null || candidate.Value.IsBetterThan(bestPreferredCandidate.Value))
            {
                bestPreferredCandidate = candidate;
            }
        }

        if (bestPreferredCandidate != null)
        {
            return bestPreferredCandidate.Value.Y;
        }

        if (bestAnyCandidate != null && desiredPosition.y - bestAnyCandidate.Value.Y <= PlayerGroundProbeFallbackMaxDrop)
        {
            return bestAnyCandidate.Value.Y;
        }

        return desiredPosition.y;
    }

    private static GroundSampleCandidate? TryResolveGroundCandidate(Vector3 desiredPosition, float startY, float maxDistance, float maxAcceptedY, Transform? ignoredRoot)
    {
        var probeOrigin = new Vector3(desiredPosition.x, startY, desiredPosition.z);
        var hits = Physics.RaycastAll(probeOrigin, Vector3.down, maxDistance, ~0, QueryTriggerInteraction.Ignore);
        if (hits.Length == 0)
        {
            return null;
        }

        var candidates = hits
            .Where(hit => hit.collider != null)
            .Where(hit => hit.normal.y > 0.35f)
            .Where(hit => !ShouldIgnoreGroundHit(hit, ignoredRoot))
            .Where(hit => hit.point.y <= maxAcceptedY)
            .Select(hit => new GroundSampleCandidate(
                hit.point.y,
                Mathf.Abs(desiredPosition.y - hit.point.y),
                Vector2.Distance(new Vector2(desiredPosition.x, desiredPosition.z), new Vector2(hit.point.x, hit.point.z)),
                hit.distance))
            .OrderBy(hit => hit.VerticalDelta)
            .ThenBy(hit => hit.HorizontalDelta)
            .ThenBy(hit => hit.RaycastDistance)
            .ToArray();

        return candidates.Length == 0 ? null : candidates[0];
    }

    private static bool TryGetLocalCharacterObject(out Character? source)
    {
        source = Character.localCharacter;
        return source != null;
    }

    private static bool TryGetLocalPlayerCharacterObject(out Character? source)
    {
        source = Player.localPlayer?.character;
        return source != null;
    }

    private static bool TryResolveCharacterPose(Character source, string sourceName, out Vector3 position, out Vector3 forward, out Transform? ignoredRoot, out string resolvedSource)
    {
        resolvedSource = sourceName;
        // Unity 的 == null 判定已覆盖已销毁对象。
        // 注意不能用 Character.IsInitialized 做守卫：机场大厅里 CharacterStats 会被游戏主动禁用
        // （Awake: "isn't a gameplay scene ... Disabling self"），该属性在机场永远为 false。
        if (source == null)
        {
            position = default;
            forward = Vector3.forward;
            ignoredRoot = null;
            return false;
        }

        var transform = source.transform;
        ignoredRoot = transform;
        position = transform.position;
        var center = source.Center;
        position = new Vector3(center.x, position.y, center.z);
        resolvedSource = $"{sourceName}.CenterXZ";

        position = new Vector3(position.x, ResolveCharacterFootY(transform, position.y), position.z);
        resolvedSource = $"{resolvedSource}.FootY";
        forward = ResolvePreferredPlayerForward(transform, position, out var forwardSource);
        resolvedSource = $"{resolvedSource}.{forwardSource}";
        return true;
    }

    private static float ResolveCharacterFootY(Transform root, float fallbackY)
    {
        var hasColliderBounds = false;
        var resolvedFootY = float.PositiveInfinity;

        foreach (var collider in root.GetComponentsInChildren<Collider>(includeInactive: true))
        {
            if (collider == null || !collider.enabled || collider.isTrigger)
            {
                continue;
            }

            var candidateY = collider.bounds.min.y;
            if (float.IsNaN(candidateY) || float.IsInfinity(candidateY))
            {
                continue;
            }

            resolvedFootY = Mathf.Min(resolvedFootY, candidateY);
            hasColliderBounds = true;
        }

        if (!hasColliderBounds)
        {
            return fallbackY;
        }

        return resolvedFootY + PlayerColliderFootPadding;
    }

    private static bool ShouldIgnoreGroundHit(RaycastHit hit, Transform? ignoredRoot)
    {
        if (ignoredRoot == null || hit.collider == null)
        {
            return false;
        }

        if (IsSameOrChildTransform(hit.collider.transform, ignoredRoot))
        {
            return true;
        }

        var attachedBody = hit.collider.attachedRigidbody;
        return attachedBody != null && IsSameOrChildTransform(attachedBody.transform, ignoredRoot);
    }

    private static bool IsSameOrChildTransform(Transform? candidate, Transform root)
    {
        return candidate != null && (candidate == root || candidate.IsChildOf(root));
    }

    private static Vector3[] BuildPlayerGroundSampleOffsets()
    {
        var directions = new[]
        {
            new Vector2(1f, 0f),
            new Vector2(-1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(0f, -1f),
            new Vector2(1f, 1f).normalized,
            new Vector2(1f, -1f).normalized,
            new Vector2(-1f, 1f).normalized,
            new Vector2(-1f, -1f).normalized,
        };
        var radii = new[]
        {
            0.35f,
            0.7f,
            1.1f,
            1.6f,
            2.2f,
            PlayerGroundProbeSearchRadius,
        };

        var offsets = new List<Vector3>(1 + (directions.Length * radii.Length))
        {
            Vector3.zero,
        };

        foreach (var radius in radii)
        {
            for (var index = 0; index < directions.Length; index++)
            {
                var direction = directions[index];
                offsets.Add(new Vector3(direction.x * radius, 0f, direction.y * radius));
            }
        }

        return offsets.ToArray();
    }

    private static Vector3 ResolvePlanarForward(Transform transform)
    {
        var planarForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        return planarForward.sqrMagnitude > 0.01f
            ? planarForward.normalized
            : Vector3.forward;
    }

    private static Vector3 ResolvePreferredPlayerForward(Transform playerRoot, Vector3 playerPosition, out string source)
    {
        if (TryResolveCameraPlanarForward(playerPosition, out var cameraForward, out var cameraSource))
        {
            source = cameraSource;
            return cameraForward;
        }

        source = "TransformForward";
        return ResolvePlanarForward(playerRoot);
    }

    private static bool TryResolveCameraPlanarForward(Vector3 playerPosition, out Vector3 forward, out string source)
    {
        source = string.Empty;
        forward = Vector3.zero;

        var mainCamera = Camera.main;
        if (IsUsableGameplayCamera(mainCamera))
        {
            var mainForward = Vector3.ProjectOnPlane(mainCamera!.transform.forward, Vector3.up);
            if (mainForward.sqrMagnitude > 0.01f)
            {
                forward = mainForward.normalized;
                source = $"Camera:{mainCamera.name}";
                return true;
            }
        }

        Camera? bestCamera = null;
        var bestDistance = float.PositiveInfinity;
        foreach (var camera in Camera.allCameras)
        {
            if (!IsUsableGameplayCamera(camera))
            {
                continue;
            }

            var planarForward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up);
            if (planarForward.sqrMagnitude <= 0.01f)
            {
                continue;
            }

            var distance = Vector3.Distance(camera.transform.position, playerPosition);
            if (distance >= bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            bestCamera = camera;
        }

        if (bestCamera == null)
        {
            return false;
        }

        forward = Vector3.ProjectOnPlane(bestCamera.transform.forward, Vector3.up).normalized;
        source = $"Camera:{bestCamera.name}";
        return true;
    }

    private static bool IsUsableGameplayCamera(Camera? camera)
    {
        return camera != null
            && camera.enabled
            && camera.gameObject.activeInHierarchy
            && !camera.orthographic;
    }

    private static void RemoveAllColliders(GameObject root)
    {
        var colliders = root.GetComponentsInChildren<Collider>(includeInactive: true);
        foreach (var collider in colliders)
        {
            collider.enabled = false;
        }
    }

    private void NormalizeRendererMaterials(GameObject root)
    {
        if (_logger == null)
        {
            return;
        }

        // 每次生成模型时刷新随机颜色缓存（若开启随机颜色，覆盖 RGB 滑块的值）
        _config?.RefreshRandomColors();
        if (_config?.RandomizeColors.Value == true && _config.LastRandomColorSummary != null)
        {
            LogVerbose($"Randomized colors: {_config.LastRandomColorSummary}.");
        }

        var stabilizeLighting = _config?.StabilizeModelLighting.Value ?? true;
        var exposureCompensation = _config?.ResolvedLightingExposureCompensation ?? 0.82f;
        var stabilizedMaterialCache = new Dictionary<Material, Material>();
        var stabilizedMaterialCount = 0;

        foreach (var renderer in root.GetComponentsInChildren<Renderer>(includeInactive: true))
        {
            renderer.enabled = true;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = false;
            renderer.allowOcclusionWhenDynamic = true;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            var expectedCount = GetExpectedMaterialCount(renderer);
            if (expectedCount > 0)
            {
                var sharedMaterials = renderer.sharedMaterials ?? Array.Empty<Material>();
                if (sharedMaterials.Length != expectedCount || sharedMaterials.Any(material => material == null))
                {
                    renderer.sharedMaterials = BuildNormalizedRendererMaterialArray(sharedMaterials, expectedCount);
                    LogVerbose(
                        $"Normalized runtime materials for '{renderer.name}'. subMeshes={expectedCount}, materials={renderer.sharedMaterials.Length}.");
                }
            }

            if (stabilizeLighting
                && TryBuildLightingStabilizedMaterialArray(
                    renderer,
                    renderer.sharedMaterials ?? Array.Empty<Material>(),
                    stabilizedMaterialCache,
                    _runtimeLightingStabilizedMaterials,
                    _runtimeLightingMaterialSnapshots,
                    exposureCompensation,
                    out var stabilizedMaterials,
                    out var newStabilizedMaterialCount))
            {
                renderer.sharedMaterials = stabilizedMaterials;
                stabilizedMaterialCount += newStabilizedMaterialCount;
            }

            EnableShadowSupport(renderer.sharedMaterials);
            ApplyLocalizedMaterialColors(renderer);
            if (renderer is SkinnedMeshRenderer skinnedRenderer)
            {
                skinnedRenderer.updateWhenOffscreen = true;
                if (skinnedRenderer.sharedMesh != null)
                {
                    var meshBounds = skinnedRenderer.sharedMesh.bounds;
                    meshBounds.Expand(0.5f);
                    skinnedRenderer.localBounds = meshBounds;
                }
            }
        }

        if (stabilizedMaterialCount > 0)
        {
            LogVerbose(
                $"Applied runtime lighting stabilization to {stabilizedMaterialCount} material(s). " +
                $"exposureCompensation={exposureCompensation:0.###}.");
        }

        RecordAppliedRuntimeLightingConfiguration(stabilizeLighting, exposureCompensation);
    }

    private void ApplyLocalizedMaterialColors(Renderer renderer)
    {
        if (renderer == null)
        {
            return;
        }

        Color hairColor = Color.white;
        Color clothColor = Color.white;
        var hasHairColor = _config != null && _config.TryResolveHairColor(out hairColor);
        var hasClothColor = _config != null && _config.TryResolveClothColor(out clothColor);

        // 颜色未启用且从未染过色时，无需实例化材质，避免破坏光照稳定材质与曝光补偿的关联。
        if (!hasHairColor && !hasClothColor && _originalMaterialColors.Count == 0)
        {
            return;
        }

        var sharedMaterials = renderer.sharedMaterials;
        if (sharedMaterials == null || sharedMaterials.Length == 0)
        {
            return;
        }

        // 先扫描（不实例化），判断此 renderer 是否含有头发/服装材质。
        // 与启用状态无关：即使关闭了染色，也需要进入实例化路径以恢复原色。
        var anyNameMatched = false;
        foreach (var material in sharedMaterials)
        {
            if (material == null)
            {
                continue;
            }

            var normalizedName = NormalizeMaterialName(material.name, renderer.name);
            if (IsHairMaterialName(normalizedName) || IsClothMaterialName(normalizedName))
            {
                anyNameMatched = true;
                break;
            }
        }

        if (!anyNameMatched)
        {
            // 详细日志：输出所有材质名，便于核对匹配规则
            if (Core.VerboseLogState.Enabled)
            {
                _logger?.LogInfo($"No material matched on '{renderer.name}' ({sharedMaterials.Length} materials). Listing all:");
                for (var i = 0; i < sharedMaterials.Length; i++)
                {
                    if (sharedMaterials[i] == null)
                    {
                        continue;
                    }

                    _logger?.LogInfo($"  [{i}] '{sharedMaterials[i].name}'");
                }
            }

            return;
        }

        // 颜色未启用但此前染过色：需要恢复原色，继续实例化。
        // 颜色已启用：需要染色，继续实例化。

        var beforeMaterials = renderer.sharedMaterials;
        var instances = renderer.materials;
        var appliedCount = 0;
        var restoredCount = 0;
        foreach (var material in instances)
        {
            if (material == null)
            {
                continue;
            }

            var normalizedName = NormalizeMaterialName(material.name, renderer.name);
            var isHairName = IsHairMaterialName(normalizedName);
            var isClothName = IsClothMaterialName(normalizedName);
            if (!isHairName && !isClothName)
            {
                continue;
            }

            // 首次遇到该材质时缓存原色（此时材质应处于原色或仅经光照稳定后的状态）。
            // 后续切换开关时，可从该快照恢复原色。
            if (!_originalMaterialColors.ContainsKey(material))
            {
                _originalMaterialColors[material] = ReadMaterialColor(material);
            }

            var originalColor = _originalMaterialColors[material];
            if (isHairName)
            {
                if (hasHairColor)
                {
                    SetMaterialColor(material, hairColor);
                    appliedCount++;
                }
                else
                {
                    SetMaterialColor(material, originalColor);
                    restoredCount++;
                }
            }
            else // isClothName
            {
                if (hasClothColor)
                {
                    SetMaterialColor(material, clothColor);
                    appliedCount++;
                }
                else
                {
                    SetMaterialColor(material, originalColor);
                    restoredCount++;
                }
            }
        }

        renderer.sharedMaterials = instances;
        // 实例化可能产生新材质引用，需要将稳定材质列表和快照迁移到新实例，
        // 否则曝光补偿修改的是旧引用而非显示的实例，导致曝光补偿失效。
        MigrateStabilizedMaterialReferences(beforeMaterials, instances);
        if (appliedCount > 0 || restoredCount > 0)
        {
            LogVerbose(
                $"Applied localized material colors on '{renderer.name}'. applied={appliedCount} restored={restoredCount} " +
                $"hair={(hasHairColor ? hairColor : default)} cloth={(hasClothColor ? clothColor : default)}.");
        }

        // 详细日志：输出每个材质的匹配结果（命中或未命中），便于核对规则覆盖情况
        if (Core.VerboseLogState.Enabled)
        {
            for (var i = 0; i < instances.Length; i++)
            {
                if (instances[i] == null)
                {
                    continue;
                }

                var normalized = NormalizeMaterialName(instances[i].name, renderer.name);
                var isHairName = IsHairMaterialName(normalized);
                var isClothName = IsClothMaterialName(normalized);
                string tag;
                if (isHairName)
                {
                    tag = hasHairColor ? "HAIR" : "HAIR(off,restored)";
                }
                else if (isClothName)
                {
                    tag = hasClothColor ? "CLOTH" : "CLOTH(off,restored)";
                }
                else
                {
                    tag = "SKIP";
                }
                _logger?.LogInfo($"  [{i}] '{instances[i].name}' -> {tag}");
            }
        }
    }

    private static Color ReadMaterialColor(Material material)
    {
        if (material.HasProperty("_BaseColor"))
        {
            return material.GetColor("_BaseColor");
        }

        if (material.HasProperty("_Color"))
        {
            return material.GetColor("_Color");
        }

        return Color.white;
    }

    // renderer.materials 实例化材质后，稳定材质列表和快照字典中的旧引用会失效。
    // 此方法将旧引用的快照迁移到新实例，使曝光补偿能继续作用于显示的材质。
    private void MigrateStabilizedMaterialReferences(Material[] beforeMaterials, Material[] afterMaterials)
    {
        if (beforeMaterials == null || afterMaterials == null || _runtimeLightingStabilizedMaterials.Count == 0)
        {
            return;
        }

        var count = Mathf.Min(beforeMaterials.Length, afterMaterials.Length);
        for (var i = 0; i < count; i++)
        {
            var before = beforeMaterials[i];
            var after = afterMaterials[i];
            if (before == null || after == null || ReferenceEquals(before, after))
            {
                continue;
            }

            if (_runtimeLightingMaterialSnapshots.TryGetValue(before, out var snapshot))
            {
                _runtimeLightingStabilizedMaterials.Remove(before);
                _runtimeLightingStabilizedMaterials.Add(after);
                _runtimeLightingMaterialSnapshots[after] = snapshot;
                _runtimeLightingMaterialSnapshots.Remove(before);
            }

            if (_originalMaterialColors.TryGetValue(before, out var originalColor))
            {
                _originalMaterialColors[after] = originalColor;
                _originalMaterialColors.Remove(before);
            }
        }
    }

    private static string NormalizeMaterialName(string materialName, string rendererName)
    {
        var combined = (materialName ?? string.Empty) + "|" + (rendererName ?? string.Empty);
        var runtimeIndex = combined.IndexOf("_Runtime", StringComparison.Ordinal);
        if (runtimeIndex >= 0)
        {
            combined = combined.Substring(0, runtimeIndex) + combined.Substring(runtimeIndex + "_Runtime".Length);
        }

        return combined;
    }

    private static bool IsHairMaterialName(string normalizedName)
    {
        // Tails/TailS 在此模型中指马尾辫，归入头发
        return normalizedName.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0
            || normalizedName.IndexOf("Tails", StringComparison.OrdinalIgnoreCase) >= 0
            || normalizedName.IndexOf("TailS", StringComparison.OrdinalIgnoreCase) >= 0
            || normalizedName.IndexOf("头发", StringComparison.Ordinal) >= 0
            || normalizedName.IndexOf("前髪", StringComparison.Ordinal) >= 0
            || normalizedName.IndexOf("後髪", StringComparison.Ordinal) >= 0
            || normalizedName.IndexOf("後ろ髪", StringComparison.Ordinal) >= 0;
    }

    private static bool IsClothMaterialName(string normalizedName)
    {
        // 注意：Sock 会误匹配 EyeSocket，已移除；Tails 已移至头发
        // 头饰/袖边类装饰（Ribbon 缎带、Bow_ 蝴蝶结、Frill_ 荷叶边、Buttons_ 纽扣）跟随服装颜色
        // 打底裤（Bloomers）保持原色，不参与服装染色
        return normalizedName.IndexOf("Cloth", StringComparison.OrdinalIgnoreCase) >= 0
            || normalizedName.IndexOf("Dress", StringComparison.OrdinalIgnoreCase) >= 0
            || normalizedName.IndexOf("Shirt", StringComparison.OrdinalIgnoreCase) >= 0
            || normalizedName.IndexOf("服装", StringComparison.Ordinal) >= 0
            || normalizedName.IndexOf("衣服", StringComparison.Ordinal) >= 0
            || normalizedName.IndexOf("上着", StringComparison.Ordinal) >= 0
            || normalizedName.IndexOf("Skirt", StringComparison.OrdinalIgnoreCase) >= 0
            || normalizedName.IndexOf("裙子", StringComparison.Ordinal) >= 0
            || normalizedName.IndexOf("Sock_", StringComparison.OrdinalIgnoreCase) >= 0
            || normalizedName.IndexOf("Bow_", StringComparison.OrdinalIgnoreCase) >= 0
            || normalizedName.IndexOf("Frill_", StringComparison.OrdinalIgnoreCase) >= 0
            || normalizedName.IndexOf("Buttons_", StringComparison.OrdinalIgnoreCase) >= 0
            || normalizedName.IndexOf("Ribbon", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void SetMaterialColor(Material material, Color color)
    {
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
    }

    private static Material[] BuildNormalizedRendererMaterialArray(Material[] sharedMaterials, int expectedCount)
    {
        var normalizedMaterials = new Material[Mathf.Max(1, expectedCount)];
        var lastResolvedMaterial = sharedMaterials.FirstOrDefault(material => material != null)
            ?? CreateFallbackRendererMaterial();

        for (var index = 0; index < normalizedMaterials.Length; index++)
        {
            var currentMaterial = index < sharedMaterials.Length
                ? sharedMaterials[index]
                : null;
            if (currentMaterial != null)
            {
                normalizedMaterials[index] = currentMaterial;
                lastResolvedMaterial = currentMaterial;
                continue;
            }

            normalizedMaterials[index] = lastResolvedMaterial;
        }

        return normalizedMaterials;
    }

    private static bool TryBuildLightingStabilizedMaterialArray(
        Renderer renderer,
        Material[] sharedMaterials,
        Dictionary<Material, Material> materialCache,
        List<Material> runtimeMaterials,
        Dictionary<Material, RuntimeLightingMaterialSnapshot> runtimeMaterialSnapshots,
        float exposureCompensation,
        out Material[] stabilizedMaterials,
        out int newStabilizedMaterialCount)
    {
        stabilizedMaterials = sharedMaterials;
        newStabilizedMaterialCount = 0;
        if (renderer == null || sharedMaterials == null || sharedMaterials.Length == 0)
        {
            return false;
        }

        Material[]? result = null;
        for (var index = 0; index < sharedMaterials.Length; index++)
        {
            var sourceMaterial = sharedMaterials[index];
            if (sourceMaterial == null || !IsLightingSensitiveMaterial(sourceMaterial))
            {
                continue;
            }

            if (!materialCache.TryGetValue(sourceMaterial, out var stabilizedMaterial))
            {
                stabilizedMaterial = CreateLightingStabilizedMaterial(sourceMaterial, exposureCompensation, out var snapshot);
                materialCache[sourceMaterial] = stabilizedMaterial;
                runtimeMaterials.Add(stabilizedMaterial);
                runtimeMaterialSnapshots[stabilizedMaterial] = snapshot;
                newStabilizedMaterialCount++;
            }

            result ??= (Material[])sharedMaterials.Clone();
            result[index] = stabilizedMaterial;
        }

        if (result == null)
        {
            return false;
        }

        stabilizedMaterials = result;
        return true;
    }

    private static bool IsLightingSensitiveMaterial(Material material)
    {
        if (material == null || material.shader == null)
        {
            return false;
        }

        var shaderName = material.shader.name ?? string.Empty;
        if (string.IsNullOrWhiteSpace(shaderName))
        {
            return HasAnyMaterialProperty(material, LightingSensitiveMaterialProperties);
        }

        var lowerShaderName = shaderName.ToLowerInvariant();
        if (LightingNeutralShaderTokens.Any(token => lowerShaderName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
            && !lowerShaderName.Contains("toon"))
        {
            return false;
        }

        return LightingSensitiveShaderTokens.Any(token => lowerShaderName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
               || HasAnyMaterialProperty(material, LightingSensitiveMaterialProperties);
    }

    private static bool IsSkinMaterialName(string materialName)
    {
        var name = materialName ?? string.Empty;
        return name.IndexOf("Skin", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("皮肤", StringComparison.Ordinal) >= 0;
    }

    // 曝光补偿影响范围：皮肤、脸部、脖子、下巴等皮肤质地材质。
    private static bool IsExposureAffectedMaterialName(string materialName)
    {
        var name = materialName ?? string.Empty;
        return name.IndexOf("Skin", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("皮肤", StringComparison.Ordinal) >= 0
            || name.IndexOf("Face", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("脸", StringComparison.Ordinal) >= 0
            || name.IndexOf("Neck", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("脖子", StringComparison.Ordinal) >= 0
            || name.IndexOf("Jaw", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("下巴", StringComparison.Ordinal) >= 0;
    }

    private static Material CreateLightingStabilizedMaterial(
        Material sourceMaterial,
        float exposureCompensation,
        out RuntimeLightingMaterialSnapshot snapshot)
    {
        var material = new Material(sourceMaterial)
        {
            name = string.IsNullOrWhiteSpace(sourceMaterial.name)
                ? "MikuLightSafeMaterial"
                : $"{sourceMaterial.name}_LightSafe",
        };

        var exposure = Mathf.Clamp(exposureCompensation, 0.4f, 1f);
        snapshot = RuntimeLightingMaterialSnapshot.Capture(material, RuntimeLightingExposureColorProperties);

        // 曝光补偿只影响皮肤与脸部材质，其他材质保持原色
        if (IsExposureAffectedMaterialName(sourceMaterial.name))
        {
            ApplyLightingExposure(material, snapshot, exposure);
        }

        ClampColorPropertyMax(material, "_Ambient", 0.22f);
        ClampColorPropertyMax(material, "_IndirectLightMinColor", 0.06f);
        ClampColorPropertyMax(material, "_SpecColor", 0.18f);
        ClampColorPropertyMax(material, "_EmissionColor", 0.45f);

        SetFloatIfPresent(material, "_Metallic", 0f);
        ClampFloatPropertyMax(material, "_Smoothness", 0.18f);
        ClampFloatPropertyMax(material, "_Glossiness", 0.18f);
        SetFloatIfPresent(material, "_SpecularHighlights", 0f);
        SetFloatIfPresent(material, "_EnvironmentReflections", 0f);
        SetFloatIfPresent(material, "_ReceiveShadowMappingAmount", 0.9f);
        ClampFloatPropertyMin(material, "_ShadowLum", 1.35f);

        // 皮肤材质额外关闭环境光遮蔽（自阴影）和平滑度，使其更不易受游戏光照干扰
        if (IsSkinMaterialName(sourceMaterial.name))
        {
            SetFloatIfPresent(material, "_Smoothness", 0f);
            SetFloatIfPresent(material, "_Glossiness", 0f);
            SetFloatIfPresent(material, "_OcclusionStrength", 0f);
        }

        if (material.HasProperty("_ToonTone"))
        {
            var toonTone = material.GetVector("_ToonTone");
            toonTone.y = Mathf.Min(toonTone.y, 0.42f);
            toonTone.z = Mathf.Min(toonTone.z, 0.45f);
            material.SetVector("_ToonTone", toonTone);
        }

        if (material.HasProperty("_SphereMapMode") && material.GetFloat("_SphereMapMode") > 1.5f)
        {
            material.SetFloat("_SphereMapMode", 1f);
        }

        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        return material;
    }

    private void RefreshRuntimeLightingIfConfigChanged()
    {
        if (_config == null)
        {
            return;
        }

        var stabilizeLighting = _config.StabilizeModelLighting.Value;
        var exposureCompensation = _config.ResolvedLightingExposureCompensation;
        if (!_pendingRuntimeLightingRefresh
            && _lastAppliedStabilizeModelLighting == stabilizeLighting
            && Mathf.Approximately(_lastAppliedLightingExposureCompensation, exposureCompensation))
        {
            return;
        }

        _pendingRuntimeLightingRefresh = false;
        if (_activeDancer == null)
        {
            RecordAppliedRuntimeLightingConfiguration(stabilizeLighting, exposureCompensation);
            return;
        }

        if (stabilizeLighting)
        {
            if (_runtimeLightingStabilizedMaterials.Count == 0)
            {
                NormalizeRendererMaterials(_activeDancer);
                LogVerbose(
                    $"Runtime lighting stabilization enabled from config without respawning model. " +
                    $"exposureCompensation={exposureCompensation:0.###}.");
                return;
            }

            ApplyRuntimeLightingExposure(exposureCompensation);
            // 曝光补偿用快照重写了材质颜色，会覆盖头发/服装染色，需要重新应用。
            ReapplyAllMaterialColors();
        }
        else
        {
            ApplyRuntimeLightingExposure(1f);
            ReapplyAllMaterialColors();
        }

        RecordAppliedRuntimeLightingConfiguration(stabilizeLighting, exposureCompensation);
        LogVerbose(
            $"Applied live runtime lighting config. stabilizeLighting={stabilizeLighting}, " +
            $"exposureCompensation={exposureCompensation:0.###}, activeMaterials={_runtimeLightingStabilizedMaterials.Count}.");
    }

    private void ApplyRuntimeLightingExposure(float exposureCompensation)
    {
        var exposure = Mathf.Clamp(exposureCompensation, 0.4f, 1f);
        foreach (var material in _runtimeLightingStabilizedMaterials)
        {
            if (material == null || !_runtimeLightingMaterialSnapshots.TryGetValue(material, out var snapshot))
            {
                continue;
            }

            // 曝光补偿只影响皮肤与脸部材质，其他材质保持原色
            if (!IsExposureAffectedMaterialName(material.name))
            {
                continue;
            }

            ApplyLightingExposure(material, snapshot, exposure);
        }
    }

    private static void ApplyLightingExposure(Material material, RuntimeLightingMaterialSnapshot snapshot, float exposure)
    {
        foreach (var propertyName in RuntimeLightingExposureColorProperties)
        {
            if (!material.HasProperty(propertyName) || !snapshot.TryGetColor(propertyName, out var color))
            {
                continue;
            }

            material.SetColor(
                propertyName,
                new Color(
                    Mathf.Clamp01(color.r * exposure),
                    Mathf.Clamp01(color.g * exposure),
                    Mathf.Clamp01(color.b * exposure),
                    color.a));
        }
    }

    private void RecordAppliedRuntimeLightingConfiguration(bool stabilizeLighting, float exposureCompensation)
    {
        _lastAppliedStabilizeModelLighting = stabilizeLighting;
        _lastAppliedLightingExposureCompensation = Mathf.Clamp(exposureCompensation, 0.4f, 1f);
    }

    private static bool HasAnyMaterialProperty(Material material, IEnumerable<string> propertyNames)
    {
        return propertyNames.Any(material.HasProperty);
    }

    private static void ClampColorPropertyMax(Material material, string propertyName, float maxChannel)
    {
        if (!material.HasProperty(propertyName))
        {
            return;
        }

        maxChannel = Mathf.Clamp01(maxChannel);
        var color = material.GetColor(propertyName);
        material.SetColor(
            propertyName,
            new Color(
                Mathf.Min(color.r, maxChannel),
                Mathf.Min(color.g, maxChannel),
                Mathf.Min(color.b, maxChannel),
                color.a));
    }

    private static void SetFloatIfPresent(Material material, string propertyName, float value)
    {
        if (material.HasProperty(propertyName))
        {
            material.SetFloat(propertyName, value);
        }
    }

    private static void ClampFloatPropertyMax(Material material, string propertyName, float maxValue)
    {
        if (!material.HasProperty(propertyName))
        {
            return;
        }

        material.SetFloat(propertyName, Mathf.Min(material.GetFloat(propertyName), maxValue));
    }

    private static void ClampFloatPropertyMin(Material material, string propertyName, float minValue)
    {
        if (!material.HasProperty(propertyName))
        {
            return;
        }

        material.SetFloat(propertyName, Mathf.Max(material.GetFloat(propertyName), minValue));
    }

    private static Material CreateFallbackRendererMaterial()
    {
        var fallbackShader = Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Unlit/Texture")
            ?? Shader.Find("Sprites/Default")
            ?? Shader.Find("Universal Render Pipeline/Lit")
            ?? Shader.Find("Standard")
            ?? Shader.Find("Legacy Shaders/Diffuse");
        return fallbackShader != null ? new Material(fallbackShader) : null!;
    }

    private void DestroyRuntimeLightingStabilizedMaterials()
    {
        if (_runtimeLightingStabilizedMaterials.Count == 0)
        {
            _runtimeLightingMaterialSnapshots.Clear();
            return;
        }

        for (var index = 0; index < _runtimeLightingStabilizedMaterials.Count; index++)
        {
            var material = _runtimeLightingStabilizedMaterials[index];
            if (material != null)
            {
                Destroy(material);
            }
        }

        _runtimeLightingStabilizedMaterials.Clear();
        _runtimeLightingMaterialSnapshots.Clear();
    }

    private static int GetExpectedMaterialCount(Renderer renderer)
    {
        if (renderer is SkinnedMeshRenderer skinnedRenderer && skinnedRenderer.sharedMesh != null)
        {
            return Mathf.Max(1, skinnedRenderer.sharedMesh.subMeshCount);
        }

        if (renderer.TryGetComponent<MeshFilter>(out var meshFilter) && meshFilter.sharedMesh != null)
        {
            return Mathf.Max(1, meshFilter.sharedMesh.subMeshCount);
        }

        return Mathf.Max(1, renderer.sharedMaterials?.Length ?? 0);
    }

    private void UpdateShadowPresentation(GameObject root)
    {
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(includeInactive: true))
        {
            if (_activeShadowBlob != null && renderer.gameObject == _activeShadowBlob)
            {
                continue;
            }

            renderer.enabled = true;
            // 使用 Unity 标准阴影投射，与游戏原人物效果一致
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = false;
            renderer.allowOcclusionWhenDynamic = true;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            EnableShadowSupport(renderer.sharedMaterials);
        }

        // 移除假 shadow blob，改用 Unity 标准方向光阴影
        DestroyActiveShadowBlob();
    }

    private void DestroyActiveShadowBlob()
    {
        if (_activeShadowBlob == null)
        {
            return;
        }

        LogVerbose($"Destroying shadow blob '{_activeShadowBlob.name}' (replaced by Unity standard shadows).");
        Destroy(_activeShadowBlob);
        _activeShadowBlob = null;
    }

    private static void EnableShadowSupport(Material[]? materials)
    {
        if (materials == null)
        {
            return;
        }

        foreach (var material in materials)
        {
            EnableShadowSupport(material);
        }
    }

    private static void EnableShadowSupport(Material? material)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_ReceiveShadows"))
        {
            material.SetFloat("_ReceiveShadows", 1f);
        }

        if (material.HasProperty("_CastShadows"))
        {
            material.SetFloat("_CastShadows", 1f);
        }

        material.SetShaderPassEnabled("ShadowCaster", true);
    }

    private sealed class RuntimeLightingMaterialSnapshot
    {
        private readonly Dictionary<string, Color> _colors = new();

        private RuntimeLightingMaterialSnapshot()
        {
        }

        public static RuntimeLightingMaterialSnapshot Capture(Material material, IEnumerable<string> propertyNames)
        {
            var snapshot = new RuntimeLightingMaterialSnapshot();
            foreach (var propertyName in propertyNames)
            {
                if (material.HasProperty(propertyName))
                {
                    snapshot._colors[propertyName] = material.GetColor(propertyName);
                }
            }

            return snapshot;
        }

        public bool TryGetColor(string propertyName, out Color color)
        {
            return _colors.TryGetValue(propertyName, out color);
        }
    }

    private void ConfigurePlayback(GameObject dancer)
    {
        CacheActiveRuntimeComponents(dancer);
        var audioSource = _activeAudioSource;
        var audioClip = ResolveAudioClip(audioSource);
        var animator = _activeAnimator;
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            ConfigureAudioSource(audioSource, audioClip);
            var primaryClip = ResolveSynchronizedAnimatorClip(animator);
            var animationSpeed = ResolveSynchronizedAnimatorSpeed(primaryClip, audioClip);
            var useNativeFacialAnimation = HasNativeFacialAnimationControl(dancer);

            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.applyRootMotion = false;
            animator.updateMode = AnimatorUpdateMode.Normal;
            animator.speed = animationSpeed;
            animator.Rebind();
            animator.Update(0f);
            StartAnimatorAtBeginning(animator, primaryClip);
            if (useNativeFacialAnimation)
            {
                StopRuntimeMorphPlayback();
            }
            else
            {
                StartMorphPlayback(dancer);
            }

            ConfigureSecondaryNativeAnimators(primaryClip, animationSpeed);
            ApplyActiveAnimationPlaybackSpeed(audioClip);
            var audioStarted = audioSource != null
                && audioClip != null
                && SynchronizeAudioWithAnimator(
                    animator,
                    audioSource,
                    audioClip,
                    primaryClip,
                    startIfNeeded: true,
                    allowFadeOnStart: true,
                    suppressFurtherRetries: true,
                    logCorrections: false);
            LogVerbose(
                $"Animator configured for '{dancer.name}', controller='{animator.runtimeAnimatorController.name}', avatar='{animator.avatar?.name ?? "(none)"}', " +
                $"clip='{primaryClip?.name ?? "(none)"}', clipLength={primaryClip?.length ?? 0f:0.###}, " +
                $"audioEnabled={_config?.EnableAudio.Value ?? false}, audioClip='{audioClip?.name ?? "(none)"}', audioLength={audioClip?.length ?? 0f:0.###}, " +
                $"audioSegmentStart={ResolveAudioSegmentStartSeconds(primaryClip, audioClip):0.###}, audioSegmentLength={ResolveAudioSegmentDuration(primaryClip, audioClip):0.###}, " +
                $"animatorSpeed={animationSpeed:0.###}, audioVolume={_config?.ResolvedAudioVolume ?? 0f:0.###}, audioRange={_config?.ResolvedAudioRangeMeters ?? 0f:0.###}, audioStarted={audioStarted}, audioIsPlaying={audioSource?.isPlaying ?? false}.");
            return;
        }

        var animation = _activeAnimation;
        if (animation != null && _loadedPrefab?.Clip != null)
        {
            animation.cullingType = AnimationCullingType.AlwaysAnimate;
            if (animation.GetClip(_loadedPrefab.Clip.name) == null)
            {
                animation.AddClip(_loadedPrefab.Clip, _loadedPrefab.Clip.name);
            }

            animation.clip = _loadedPrefab.Clip;
            animation.wrapMode = WrapMode.Loop;
            animation.Stop();
            animation.Rewind();
            animation.Play(_loadedPrefab.Clip.name);
            ConfigureAudioSource(audioSource, audioClip);
            ApplyActiveAnimationPlaybackSpeed(audioClip);
            if (HasNativeFacialAnimationControl(dancer))
            {
                StopRuntimeMorphPlayback();
            }
            StartAudioIfEnabled(audioSource, audioClip, _loadedPrefab.Clip, suppressFurtherRetries: true);
            LogVerbose($"Animation configured for '{dancer.name}', clip='{_loadedPrefab.Clip.name}', isPlaying={animation.isPlaying}.");
            return;
        }

        if (HasNativeFacialAnimationControl(dancer))
        {
            StopRuntimeMorphPlayback();
            return;
        }

        StartMorphPlayback(dancer);
    }

    private void ConfigureFacialExpressions(GameObject dancer)
    {
        CacheActiveRuntimeComponents(dancer);

        if (HasNativeFacialAnimationControl(dancer))
        {
            var existingFallbackController = dancer.GetComponent<MikuFacialExpressionController>();
            if (existingFallbackController != null)
            {
                existingFallbackController.enabled = false;
            }

            StopRuntimeMorphPlayback();
            LogVerbose($"Skipping runtime facial expression playback for '{dancer.name}' because Unity-native facial animation control was detected.");
            return;
        }

        if (_loadedPrefab?.MorphClip != null)
        {
            var existingController = dancer.GetComponent<MikuFacialExpressionController>();
            if (existingController != null)
            {
                existingController.enabled = false;
            }

            LogVerbose($"Skipping fallback facial expression controller for '{dancer.name}' because a bundled morph clip is available.");
            return;
        }

        if (_loadedPrefab?.Source == LoadedPrefabSource.UnityAssetBundle
            && _activeAnimator != null
            && _activeAnimator.runtimeAnimatorController != null
            && _activePrimaryAnimatorClip != null)
        {
            LogVerbose(
                $"Unity AssetBundle Animator clip '{_activePrimaryAnimatorClip.name}' is active on '{dancer.name}', but no morph bridge is available. Falling back to simplified facial controller.");
        }

        var controller = dancer.GetComponent<MikuFacialExpressionController>() ?? dancer.AddComponent<MikuFacialExpressionController>();
        controller.Initialize(_activeAudioSource, _activeAnimator, _logger);
    }

    private void RefreshLivePlayback(GameObject dancer)
    {
        CacheActiveRuntimeComponents(dancer);
        var animator = _activeAnimator;
        var audioSource = _activeAudioSource;
        var audioClip = ResolveAudioClip(audioSource);
        ConfigureAudioSource(audioSource, audioClip);
        HandleAudioToggleState();
        if (HandlePlaybackPause(dancer, animator, audioSource, audioClip))
        {
            return;
        }

        // 循环间隔暂停期间（动画冻结、音频已停止），不要恢复动画速度或重启音频。
        // 否则会覆盖 BeginLoopInterval 设置的 animator.speed=0f 并在暂停期间播放音频。
        if (_isInLoopInterval)
        {
            return;
        }

        if (animator != null && animator.runtimeAnimatorController != null)
        {
            var primaryClip = ResolveSynchronizedAnimatorClip(animator);
            animator.speed = ResolveSynchronizedAnimatorSpeed(primaryClip, audioClip);
            ForceAnimatorLayerWeights(animator, 1f);
            RefreshSecondaryNativeAnimators(animator, primaryClip);
            ApplyActiveAnimationPlaybackSpeed(audioClip);

            if (_config?.EnableAudio.Value == true && audioSource != null && audioClip != null && !_audioRetrySuppressed)
            {
                var audioWasPlaying = audioSource.isPlaying;
                if (SynchronizeAudioWithAnimator(
                    animator,
                    audioSource,
                    audioClip,
                    primaryClip,
                    startIfNeeded: true,
                    allowFadeOnStart: true,
                    suppressFurtherRetries: true,
                    logCorrections: false) && !audioWasPlaying)
                {
                    LogVerbose($"Audio was enabled while the model was already active. Aligned synchronized playback with clip '{audioClip.name}'.");
                }
            }

            return;
        }

        if (_config?.EnableAudio.Value == true && audioSource != null && audioClip != null && !audioSource.isPlaying && !_audioRetrySuppressed)
        {
            StartAudioIfEnabled(audioSource, audioClip, _loadedPrefab?.Clip, suppressFurtherRetries: true);
        }
    }

    private void HandleAudioToggleState()
    {
        var audioEnabled = _config?.EnableAudio.Value == true;
        if (audioEnabled != _lastAudioEnabledState)
        {
            _audioRetrySuppressed = false;
            _lastAudioEnabledState = audioEnabled;
        }
    }

    private void ResetAudioPlaybackState()
    {
        _audioRetrySuppressed = false;
        _lastAudioEnabledState = _config?.EnableAudio.Value == true;
        _animatorSegmentLoopIndex = 0;
        _lastAnimatorLoopIndex = -1;
        _lastMorphLoopIndex = -1;
        _isInLoopInterval = false;
        _loopIntervalEndTime = -1f;
        _isPausedForOfflineMenu = false;
        _isPausedForDistance = false;
        _audioPausedForPlaybackPause = false;
        ClearActiveAudioFade();
    }

    private void ConfigureAudioSource(AudioSource? audioSource, AudioClip? audioClip)
    {
        if (audioSource == null)
        {
            return;
        }

        if (audioClip != null && audioSource.clip == null)
        {
            audioSource.clip = audioClip;
        }

        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.mute = _config?.EnableAudio.Value != true;
        if (!IsActiveAudioFade(audioSource))
        {
            audioSource.volume = _config?.ResolvedAudioVolume ?? 1f;
        }
        var maxDistance = Mathf.Max(AudioMinDistanceMeters + 0.01f, _config?.ResolvedAudioRangeMeters ?? 5f);
        audioSource.spatialBlend = 1f;
        audioSource.dopplerLevel = 0f;
        audioSource.priority = 32;
        audioSource.rolloffMode = AudioRolloffMode.Linear;
        audioSource.minDistance = AudioMinDistanceMeters;
        audioSource.maxDistance = maxDistance;
        audioSource.spread = 0f;

        if (_config?.EnableAudio.Value == true)
        {
            RequestAudioClipLoad(audioClip);
        }

        if (_config?.EnableAudio.Value != true && audioSource.isPlaying)
        {
            ClearActiveAudioFade(audioSource);
            audioSource.Stop();
            audioSource.time = 0f;
        }
    }

    private bool StartAudioIfEnabled(AudioSource? audioSource, AudioClip? audioClip, AnimationClip? animationClip, bool suppressFurtherRetries)
    {
        if (_config?.EnableAudio.Value != true || audioSource == null || audioClip == null)
        {
            if (audioSource != null)
            {
                ClearActiveAudioFade(audioSource);
                audioSource.Stop();
                audioSource.time = 0f;
            }

            _audioRetrySuppressed = false;
            return false;
        }

        var segmentStart = ResolveAudioSegmentStartSeconds(animationClip, audioClip);
        var segmentDuration = ResolveAudioSegmentDuration(animationClip, audioClip);
        if (segmentDuration <= 0.01f)
        {
            _logger?.LogWarning($"Audio segment duration is invalid for clip '{audioClip.name}'.");
            return false;
        }

        if (!EnsureAudioClipReadyForPlayback(audioClip, suppressFurtherRetries, "audio start"))
        {
            return false;
        }

        try
        {
            audioSource.clip = audioClip;
            audioSource.loop = false;
            SeekAudioToTime(audioSource, audioClip, segmentStart);
            audioSource.volume = 0f;
            audioSource.Play();
            BeginAudioFade(audioSource, Mathf.Min(AudioFadeDurationSeconds, segmentDuration * 0.5f));
        }
        catch (Exception exception)
        {
            if (suppressFurtherRetries)
            {
                _audioRetrySuppressed = true;
            }

            _logger?.LogWarning($"Audio playback failed for clip '{audioClip.name}': {exception.Message}");
            return false;
        }

        var started = audioSource.isPlaying;
        if (!started && suppressFurtherRetries)
        {
            _audioRetrySuppressed = true;
            _logger?.LogWarning($"Audio playback could not start for clip '{audioClip.name}'. Automatic retries are suppressed until audio is toggled again or the model is respawned.");
        }

        return started;
    }

    private void MaintainSynchronizedLoopPlayback()
    {
        if (_activeDancer == null || _isPausedForOfflineMenu || _isPausedForDistance)
        {
            return;
        }

        if (_activeAnimator == null || (_config?.EnableAudio.Value == true && _activeAudioSource == null))
        {
            CacheActiveRuntimeComponents(_activeDancer);
        }

        var animator = _activeAnimator;
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            return;
        }

        var primaryClip = ResolveSynchronizedAnimatorClip(animator);
        if (primaryClip == null)
        {
            return;
        }

        if (_isInLoopInterval)
        {
            if (Time.time < _loopIntervalEndTime)
            {
                return;
            }

            _isInLoopInterval = false;
            _loopIntervalEndTime = -1f;
            StartAnimatorAtBeginning(animator, primaryClip);
            _lastAnimatorLoopIndex = -1;
            _lastMorphLoopIndex = -1;
            LogVerbose("Loop interval elapsed; resuming synchronized playback.");
        }

        if (animator.speed == 0f)
        {
            var resyncAudioClip = ResolveAudioClip(_activeAudioSource);
            var targetLoopDuration = ResolveTargetLoopDurationSeconds(primaryClip, resyncAudioClip);
            animator.speed = ResolveAnimationPlaybackSpeed(primaryClip, targetLoopDuration);
        }

        ResolveAnimatorSegmentProgress(animator, primaryClip, out var segmentLoopProgress, out _);
        if (segmentLoopProgress >= 0.999f)
        {
            BeginLoopInterval(animator, primaryClip);
            return;
        }

        var loopIndexBeforeUpdate = _animatorSegmentLoopIndex;
        MaintainAnimatorSegmentLoop(animator, primaryClip);

        if (_animatorSegmentLoopIndex > loopIndexBeforeUpdate)
        {
            BeginLoopInterval(animator, primaryClip);
            return;
        }

        if (_config?.EnableAudio.Value != true)
        {
            return;
        }

        var audioSource = _activeAudioSource;
        var audioClip = ResolveAudioClip(audioSource);
        if (audioSource == null || audioClip == null)
        {
            return;
        }

        SynchronizeAudioWithAnimator(
            animator,
            audioSource,
            audioClip,
            primaryClip,
            startIfNeeded: true,
            allowFadeOnStart: true,
            suppressFurtherRetries: true,
            logCorrections: false);
    }

    private void BeginLoopInterval(Animator animator, AnimationClip primaryClip)
    {
        _isInLoopInterval = true;
        _loopIntervalEndTime = Time.time + LoopIntervalSeconds;

        var animatorSpeed = animator.speed;
        animator.speed = 0f;

        var audioSource = _activeAudioSource;
        if (audioSource != null && audioSource.isPlaying)
        {
            audioSource.Stop();
        }

        LogVerbose($"Animation loop completed; pausing for {LoopIntervalSeconds:0.##}s before replay. (animatorSpeed was {animatorSpeed:0.###})");
    }

    private void MaintainMorphPlaybackAfterAnimator()
    {
        if (_activeDancer == null || _isPausedForOfflineMenu || _isPausedForDistance || _isInLoopInterval)
        {
            return;
        }

        if (_activeAnimator == null)
        {
            CacheActiveRuntimeComponents(_activeDancer);
        }

        var animator = _activeAnimator;
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            return;
        }

        var primaryClip = ResolveSynchronizedAnimatorClip(animator);
        if (primaryClip == null)
        {
            return;
        }

        if (_hasNativeFacialAnimationControl)
        {
            StopRuntimeMorphPlayback();
            SynchronizeNativeFacialAnimatorsWithPrimary(animator, primaryClip);
            return;
        }

        SynchronizeMorphPlaybackWithAnimator(animator, primaryClip);
    }

    private void StartMorphPlayback(GameObject? dancer)
    {
        if (dancer == null || _loadedPrefab?.MorphClip == null)
        {
            return;
        }

        if (HasNativeFacialAnimationControl(dancer))
        {
            StopRuntimeMorphPlayback();
            return;
        }

        if (ShouldUseDirectMorphSampling())
        {
            var existingAnimation = _activeAnimation;
            if (existingAnimation != null && existingAnimation.clip != null)
            {
                existingAnimation.Stop(existingAnimation.clip.name);
            }

            _activeAnimation = null;
            SampleMorphClip(_loadedPrefab.MorphClip, _activeAnimator, 0f);
            _lastMorphLoopIndex = -1;
            return;
        }

        var morphHost = ResolveMorphPlaybackHost(dancer);
        var morphAnimation = morphHost.GetComponent<Animation>() ?? morphHost.AddComponent<Animation>();
        _activeAnimation = morphAnimation;
        morphAnimation.cullingType = AnimationCullingType.AlwaysAnimate;
        if (morphAnimation.GetClip(_loadedPrefab.MorphClip.name) == null)
        {
            morphAnimation.AddClip(_loadedPrefab.MorphClip, _loadedPrefab.MorphClip.name);
        }

        morphAnimation.clip = _loadedPrefab.MorphClip;
        morphAnimation.wrapMode = WrapMode.Loop;
        morphAnimation.playAutomatically = false;
        morphAnimation.Stop(_loadedPrefab.MorphClip.name);
        morphAnimation.Rewind(_loadedPrefab.MorphClip.name);
        morphAnimation.Play(_loadedPrefab.MorphClip.name);
        var state = morphAnimation[_loadedPrefab.MorphClip.name];
        if (state != null)
        {
            state.enabled = true;
            state.weight = 1f;
            state.wrapMode = WrapMode.Loop;
            state.time = 0f;
            if (_activeAnimator != null && _activeAnimator.runtimeAnimatorController != null && _activePrimaryAnimatorClip != null)
            {
                state.speed = 0f;
                morphAnimation.Sample();
            }
            else
            {
                ApplyActiveAnimationPlaybackSpeed(ResolveAudioClip(_activeAudioSource));
            }
        }

        _lastMorphLoopIndex = -1;
    }

    private void SynchronizeMorphPlaybackWithAnimator(Animator animator, AnimationClip? primaryClip)
    {
        var morphClip = _loadedPrefab?.MorphClip;
        var sampleRoot = _activeDancer ?? animator.gameObject;
        if (morphClip == null || sampleRoot == null || morphClip.length <= 0.01f)
        {
            return;
        }

        ResolveAnimatorSegmentProgress(animator, primaryClip, out var loopProgress, out var loopIndex);
        var targetTime = Mathf.Clamp(
            loopProgress * morphClip.length,
            0f,
            Mathf.Max(0f, morphClip.length - 0.0001f));

        if (ShouldUseDirectMorphSampling())
        {
            SampleMorphClip(morphClip, animator, targetTime);
            _lastMorphLoopIndex = loopIndex;
            return;
        }

        var morphAnimation = _activeAnimation;
        if (morphAnimation == null || morphAnimation.clip == null)
        {
            return;
        }

        var clipName = morphAnimation.clip.name;
        var state = morphAnimation[clipName];
        if (state == null)
        {
            return;
        }

        state.enabled = true;
        state.weight = 1f;
        state.wrapMode = WrapMode.Loop;
        state.speed = 0f;
        var loopChanged = _lastMorphLoopIndex >= 0 && loopIndex != _lastMorphLoopIndex;

        if (loopChanged)
        {
            morphAnimation.Stop(clipName);
            state.time = 0f;
            morphAnimation.Play(clipName);
        }

        if (!morphAnimation.IsPlaying(clipName))
        {
            morphAnimation.Play(clipName);
        }

        state.time = targetTime;
        morphAnimation.Sample();
        _lastMorphLoopIndex = loopIndex;
    }

    private void StopRuntimeMorphPlayback()
    {
        var morphClip = _loadedPrefab?.MorphClip;
        if (morphClip == null)
        {
            return;
        }

        var morphAnimation = _activeAnimation;
        if (morphAnimation != null)
        {
            if (morphAnimation.GetClip(morphClip.name) != null)
            {
                morphAnimation.Stop(morphClip.name);
            }

            if (morphAnimation.clip != null && string.Equals(morphAnimation.clip.name, morphClip.name, StringComparison.Ordinal))
            {
                morphAnimation.clip = null;
            }
        }

        _lastMorphLoopIndex = -1;
    }

    private GameObject ResolveMorphPlaybackHost(GameObject dancer)
    {
        if (_activeAnimator != null)
        {
            return _activeAnimator.gameObject;
        }

        var wrappedModelTransform = dancer.transform.Find(WrappedModelRootName);
        return wrappedModelTransform != null ? wrappedModelTransform.gameObject : dancer;
    }

    private void SampleMorphClip(AnimationClip morphClip, Animator? animator, float timeSeconds)
    {
        var wrapperRoot = _activeDancer;
        if (wrapperRoot == null)
        {
            return;
        }

        SampleMorphClipAtRoot(morphClip, wrapperRoot, timeSeconds);

        var wrappedModelTransform = wrapperRoot.transform.Find(WrappedModelRootName);
        if (wrappedModelTransform != null)
        {
            SampleMorphClipAtRoot(morphClip, wrappedModelTransform.gameObject, timeSeconds);
        }

        var animatorRoot = animator != null
            ? animator.gameObject
            : ResolveMorphPlaybackHost(wrapperRoot);
        if (animatorRoot != null)
        {
            SampleMorphClipAtRoot(morphClip, animatorRoot, timeSeconds);
        }
    }

    private static void SampleMorphClipAtRoot(AnimationClip morphClip, GameObject root, float timeSeconds)
    {
        if (root == null)
        {
            return;
        }

        morphClip.SampleAnimation(root, timeSeconds);
    }

    private void BeginAudioFade(AudioSource audioSource, float durationSeconds)
    {
        _fadingAudioSource = audioSource;
        _audioFadeStartTime = Time.unscaledTime;
        _audioFadeDuration = Mathf.Max(0.01f, durationSeconds);
        _audioFadeTargetVolume = _config?.ResolvedAudioVolume ?? 1f;
        audioSource.volume = 0f;
    }

    private void UpdateActiveAudioFade()
    {
        if (_fadingAudioSource == null)
        {
            return;
        }

        if (_config?.EnableAudio.Value != true || !_fadingAudioSource.isPlaying)
        {
            ClearActiveAudioFade(_fadingAudioSource);
            return;
        }

        var elapsed = Time.unscaledTime - _audioFadeStartTime;
        var t = Mathf.Clamp01(elapsed / _audioFadeDuration);
        _fadingAudioSource.volume = Mathf.Lerp(0f, _audioFadeTargetVolume, t);
        if (t >= 0.999f)
        {
            ClearActiveAudioFade(_fadingAudioSource);
        }
    }

    private bool IsActiveAudioFade(AudioSource? audioSource)
    {
        return audioSource != null && ReferenceEquals(audioSource, _fadingAudioSource);
    }

    private void ClearActiveAudioFade(AudioSource? audioSource = null)
    {
        if (_fadingAudioSource == null)
        {
            return;
        }

        if (audioSource != null && !ReferenceEquals(audioSource, _fadingAudioSource))
        {
            return;
        }

        if (_fadingAudioSource != null)
        {
            _fadingAudioSource.volume = _config?.ResolvedAudioVolume ?? 1f;
        }

        _fadingAudioSource = null;
        _audioFadeStartTime = 0f;
        _audioFadeDuration = 0f;
        _audioFadeTargetVolume = 0f;
    }

    private bool HandlePlaybackPause(GameObject dancer, Animator? animator, AudioSource? audioSource, AudioClip? audioClip)
    {
        var shouldPauseForOfflineMenu = ShouldPauseForOfflineMenu();
        var shouldPauseForDistance = ShouldPauseForDistance(dancer, out var distanceMeters);
        var wasPaused = _isPausedForOfflineMenu || _isPausedForDistance;

        _isPausedForOfflineMenu = shouldPauseForOfflineMenu;
        _isPausedForDistance = shouldPauseForDistance;

        var shouldPause = _isPausedForOfflineMenu || _isPausedForDistance;
        if (shouldPause)
        {
            if (!wasPaused)
            {
                _audioPausedForPlaybackPause = audioSource != null && audioSource.isPlaying;
                if (_audioPausedForPlaybackPause)
                {
                    ClearActiveAudioFade(audioSource);
                    audioSource!.Pause();
                }

                if (shouldPauseForOfflineMenu && shouldPauseForDistance)
                {
                    LogVerbose(
                        $"Paused dancer playback because the offline menu is open and the local player is {distanceMeters:0.##}m away.");
                }
                else if (shouldPauseForOfflineMenu)
                {
                    LogVerbose("Paused dancer playback because the offline menu is open.");
                }
                else
                {
                    LogVerbose(
                        $"Paused dancer playback because the local player is {distanceMeters:0.##}m away, beyond the {DistancePauseThresholdMeters:0.##}m optimization range.");
                }
            }

            SetPlaybackPausedState(animator, audioClip, paused: true);
            return true;
        }

        if (!wasPaused)
        {
            return false;
        }

        if (_audioPausedForPlaybackPause && _config?.EnableAudio.Value == true && audioSource != null && audioClip != null)
        {
            audioSource.UnPause();
        }

        _audioPausedForPlaybackPause = false;
        SetPlaybackPausedState(animator, audioClip, paused: false);
        LogVerbose(
            $"Resumed dancer playback because the local player is within {DistancePauseThresholdMeters:0.##}m and no offline pause condition is active.");
        return false;
    }

    private void LogVerbose(string message)
    {
        if (Core.VerboseLogState.Enabled)
        {
            _logger?.LogInfo(message);
        }
    }

    private bool ShouldPauseForDistance(GameObject dancer, out float distanceMeters)
    {
        distanceMeters = 0f;
        if (!TryResolveCurrentLocalPlayerPose(out var playerPosition, out _, out _, out _))
        {
            return false;
        }

        var modelReferencePosition = _hasActivePlacement
            ? _activePlacement.Position
            : dancer.transform.position;
        distanceMeters = Vector3.Distance(playerPosition, modelReferencePosition);
        return distanceMeters > DistancePauseThresholdMeters;
    }

    private void SetPlaybackPausedState(Animator? animator, AudioClip? audioClip, bool paused)
    {
        if (animator != null)
        {
            animator.speed = paused
                ? 0f
                : ResolveSynchronizedAnimatorSpeed(_activePrimaryAnimatorClip, audioClip);
            ForceAnimatorLayerWeights(animator, 1f);
        }

        foreach (var secondaryAnimator in EnumerateSecondaryAnimators())
        {
            secondaryAnimator.speed = paused
                ? 0f
                : ResolveSynchronizedAnimatorSpeed(ResolveAnimatorPlaybackClip(secondaryAnimator), audioClip);
            ForceAnimatorLayerWeights(secondaryAnimator, 1f);
        }

        if (ShouldUseDirectMorphSampling())
        {
            return;
        }

        var activeAnimation = _activeAnimation;
        if (activeAnimation == null || activeAnimation.clip == null)
        {
            return;
        }

        var animationState = activeAnimation[activeAnimation.clip.name];
        if (animationState == null)
        {
            return;
        }

        if (ShouldSynchronizeMorphWithAnimator())
        {
            animationState.speed = 0f;
            return;
        }

        animationState.speed = paused
            ? 0f
            : ResolveDesiredActiveAnimationSpeed(audioClip);
    }

    private static bool ShouldPauseForOfflineMenu()
    {
        if (!PhotonNetwork.OfflineMode)
        {
            return false;
        }

        if (Time.timeScale <= OfflinePauseTimeScaleThreshold)
        {
            return true;
        }

        var activeWindows = MenuWindow.AllActiveWindows;
        if (activeWindows == null)
        {
            return false;
        }

        foreach (var window in activeWindows)
        {
            if (window == null)
            {
                continue;
            }

            if (window.isOpen)
            {
                return true;
            }
        }

        return false;
    }

    private static AudioClip? ResolveAudioClip(AudioSource? audioSource)
    {
        if (audioSource == null)
        {
            return null;
        }

        if (audioSource.clip != null)
        {
            return audioSource.clip;
        }

        return audioSource.resource as AudioClip;
    }

    private void CacheActiveRuntimeComponents(GameObject? dancer)
    {
        if (dancer == null)
        {
            ClearActiveRuntimeComponents();
            return;
        }

        if (!_hasCachedRuntimeComponents || !ReferenceEquals(_cachedRuntimeComponentRoot, dancer))
        {
            _cachedRuntimeComponentRoot = dancer;
            _hasCachedRuntimeComponents = true;
            _activeAnimators = dancer
                .GetComponentsInChildren<Animator>(includeInactive: true)
                .Where(animator => animator != null)
                .ToArray();
            _activeAnimator = ResolvePrimaryRuntimeAnimator(_activeAnimators);
            _activeAnimation = dancer.GetComponentInChildren<Animation>(includeInactive: true);
            _activeAudioSource = dancer.GetComponentInChildren<AudioSource>(includeInactive: true);
            _cachedAnimatorController = null;
            _activePrimaryAnimatorClip = null;
        }

        RefreshCachedPrimaryAnimatorClip();
        RefreshNativeFacialAnimationDetection(dancer);
    }

    private void ClearActiveRuntimeComponents()
    {
        _activeAnimator = null;
        _activeAnimators = Array.Empty<Animator>();
        _activeAnimation = null;
        _activeAudioSource = null;
        _activePrimaryAnimatorClip = null;
        _activeShadowBlob = null;
        _hasCachedRuntimeComponents = false;
        _cachedRuntimeComponentRoot = null;
        _cachedAnimatorController = null;
        _hasNativeFacialAnimationControl = false;
    }

    private void RefreshCachedPrimaryAnimatorClip()
    {
        var animator = _activeAnimator;
        var controller = animator?.runtimeAnimatorController;
        if (ReferenceEquals(controller, _cachedAnimatorController))
        {
            return;
        }

        _cachedAnimatorController = controller;
        _activePrimaryAnimatorClip = animator != null && controller != null
            ? ResolvePrimaryAnimatorClip(animator)
            : null;
    }

    private static AnimationClip? ResolvePrimaryAnimatorClip(Animator animator)
    {
        var clips = animator.runtimeAnimatorController?.animationClips;
        if (clips == null || clips.Length == 0)
        {
            return null;
        }

        AnimationClip? longestClip = null;
        var longestLength = float.MinValue;
        for (var index = 0; index < clips.Length; index++)
        {
            var clip = clips[index];
            if (clip == null || clip.length <= longestLength)
            {
                continue;
            }

            longestClip = clip;
            longestLength = clip.length;
        }

        return longestClip;
    }

    private static Animator? ResolvePrimaryRuntimeAnimator(IEnumerable<Animator> animators)
    {
        if (animators == null)
        {
            return null;
        }

        return animators
            .Where(animator => animator != null)
            .OrderBy(animator =>
            {
                var controller = animator.runtimeAnimatorController;
                if (controller == null)
                {
                    return 2;
                }

                return IsLikelyNativeFacialController(controller) ? 1 : 0;
            })
            .ThenByDescending(animator => ResolvePrimaryAnimatorClip(animator)?.length ?? 0f)
            .FirstOrDefault();
    }

    private AnimationClip? ResolveSynchronizedAnimatorClip(Animator animator)
    {
        var currentClip = TryResolveCurrentAnimatorClip(animator);
        if (currentClip != null)
        {
            _activePrimaryAnimatorClip = currentClip;
            return currentClip;
        }

        RefreshCachedPrimaryAnimatorClip();
        return _activePrimaryAnimatorClip;
    }

    private static AnimationClip? TryResolveCurrentAnimatorClip(Animator animator)
    {
        if (animator == null)
        {
            return null;
        }

        try
        {
            var currentClips = animator.GetCurrentAnimatorClipInfo(0);
            if (currentClips != null && currentClips.Length > 0)
            {
                var currentClip = currentClips
                    .Select(info => info.clip)
                    .FirstOrDefault(clip => clip != null && clip.length > 0.01f);
                if (currentClip != null)
                {
                    return currentClip;
                }
            }
        }
        catch (Exception)
        {
            // GetCurrentAnimatorClipInfo 在动画器尚未就绪或无运行时控制器时会抛出 InvalidOperationException，
            // 此处视为"无当前片段"，回退到下一片段查找。
        }

        try
        {
            var nextClips = animator.GetNextAnimatorClipInfo(0);
            if (nextClips != null && nextClips.Length > 0)
            {
                var nextClip = nextClips
                    .Select(info => info.clip)
                    .FirstOrDefault(clip => clip != null && clip.length > 0.01f);
                if (nextClip != null)
                {
                    return nextClip;
                }
            }
        }
        catch (Exception)
        {
            // GetNextAnimatorClipInfo 在过渡尚未开始时会抛出 InvalidOperationException，此处视为"无下一片段"。
        }

        return null;
    }

    private void ApplyActiveAnimationPlaybackSpeed(AudioClip? audioClip)
    {
        if (ShouldUseDirectMorphSampling() || ShouldSynchronizeMorphWithAnimator())
        {
            return;
        }

        var activeAnimation = _activeAnimation;
        var activeClip = activeAnimation?.clip;
        if (activeAnimation == null || activeClip == null)
        {
            return;
        }

        var state = activeAnimation[activeClip.name];
        if (state == null)
        {
            return;
        }

        state.speed = ResolveDesiredActiveAnimationSpeed(audioClip);
    }

    private float ResolveDesiredActiveAnimationSpeed(AudioClip? audioClip)
    {
        var activeClip = _activeAnimation?.clip;
        if (activeClip == null || activeClip.length <= 0.01f)
        {
            return 1f;
        }

        if (_activeAnimator != null && _activeAnimator.runtimeAnimatorController != null && _activePrimaryAnimatorClip != null)
        {
            var bodyLoopDuration = ResolveTargetLoopDurationSeconds(_activePrimaryAnimatorClip, audioClip);
            return ResolveAnimationPlaybackSpeed(activeClip, bodyLoopDuration);
        }

        var targetLoopDuration = ResolveTargetLoopDurationSeconds(activeClip, audioClip);
        return ResolveAnimationPlaybackSpeed(activeClip, targetLoopDuration);
    }

    private bool SynchronizeAudioWithAnimator(
        Animator animator,
        AudioSource audioSource,
        AudioClip audioClip,
        AnimationClip? primaryClip,
        bool startIfNeeded,
        bool allowFadeOnStart,
        bool suppressFurtherRetries,
        bool logCorrections)
    {
        if (!TryResolveAnimatorAudioPosition(animator, primaryClip, audioClip, out var expectedAudioTime, out var animatorLoopIndex, out var segmentDuration))
        {
            return false;
        }

        var loopChanged = _lastAnimatorLoopIndex >= 0 && animatorLoopIndex != _lastAnimatorLoopIndex;
        _lastAnimatorLoopIndex = animatorLoopIndex;

        if (!audioSource.isPlaying)
        {
            if (!startIfNeeded)
            {
                return false;
            }

            return StartAudioAtTime(
                audioSource,
                audioClip,
                expectedAudioTime,
                segmentDuration,
                allowFadeOnStart,
                suppressFurtherRetries,
                loopChanged ? "animator loop restart" : "audio start");
        }

        if (loopChanged)
        {
            return StartAudioAtTime(
                audioSource,
                audioClip,
                expectedAudioTime,
                segmentDuration,
                allowFadeOnStart,
                suppressFurtherRetries,
                "animator loop restart");
        }

        var currentAudioTime = Mathf.Clamp(audioSource.time, 0f, Mathf.Max(0f, audioClip.length - 0.01f));
        var driftSeconds = Mathf.Abs(currentAudioTime - expectedAudioTime);
        if (driftSeconds < AudioHardResyncThresholdSeconds)
        {
            if (logCorrections && driftSeconds >= AudioDriftCorrectionThresholdSeconds)
            {
                _logger?.LogInfo(
                    $"Leaving minor audio drift uncorrected for '{audioClip.name}' to avoid audible seek stutter. " +
                    $"expected={expectedAudioTime:0.###}, current={currentAudioTime:0.###}, drift={driftSeconds:0.###}, loop={animatorLoopIndex}.");
            }

            return true;
        }

        if (logCorrections)
        {
            _logger?.LogInfo(
                $"Correcting audio drift for '{audioClip.name}'. expected={expectedAudioTime:0.###}, current={currentAudioTime:0.###}, " +
                $"drift={driftSeconds:0.###}, loop={animatorLoopIndex}.");
        }

        return StartAudioAtTime(
            audioSource,
            audioClip,
            expectedAudioTime,
            segmentDuration,
            fadeIn: false,
            suppressFurtherRetries: suppressFurtherRetries,
            logReason: "hard resync");
    }

    private bool TryResolveAnimatorAudioPosition(
        Animator animator,
        AnimationClip? primaryClip,
        AudioClip? audioClip,
        out float expectedAudioTime,
        out int animatorLoopIndex,
        out float segmentDuration)
    {
        expectedAudioTime = 0f;
        animatorLoopIndex = 0;
        segmentDuration = 0f;

        if (audioClip == null || audioClip.length <= 0.01f)
        {
            return false;
        }

        var segmentStart = ResolveAudioSegmentStartSeconds(primaryClip, audioClip);
        segmentDuration = ResolveAudioSegmentDuration(primaryClip, audioClip);
        if (segmentDuration <= 0.01f)
        {
            return false;
        }

        ResolveAnimatorSegmentProgress(animator, primaryClip, out var loopProgress, out animatorLoopIndex);
        var segmentEnd = Mathf.Min(audioClip.length, segmentStart + segmentDuration);
        expectedAudioTime = Mathf.Lerp(segmentStart, segmentEnd, loopProgress);
        expectedAudioTime = Mathf.Clamp(expectedAudioTime, segmentStart, Mathf.Max(segmentStart, segmentEnd - 0.01f));
        return true;
    }

    private bool StartAudioAtTime(
        AudioSource audioSource,
        AudioClip audioClip,
        float startTime,
        float segmentDuration,
        bool fadeIn,
        bool suppressFurtherRetries,
        string logReason)
    {
        if (!EnsureAudioClipReadyForPlayback(audioClip, suppressFurtherRetries, logReason))
        {
            return false;
        }

        try
        {
            ClearActiveAudioFade(audioSource);
            audioSource.Stop();
            audioSource.clip = audioClip;
            audioSource.loop = false;
            SeekAudioToTime(audioSource, audioClip, startTime);
            audioSource.volume = fadeIn ? 0f : (_config?.ResolvedAudioVolume ?? 1f);
            audioSource.Play();
            if (fadeIn)
            {
                BeginAudioFade(audioSource, Mathf.Min(AudioFadeDurationSeconds, segmentDuration * 0.5f));
            }
        }
        catch (Exception exception)
        {
            if (suppressFurtherRetries)
            {
                _audioRetrySuppressed = true;
            }

            _logger?.LogWarning($"Audio playback failed for clip '{audioClip.name}' during {logReason}: {exception.Message}");
            return false;
        }

        var started = audioSource.isPlaying;
        if (!started && suppressFurtherRetries)
        {
            _audioRetrySuppressed = true;
            _logger?.LogWarning($"Audio playback could not start for clip '{audioClip.name}' during {logReason}. Automatic retries are suppressed until audio is toggled again or the model is respawned.");
        }

        return started;
    }

    private void RequestAudioClipLoad(AudioClip? audioClip)
    {
        if (audioClip == null)
        {
            return;
        }

        try
        {
            if (audioClip.loadState == AudioDataLoadState.Unloaded)
            {
                audioClip.LoadAudioData();
            }
        }
        catch (Exception exception)
        {
            LogVerbose($"Failed to request audio clip load for '{audioClip.name}': {exception.Message}");
        }
    }

    private bool EnsureAudioClipReadyForPlayback(AudioClip audioClip, bool suppressFurtherRetries, string logReason)
    {
        if (audioClip == null)
        {
            return false;
        }

        RequestAudioClipLoad(audioClip);
        var loadState = audioClip.loadState;
        if (loadState == AudioDataLoadState.Loaded)
        {
            return true;
        }

        if (loadState == AudioDataLoadState.Failed)
        {
            if (suppressFurtherRetries)
            {
                _audioRetrySuppressed = true;
            }

            _logger?.LogWarning($"Audio clip '{audioClip.name}' failed to load during {logReason}.");
            return false;
        }

        LogVerbose($"Audio clip '{audioClip.name}' is still loading during {logReason}. Playback will retry automatically.");
        return false;
    }

    private static void SeekAudioToTime(AudioSource audioSource, AudioClip audioClip, float timeSeconds)
    {
        var clampedTime = Mathf.Clamp(timeSeconds, 0f, Mathf.Max(0f, audioClip.length - 0.01f));
        var maxSamples = Mathf.Max(0, audioClip.samples - 1);
        if (audioClip.frequency > 0 && maxSamples > 0)
        {
            try
            {
                var targetSamples = Mathf.Clamp(Mathf.RoundToInt(clampedTime * audioClip.frequency), 0, maxSamples);
                audioSource.timeSamples = targetSamples;
                return;
            }
            catch (Exception)
            {
                // AudioSource.timeSamples 在某些状态下（如未播放、被销毁）会抛出 InvalidOperationException，
                // 此处忽略，后续使用 time 属性作为回退。
            }
        }

        audioSource.time = clampedTime;
    }

    private float ResolveSynchronizedAnimatorSpeed(AnimationClip? animationClip, AudioClip? audioClip)
    {
        if (animationClip == null || animationClip.length <= 0.01f)
        {
            return 1f;
        }

        if (ShouldPreserveOriginalAnimatorTiming(animationClip))
        {
            return 1f;
        }

        if (audioClip == null || audioClip.length <= 0.01f)
        {
            return 1f;
        }

        var motionSegmentDuration = ResolveMotionSegmentDurationSeconds(animationClip);
        var segmentDuration = ResolveAudioSegmentDuration(animationClip, audioClip);
        if (segmentDuration <= 0.01f || motionSegmentDuration <= 0.01f)
        {
            return 1f;
        }

        return Mathf.Clamp(motionSegmentDuration / segmentDuration, 0.25f, 4f);
    }

    private bool ShouldPreserveOriginalAnimatorTiming(AnimationClip? animationClip)
    {
        if (animationClip == null || animationClip.length <= 0.01f)
        {
            return true;
        }

        var metadata = ResolvePlaybackMetadata();
        var startSeconds = metadata.ResolveMotionStartSeconds(animationClip.length);
        var endSeconds = metadata.ResolveMotionEndSeconds(animationClip.length);
        return startSeconds <= 0.01f && endSeconds >= animationClip.length - 0.01f;
    }

    private float ResolveAudioSegmentStartSeconds(AnimationClip? animationClip, AudioClip? audioClip)
    {
        if (audioClip == null || audioClip.length <= 0.01f)
        {
            return 0f;
        }

        var metadata = ResolvePlaybackMetadata();
        return Mathf.Clamp(
            metadata.ResolveAudioSegmentStartSeconds(),
            0f,
            Mathf.Max(0f, audioClip.length - 0.01f));
    }

    private float ResolveAudioSegmentDuration(AnimationClip? animationClip, AudioClip? audioClip)
    {
        if (audioClip == null || audioClip.length <= 0.01f)
        {
            return 0f;
        }

        var metadata = ResolvePlaybackMetadata();
        return metadata.ResolveAudioSegmentDurationSeconds(ResolveMotionSegmentDurationSeconds(animationClip), audioClip.length);
    }

    private float ResolveTargetLoopDurationSeconds(AnimationClip? animationClip, AudioClip? audioClip)
    {
        if (animationClip == null || animationClip.length <= 0.01f)
        {
            return 0f;
        }

        if (audioClip == null)
        {
            return ResolveMotionSegmentDurationSeconds(animationClip);
        }

        var segmentDuration = ResolveAudioSegmentDuration(animationClip, audioClip);
        return segmentDuration > 0.01f ? segmentDuration : ResolveMotionSegmentDurationSeconds(animationClip);
    }

    private static float ResolveAnimationPlaybackSpeed(AnimationClip animationClip, float targetLoopDurationSeconds)
    {
        if (animationClip.length <= 0.01f || targetLoopDurationSeconds <= 0.01f)
        {
            return 1f;
        }

        return Mathf.Clamp(animationClip.length / targetLoopDurationSeconds, 0.25f, 4f);
    }

    private DancePlaybackMetadata ResolvePlaybackMetadata()
    {
        var metadata = _loadedPrefab?.PlaybackMetadata;
        if (metadata != null)
        {
            return metadata;
        }

        LegacyPlaybackMetadata.SourceFrameRate = LegacyMotionFrameRate;
        LegacyPlaybackMetadata.MotionStartFrame = Mathf.RoundToInt(LegacyMotionTrimStartFrame);
        LegacyPlaybackMetadata.HasMotionEndFrame = false;
        LegacyPlaybackMetadata.MotionEndFrame = 0;
        LegacyPlaybackMetadata.AudioSegmentStartSeconds = LegacyMotionTrimStartFrame / LegacyMotionFrameRate;
        LegacyPlaybackMetadata.AudioSegmentDurationSeconds = 0f;
        return LegacyPlaybackMetadata;
    }

    private float ResolveMotionSegmentStartSeconds(AnimationClip? animationClip)
    {
        if (animationClip == null || animationClip.length <= 0.01f)
        {
            return 0f;
        }

        return ResolvePlaybackMetadata().ResolveMotionStartSeconds(animationClip.length);
    }

    private float ResolveMotionSegmentDurationSeconds(AnimationClip? animationClip)
    {
        if (animationClip == null || animationClip.length <= 0.01f)
        {
            return 0f;
        }

        return ResolvePlaybackMetadata().ResolveMotionSegmentDurationSeconds(animationClip.length);
    }

    private bool TryResolveMotionSegmentNormalizedRange(
        AnimationClip? animationClip,
        out float startNormalized,
        out float durationNormalized,
        out float durationSeconds)
    {
        startNormalized = 0f;
        durationNormalized = 1f;
        durationSeconds = 0f;
        if (animationClip == null || animationClip.length <= 0.01f)
        {
            return false;
        }

        var metadata = ResolvePlaybackMetadata();
        var startSeconds = metadata.ResolveMotionStartSeconds(animationClip.length);
        var endSeconds = metadata.ResolveMotionEndSeconds(animationClip.length);
        durationSeconds = Mathf.Max(0f, endSeconds - startSeconds);
        if (durationSeconds <= 0.01f)
        {
            durationSeconds = animationClip.length;
            return false;
        }

        if (startSeconds <= 0.01f && endSeconds >= animationClip.length - 0.01f)
        {
            return false;
        }

        startNormalized = Mathf.Clamp01(startSeconds / animationClip.length);
        durationNormalized = Mathf.Clamp(durationSeconds / animationClip.length, 0.0001f, 1f);
        return true;
    }

    private void ResolveAnimatorSegmentProgress(Animator animator, AnimationClip? primaryClip, out float loopProgress, out int loopIndex)
    {
        loopProgress = 0f;
        loopIndex = 0;

        var stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        var normalizedTime = Mathf.Max(0f, stateInfo.normalizedTime);
        if (!TryResolveMotionSegmentNormalizedRange(primaryClip, out var startNormalized, out var durationNormalized, out _))
        {
            loopIndex = Mathf.Max(0, Mathf.FloorToInt(normalizedTime));
            loopProgress = Mathf.Repeat(normalizedTime, 1f);
            return;
        }

        if (normalizedTime <= startNormalized + 0.0001f)
        {
            loopProgress = 0f;
            loopIndex = Mathf.Max(0, _animatorSegmentLoopIndex);
            return;
        }

        var segmentProgress = Mathf.Max(0f, normalizedTime - startNormalized);
        var wrappedProgress = Mathf.Repeat(segmentProgress, durationNormalized);
        loopProgress = durationNormalized > 0.0001f
            ? Mathf.Clamp01(wrappedProgress / durationNormalized)
            : 0f;
        loopIndex = Mathf.Max(0, _animatorSegmentLoopIndex);
    }

    private void MaintainAnimatorSegmentLoop(Animator animator, AnimationClip? primaryClip)
    {
        if (animator == null || primaryClip == null || primaryClip.length <= 0.01f)
        {
            return;
        }

        if (!TryResolveMotionSegmentNormalizedRange(primaryClip, out var startNormalized, out var durationNormalized, out _))
        {
            var currentStateInfo = animator.GetCurrentAnimatorStateInfo(0);
            if (currentStateInfo.loop || primaryClip.isLooping)
            {
                return;
            }

            var currentNormalizedTime = Mathf.Max(0f, currentStateInfo.normalizedTime);
            if (currentNormalizedTime < 0.9999f)
            {
                return;
            }

            _animatorSegmentLoopIndex += Mathf.Max(1, Mathf.FloorToInt(currentNormalizedTime));
            animator.Play(0, 0, 0f);
            animator.Update(0f);
            return;
        }

        var stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        var normalizedTime = Mathf.Max(0f, stateInfo.normalizedTime);
        if (normalizedTime + 0.0001f < startNormalized)
        {
            animator.Play(0, 0, startNormalized);
            animator.Update(0f);
            return;
        }

        var segmentProgress = Mathf.Max(0f, normalizedTime - startNormalized);
        if (segmentProgress < durationNormalized - 0.0001f)
        {
            return;
        }

        var completedLoops = Mathf.Max(1, Mathf.FloorToInt(segmentProgress / durationNormalized));
        _animatorSegmentLoopIndex += completedLoops;
        var wrappedProgress = Mathf.Repeat(segmentProgress, durationNormalized);
        var wrappedNormalizedTime = startNormalized + wrappedProgress;
        if (Mathf.Abs(wrappedNormalizedTime - normalizedTime) <= 0.0001f)
        {
            return;
        }

        animator.Play(0, 0, wrappedNormalizedTime);
        animator.Update(0f);
    }

    private void StartAnimatorAtBeginning(Animator animator, AnimationClip? primaryClip)
    {
        _animatorSegmentLoopIndex = 0;
        _lastAnimatorLoopIndex = -1;
        _lastMorphLoopIndex = -1;

        var startNormalized = 0f;
        if (primaryClip != null && primaryClip.length > 0.01f)
        {
            startNormalized = Mathf.Clamp01(ResolveMotionSegmentStartSeconds(primaryClip) / primaryClip.length);
        }

        animator.Play(0, 0, startNormalized);
        animator.Update(0f);
    }

    private bool ShouldUseDirectMorphSampling()
    {
        return ShouldSynchronizeMorphWithAnimator();
    }

    private IEnumerable<Animator> EnumerateSecondaryAnimators()
    {
        if (_activeAnimators == null || _activeAnimators.Length == 0)
        {
            yield break;
        }

        for (var index = 0; index < _activeAnimators.Length; index++)
        {
            var animator = _activeAnimators[index];
            if (animator == null || ReferenceEquals(animator, _activeAnimator) || animator.runtimeAnimatorController == null)
            {
                continue;
            }

            yield return animator;
        }
    }

    private bool ShouldSynchronizeMorphWithAnimator()
    {
        return _loadedPrefab?.MorphClip != null
               && !_hasNativeFacialAnimationControl
               && _activeAnimator != null
               && _activeAnimator.runtimeAnimatorController != null
               && _activePrimaryAnimatorClip != null;
    }

    private void ConfigureSecondaryNativeAnimators(AnimationClip? primaryClip, float animationSpeed)
    {
        foreach (var secondaryAnimator in EnumerateSecondaryAnimators())
        {
            secondaryAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            secondaryAnimator.applyRootMotion = false;
            secondaryAnimator.updateMode = AnimatorUpdateMode.Normal;
            secondaryAnimator.speed = animationSpeed;
            ForceAnimatorLayerWeights(secondaryAnimator, 1f);
            secondaryAnimator.Rebind();
            secondaryAnimator.Update(0f);

            var secondaryClip = ResolveAnimatorPlaybackClip(secondaryAnimator);
            var startNormalized = ResolveClipStartNormalized(primaryClip, secondaryClip);
            secondaryAnimator.Play(0, 0, startNormalized);
            secondaryAnimator.Update(0f);
        }
    }

    private void RefreshSecondaryNativeAnimators(Animator primaryAnimator, AnimationClip? primaryClip)
    {
        if (!_hasNativeFacialAnimationControl)
        {
            return;
        }

        SynchronizeNativeFacialAnimatorsWithPrimary(primaryAnimator, primaryClip);
    }

    private void SynchronizeNativeFacialAnimatorsWithPrimary(Animator primaryAnimator, AnimationClip? primaryClip)
    {
        if (primaryAnimator == null)
        {
            return;
        }

        ResolveAnimatorSegmentProgress(primaryAnimator, primaryClip, out var loopProgress, out _);
        foreach (var secondaryAnimator in EnumerateSecondaryAnimators())
        {
            var secondaryClip = ResolveAnimatorPlaybackClip(secondaryAnimator);
            if (secondaryClip == null || secondaryClip.length <= 0.01f)
            {
                continue;
            }

            secondaryAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            secondaryAnimator.applyRootMotion = false;
            secondaryAnimator.updateMode = AnimatorUpdateMode.Normal;
            secondaryAnimator.speed = primaryAnimator.speed;
            ForceAnimatorLayerWeights(secondaryAnimator, 1f);

            var targetNormalized = Mathf.Clamp01(loopProgress);
            var currentState = secondaryAnimator.GetCurrentAnimatorStateInfo(0);
            var currentNormalizedTime = Mathf.Max(0f, currentState.normalizedTime);
            var currentNormalized = Mathf.Repeat(currentNormalizedTime, 1f);
            var normalizedDelta = Mathf.Abs(Mathf.DeltaAngle(currentNormalized * 360f, targetNormalized * 360f)) / 360f;
            var isFrozenAtClipEnd = !currentState.loop
                && !secondaryClip.isLooping
                && currentNormalizedTime >= 0.999f
                && targetNormalized <= 0.98f;
            if (!isFrozenAtClipEnd && normalizedDelta <= 0.02f && currentState.length > 0f)
            {
                continue;
            }

            secondaryAnimator.Play(0, 0, targetNormalized);
            secondaryAnimator.Update(0f);
        }
    }

    private static AnimationClip? ResolveAnimatorPlaybackClip(Animator animator)
    {
        if (animator == null)
        {
            return null;
        }

        return TryResolveCurrentAnimatorClip(animator) ?? ResolvePrimaryAnimatorClip(animator);
    }

    private float ResolveClipStartNormalized(AnimationClip? timingReferenceClip, AnimationClip? targetClip)
    {
        if (targetClip == null || targetClip.length <= 0.01f)
        {
            return 0f;
        }

        var startSeconds = timingReferenceClip != null
            ? Mathf.Max(0f, ResolveMotionSegmentStartSeconds(timingReferenceClip))
            : 0f;
        return Mathf.Clamp01(startSeconds / targetClip.length);
    }

    private static void ForceAnimatorLayerWeights(Animator animator, float weight)
    {
        if (animator == null)
        {
            return;
        }

        for (var layerIndex = 0; layerIndex < animator.layerCount; layerIndex++)
        {
            animator.SetLayerWeight(layerIndex, weight);
        }
    }

    private bool HasNativeFacialAnimationControl(GameObject? dancer)
    {
        if (dancer == null)
        {
            return false;
        }

        if (_hasCachedRuntimeComponents && ReferenceEquals(_cachedRuntimeComponentRoot, dancer))
        {
            return _hasNativeFacialAnimationControl;
        }

        return DetectNativeFacialAnimationControl(dancer);
    }

    private void RefreshNativeFacialAnimationDetection(GameObject? dancer)
    {
        var hasNativeFacialAnimationControl = DetectNativeFacialAnimationControl(dancer);
        if (_hasNativeFacialAnimationControl != hasNativeFacialAnimationControl)
        {
            LogVerbose($"Native facial animation detection changed: {hasNativeFacialAnimationControl}.");
        }

        _hasNativeFacialAnimationControl = hasNativeFacialAnimationControl;
    }

    private bool DetectNativeFacialAnimationControl(GameObject? dancer)
    {
        if (dancer == null)
        {
            return false;
        }

        var animators = dancer
            .GetComponentsInChildren<Animator>(includeInactive: true)
            .Where(animator => animator != null && animator.runtimeAnimatorController != null)
            .ToArray();
        if (animators.Length > 1)
        {
            return true;
        }

        for (var index = 0; index < animators.Length; index++)
        {
            var controller = animators[index].runtimeAnimatorController;
            if (controller == null)
            {
                continue;
            }

            if (IsLikelyNativeFacialController(controller))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLikelyNativeFacialController(RuntimeAnimatorController controller)
    {
        if (controller == null)
        {
            return false;
        }

        if (ContainsFacialToken(controller.name))
        {
            return true;
        }

        var clips = controller.animationClips;
        if (clips == null || clips.Length == 0)
        {
            return false;
        }

        for (var index = 0; index < clips.Length; index++)
        {
            var clip = clips[index];
            if (clip == null)
            {
                continue;
            }

            if (ContainsFacialToken(clip.name))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsFacialToken(string? value)
    {
        var text = value ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        for (var index = 0; index < NativeFacialClipTokens.Length; index++)
        {
            var token = NativeFacialClipTokens[index];
            if (!string.IsNullOrEmpty(token)
                && text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private readonly struct PlacementPose
    {
        private PlacementPose(Vector3 position, Vector3 forward, string source)
        {
            Position = position;
            Forward = forward;
            Source = source;
        }

        public Vector3 Position { get; }
        public Vector3 Forward { get; }
        public string Source { get; }

        public static PlacementPose Create(Vector3 position, Vector3 forward, string source)
        {
            return new PlacementPose(position, forward, source);
        }
    }
}

internal readonly struct GroundSampleCandidate
{
    public GroundSampleCandidate(float y, float verticalDelta, float horizontalDelta, float raycastDistance)
    {
        Y = y;
        VerticalDelta = verticalDelta;
        HorizontalDelta = horizontalDelta;
        RaycastDistance = raycastDistance;
    }

    public float Y { get; }
    public float VerticalDelta { get; }
    public float HorizontalDelta { get; }
    public float RaycastDistance { get; }

    public bool IsBetterThan(GroundSampleCandidate other)
    {
        if (VerticalDelta < other.VerticalDelta - 0.001f)
        {
            return true;
        }

        if (Mathf.Abs(VerticalDelta - other.VerticalDelta) > 0.001f)
        {
            return false;
        }

        if (HorizontalDelta < other.HorizontalDelta - 0.001f)
        {
            return true;
        }

        if (Mathf.Abs(HorizontalDelta - other.HorizontalDelta) > 0.001f)
        {
            return false;
        }

        return RaycastDistance < other.RaycastDistance;
    }
}
