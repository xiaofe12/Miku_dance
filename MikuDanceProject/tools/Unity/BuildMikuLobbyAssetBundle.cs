#nullable enable annotations
#nullable disable warnings

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

public static class BuildMikuLobbyAssetBundle
{
    private const string WrappedDisplayRootName = "MikuLobbyDisplay";
    private const string WrappedModelRootName = "MikuLobbyModel";
    private const string DefaultDisplayAssetPath = "Assets/Models/Sour Cherry dress Miku Ver1.3/Sour Cherry dress Miku Ver1.3.prefab";
    private const string DefaultBodyControllerAssetPath = "Assets/Models/Sour Cherry dress Miku Ver1.3/Sour Cherry dress Miku Ver1.3.controller";
    private const string DefaultMorphClipSourceAssetPath = "Assets/Models/Sour Cherry dress Miku Ver1.3/morph2.anim";
    private const string LegacyMorphClipSourceAssetPath = "Assets/CodexBuild/MikuLobbyMorph.anim";
    private const string DefaultAudioAssetPath = "Assets/Models/Sour Cherry dress Miku Ver1.3/galaxias.mp3";
    private const string DefaultProfileAssetPath = "Assets/MikuBundleBuildProfile.asset";
    private const string TemporaryAssetFolder = "Assets/CodexBuild";
    private const string TemporaryPrefabPath = TemporaryAssetFolder + "/MikuLobby.prefab";
    private const string TemporaryBodyClipPath = TemporaryAssetFolder + "/MikuLobbyBody.anim";
    private const string TemporaryMorphClipPath = TemporaryAssetFolder + "/MikuLobbyMorph.anim";
    private const string TemporaryFacialClipPath = TemporaryAssetFolder + "/MikuLobbyFacial.anim";
    private const string TemporaryPlaybackMetadataPath = TemporaryAssetFolder + "/MikuLobbyPlayback.json";
    private const string TemporaryRuntimeVmdPath = TemporaryAssetFolder + "/MikuLobbyMotion.vmd.bytes";
    private const string TemporaryControllerPath = TemporaryAssetFolder + "/MikuLobby.controller";
    private const string TemporaryFacialControllerPath = TemporaryAssetFolder + "/MikuLobbyFace.controller";
    private const string TemporaryMaterialFolder = TemporaryAssetFolder + "/GeneratedMaterials";
    private const string DefaultOutputBundleName = "miku_lobby_display.bundle";
    private const string BundleOutputEnvironmentVariable = "CODEX_MIKU_BUNDLE_OUTPUT";
    private const string BundleProfileEnvironmentVariable = "CODEX_MIKU_BUNDLE_PROFILE";
    private const string MotionStartFrameEnvironmentVariable = "CODEX_MIKU_PLAYBACK_START_FRAME";
    private const string MotionEndFrameEnvironmentVariable = "CODEX_MIKU_PLAYBACK_END_FRAME";
    private const string ImportedModelAutomationEnvironmentVariable = "CODEX_MIKU_RUN_MODEL_AUTOMATION";
    private const string RuntimePhysicsClothSetupVersion = "mmd-physics-cloth-2026-05-05-v2";
    private const string RuntimeVmdBundleSetupVersion = "animator-motion-2026-05-06-v1";
    private const string ImportedBodyClipSetupVersion = "high-fidelity-imported-body-clip-2026-05-06-v1";
    private const int OversizedGenericBodyClipBindingThreshold = 1000;

    private static readonly string[] EssentialBodyCurvePathTokens =
    {
        "joint_Actuator",
        "joint_Master",
        "joint_Center",
        "joint_Waist",
        "joint_HipMaster",
        "joint_Torso",
        "joint_Neck",
        "joint_Head",
        "joint_Shoulder",
        "joint_RightShoulder",
        "joint_LeftShoulder",
        "joint_RightArm",
        "joint_LeftArm",
        "joint_RightElbow",
        "joint_LeftElbow",
        "joint_RightHand",
        "joint_LeftHand",
        "joint_RightWrist",
        "joint_LeftWrist",
        "joint_RightHip",
        "joint_LeftHip",
        "joint_RightKnee",
        "joint_LeftKnee",
        "joint_RightFoot",
        "joint_LeftFoot",
        "joint_RightToe",
        "joint_LeftToe",
        "joint_RightThumb",
        "joint_LeftThumb",
        "joint_RightIndex",
        "joint_LeftIndex",
        "joint_RightMiddle",
        "joint_LeftMiddle",
        "joint_RightRing",
        "joint_LeftRing",
        "joint_RightPinky",
        "joint_LeftPinky",
        "joint_RightEye",
        "joint_LeftEye",
        "IK_RightHip",
        "IK_LeftHip",
        "IK_RightToe",
        "IK_LeftToe",
        "joint_Shirt",
        "joint_Skirt",
        "joint_FHair",
        "joint_BHair",
        "joint_UHair",
        "joint_FrontHair",
        "joint_hidariFHair",
        "joint_migiFHair",
        "joint_hidariRibbon",
        "joint_migiRibbon",
        "joint_hidariTRibbon",
        "joint_migiTRibbon",
        "joint_Tail",
        "joint_Tails",
        "joint_hidariTailS",
        "joint_migiTailS",
        "joint_migitail_1",
        "joint_migitail_2",
        "joint_migitail_3",
        "joint_migitail_4",
        "joint_migitail_5",
        "joint_migitail_6",
        "joint_migitail_7",
        "joint_migitail_8",
        "joint_migitail_9",
        "joint_migitail_10",
        "joint_migitail_11",
        "joint_migitail_12",
        "joint_hidaritail_1",
        "joint_hidaritail_2",
        "joint_hidaritail_3",
        "joint_hidaritail_4",
        "joint_hidaritail_5",
        "joint_hidaritail_6",
        "joint_hidaritail_7",
        "joint_hidaritail_8",
        "joint_hidaritail_9",
        "joint_hidaritail_10",
        "joint_hidaritail_11",
        "joint_hidaritail_12",
        "joint_hidarisleeveM",
        "joint_migisleeveM",
        "joint_Belt",
        "joint_RightBreast",
        "joint_LeftBreast",
        "BreastUpper",
        "BreastLower",
        "BreastTip",
    };

    private static readonly string[] EssentialBodyPositionCurvePathTokens =
    {
        "joint_Actuator",
        "joint_Master",
        "joint_Center",
        "joint_Waist",
        "joint_HipMaster",
        "IK_RightHip",
        "IK_LeftHip",
        "IK_RightToe",
        "IK_LeftToe",
    };

    private static readonly string[] ExcludedBodyCurvePathTokens =
    {
        "ShoulderC",
        "Dummy",
        "ArmTwist",
        "ElbowTwist",
        "HandTwist",
        "Thumb0M",
        "FHair",
        "BHair",
        "UHair",
        "FrontHair",
        "Ribbon",
        "Tail",
        "Tails",
        "Skirt",
        "Shirt",
        "Sleeve",
        "Belt",
        "Breast",
    };

    private static readonly string[] HighPriorityTextureNames =
    {
        "Texture Others.png",
        "Eye Normal Edition.png",
        "Eye Special Edition.png",
    };

    private static readonly string[] HighPriorityTextureTokens =
    {
        "face",
        "skin",
        "cheek",
        "blush",
        "mouth",
        "lip",
        "eye",
        "brow",
        "lash",
        "body",
        "head",
        "kao",
        "hada",
        "hair",
        "kami",
        "cloth",
        "dress",
        "skirt",
        "tail",
        "mimi",
    };

    private static readonly string[] FacialAnimatorTokens =
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
        "blink",
        "表情",
    };

    public static void Run()
    {
        var outputDirectory = Environment.GetEnvironmentVariable(BundleOutputEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new InvalidOperationException($"Environment variable {BundleOutputEnvironmentVariable} is not set.");
        }

        var profileAssetPath = ResolveBatchProfileAssetPath();

        var profile = AssetDatabase.LoadAssetAtPath<MikuBundleBuildProfile>(profileAssetPath);
        var request = profile != null
            ? CreateRequest(profile)
            : CreateDefaultRequest(Path.GetFullPath(outputDirectory));
        ApplySpaShowcaseDefaults(request);
        request.OutputDirectory = Path.GetFullPath(outputDirectory);
        ApplyEnvironmentOverrides(request);
        Build(request);
    }

    public static string BuildFromProfile(MikuBundleBuildProfile profile)
    {
        if (profile == null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        var request = CreateRequest(profile);
        ApplySpaShowcaseDefaults(request);
        return Build(request);
    }

    public static string PrepareProfileAssets(MikuBundleBuildProfile profile)
    {
        if (profile == null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        var request = CreateRequest(profile);
        ApplySpaShowcaseDefaults(request);
        request.PreferPreparedAssets = false;
        request.PreparedPrefabAssetPath = null;
        request.PreparedBodyAnimationClipAssetPath = null;
        request.PreparedFacialAnimationClipAssetPath = null;
        request.PreparedPlaybackMetadataAssetPath = null;
        request.PreparedControllerAssetPath = null;
        request.PreparedAssetFolderPath = null;

        ValidateRequest(request);

        var preparedPaths = BuildAssetPaths.CreatePrepared(profile);
        EnsureAssetFolderExists(preparedPaths.RootFolderPath);
        ClearAssetOutputs(preparedPaths);

        var preparedAssets = PrepareBundleAssets(request, preparedPaths);
        UpdatePreparedCache(profile, preparedPaths, preparedAssets);
        return preparedAssets.PrefabAssetPath;
    }

    public static void ClearPreparedProfileAssets(MikuBundleBuildProfile profile)
    {
        if (profile == null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        var preparedPaths = BuildAssetPaths.CreatePrepared(profile);
        ClearAssetOutputs(preparedPaths);

        profile.PreparedPrefab = null;
        profile.PreparedBodyAnimationClip = null;
        profile.PreparedFacialAnimationClip = null;
        profile.PreparedController = null;
        profile.PreparedPlaybackMetadata = null;
        profile.PreparedAssetFolderPath = string.Empty;
        profile.PreparedCacheSignature = string.Empty;
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    public static bool HasUsablePreparedAssets(MikuBundleBuildProfile profile)
    {
        if (profile == null)
        {
            return false;
        }

        return profile.PreferPreparedAssets
            && IsPreparedCacheValid(profile, BuildPreparedCacheSignature(profile));
    }

    public static RuntimeAnimatorController TryResolveSuggestedBodyController(GameObject displayAsset)
    {
        if (displayAsset == null)
        {
            return null;
        }

        var animator = displayAsset.GetComponentInChildren<Animator>(includeInactive: true);
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            return animator.runtimeAnimatorController;
        }

        var assetPath = AssetDatabase.GetAssetPath(displayAsset);
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return null;
        }

        var folderPath = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
        var modelName = Path.GetFileNameWithoutExtension(assetPath);
        if (!string.IsNullOrWhiteSpace(folderPath) && !string.IsNullOrWhiteSpace(modelName))
        {
            var sameNameControllerPath = $"{folderPath}/{modelName}.controller";
            var sameNameController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(sameNameControllerPath);
            if (sameNameController != null)
            {
                return sameNameController;
            }
        }

        var embeddedController = AssetDatabase.LoadAllAssetsAtPath(assetPath)
            .OfType<RuntimeAnimatorController>()
            .FirstOrDefault();
        if (embeddedController != null)
        {
            return embeddedController;
        }

        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return null;
        }

        return AssetDatabase.FindAssets("t:RuntimeAnimatorController", new[] { folderPath })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>)
            .FirstOrDefault(controller => controller != null);
    }

    public static AnimationClip TryResolveSuggestedBodyAnimationClip(GameObject displayAsset)
    {
        if (displayAsset == null)
        {
            return null;
        }

        var assetPath = AssetDatabase.GetAssetPath(displayAsset);
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return null;
        }

        var embeddedClip = AssetDatabase.LoadAllAssetsAtPath(assetPath)
            .OfType<AnimationClip>()
            .Where(IsCandidateBodyAnimationClip)
            .OrderByDescending(clip => clip.length)
            .FirstOrDefault();
        if (embeddedClip != null)
        {
            return embeddedClip;
        }

        var folderPath = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return null;
        }

        return AssetDatabase.FindAssets("t:AnimationClip", new[] { folderPath })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<AnimationClip>)
            .Where(IsCandidateBodyAnimationClip)
            .OrderByDescending(clip => clip.length)
            .FirstOrDefault();
    }

    public static AnimationClip TryResolveSuggestedFacialAnimationClip(
        GameObject displayAsset = null,
        AnimationClip bodyAnimationClip = null,
        RuntimeAnimatorController bodyController = null,
        bool allowAutoGenerate = false)
    {
        if (allowAutoGenerate)
        {
            if (ExportMikuLobbyMorphClip.ContainsBlendShapeCurves(bodyAnimationClip))
            {
                return ExportMikuLobbyMorphClip.ExportToAsset(
                    bodyAnimationClip,
                    ResolveDefaultMorphClipSourceAssetPath(),
                    out _,
                    out _,
                    out _);
            }

            var controllerClip = TryResolveMorphClipFromController(bodyController);
            if (controllerClip != null)
            {
                return ExportMikuLobbyMorphClip.ExportToAsset(
                    controllerClip,
                    ResolveDefaultMorphClipSourceAssetPath(),
                    out _,
                    out _,
                    out _);
            }

            var displayAnimator = displayAsset != null
                ? displayAsset.GetComponentInChildren<Animator>(includeInactive: true)
                : null;
            var animatorClip = TryResolveMorphClipFromController(displayAnimator != null ? displayAnimator.runtimeAnimatorController : null);
            if (animatorClip != null)
            {
                return ExportMikuLobbyMorphClip.ExportToAsset(
                    animatorClip,
                    ResolveDefaultMorphClipSourceAssetPath(),
                    out _,
                    out _,
                    out _);
            }
        }

        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ResolveDefaultMorphClipSourceAssetPath());
        if (clip != null)
        {
            return clip;
        }

        foreach (var searchTerm in new[] { "morph2", "MikuLobbyMorph" })
        {
            var candidate = AssetDatabase.FindAssets($"t:AnimationClip {searchTerm}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<AnimationClip>)
                .FirstOrDefault(found => found != null);
            if (candidate != null)
            {
                return candidate;
            }
        }

        return null;
    }

    public static AudioClip TryResolveSuggestedBackgroundMusic()
    {
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(DefaultAudioAssetPath);
        if (clip != null)
        {
            return clip;
        }

        foreach (var searchTerm in new[] { "galaxias", "極楽浄土" })
        {
            var candidate = AssetDatabase.FindAssets($"t:AudioClip {searchTerm}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<AudioClip>)
                .FirstOrDefault(found => found != null);
            if (candidate != null)
            {
                return candidate;
            }
        }

        return null;
    }

    private static string Build(MikuBundleBuildRequest request)
    {
        ValidateRequest(request);
        Directory.CreateDirectory(request.OutputDirectory);

        if (CanBuildFromPreparedAssets(request))
        {
            var cachedAssets = new PreparedBundleAssets(
                request.PreparedPrefabAssetPath,
                request.PreparedBodyAnimationClipAssetPath,
                request.PreparedFacialAnimationClipAssetPath,
                request.PreparedPlaybackMetadataAssetPath,
                request.PreparedRuntimeVmdAssetPath,
                request.PreparedControllerAssetPath);
            Debug.Log(
                $"[Codex] Using prepared build cache for '{request.OutputBundleName}'. prefab='{request.PreparedPrefabAssetPath}', " +
                $"morph='{request.PreparedFacialAnimationClipAssetPath}', playback='{request.PreparedPlaybackMetadataAssetPath}'.");
            OptimizeFinalBundleModelImportSettings(request, ShouldStripImportedAnimations(cachedAssets));
            return BuildPreparedAssetBundle(request, cachedAssets);
        }

        var temporaryPaths = BuildAssetPaths.CreateTemporary();
        EnsureAssetFolderExists(temporaryPaths.RootFolderPath);
        ClearAssetOutputs(temporaryPaths);

        var preparedAssets = PrepareBundleAssets(request, temporaryPaths);
        OptimizeFinalBundleModelImportSettings(request, ShouldStripImportedAnimations(preparedAssets));
        return BuildPreparedAssetBundle(request, preparedAssets);
    }

    private static bool ShouldStripImportedAnimations(PreparedBundleAssets preparedAssets)
    {
        return preparedAssets.HasRuntimeVmdMotion
            || !string.IsNullOrWhiteSpace(preparedAssets.BodyClipAssetPath);
    }

    private static bool CanBuildFromPreparedAssets(MikuBundleBuildRequest request)
    {
        return request.PreferPreparedAssets
            && !string.IsNullOrWhiteSpace(request.PreparedPrefabAssetPath)
            && !string.IsNullOrWhiteSpace(request.PreparedFacialAnimationClipAssetPath)
            && !string.IsNullOrWhiteSpace(request.PreparedPlaybackMetadataAssetPath)
            && AssetDatabase.LoadAssetAtPath<GameObject>(request.PreparedPrefabAssetPath) != null
            && AssetDatabase.LoadAssetAtPath<AnimationClip>(request.PreparedFacialAnimationClipAssetPath) != null
            && AssetDatabase.LoadAssetAtPath<TextAsset>(request.PreparedPlaybackMetadataAssetPath) != null;
    }

    private static PreparedBundleAssets PrepareBundleAssets(MikuBundleBuildRequest request, BuildAssetPaths assetPaths)
    {
        var sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(request.DisplayAssetPath);
        if (sourcePrefab == null)
        {
            throw new FileNotFoundException($"Display asset was not found at '{request.DisplayAssetPath}'.");
        }

        if (ShouldRunImportedModelAutomation() && MikuImportedModelAutomation.ProcessImportedModelForObject(sourcePrefab))
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(request.DisplayAssetPath);
            if (sourcePrefab == null)
            {
                throw new FileNotFoundException($"Display asset was not found after automated repair at '{request.DisplayAssetPath}'.");
            }
        }

        var materialOverrides = ResolveMaterialOverrides(request.MaterialOverrides);
        OptimizeSourceAssetImportSettings(sourcePrefab, request, materialOverrides.SelectMany(item => item.Materials));
        var bodyMotion = PrepareBodyMotion(request, sourcePrefab, assetPaths.BodyClipPath);
        var embeddedMorphClipAssetPath = PrepareMorphClipAsset(request, sourcePrefab, bodyMotion, assetPaths.MorphClipPath);
        var nativeFacialClipAssetPath = PrepareNativeFacialClipAsset(request, sourcePrefab, bodyMotion, assetPaths.FacialClipPath);
        var selectedAudioClip = LoadSelectedAudioClip(request.BackgroundMusicAssetPath);
        var playbackMetadataAssetPath = PreparePlaybackMetadataAsset(request, bodyMotion, selectedAudioClip, assetPaths.PlaybackMetadataPath);
        string runtimeVmdAssetPath = null;

        Debug.Log(
            $"[Codex] Preparing bundle assets for '{request.OutputBundleName}' from display asset '{request.DisplayAssetPath}'. " +
            $"prefab='{assetPaths.PrefabPath}', morphClip='{embeddedMorphClipAssetPath ?? "(none)"}', " +
            $"bodyClip='{bodyMotion.PlaybackClipAssetPath ?? "(none)"}', runtimeVmd='{runtimeVmdAssetPath ?? "(none)"}', " +
            $"playbackMetadata='{playbackMetadataAssetPath ?? "(none)"}', " +
            $"textureSampling='{request.TextureSampling}', materialHandling='{request.MaterialHandling}'.");

        var instance = PrefabUtility.InstantiatePrefab(sourcePrefab) as GameObject;
        if (instance == null)
        {
            throw new InvalidOperationException("PrefabUtility.InstantiatePrefab returned null.");
        }

        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        var wrapperRoot = new GameObject(WrappedDisplayRootName);
        try
        {
            wrapperRoot.transform.position = Vector3.zero;
            wrapperRoot.transform.rotation = Quaternion.identity;
            wrapperRoot.transform.localScale = Vector3.one;

            instance.name = WrappedModelRootName;
            instance.transform.SetParent(wrapperRoot.transform, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            PrepareBundleRuntimePhysics(instance);
            StripUnsupportedComponents(wrapperRoot);
            ApplyMaterialOverrides(instance, materialOverrides);
            AlignRendererMaterialSlots(instance);
            MikuBundleMaterialCompatibilityUtility.PrepareRuntimeCompatibleMaterials(
                instance,
                assetPaths.MaterialFolderPath,
                request.MaterialHandling);
            AlignRendererMaterialSlots(instance);
            ConfigureRealtimeLightingRenderers(instance);
            ConfigureAudioSources(instance, request.BackgroundMusicAssetPath);
            string controllerAssetPath = null;
            if (!string.IsNullOrWhiteSpace(runtimeVmdAssetPath))
            {
                StripAnimatorControllersForRuntimeVmd(instance);
            }
            else
            {
                controllerAssetPath = EnsureAnimatorControllerAssigned(instance, request, bodyMotion, assetPaths.ControllerPath);
                EnsureFacialAnimatorControllersAssigned(instance, nativeFacialClipAssetPath, assetPaths.FacialControllerPath);
            }

            ValidatePlayableComponents(instance, request.DisplayAssetPath, runtimeVmdAssetPath);
            NormalizeWrapperPivot(wrapperRoot, instance);

            var legacyAnimation = instance.GetComponent<Animation>();
            if (legacyAnimation != null)
            {
                UnityEngine.Object.DestroyImmediate(legacyAnimation, allowDestroyingAssets: true);
            }

            var savedPrefab = PrefabUtility.SaveAsPrefabAsset(wrapperRoot, assetPaths.PrefabPath);
            if (savedPrefab == null)
            {
                throw new InvalidOperationException($"Failed to save prepared prefab to '{assetPaths.PrefabPath}'.");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return new PreparedBundleAssets(
                assetPaths.PrefabPath,
                bodyMotion.PlaybackClipAssetPath,
                embeddedMorphClipAssetPath,
                playbackMetadataAssetPath,
                runtimeVmdAssetPath,
                controllerAssetPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(wrapperRoot);
        }
    }

    private static string BuildPreparedAssetBundle(MikuBundleBuildRequest request, PreparedBundleAssets preparedAssets)
    {
        if (!string.IsNullOrWhiteSpace(request.BackgroundMusicAssetPath))
        {
            ConfigureAudioImporter(request.BackgroundMusicAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        var assetNames = new List<string>
        {
            preparedAssets.PrefabAssetPath,
        };

        if (!string.IsNullOrWhiteSpace(preparedAssets.BodyClipAssetPath))
        {
            assetNames.Add(preparedAssets.BodyClipAssetPath);
        }

        if (!string.IsNullOrWhiteSpace(preparedAssets.MorphClipAssetPath))
        {
            assetNames.Add(preparedAssets.MorphClipAssetPath);
        }

        if (!string.IsNullOrWhiteSpace(preparedAssets.PlaybackMetadataAssetPath))
        {
            assetNames.Add(preparedAssets.PlaybackMetadataAssetPath);
        }

        if (!string.IsNullOrWhiteSpace(preparedAssets.RuntimeVmdAssetPath))
        {
            assetNames.Add(preparedAssets.RuntimeVmdAssetPath);
        }

        if (!string.IsNullOrWhiteSpace(preparedAssets.ControllerAssetPath))
        {
            assetNames.Add(preparedAssets.ControllerAssetPath);
        }

        assetNames.AddRange(CollectExplicitShaderAssetPaths(preparedAssets.PrefabAssetPath));

        var manifest = BuildPipeline.BuildAssetBundles(
            request.OutputDirectory,
            new[]
            {
                new AssetBundleBuild
                {
                    assetBundleName = request.OutputBundleName,
                    assetNames = assetNames
                        .Where(path => !string.IsNullOrWhiteSpace(path))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                },
            },
            BuildAssetBundleOptions.ForceRebuildAssetBundle | BuildAssetBundleOptions.StrictMode,
            BuildTarget.StandaloneWindows64);

        if (manifest == null)
        {
            throw new InvalidOperationException("BuildPipeline.BuildAssetBundles returned null.");
        }

        var bundlePath = Path.Combine(request.OutputDirectory, request.OutputBundleName);
        if (!File.Exists(bundlePath))
        {
            throw new FileNotFoundException($"Expected AssetBundle file was not produced at '{bundlePath}'.");
        }

        Debug.Log(
            $"[Codex] Bundle built successfully: '{bundlePath}'. controller='{preparedAssets.ControllerAssetPath ?? "(none)"}', " +
            $"embeddedMorphClip={!string.IsNullOrWhiteSpace(preparedAssets.MorphClipAssetPath)}, " +
            $"runtimeVmd={!string.IsNullOrWhiteSpace(preparedAssets.RuntimeVmdAssetPath)}, " +
            $"usedPreparedAssets={request.PreferPreparedAssets && CanBuildFromPreparedAssets(request)}.");
        return bundlePath;
    }

    private static MikuBundleBuildRequest CreateDefaultRequest(string outputDirectory)
    {
        return new MikuBundleBuildRequest
        {
            DisplayAssetPath = DefaultDisplayAssetPath,
            BodyControllerAssetPath = DefaultBodyControllerAssetPath,
            FacialAnimationClipAssetPath = ResolveDefaultMorphClipSourceAssetPath(),
            BackgroundMusicAssetPath = DefaultAudioAssetPath,
            OutputDirectory = outputDirectory,
            OutputBundleName = DefaultOutputBundleName,
            TextureSampling = MikuBundleTextureSampling.Original,
            MaterialHandling = MikuBundleMaterialHandling.AutoCompatible,
            MotionStartFrame = 0,
        };
    }

    private static string ResolveDefaultMorphClipSourceAssetPath()
    {
        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(DefaultMorphClipSourceAssetPath) != null)
        {
            return DefaultMorphClipSourceAssetPath;
        }

        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(LegacyMorphClipSourceAssetPath) != null)
        {
            return LegacyMorphClipSourceAssetPath;
        }

        return DefaultMorphClipSourceAssetPath;
    }

    private static MikuBundleBuildRequest CreateRequest(MikuBundleBuildProfile profile)
    {
        var displayAssetPath = AssetDatabase.GetAssetPath(profile.DisplayAsset);
        var bodyControllerAssetPath = AssetDatabase.GetAssetPath(profile.BodyController);
        var bodyAnimationClipAssetPath = AssetDatabase.GetAssetPath(profile.BodyAnimationClip);
        var facialAnimationClipAssetPath = AssetDatabase.GetAssetPath(profile.FacialAnimationClip);
        var backgroundMusicAssetPath = AssetDatabase.GetAssetPath(profile.BackgroundMusic);
        var preparedCacheSignature = BuildPreparedCacheSignature(profile);
        var canUsePreparedAssets = profile.PreferPreparedAssets
            && IsPreparedCacheValid(profile, preparedCacheSignature);

        return new MikuBundleBuildRequest
        {
            DisplayAssetPath = displayAssetPath,
            BodyControllerAssetPath = bodyControllerAssetPath,
            BodyAnimationClipAssetPath = bodyAnimationClipAssetPath,
            FacialAnimationClipAssetPath = facialAnimationClipAssetPath,
            BackgroundMusicAssetPath = backgroundMusicAssetPath,
            PreferPreparedAssets = canUsePreparedAssets,
            PreparedPrefabAssetPath = canUsePreparedAssets ? AssetDatabase.GetAssetPath(profile.PreparedPrefab) : null,
            PreparedBodyAnimationClipAssetPath = canUsePreparedAssets ? AssetDatabase.GetAssetPath(profile.PreparedBodyAnimationClip) : null,
            PreparedFacialAnimationClipAssetPath = canUsePreparedAssets ? AssetDatabase.GetAssetPath(profile.PreparedFacialAnimationClip) : null,
            PreparedPlaybackMetadataAssetPath = canUsePreparedAssets ? AssetDatabase.GetAssetPath(profile.PreparedPlaybackMetadata) : null,
            PreparedRuntimeVmdAssetPath = canUsePreparedAssets ? ResolvePreparedRuntimeVmdAssetPath(profile.PreparedAssetFolderPath) : null,
            PreparedControllerAssetPath = canUsePreparedAssets ? AssetDatabase.GetAssetPath(profile.PreparedController) : null,
            PreparedAssetFolderPath = canUsePreparedAssets ? profile.PreparedAssetFolderPath : null,
            PreparedCacheSignature = canUsePreparedAssets ? preparedCacheSignature : null,
            OutputDirectory = profile.ResolveOutputDirectory(),
            OutputBundleName = MikuBundleMaterialCompatibilityUtility.NormalizeBundleFileName(profile.BundleName, DefaultOutputBundleName),
            TextureSampling = profile.TextureSampling,
            MaterialHandling = profile.MaterialHandling,
            MotionStartFrame = Mathf.Max(0, profile.MotionStartFrame),
            HasMotionEndFrame = profile.LimitMotionEndFrame,
            MotionEndFrame = Mathf.Max(0, profile.MotionEndFrame),
            MaterialOverrides = profile.MaterialOverrides
                .Where(item => item != null)
                .Select(item => new MikuBundleBuildMaterialOverrideRequest
                {
                    RendererPath = item.RendererPath?.Trim() ?? string.Empty,
                    MaterialAssetPaths = item.Materials
                        .Where(material => material != null)
                        .Select(AssetDatabase.GetAssetPath)
                        .Where(path => !string.IsNullOrWhiteSpace(path))
                        .ToArray(),
                })
                .ToList(),
        };
    }

    private static void ApplyEnvironmentOverrides(MikuBundleBuildRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (TryReadIntEnvironmentVariable(MotionStartFrameEnvironmentVariable, out var motionStartFrame))
        {
            request.MotionStartFrame = Mathf.Max(0, motionStartFrame);
        }

        var motionEndFrameRaw = Environment.GetEnvironmentVariable(MotionEndFrameEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(motionEndFrameRaw))
        {
            return;
        }

        if (int.TryParse(motionEndFrameRaw.Trim(), out var motionEndFrame))
        {
            request.HasMotionEndFrame = true;
            request.MotionEndFrame = Mathf.Max(0, motionEndFrame);
        }
    }

    private static void ApplySpaShowcaseDefaults(MikuBundleBuildRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        request.DisplayAssetPath = ResolveConfiguredAssetPathOrDefault(request.DisplayAssetPath, DefaultDisplayAssetPath);
        request.BodyControllerAssetPath = ResolveBodyControllerAssetPath(request.BodyControllerAssetPath, request.DisplayAssetPath);
        request.BodyAnimationClipAssetPath = ResolveExistingAssetPathOrNull(request.BodyAnimationClipAssetPath);
        request.FacialAnimationClipAssetPath = ResolveConfiguredAssetPathOrDefault(
            request.FacialAnimationClipAssetPath,
            ResolveDefaultMorphClipSourceAssetPath());
        request.BackgroundMusicAssetPath = ResolveConfiguredAssetPathOrDefault(request.BackgroundMusicAssetPath, DefaultAudioAssetPath);
    }

    private static string ResolveConfiguredAssetPathOrDefault(string configuredAssetPath, string fallbackAssetPath)
    {
        var configuredResolvedPath = ResolveExistingAssetPathOrNull(configuredAssetPath);
        if (!string.IsNullOrWhiteSpace(configuredResolvedPath))
        {
            return configuredResolvedPath;
        }

        var fallbackResolvedPath = ResolveExistingAssetPathOrNull(fallbackAssetPath);
        return !string.IsNullOrWhiteSpace(fallbackResolvedPath)
            ? fallbackResolvedPath
            : configuredAssetPath;
    }

    private static string ResolveBodyControllerAssetPath(string configuredAssetPath, string displayAssetPath)
    {
        var configuredResolvedPath = ResolveExistingAssetPathOrNull(configuredAssetPath);
        if (!string.IsNullOrWhiteSpace(configuredResolvedPath))
        {
            return configuredResolvedPath;
        }

        var defaultDisplayPath = ResolveExistingAssetPathOrNull(DefaultDisplayAssetPath);
        if (!string.IsNullOrWhiteSpace(defaultDisplayPath)
            && string.Equals(NormalizeAssetPathForComparison(displayAssetPath), NormalizeAssetPathForComparison(defaultDisplayPath), StringComparison.OrdinalIgnoreCase))
        {
            return ResolveExistingAssetPathOrNull(DefaultBodyControllerAssetPath);
        }

        return null;
    }

    private static string NormalizeAssetPathForComparison(string assetPath)
    {
        return string.IsNullOrWhiteSpace(assetPath)
            ? string.Empty
            : assetPath.Trim().Replace('\\', '/');
    }

    private static string ResolveExistingAssetPathOrNull(string assetPath)
    {
        return !string.IsNullOrWhiteSpace(assetPath)
            && AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null
                ? assetPath
                : null;
    }

    private static string BuildPreparedCacheSignature(MikuBundleBuildProfile profile)
    {
        if (profile == null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        var parts = new List<string>
        {
            $"display={AssetDatabase.GetAssetPath(profile.DisplayAsset) ?? string.Empty}",
            $"bodyController={AssetDatabase.GetAssetPath(profile.BodyController) ?? string.Empty}",
            $"bodyClip={AssetDatabase.GetAssetPath(profile.BodyAnimationClip) ?? string.Empty}",
            $"facialClip={AssetDatabase.GetAssetPath(profile.FacialAnimationClip) ?? string.Empty}",
            $"audio={AssetDatabase.GetAssetPath(profile.BackgroundMusic) ?? string.Empty}",
            $"start={Mathf.Max(0, profile.MotionStartFrame)}",
            $"hasEnd={profile.LimitMotionEndFrame}",
            $"end={Mathf.Max(0, profile.MotionEndFrame)}",
            $"texture={profile.TextureSampling}",
            $"material={profile.MaterialHandling}",
            $"physicsClothSetup={RuntimePhysicsClothSetupVersion}",
            $"runtimeVmdSetup={RuntimeVmdBundleSetupVersion}",
            $"importedBodyClipSetup={ImportedBodyClipSetupVersion}",
        };

        if (profile.MaterialOverrides != null)
        {
            for (var index = 0; index < profile.MaterialOverrides.Count; index++)
            {
                var item = profile.MaterialOverrides[index];
                if (item == null)
                {
                    continue;
                }

                var materialPaths = item.Materials == null
                    ? Array.Empty<string>()
                    : item.Materials
                        .Where(material => material != null)
                        .Select(AssetDatabase.GetAssetPath)
                        .Where(path => !string.IsNullOrWhiteSpace(path))
                        .ToArray();
                parts.Add($"override[{index}]={item.RendererPath?.Trim() ?? string.Empty}:{string.Join(",", materialPaths)}");
            }
        }

        return string.Join("|", parts);
    }

    private static bool IsPreparedCacheValid(MikuBundleBuildProfile profile, string expectedSignature)
    {
        if (profile == null)
        {
            return false;
        }

        if (profile.PreparedPrefab == null
            || profile.PreparedFacialAnimationClip == null
            || profile.PreparedPlaybackMetadata == null
            || string.IsNullOrWhiteSpace(profile.PreparedCacheSignature))
        {
            return false;
        }

        if (!string.Equals(profile.PreparedCacheSignature, expectedSignature, StringComparison.Ordinal))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(AssetDatabase.GetAssetPath(profile.PreparedPrefab))
            && !string.IsNullOrWhiteSpace(AssetDatabase.GetAssetPath(profile.PreparedFacialAnimationClip))
            && !string.IsNullOrWhiteSpace(AssetDatabase.GetAssetPath(profile.PreparedPlaybackMetadata));
    }

    private static bool TryReadIntEnvironmentVariable(string variableName, out int value)
    {
        value = 0;
        var rawValue = Environment.GetEnvironmentVariable(variableName);
        return !string.IsNullOrWhiteSpace(rawValue)
               && int.TryParse(rawValue.Trim(), out value);
    }

    private static void ValidateRequest(MikuBundleBuildRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayAssetPath))
        {
            throw new InvalidOperationException("A display prefab/model asset must be selected.");
        }

        if (string.IsNullOrWhiteSpace(request.OutputDirectory))
        {
            throw new InvalidOperationException("An output directory must be configured.");
        }

        if (string.IsNullOrWhiteSpace(request.OutputBundleName))
        {
            throw new InvalidOperationException("A bundle file name must be configured.");
        }

        if (request.MotionStartFrame < 0)
        {
            throw new InvalidOperationException("MotionStartFrame must not be negative.");
        }

        if (request.HasMotionEndFrame && request.MotionEndFrame <= request.MotionStartFrame)
        {
            throw new InvalidOperationException("MotionEndFrame must be greater than MotionStartFrame when an end frame limit is enabled.");
        }

        EnsureAuxiliaryAssetExists(request.DisplayAssetPath);

        if (!string.IsNullOrWhiteSpace(request.BodyControllerAssetPath))
        {
            EnsureAuxiliaryAssetExists(request.BodyControllerAssetPath);
        }

        if (!string.IsNullOrWhiteSpace(request.BodyAnimationClipAssetPath))
        {
            EnsureAuxiliaryAssetExists(request.BodyAnimationClipAssetPath);
        }

        if (!string.IsNullOrWhiteSpace(request.FacialAnimationClipAssetPath))
        {
            EnsureAuxiliaryAssetExists(request.FacialAnimationClipAssetPath);
        }

        if (!string.IsNullOrWhiteSpace(request.BackgroundMusicAssetPath))
        {
            EnsureAuxiliaryAssetExists(request.BackgroundMusicAssetPath);
        }
    }

    private static void OptimizeSourceAssetImportSettings(
        GameObject sourcePrefab,
        MikuBundleBuildRequest request,
        IEnumerable<Material> overrideMaterials)
    {
        var modelAssetPaths = CollectModelAssetPaths(sourcePrefab);
        foreach (var modelAssetPath in modelAssetPaths)
        {
            ConfigureModelImporterForHighFidelity(modelAssetPath);
        }

        var materials = CollectReferencedMaterials(sourcePrefab)
            .Concat(overrideMaterials.Where(material => material != null))
            .Distinct()
            .ToArray();
        var textureAssetPaths = CollectTextureAssetPaths(materials);
        foreach (var textureAssetPath in textureAssetPaths)
        {
            ConfigureTextureImporter(textureAssetPath, request.TextureSampling);
        }

        if (!string.IsNullOrWhiteSpace(request.BackgroundMusicAssetPath))
        {
            ConfigureAudioImporter(request.BackgroundMusicAssetPath);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void OptimizeFinalBundleModelImportSettings(MikuBundleBuildRequest request, bool stripImportedAnimations)
    {
        var sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(request.DisplayAssetPath);
        if (sourcePrefab == null)
        {
            Debug.LogWarning($"[Codex] Skipped final model import optimization because '{request.DisplayAssetPath}' was not found.");
            return;
        }

        var modelAssetPaths = CollectModelAssetPaths(sourcePrefab).ToArray();
        Debug.Log(
            $"[Codex] Optimizing {modelAssetPaths.Length} model importer(s) for bundle output. " +
            "Mesh compression Low, read/write disabled, tangents stripped. FBX animations and blend shape normals preserved.");
        foreach (var modelAssetPath in modelAssetPaths)
        {
            ConfigureModelImporterForHighFidelity(modelAssetPath);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static string ResolveBatchProfileAssetPath()
    {
        var environmentProfileAssetPath = NormalizeProjectAssetPath(Environment.GetEnvironmentVariable(BundleProfileEnvironmentVariable));
        if (!string.IsNullOrWhiteSpace(environmentProfileAssetPath)
            && AssetDatabase.LoadAssetAtPath<MikuBundleBuildProfile>(environmentProfileAssetPath) != null)
        {
            return environmentProfileAssetPath;
        }

        var selectedProfileAssetPath = MikuBundleBuildProfileAutoSync.TryResolveSelectedProfileAssetPath();
        if (!string.IsNullOrWhiteSpace(selectedProfileAssetPath)
            && AssetDatabase.LoadAssetAtPath<MikuBundleBuildProfile>(selectedProfileAssetPath) != null)
        {
            return selectedProfileAssetPath;
        }

        return DefaultProfileAssetPath;
    }

    private static string? NormalizeProjectAssetPath(string? assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return null;
        }

        return assetPath.Replace("\\", "/").Trim();
    }

    private static IEnumerable<string> CollectModelAssetPaths(GameObject sourcePrefab)
    {
        var modelAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sourcePrefabPath = AssetDatabase.GetAssetPath(sourcePrefab);
        if (!string.IsNullOrWhiteSpace(sourcePrefabPath) && AssetImporter.GetAtPath(sourcePrefabPath) is ModelImporter)
        {
            modelAssetPaths.Add(sourcePrefabPath);
        }

        foreach (var renderer in sourcePrefab.GetComponentsInChildren<Renderer>(includeInactive: true))
        {
            string assetPath = null;
            if (renderer is SkinnedMeshRenderer skinnedRenderer && skinnedRenderer.sharedMesh != null)
            {
                assetPath = AssetDatabase.GetAssetPath(skinnedRenderer.sharedMesh);
            }
            else if (renderer.TryGetComponent<MeshFilter>(out var meshFilter) && meshFilter.sharedMesh != null)
            {
                assetPath = AssetDatabase.GetAssetPath(meshFilter.sharedMesh);
            }

            if (!string.IsNullOrWhiteSpace(assetPath) && AssetImporter.GetAtPath(assetPath) is ModelImporter)
            {
                modelAssetPaths.Add(assetPath);
            }
        }

        return modelAssetPaths;
    }

    private static IEnumerable<Material> CollectReferencedMaterials(GameObject sourcePrefab)
    {
        return sourcePrefab
            .GetComponentsInChildren<Renderer>(includeInactive: true)
            .SelectMany(renderer => renderer.sharedMaterials)
            .Where(material => material != null)!;
    }

    private static IEnumerable<string> CollectTextureAssetPaths(IEnumerable<Material> materials)
    {
        var textureAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var material in materials.Where(material => material != null))
        {
            foreach (var texturePropertyName in material.GetTexturePropertyNames())
            {
                var texture = material.GetTexture(texturePropertyName);
                if (texture == null)
                {
                    continue;
                }

                var assetPath = AssetDatabase.GetAssetPath(texture);
                if (!string.IsNullOrWhiteSpace(assetPath))
                {
                    textureAssetPaths.Add(assetPath);
                }
            }
        }

        return textureAssetPaths;
    }

    private static IEnumerable<string> CollectExplicitShaderAssetPaths(string prefabAssetPath)
    {
        if (string.IsNullOrWhiteSpace(prefabAssetPath))
        {
            return Array.Empty<string>();
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabAssetPath);
        if (prefab == null)
        {
            return Array.Empty<string>();
        }

        return CollectShaderAssetPaths(CollectReferencedMaterials(prefab));
    }

    private static IEnumerable<string> CollectShaderAssetPaths(IEnumerable<Material> materials)
    {
        var shaderAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var material in materials.Where(material => material != null))
        {
            var shader = material.shader;
            if (shader == null)
            {
                continue;
            }

            var shaderAssetPath = AssetDatabase.GetAssetPath(shader);
            if (string.IsNullOrWhiteSpace(shaderAssetPath)
                || !shaderAssetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            shaderAssetPaths.Add(shaderAssetPath);
        }

        return shaderAssetPaths;
    }

    private static void ConfigureModelImporter(string assetPath, bool keepMeshReadable, bool importAnimations = true)
    {
        if (AssetImporter.GetAtPath(assetPath) is not ModelImporter importer)
        {
            throw new InvalidOperationException($"Model importer was not found at '{assetPath}'.");
        }

        var changed = false;
        changed |= SetIfDifferent(
            () => importer.meshCompression,
            value => importer.meshCompression = value,
            keepMeshReadable ? ModelImporterMeshCompression.Off : ModelImporterMeshCompression.High);
        changed |= SetIfDifferent(() => importer.isReadable, value => importer.isReadable = value, keepMeshReadable);
        changed |= SetIfDifferent(() => importer.importCameras, value => importer.importCameras = value, false);
        changed |= SetIfDifferent(() => importer.importLights, value => importer.importLights = value, false);
        changed |= SetIfDifferent(() => importer.importVisibility, value => importer.importVisibility = value, false);
        changed |= SetIfDifferent(() => importer.importAnimation, value => importer.importAnimation = value, importAnimations);
        changed |= SetIfDifferent(() => importer.resampleCurves, value => importer.resampleCurves = value, keepMeshReadable);
        changed |= SetIfDifferent(() => importer.removeConstantScaleCurves, value => importer.removeConstantScaleCurves = value, true);
        changed |= SetIfDifferent(
            () => importer.animationCompression,
            value => importer.animationCompression = value,
            keepMeshReadable ? ModelImporterAnimationCompression.Off : ModelImporterAnimationCompression.Optimal);
        changed |= SetIfDifferent(
            () => importer.animationRotationError,
            value => importer.animationRotationError = value,
            keepMeshReadable ? 0.01f : 0.5f);
        changed |= SetIfDifferent(
            () => importer.animationPositionError,
            value => importer.animationPositionError = value,
            keepMeshReadable ? 0.01f : 0.5f);
        changed |= SetIfDifferent(
            () => importer.animationScaleError,
            value => importer.animationScaleError = value,
            keepMeshReadable ? 0.01f : 0.5f);
        changed |= SetIfDifferent(
            () => importer.importBlendShapeNormals,
            value => importer.importBlendShapeNormals = value,
            keepMeshReadable ? ModelImporterNormals.Import : ModelImporterNormals.None);
        changed |= SetIfDifferent(
            () => importer.importTangents,
            value => importer.importTangents = value,
            keepMeshReadable ? ModelImporterTangents.CalculateMikk : ModelImporterTangents.None);

        if (changed)
        {
            Debug.Log(
                $"[Codex] Optimizing model importer '{assetPath}'. " +
                $"meshCompression={importer.meshCompression}, readable={importer.isReadable}, blendShapeNormals={importer.importBlendShapeNormals}, " +
                $"tangents={importer.importTangents}, importAnimation={importer.importAnimation}, " +
                $"resampleCurves={importer.resampleCurves}, animationCompression={importer.animationCompression}, " +
                $"rotationError={importer.animationRotationError}, positionError={importer.animationPositionError}, scaleError={importer.animationScaleError}.");
            importer.SaveAndReimport();
        }
    }

    private static void ConfigureModelImporterForHighFidelity(string assetPath)
    {
        if (AssetImporter.GetAtPath(assetPath) is not ModelImporter importer)
        {
            throw new InvalidOperationException($"Model importer was not found at '{assetPath}'.");
        }

        var changed = false;
        changed |= SetIfDifferent(() => importer.meshCompression, value => importer.meshCompression = value, ModelImporterMeshCompression.Low);
        changed |= SetIfDifferent(() => importer.isReadable, value => importer.isReadable = value, false);
        changed |= SetIfDifferent(() => importer.importCameras, value => importer.importCameras = value, false);
        changed |= SetIfDifferent(() => importer.importLights, value => importer.importLights = value, false);
        changed |= SetIfDifferent(() => importer.importVisibility, value => importer.importVisibility = value, false);
        changed |= SetIfDifferent(() => importer.importAnimation, value => importer.importAnimation = value, true);
        changed |= SetIfDifferent(() => importer.resampleCurves, value => importer.resampleCurves = value, true);
        changed |= SetIfDifferent(() => importer.removeConstantScaleCurves, value => importer.removeConstantScaleCurves = value, true);
        changed |= SetIfDifferent(
            () => importer.animationCompression,
            value => importer.animationCompression = value,
            ModelImporterAnimationCompression.Off);
        changed |= SetIfDifferent(() => importer.animationRotationError, value => importer.animationRotationError = value, 0.01f);
        changed |= SetIfDifferent(() => importer.animationPositionError, value => importer.animationPositionError = value, 0.01f);
        changed |= SetIfDifferent(() => importer.animationScaleError, value => importer.animationScaleError = value, 0.01f);
        changed |= SetIfDifferent(
            () => importer.importBlendShapeNormals,
            value => importer.importBlendShapeNormals = value,
            ModelImporterNormals.Import);
        changed |= SetIfDifferent(
            () => importer.importTangents,
            value => importer.importTangents = value,
            ModelImporterTangents.None);

        if (changed)
        {
            Debug.Log(
                $"[Codex] Optimizing model importer for bundle '{assetPath}'. " +
                $"meshCompression={importer.meshCompression}, readable={importer.isReadable}, " +
                $"blendShapeNormals={importer.importBlendShapeNormals}, tangents={importer.importTangents}, " +
                $"importAnimation={importer.importAnimation}, resampleCurves={importer.resampleCurves}, " +
                $"animationCompression={importer.animationCompression}, rotationError={importer.animationRotationError}, " +
                $"positionError={importer.animationPositionError}, scaleError={importer.animationScaleError}.");
            importer.SaveAndReimport();
        }
    }

    private static void ConfigureTextureImporter(string assetPath, MikuBundleTextureSampling textureSampling)
    {
        if (AssetImporter.GetAtPath(assetPath) is not TextureImporter importer)
        {
            return;
        }

        var fileName = Path.GetFileName(assetPath);
        var isHighPriority = IsHighPriorityTextureAsset(assetPath);
        var targetSize = isHighPriority ? 2048 : 512;
        var hasAlpha = importer.DoesSourceTextureHaveAlpha();
        var textureSettings = GetTextureImportSettings(textureSampling, isHighPriority, hasAlpha);
        if (textureSettings == null)
        {
            return;
        }

        var targetFormat = textureSettings.Format;
        var targetQuality = textureSettings.CompressionQuality;
        targetSize = textureSettings.MaxTextureSize;
        var changed = false;

        changed |= SetIfDifferent(() => importer.mipmapEnabled, value => importer.mipmapEnabled = value, false);
        changed |= SetIfDifferent(() => importer.streamingMipmaps, value => importer.streamingMipmaps = value, false);
        changed |= SetIfDifferent(() => importer.alphaIsTransparency, value => importer.alphaIsTransparency = value, hasAlpha);
        changed |= SetIfDifferent(() => importer.maxTextureSize, value => importer.maxTextureSize = value, targetSize);

        var standalone = importer.GetPlatformTextureSettings("Standalone");
        standalone.overridden = true;
        standalone.maxTextureSize = targetSize;
        standalone.resizeAlgorithm = TextureResizeAlgorithm.Mitchell;
        standalone.textureCompression = textureSettings.TextureCompression;
        standalone.crunchedCompression = textureSettings.CrunchedCompression;
        standalone.compressionQuality = targetQuality;
        standalone.format = targetFormat;

        var currentStandalone = importer.GetPlatformTextureSettings("Standalone");
        if (!PlatformSettingsEqual(currentStandalone, standalone))
        {
            importer.SetPlatformTextureSettings(standalone);
            changed = true;
        }

        if (changed)
        {
            Debug.Log(
                $"[Codex] Optimizing texture '{fileName}'. preset={textureSampling}, highPriority={isHighPriority}, " +
                $"maxSize={targetSize}, format={targetFormat}, crunchQuality={targetQuality}, alpha={hasAlpha}.");
            importer.SaveAndReimport();
        }
    }

    private static bool IsHighPriorityTextureAsset(string assetPath)
    {
        var fileName = Path.GetFileName(assetPath);
        if (HighPriorityTextureNames.Contains(fileName, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalizedAssetPath = assetPath.Replace("\\", "/");
        return HighPriorityTextureTokens.Any(token =>
            normalizedAssetPath.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static TextureImportSettings GetTextureImportSettings(
        MikuBundleTextureSampling textureSampling,
        bool isHighPriority,
        bool hasAlpha)
    {
        if (hasAlpha && isHighPriority)
        {
            return textureSampling switch
            {
                MikuBundleTextureSampling.Original => null,
                MikuBundleTextureSampling.Compact => new TextureImportSettings(
                    1024,
                    TextureImporterFormat.BC7,
                    100,
                    TextureImporterCompression.CompressedHQ,
                    false),
                _ => new TextureImportSettings(
                    2048,
                    TextureImporterFormat.BC7,
                    100,
                    TextureImporterCompression.CompressedHQ,
                    false),
            };
        }

        var format = hasAlpha ? TextureImporterFormat.DXT5Crunched : TextureImporterFormat.DXT1Crunched;

        switch (textureSampling)
        {
            case MikuBundleTextureSampling.Original:
                return null;

            case MikuBundleTextureSampling.High:
                return new TextureImportSettings(
                    isHighPriority ? 2048 : 1024,
                    format,
                    isHighPriority ? 85 : 70,
                    TextureImporterCompression.Compressed,
                    true);

            case MikuBundleTextureSampling.Compact:
                return new TextureImportSettings(
                    isHighPriority ? 512 : 256,
                    format,
                    isHighPriority ? 45 : 25,
                    TextureImporterCompression.Compressed,
                    true);

            default:
                return new TextureImportSettings(
                    isHighPriority ? 1024 : 512,
                    format,
                    isHighPriority ? 60 : 35,
                    TextureImporterCompression.Compressed,
                    true);
        }
    }

    private static void ConfigureAudioImporter(string assetPath)
    {
        if (AssetImporter.GetAtPath(assetPath) is not AudioImporter importer)
        {
            throw new InvalidOperationException($"Audio importer was not found at '{assetPath}'.");
        }

        var changed = false;
        var settings = importer.defaultSampleSettings;
        var sourceFileSize = ResolveAssetFileSize(assetPath);
        const AudioCompressionFormat targetCompressionFormat = AudioCompressionFormat.Vorbis;
        const float targetQuality = 0.5f;

        if (settings.loadType != AudioClipLoadType.CompressedInMemory)
        {
            settings.loadType = AudioClipLoadType.CompressedInMemory;
            changed = true;
        }

        if (settings.sampleRateSetting != AudioSampleRateSetting.PreserveSampleRate)
        {
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            changed = true;
        }

        if (settings.sampleRateOverride != 44100)
        {
            settings.sampleRateOverride = 44100;
            changed = true;
        }

        if (settings.compressionFormat != targetCompressionFormat)
        {
            settings.compressionFormat = targetCompressionFormat;
            changed = true;
        }

        if (Math.Abs(settings.quality - targetQuality) > 0.0001f)
        {
            settings.quality = targetQuality;
            changed = true;
        }

        if (!settings.preloadAudioData)
        {
            settings.preloadAudioData = true;
            changed = true;
        }

        if (importer.defaultSampleSettings.loadType != settings.loadType
            || importer.defaultSampleSettings.sampleRateSetting != settings.sampleRateSetting
            || importer.defaultSampleSettings.sampleRateOverride != settings.sampleRateOverride
            || importer.defaultSampleSettings.compressionFormat != settings.compressionFormat
            || Math.Abs(importer.defaultSampleSettings.quality - settings.quality) > 0.0001f
            || importer.defaultSampleSettings.preloadAudioData != settings.preloadAudioData)
        {
            importer.defaultSampleSettings = settings;
            changed = true;
        }

        changed |= SetIfDifferent(() => importer.forceToMono, value => importer.forceToMono = value, false);
        changed |= SetIfDifferent(() => importer.loadInBackground, value => importer.loadInBackground = value, false);
        changed |= SetIfDifferent(() => importer.ambisonic, value => importer.ambisonic = value, false);

        if (changed)
        {
            Debug.Log(
                $"[Codex] Optimizing audio importer '{assetPath}'. loadType={settings.loadType}, " +
                $"sampleRate={settings.sampleRateSetting}, format={settings.compressionFormat}, quality={settings.quality:0.##}, " +
                $"preload={settings.preloadAudioData}, mono={importer.forceToMono}, sourceBytes={sourceFileSize}.");
            importer.SaveAndReimport();
        }
    }

    private static long ResolveAssetFileSize(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return 0L;
        }

        var normalizedPath = assetPath.Replace("\\", "/");
        var absolutePath = normalizedPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", normalizedPath))
            : Path.GetFullPath(normalizedPath);
        return File.Exists(absolutePath) ? new FileInfo(absolutePath).Length : 0L;
    }

    private static bool PlatformSettingsEqual(TextureImporterPlatformSettings left, TextureImporterPlatformSettings right)
    {
        return left.overridden == right.overridden
            && left.maxTextureSize == right.maxTextureSize
            && left.resizeAlgorithm == right.resizeAlgorithm
            && left.textureCompression == right.textureCompression
            && left.crunchedCompression == right.crunchedCompression
            && left.compressionQuality == right.compressionQuality
            && left.format == right.format;
    }

    private static bool SetIfDifferent<T>(Func<T> getter, Action<T> setter, T expected)
    {
        if (EqualityComparer<T>.Default.Equals(getter(), expected))
        {
            return false;
        }

        setter(expected);
        return true;
    }

    private static void EnsureAssetFolderExists(string assetFolderPath)
    {
        if (string.IsNullOrWhiteSpace(assetFolderPath))
        {
            throw new ArgumentException("Asset folder path must not be empty.", nameof(assetFolderPath));
        }

        var normalizedPath = assetFolderPath.Replace("\\", "/").TrimEnd('/');
        if (!normalizedPath.StartsWith("Assets", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Asset folder '{assetFolderPath}' must be inside the Assets folder.");
        }

        if (AssetDatabase.IsValidFolder(normalizedPath))
        {
            return;
        }

        var segments = normalizedPath.Split('/');
        var current = segments[0];
        for (var index = 1; index < segments.Length; index++)
        {
            var next = $"{current}/{segments[index]}";
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, segments[index]);
            }

            current = next;
        }
    }

    private static void ClearAssetOutputs(BuildAssetPaths assetPaths)
    {
        if (assetPaths == null)
        {
            throw new ArgumentNullException(nameof(assetPaths));
        }

        AssetDatabase.DeleteAsset(assetPaths.PrefabPath);
        AssetDatabase.DeleteAsset(assetPaths.BodyClipPath);
        AssetDatabase.DeleteAsset(assetPaths.MorphClipPath);
        AssetDatabase.DeleteAsset(assetPaths.FacialClipPath);
        AssetDatabase.DeleteAsset(assetPaths.PlaybackMetadataPath);
        AssetDatabase.DeleteAsset(assetPaths.RuntimeVmdPath);
        AssetDatabase.DeleteAsset(assetPaths.ControllerPath);
        AssetDatabase.DeleteAsset(assetPaths.FacialControllerPath);
        AssetDatabase.DeleteAsset(assetPaths.MaterialFolderPath);
    }

    private static bool ShouldRunImportedModelAutomation()
    {
        var overrideValue = Environment.GetEnvironmentVariable(ImportedModelAutomationEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overrideValue))
        {
            var normalized = overrideValue.Trim();
            if (string.Equals(normalized, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "yes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "on", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(normalized, "0", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "false", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "no", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "off", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return false;
    }

    private static PreparedBodyMotion PrepareBodyMotion(MikuBundleBuildRequest request, GameObject sourcePrefab, string outputClipAssetPath)
    {
        var sourceBodyClip = ResolveSourceBodyAnimationClip(request, sourcePrefab);
        var sourceFrameRate = sourceBodyClip != null && sourceBodyClip.frameRate > 0.01f
            ? sourceBodyClip.frameRate
            : 30f;
        var startSeconds = Mathf.Max(0f, request.MotionStartFrame / sourceFrameRate);
        float? endSeconds = null;
        if (request.HasMotionEndFrame && request.MotionEndFrame > request.MotionStartFrame)
        {
            endSeconds = request.MotionEndFrame / sourceFrameRate;
        }

        var trimRangeActive = request.MotionStartFrame > 0
                              || (request.HasMotionEndFrame && request.MotionEndFrame > request.MotionStartFrame);
        if (sourceBodyClip == null)
        {
            return new PreparedBodyMotion(
                sourceClip: null,
                playbackClip: null,
                playbackClipAssetPath: null,
                sourceFrameRate: sourceFrameRate,
                motionStartFrame: request.MotionStartFrame,
                hasMotionEndFrame: request.HasMotionEndFrame,
                motionEndFrame: request.MotionEndFrame,
                startSeconds: startSeconds,
                endSeconds: endSeconds,
                trimRangeActive: trimRangeActive);
        }

        var sourceBindings = AnimationUtility.GetCurveBindings(sourceBodyClip);
        var sourceBindingCount = sourceBindings.Length;
        var filterOversizedGenericClip = false;
        var useImportedClip = !trimRangeActive && !filterOversizedGenericClip;
        if (useImportedClip)
        {
            Debug.Log(
                $"[Codex] Using imported source body clip '{AssetDatabase.GetAssetPath(sourceBodyClip)}' without standalone export. " +
                $"sourceBindings={sourceBindingCount}, humanMotion={sourceBodyClip.humanMotion}, " +
                "build range starts at frame 0 and has no explicit end frame.");
            return new PreparedBodyMotion(
                sourceClip: sourceBodyClip,
                playbackClip: sourceBodyClip,
                playbackClipAssetPath: null,
                sourceFrameRate: sourceFrameRate,
                motionStartFrame: request.MotionStartFrame,
                hasMotionEndFrame: request.HasMotionEndFrame,
                motionEndFrame: request.MotionEndFrame,
                startSeconds: startSeconds,
                endSeconds: endSeconds,
                trimRangeActive: trimRangeActive);
        }

        if (filterOversizedGenericClip)
        {
            var retainedBindingCount = sourceBindings.Count(ShouldKeepEssentialBodyCurve);
            Debug.Log(
                $"[Codex] Source body clip '{sourceBodyClip.name}' is a large Generic clip; exporting optimized body-only curves. " +
                $"sourceBindings={sourceBindingCount}, retainedBindings={retainedBindingCount}.");
        }

        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(outputClipAssetPath) != null)
        {
            AssetDatabase.DeleteAsset(outputClipAssetPath);
        }

        var exportedClip = filterOversizedGenericClip
            ? MikuAnimationClipTrimUtility.ExportFilteredClipAsset(
                sourceBodyClip,
                outputClipAssetPath,
                startSeconds,
                endSeconds,
                ShouldKeepEssentialBodyCurve,
                "MikuLobbyBody",
                simplifyFloatCurves: true)
            : MikuAnimationClipTrimUtility.ExportTrimmedClipAsset(
                sourceBodyClip,
                outputClipAssetPath,
                startSeconds,
                endSeconds,
                "MikuLobbyBody");
        var exportedBindingCount = AnimationUtility.GetCurveBindings(exportedClip).Length;

        Debug.Log(
            $"[Codex] Exported standalone body clip '{outputClipAssetPath}' from '{AssetDatabase.GetAssetPath(sourceBodyClip)}'. " +
            $"startFrame={request.MotionStartFrame}, endFrame={(request.HasMotionEndFrame ? request.MotionEndFrame.ToString() : "(none)")}, " +
            $"startSeconds={startSeconds:0.###}, endSeconds={(endSeconds.HasValue ? endSeconds.Value.ToString("0.###") : "(none)")}, " +
            $"sourceLength={sourceBodyClip.length:0.###}, exportedLength={exportedClip.length:0.###}, " +
            $"trimRangeActive={trimRangeActive}, filteredBodyOnly={filterOversizedGenericClip}, " +
            $"sourceBindings={sourceBindingCount}, exportedBindings={exportedBindingCount}.");
        return new PreparedBodyMotion(
            sourceBodyClip,
            exportedClip,
            outputClipAssetPath,
            sourceFrameRate,
            request.MotionStartFrame,
            request.HasMotionEndFrame,
            request.MotionEndFrame,
            startSeconds,
            endSeconds,
            trimRangeActive);
    }

    private static bool ShouldExportFilteredBodyClip(AnimationClip clip, int curveBindingCount)
    {
        return clip != null
            && !clip.humanMotion
            && curveBindingCount >= OversizedGenericBodyClipBindingThreshold;
    }

    private static bool ShouldKeepEssentialBodyCurve(EditorCurveBinding binding)
    {
        if (binding.type != typeof(Transform))
        {
            return false;
        }

        if (binding.propertyName.IndexOf("m_LocalScale", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(binding.path))
        {
            return true;
        }

        var boneName = NormalizeBoneName(GetLastPathSegment(binding.path));
        if (ShouldExcludeBodyCurveBone(boneName))
        {
            return false;
        }

        var isPositionCurve = binding.propertyName.IndexOf("m_LocalPosition", StringComparison.OrdinalIgnoreCase) >= 0;
        var tokens = isPositionCurve
            ? EssentialBodyPositionCurvePathTokens
            : EssentialBodyCurvePathTokens;

        return tokens.Any(token => MatchesBoneToken(boneName, token));
    }

    private static bool ShouldExcludeBodyCurveBone(string boneName)
    {
        if (string.IsNullOrWhiteSpace(boneName))
        {
            return false;
        }

        return ExcludedBodyCurvePathTokens.Any(token =>
            boneName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static string NormalizeBoneName(string boneName)
    {
        if (string.IsNullOrWhiteSpace(boneName))
        {
            return string.Empty;
        }

        var normalized = boneName.Trim();
        var separatorIndex = normalized.IndexOf('.');
        if (separatorIndex > 0)
        {
            var numericPrefix = normalized.Substring(0, separatorIndex);
            if (numericPrefix.All(char.IsDigit))
            {
                normalized = normalized.Substring(separatorIndex + 1);
            }
        }

        while (normalized.Length > 0 && normalized[0] == '!')
        {
            normalized = normalized.Substring(1);
        }

        return normalized;
    }

    private static bool MatchesBoneToken(string boneName, string token)
    {
        if (string.IsNullOrWhiteSpace(boneName) || string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        if (!boneName.StartsWith(token, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (boneName.Length == token.Length)
        {
            return true;
        }

        var nextCharacter = boneName[token.Length];
        var tokenEndsWithDigit = char.IsDigit(token[token.Length - 1]);
        return tokenEndsWithDigit
            ? !char.IsLetterOrDigit(nextCharacter)
            : !char.IsLetter(nextCharacter);
    }

    private static string GetLastPathSegment(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var separatorIndex = path.LastIndexOf('/');
        return separatorIndex >= 0 && separatorIndex < path.Length - 1
            ? path.Substring(separatorIndex + 1)
            : path;
    }

    private static string? PreparePlaybackMetadataAsset(
        MikuBundleBuildRequest request,
        PreparedBodyMotion bodyMotion,
        AudioClip? selectedAudioClip,
        string outputMetadataAssetPath)
    {
        var sourceFrameRate = bodyMotion.SourceFrameRate > 0.01f ? bodyMotion.SourceFrameRate : 30f;
        var audioSegmentStartSeconds = Mathf.Max(0f, request.MotionStartFrame / sourceFrameRate);
        var desiredSegmentDuration = ResolvePreparedMotionSegmentDuration(bodyMotion);
        var audioSegmentDuration = selectedAudioClip != null
            ? Mathf.Min(desiredSegmentDuration > 0.01f ? desiredSegmentDuration : Mathf.Max(0f, selectedAudioClip.length - audioSegmentStartSeconds),
                Mathf.Max(0f, selectedAudioClip.length - audioSegmentStartSeconds))
            : Mathf.Max(0f, desiredSegmentDuration);

        var metadata = new PlaybackMetadataJson
        {
            SourceFrameRate = sourceFrameRate,
            MotionStartFrame = request.MotionStartFrame,
            HasMotionEndFrame = bodyMotion.HasMotionEndFrame && bodyMotion.MotionEndFrame > bodyMotion.MotionStartFrame,
            MotionEndFrame = bodyMotion.HasMotionEndFrame ? bodyMotion.MotionEndFrame : 0,
            AudioSegmentStartSeconds = audioSegmentStartSeconds,
            AudioSegmentDurationSeconds = audioSegmentDuration,
        };

        var absolutePath = Path.GetFullPath(outputMetadataAssetPath);
        File.WriteAllText(absolutePath, JsonUtility.ToJson(metadata, prettyPrint: true));
        AssetDatabase.ImportAsset(outputMetadataAssetPath, ImportAssetOptions.ForceUpdate);
        Debug.Log(
            $"[Codex] Generated playback metadata '{outputMetadataAssetPath}'. " +
            $"startFrame={metadata.MotionStartFrame}, endFrame={(metadata.HasMotionEndFrame ? metadata.MotionEndFrame.ToString() : "(none)")}, " +
            $"frameRate={metadata.SourceFrameRate:0.###}, audioStart={metadata.AudioSegmentStartSeconds:0.###}, " +
            $"audioDuration={metadata.AudioSegmentDurationSeconds:0.###}.");
        return outputMetadataAssetPath;
    }

    private static string? PrepareRuntimeVmdAsset(
        MikuBundleBuildRequest request,
        AnimationClip? sourceBodyClip,
        string outputRuntimeVmdAssetPath)
    {
        var sourceVmdAssetPath = ResolveRuntimeVmdSourceAssetPath(request, sourceBodyClip);
        if (string.IsNullOrWhiteSpace(sourceVmdAssetPath))
        {
            Debug.LogWarning(
                "[Codex] No matching source VMD file was found. The bundle will keep Animator playback, " +
                "so the FBX animation payload may remain large.");
            return null;
        }

        var sourceAbsolutePath = AssetPathToAbsolutePath(sourceVmdAssetPath);
        var outputAbsolutePath = AssetPathToAbsolutePath(outputRuntimeVmdAssetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputAbsolutePath) ?? string.Empty);
        File.Copy(sourceAbsolutePath, outputAbsolutePath, overwrite: true);
        AssetDatabase.ImportAsset(outputRuntimeVmdAssetPath, ImportAssetOptions.ForceUpdate);

        var importedBytes = AssetDatabase.LoadAssetAtPath<TextAsset>(outputRuntimeVmdAssetPath);
        if (importedBytes == null)
        {
            throw new InvalidOperationException($"Failed to import runtime VMD bytes as a TextAsset at '{outputRuntimeVmdAssetPath}'.");
        }

        Debug.Log(
            $"[Codex] Prepared runtime VMD motion '{outputRuntimeVmdAssetPath}' from '{sourceVmdAssetPath}'. " +
            $"bytes={importedBytes.bytes?.Length ?? 0}.");
        return outputRuntimeVmdAssetPath;
    }

    private static string? ResolveRuntimeVmdSourceAssetPath(MikuBundleBuildRequest request, AnimationClip? sourceBodyClip)
    {
        var candidateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddRuntimeVmdCandidateName(candidateNames, sourceBodyClip?.name);
        AddRuntimeVmdCandidateName(candidateNames, Path.GetFileNameWithoutExtension(request.BodyAnimationClipAssetPath));

        if (candidateNames.Count == 0)
        {
            return null;
        }

        var searchFolders = new[]
            {
                Path.GetDirectoryName(request.BackgroundMusicAssetPath)?.Replace('\\', '/'),
                "Assets/Motions",
                "Assets",
            }
            .Where(path => !string.IsNullOrWhiteSpace(path) && AssetDatabase.IsValidFolder(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var folder in searchFolders)
        {
            var exactMatch = AssetDatabase.FindAssets(string.Empty, new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith(".vmd", StringComparison.OrdinalIgnoreCase))
                .Where(path => candidateNames.Contains(Path.GetFileNameWithoutExtension(path)))
                .OrderBy(path => path.Length)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(exactMatch))
            {
                return exactMatch;
            }
        }

        return null;
    }

    private static void AddRuntimeVmdCandidateName(ISet<string> candidateNames, string? rawName)
    {
        if (candidateNames == null || string.IsNullOrWhiteSpace(rawName))
        {
            return;
        }

        var name = rawName.Trim();
        candidateNames.Add(name);
        if (name.EndsWith("_vmd", StringComparison.OrdinalIgnoreCase))
        {
            candidateNames.Add(name.Substring(0, name.Length - "_vmd".Length));
        }

        if (name.EndsWith(".vmd", StringComparison.OrdinalIgnoreCase))
        {
            candidateNames.Add(Path.GetFileNameWithoutExtension(name));
        }
    }

    private static string? ResolvePreparedRuntimeVmdAssetPath(string preparedAssetFolderPath)
    {
        if (string.IsNullOrWhiteSpace(preparedAssetFolderPath))
        {
            return null;
        }

        var candidate = $"{preparedAssetFolderPath.TrimEnd('/', '\\')}/MikuLobbyMotion.vmd.bytes";
        return AssetDatabase.LoadAssetAtPath<TextAsset>(candidate) != null ? candidate : null;
    }

    private static string AssetPathToAbsolutePath(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Expected a project asset path under Assets/: '{assetPath}'.", nameof(assetPath));
        }

        var projectRoot = Directory.GetParent(Application.dataPath)?.FullName
            ?? throw new InvalidOperationException("Could not resolve the Unity project root from Application.dataPath.");
        return Path.GetFullPath(Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static float ResolvePreparedMotionSegmentDuration(PreparedBodyMotion bodyMotion)
    {
        var clipLength = bodyMotion.SourceClip != null
            ? Mathf.Max(0f, bodyMotion.SourceClip.length)
            : bodyMotion.PlaybackClip != null
                ? Mathf.Max(0f, bodyMotion.PlaybackClip.length)
                : 0f;
        if (clipLength <= 0.01f)
        {
            return bodyMotion.HasMotionEndFrame && bodyMotion.MotionEndFrame > bodyMotion.MotionStartFrame
                ? Mathf.Max(0f, (bodyMotion.MotionEndFrame - bodyMotion.MotionStartFrame) / Mathf.Max(0.01f, bodyMotion.SourceFrameRate))
                : 0f;
        }

        var safeStartSeconds = Mathf.Clamp(bodyMotion.StartSeconds, 0f, clipLength);
        var safeEndSeconds = bodyMotion.EndSeconds.HasValue
            ? Mathf.Clamp(bodyMotion.EndSeconds.Value, safeStartSeconds, clipLength)
            : clipLength;
        var segmentDuration = Mathf.Max(0f, safeEndSeconds - safeStartSeconds);
        return segmentDuration > 0.01f ? segmentDuration : clipLength;
    }

    private static string? PrepareMorphClipAsset(
        MikuBundleBuildRequest request,
        GameObject sourcePrefab,
        PreparedBodyMotion bodyMotion,
        string outputMorphClipAssetPath)
    {
        var autoSourceClip = ResolveAutoMorphSourceClip(request, sourcePrefab, bodyMotion);
        if (autoSourceClip != null)
        {
            var exportedClip = ExportMikuLobbyMorphClip.ExportToAsset(
                autoSourceClip,
                outputMorphClipAssetPath,
                out var copiedBindingCount,
                out var copiedKeyCount,
                out var bindingSamples);
            if (bodyMotion.TrimRangeActive)
            {
                exportedClip = MikuAnimationClipTrimUtility.ExportTrimmedClipAsset(
                    exportedClip,
                    outputMorphClipAssetPath,
                    bodyMotion.StartSeconds,
                    bodyMotion.EndSeconds,
                    "MikuLobbyMorph");
            }

            RemapMorphClipBindingsToWrappedModelRoot(exportedClip, outputMorphClipAssetPath);
            EnsureLoopingAnimationClip(exportedClip, outputMorphClipAssetPath);

            Debug.Log(
                $"[Codex] Auto-generated facial clip '{outputMorphClipAssetPath}' from '{AssetDatabase.GetAssetPath(autoSourceClip)}'. " +
                $"bindings={copiedBindingCount}, keys={copiedKeyCount}, samples=[{string.Join(", ", bindingSamples)}].");
            return AssetDatabase.GetAssetPath(exportedClip);
        }

        var sourceAssetPath = request.FacialAnimationClipAssetPath;
        if (string.IsNullOrWhiteSpace(sourceAssetPath))
        {
            return null;
        }

        var sourceClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(sourceAssetPath);
        if (sourceClip == null)
        {
            throw new InvalidOperationException($"Facial AnimationClip asset was not found at '{sourceAssetPath}'.");
        }

        if (bodyMotion.TrimRangeActive)
        {
            var trimmedClip = MikuAnimationClipTrimUtility.ExportTrimmedClipAsset(
                sourceClip,
                outputMorphClipAssetPath,
                bodyMotion.StartSeconds,
                bodyMotion.EndSeconds,
                "MikuLobbyMorph");
            RemapMorphClipBindingsToWrappedModelRoot(trimmedClip, outputMorphClipAssetPath);
            EnsureLoopingAnimationClip(trimmedClip, outputMorphClipAssetPath);
            Debug.Log(
                $"[Codex] Trimmed facial clip '{sourceAssetPath}' into '{outputMorphClipAssetPath}'. " +
                $"trimmedLength={trimmedClip.length:0.###}.");
            return outputMorphClipAssetPath;
        }

        AssetDatabase.DeleteAsset(outputMorphClipAssetPath);
        if (!AssetDatabase.CopyAsset(sourceAssetPath, outputMorphClipAssetPath))
        {
            throw new InvalidOperationException($"Failed to copy facial clip from '{sourceAssetPath}' to '{outputMorphClipAssetPath}'.");
        }

        AssetDatabase.ImportAsset(outputMorphClipAssetPath, ImportAssetOptions.ForceUpdate);
        RemapMorphClipBindingsToWrappedModelRoot(
            AssetDatabase.LoadAssetAtPath<AnimationClip>(outputMorphClipAssetPath),
            outputMorphClipAssetPath);
        EnsureLoopingAnimationClip(
            AssetDatabase.LoadAssetAtPath<AnimationClip>(outputMorphClipAssetPath),
            outputMorphClipAssetPath);
        Debug.Log($"[Codex] Copied facial clip '{sourceAssetPath}' to '{outputMorphClipAssetPath}'.");
        return outputMorphClipAssetPath;
    }

    private static string? PrepareNativeFacialClipAsset(
        MikuBundleBuildRequest request,
        GameObject sourcePrefab,
        PreparedBodyMotion bodyMotion,
        string outputFacialClipAssetPath)
    {
        var autoSourceClip = ResolveAutoMorphSourceClip(request, sourcePrefab, bodyMotion);
        if (autoSourceClip != null)
        {
            var exportedClip = ExportMikuLobbyMorphClip.ExportToAsset(
                autoSourceClip,
                outputFacialClipAssetPath,
                out var copiedBindingCount,
                out var copiedKeyCount,
                out var bindingSamples);
            if (bodyMotion.TrimRangeActive)
            {
                exportedClip = MikuAnimationClipTrimUtility.ExportTrimmedClipAsset(
                    exportedClip,
                    outputFacialClipAssetPath,
                    bodyMotion.StartSeconds,
                    bodyMotion.EndSeconds,
                    "MikuLobbyFacial");
            }

            EnsureLoopingAnimationClip(exportedClip, outputFacialClipAssetPath);
            Debug.Log(
                $"[Codex] Prepared native facial clip '{outputFacialClipAssetPath}' from '{AssetDatabase.GetAssetPath(autoSourceClip)}'. " +
                $"bindings={copiedBindingCount}, keys={copiedKeyCount}, samples=[{string.Join(", ", bindingSamples)}].");
            return AssetDatabase.GetAssetPath(exportedClip);
        }

        var sourceAssetPath = request.FacialAnimationClipAssetPath;
        if (string.IsNullOrWhiteSpace(sourceAssetPath))
        {
            return null;
        }

        var sourceClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(sourceAssetPath);
        if (sourceClip == null)
        {
            throw new InvalidOperationException($"Facial AnimationClip asset was not found at '{sourceAssetPath}'.");
        }

        if (bodyMotion.TrimRangeActive)
        {
            var trimmedClip = MikuAnimationClipTrimUtility.ExportTrimmedClipAsset(
                sourceClip,
                outputFacialClipAssetPath,
                bodyMotion.StartSeconds,
                bodyMotion.EndSeconds,
                "MikuLobbyFacial");
            EnsureLoopingAnimationClip(trimmedClip, outputFacialClipAssetPath);
            Debug.Log(
                $"[Codex] Trimmed native facial clip '{sourceAssetPath}' into '{outputFacialClipAssetPath}'. " +
                $"trimmedLength={trimmedClip.length:0.###}.");
            return outputFacialClipAssetPath;
        }

        AssetDatabase.DeleteAsset(outputFacialClipAssetPath);
        if (!AssetDatabase.CopyAsset(sourceAssetPath, outputFacialClipAssetPath))
        {
            throw new InvalidOperationException($"Failed to copy native facial clip from '{sourceAssetPath}' to '{outputFacialClipAssetPath}'.");
        }

        AssetDatabase.ImportAsset(outputFacialClipAssetPath, ImportAssetOptions.ForceUpdate);
        EnsureLoopingAnimationClip(
            AssetDatabase.LoadAssetAtPath<AnimationClip>(outputFacialClipAssetPath),
            outputFacialClipAssetPath);
        Debug.Log($"[Codex] Copied native facial clip '{sourceAssetPath}' to '{outputFacialClipAssetPath}'.");
        return outputFacialClipAssetPath;
    }

    private static void EnsureLoopingAnimationClip(AnimationClip? clip, string clipAssetPath)
    {
        if (clip == null)
        {
            return;
        }

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        settings.loopBlend = false;
        settings.stopTime = Mathf.Max(settings.stopTime, clip.length);
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        clip.wrapMode = WrapMode.Loop;
        clip.EnsureQuaternionContinuity();
        EditorUtility.SetDirty(clip);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(clipAssetPath, ImportAssetOptions.ForceUpdate);
    }

    private static void RemapMorphClipBindingsToWrappedModelRoot(AnimationClip? clip, string clipAssetPath)
    {
        if (clip == null)
        {
            return;
        }

        var remappedFloatBindings = 0;
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            var remappedPath = RemapMorphBindingPath(binding.path);
            if (string.Equals(remappedPath, binding.path, StringComparison.Ordinal))
            {
                continue;
            }

            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            var remappedBinding = binding;
            remappedBinding.path = remappedPath;
            AnimationUtility.SetEditorCurve(clip, binding, null);
            AnimationUtility.SetEditorCurve(clip, remappedBinding, curve);
            remappedFloatBindings++;
        }

        var remappedObjectBindings = 0;
        foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
        {
            var remappedPath = RemapMorphBindingPath(binding.path);
            if (string.Equals(remappedPath, binding.path, StringComparison.Ordinal))
            {
                continue;
            }

            var keyframes = AnimationUtility.GetObjectReferenceCurve(clip, binding);
            var remappedBinding = binding;
            remappedBinding.path = remappedPath;
            AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
            AnimationUtility.SetObjectReferenceCurve(clip, remappedBinding, keyframes);
            remappedObjectBindings++;
        }

        if (remappedFloatBindings == 0 && remappedObjectBindings == 0)
        {
            return;
        }

        EditorUtility.SetDirty(clip);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(clipAssetPath, ImportAssetOptions.ForceUpdate);
        Debug.Log(
            $"[Codex] Remapped facial clip bindings for wrapped prefab root. clip='{clip.name}', asset='{clipAssetPath}', " +
            $"floatBindings={remappedFloatBindings}, objectBindings={remappedObjectBindings}, targetRoot='{WrappedModelRootName}'.");
    }

    private static string RemapMorphBindingPath(string? originalPath)
    {
        if (string.IsNullOrWhiteSpace(originalPath))
        {
            return WrappedModelRootName;
        }

        if (string.Equals(originalPath, WrappedModelRootName, StringComparison.Ordinal)
            || originalPath.StartsWith($"{WrappedModelRootName}/", StringComparison.Ordinal))
        {
            return originalPath;
        }

        return $"{WrappedModelRootName}/{originalPath}";
    }

    private static AnimationClip? ResolveAutoMorphSourceClip(
        MikuBundleBuildRequest request,
        GameObject sourcePrefab,
        PreparedBodyMotion bodyMotion)
    {
        if (ExportMikuLobbyMorphClip.ContainsBlendShapeCurves(bodyMotion.PlaybackClip))
        {
            return bodyMotion.PlaybackClip;
        }

        var explicitController = string.IsNullOrWhiteSpace(request.BodyControllerAssetPath)
            ? null
            : AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(request.BodyControllerAssetPath);
        var controllerMorphClip = TryResolveMorphClipFromController(explicitController);
        if (controllerMorphClip != null)
        {
            return controllerMorphClip;
        }

        var sourceAnimator = sourcePrefab.GetComponentInChildren<Animator>(includeInactive: true);
        var embeddedMorphClip = TryResolveMorphClipFromController(sourceAnimator != null ? sourceAnimator.runtimeAnimatorController : null);
        if (embeddedMorphClip != null)
        {
            return embeddedMorphClip;
        }

        return null;
    }

    private static AnimationClip? ResolveSourceBodyAnimationClip(MikuBundleBuildRequest request, GameObject sourcePrefab)
    {
        if (!string.IsNullOrWhiteSpace(request.BodyAnimationClipAssetPath))
        {
            var explicitBodyClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(request.BodyAnimationClipAssetPath);
            if (IsCandidateBodyAnimationClip(explicitBodyClip))
            {
                return explicitBodyClip;
            }

            if (explicitBodyClip != null)
            {
                Debug.LogWarning(
                    $"[Codex] Ignoring explicit body animation clip '{request.BodyAnimationClipAssetPath}' because it has no usable body curves. " +
                    "Falling back to the display prefab AnimatorController.");
            }
        }

        if (!string.IsNullOrWhiteSpace(request.BodyControllerAssetPath))
        {
            var explicitController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(request.BodyControllerAssetPath);
            var controllerClip = TryResolvePrimaryBodyClipFromController(explicitController);
            if (controllerClip != null)
            {
                return controllerClip;
            }
        }

        var sourceAnimator = sourcePrefab.GetComponentInChildren<Animator>(includeInactive: true);
        var animatorClip = TryResolvePrimaryBodyClipFromController(sourceAnimator != null ? sourceAnimator.runtimeAnimatorController : null);
        if (animatorClip != null)
        {
            return animatorClip;
        }

        return TryResolveSuggestedBodyAnimationClip(sourcePrefab);
    }

    private static AnimationClip? TryResolveMorphClipFromController(RuntimeAnimatorController controller)
    {
        if (controller == null)
        {
            return null;
        }

        return controller.animationClips
            .Where(clip => clip != null)
            .Distinct()
            .Where(ExportMikuLobbyMorphClip.ContainsBlendShapeCurves)
            .OrderByDescending(clip => clip.length)
            .FirstOrDefault();
    }

    private static AnimationClip? TryResolvePrimaryBodyClipFromController(RuntimeAnimatorController? controller)
    {
        if (controller == null)
        {
            return null;
        }

        return controller.animationClips
            .Where(IsCandidateBodyAnimationClip)
            .Distinct()
            .OrderByDescending(clip => clip.length)
            .FirstOrDefault();
    }

    private static AudioClip? LoadSelectedAudioClip(string? audioAssetPath)
    {
        return string.IsNullOrWhiteSpace(audioAssetPath)
            ? null
            : AssetDatabase.LoadAssetAtPath<AudioClip>(audioAssetPath);
    }

    private static IReadOnlyList<ResolvedMaterialOverride> ResolveMaterialOverrides(
        IReadOnlyList<MikuBundleBuildMaterialOverrideRequest> overrides)
    {
        var resolved = new List<ResolvedMaterialOverride>(overrides.Count);
        foreach (var item in overrides)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.RendererPath))
            {
                continue;
            }

            var materials = item.MaterialAssetPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => AssetDatabase.LoadAssetAtPath<Material>(path))
                .Where(material => material != null)
                .ToArray();
            if (materials.Length == 0)
            {
                continue;
            }

            resolved.Add(new ResolvedMaterialOverride(item.RendererPath, materials!));
        }

        return resolved;
    }

    private static void ApplyMaterialOverrides(GameObject root, IReadOnlyList<ResolvedMaterialOverride> overrides)
    {
        foreach (var item in overrides)
        {
            var target = root.transform.Find(item.RendererPath);
            if (target == null)
            {
                Debug.LogWarning($"[Codex] Material override path was not found: '{item.RendererPath}'.");
                continue;
            }

            var renderer = target.GetComponent<Renderer>();
            if (renderer == null)
            {
                Debug.LogWarning($"[Codex] Material override target '{item.RendererPath}' does not have a Renderer component.");
                continue;
            }

            renderer.sharedMaterials = GetAlignedMaterialsForRenderer(renderer, item.Materials);
            Debug.Log(
                $"[Codex] Applied material override to '{item.RendererPath}'. materials=[{string.Join(", ", item.Materials.Select(material => material.name))}].");
        }
    }

    private static void ConfigureAudioSources(GameObject root, string audioAssetPath)
    {
        var selectedAudioClip = string.IsNullOrWhiteSpace(audioAssetPath)
            ? null
            : AssetDatabase.LoadAssetAtPath<AudioClip>(audioAssetPath);
        var audioSources = root.GetComponentsInChildren<AudioSource>(includeInactive: true).ToList();

        AudioSource primarySource = audioSources.FirstOrDefault();
        if (primarySource == null && selectedAudioClip != null)
        {
            primarySource = root.AddComponent<AudioSource>();
            audioSources.Add(primarySource);
        }

        for (var index = audioSources.Count - 1; index >= 0; index--)
        {
            var audioSource = audioSources[index];
            if (audioSource == primarySource)
            {
                continue;
            }

            UnityEngine.Object.DestroyImmediate(audioSource, allowDestroyingAssets: true);
            audioSources.RemoveAt(index);
            Debug.Log($"[Codex] Removed extra AudioSource from '{root.name}' while normalizing bundle content.");
        }

        if (primarySource == null)
        {
            return;
        }

        if (selectedAudioClip != null)
        {
            primarySource.clip = selectedAudioClip;
        }

        var clip = primarySource.clip ?? primarySource.resource as AudioClip;
        primarySource.playOnAwake = false;
        primarySource.loop = false;
        primarySource.spatialBlend = 0f;
        primarySource.dopplerLevel = 0f;

        Debug.Log(
            $"[Codex] AudioSource '{primarySource.name}' preserved. clip='{clip?.name ?? "(none)"}', " +
            $"length={clip?.length ?? 0f:0.###}, volume={primarySource.volume:0.###}, resource='{primarySource.resource?.name ?? "(none)"}'.");
    }

    private static void ConfigureRealtimeLightingRenderers(GameObject root)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(includeInactive: true))
        {
            GameObjectUtility.SetStaticEditorFlags(transform.gameObject, 0);
        }

        var rendererCount = 0;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(includeInactive: true))
        {
            rendererCount++;
            renderer.receiveShadows = true;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            renderer.allowOcclusionWhenDynamic = true;
            EditorUtility.SetDirty(renderer);
        }

        Debug.Log(
            $"[Codex] Configured realtime lighting for model renderers. " +
            $"renderers={rendererCount}, receiveShadows=True, shadowCastingMode=On, " +
            $"lightProbeUsage=BlendProbes, reflectionProbeUsage=BlendProbes, staticFlags=0.");
    }

    private static string EnsureAnimatorControllerAssigned(
        GameObject root,
        MikuBundleBuildRequest request,
        PreparedBodyMotion bodyMotion,
        string outputControllerAssetPath)
    {
        var animators = root.GetComponentsInChildren<Animator>(includeInactive: true);
        var animator = ResolvePrimaryAnimator(animators);
        if (animator == null)
        {
            animator = root.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Debug.LogWarning(
                $"[Codex] Display asset '{request.DisplayAssetPath}' does not contain an Animator component. " +
                "A temporary Animator was added to the root object for bundle build.");
        }

        RuntimeAnimatorController controller = null;
        string controllerAssetPath = null;

        if (!string.IsNullOrWhiteSpace(bodyMotion.PlaybackClipAssetPath))
        {
            controller = CreateAnimatorControllerFromClip(bodyMotion.PlaybackClipAssetPath, outputControllerAssetPath);
            controllerAssetPath = outputControllerAssetPath;
        }
        else if (!string.IsNullOrWhiteSpace(request.BodyControllerAssetPath))
        {
            controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(request.BodyControllerAssetPath);
            if (controller == null)
            {
                throw new InvalidOperationException(
                    $"Body controller asset '{request.BodyControllerAssetPath}' is not a valid RuntimeAnimatorController.");
            }

            controllerAssetPath = request.BodyControllerAssetPath;
        }
        else if (!string.IsNullOrWhiteSpace(bodyMotion.PlaybackClipAssetPath))
        {
            controller = CreateAnimatorControllerFromClip(bodyMotion.PlaybackClipAssetPath, outputControllerAssetPath);
            controllerAssetPath = outputControllerAssetPath;
        }
        else if (animator.runtimeAnimatorController != null)
        {
            controller = animator.runtimeAnimatorController;
            controllerAssetPath = AssetDatabase.GetAssetPath(controller);
        }

        if (controller == null)
        {
            throw new InvalidOperationException("No body animation controller or animation clip is available for the selected display asset.");
        }

        animator.runtimeAnimatorController = controller;
        Debug.Log($"[Codex] Assigned AnimatorController '{controller.name}' to the temporary prefab.");
        return string.IsNullOrWhiteSpace(controllerAssetPath) ? null : controllerAssetPath;
    }

    private static string? EnsureFacialAnimatorControllersAssigned(
        GameObject root,
        string? facialClipAssetPath,
        string outputControllerAssetPath)
    {
        if (string.IsNullOrWhiteSpace(facialClipAssetPath))
        {
            return null;
        }

        var facialClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(facialClipAssetPath);
        if (facialClip == null)
        {
            throw new InvalidOperationException($"Native facial AnimationClip asset was not found at '{facialClipAssetPath}'.");
        }

        EnsureLoopingAnimationClip(facialClip, facialClipAssetPath);
        var facialController = CreateAnimatorControllerFromClip(facialClipAssetPath, outputControllerAssetPath);
        var animators = root.GetComponentsInChildren<Animator>(includeInactive: true);
        var primaryAnimator = ResolvePrimaryAnimator(animators);
        var secondaryAnimators = animators
            .Where(animator => animator != null && !ReferenceEquals(animator, primaryAnimator))
            .ToArray();
        if (secondaryAnimators.Length == 0)
        {
            return null;
        }

        var assignedAnimators = secondaryAnimators
            .Where(IsFacialAnimatorCandidate)
            .ToList();
        if (assignedAnimators.Count == 0)
        {
            assignedAnimators.AddRange(secondaryAnimators);
        }

        foreach (var animator in assignedAnimators)
        {
            animator.runtimeAnimatorController = facialController;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.updateMode = AnimatorUpdateMode.Normal;
        }

        Debug.Log(
            $"[Codex] Assigned facial AnimatorController '{facialController.name}' to {assignedAnimators.Count} secondary Animator(s). " +
            $"clip='{facialClip.name}', asset='{facialClipAssetPath}'.");
        return outputControllerAssetPath;
    }

    private static RuntimeAnimatorController CreateAnimatorControllerFromClip(string clipAssetPath, string assetPath)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipAssetPath);
        if (clip == null)
        {
            throw new InvalidOperationException($"AnimationClip asset was not found at '{clipAssetPath}'.");
        }

        AssetDatabase.DeleteAsset(assetPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(assetPath);
        var layers = controller.layers;
        var baseLayer = layers[0];
        baseLayer.defaultWeight = 1f;
        controller.layers = layers;
        var stateMachine = baseLayer.stateMachine;
        foreach (var childState in stateMachine.states)
        {
            stateMachine.RemoveState(childState.state);
        }

        var state = stateMachine.AddState(string.IsNullOrWhiteSpace(clip.name) ? "BodyMotion" : clip.name);
        state.motion = clip;
        state.writeDefaultValues = true;
        stateMachine.defaultState = state;

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        Debug.Log($"[Codex] Created temporary AnimatorController '{assetPath}' from AnimationClip '{clipAssetPath}'.");

        return AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(assetPath)
            ?? throw new InvalidOperationException($"Failed to load AnimatorController from '{assetPath}'.");
    }

    private static Animator? ResolvePrimaryAnimator(IEnumerable<Animator> animators)
    {
        return (animators ?? Array.Empty<Animator>())
            .Where(animator => animator != null)
            .OrderByDescending(GetPrimaryAnimatorScore)
            .FirstOrDefault();
    }

    private static int GetPrimaryAnimatorScore(Animator animator)
    {
        if (animator == null)
        {
            return int.MinValue;
        }

        var score = 0;
        var controller = animator.runtimeAnimatorController;
        if (TryResolvePrimaryBodyClipFromController(controller) != null)
        {
            score += 1000;
        }

        if (controller != null && !ContainsFacialAnimatorToken(controller.name))
        {
            score += 100;
        }

        if (!ContainsFacialAnimatorToken(animator.name))
        {
            score += 10;
        }

        return score;
    }

    private static bool IsFacialAnimatorCandidate(Animator animator)
    {
        if (animator == null)
        {
            return false;
        }

        if (ContainsFacialAnimatorToken(animator.name))
        {
            return true;
        }

        if (ContainsFacialAnimatorToken(BuildTransformPath(animator.transform, animator.transform.root)))
        {
            return true;
        }

        var controller = animator.runtimeAnimatorController;
        if (controller == null)
        {
            return false;
        }

        if (ContainsFacialAnimatorToken(controller.name))
        {
            return true;
        }

        return controller.animationClips.Any(clip =>
            clip != null
            && (ExportMikuLobbyMorphClip.ContainsBlendShapeCurves(clip)
                || ContainsFacialAnimatorToken(clip.name)));
    }

    private static bool ContainsFacialAnimatorToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        for (var index = 0; index < FacialAnimatorTokens.Length; index++)
        {
            if (value.IndexOf(FacialAnimatorTokens[index], StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildTransformPath(Transform transform, Transform? root)
    {
        if (transform == null)
        {
            return string.Empty;
        }

        var segments = new Stack<string>();
        var current = transform;
        while (current != null && current != root)
        {
            segments.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", segments);
    }

    private static bool IsCandidateBodyAnimationClip(AnimationClip clip)
    {
        if (clip == null || string.IsNullOrWhiteSpace(clip.name))
        {
            return false;
        }

        if (clip.name.IndexOf("__preview__", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return false;
        }

        var bindings = AnimationUtility.GetCurveBindings(clip);
        if (bindings.Length == 0)
        {
            return false;
        }

        return bindings.Any(binding =>
            binding.type != typeof(SkinnedMeshRenderer)
            || binding.propertyName.IndexOf("blendShape.", StringComparison.OrdinalIgnoreCase) < 0);
    }

    private static void PrepareBundleRuntimePhysics(GameObject modelRoot)
    {
        if (modelRoot == null)
        {
            return;
        }

        if (!ContainsMmd4MecanimComponent(modelRoot))
        {
            return;
        }

        InvokeMmd4MecanimClothAutoSetup("RemoveSceneObjectCloth", modelRoot, out _);
        var appliedUnityCloth = InvokeMmd4MecanimClothAutoSetup("RepairSceneObjectCloth", modelRoot, out var repairResult)
            && repairResult;
        Debug.Log(appliedUnityCloth
            ? $"[Codex] Converted transient MMD runtime physics to Unity Cloth for '{modelRoot.name}' before stripping MMD4Mecanim components."
            : $"[Codex] No Unity Cloth conversion was applied for '{modelRoot.name}' before stripping MMD4Mecanim components.");
    }

    private static bool ContainsMmd4MecanimComponent(GameObject root)
    {
        if (root == null)
        {
            return false;
        }

        return root.GetComponentsInChildren<MonoBehaviour>(includeInactive: true)
            .Where(behaviour => behaviour != null)
            .Any(behaviour => IsMmd4MecanimType(behaviour.GetType()));
    }

    private static bool InvokeMmd4MecanimClothAutoSetup(string methodName, GameObject modelRoot, out bool boolResult)
    {
        boolResult = false;
        var setupType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("MMD4MecanimClothAutoSetup", throwOnError: false))
            .FirstOrDefault(type => type != null);
        var method = setupType?.GetMethod(
            methodName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static,
            binder: null,
            types: new[] { typeof(GameObject) },
            modifiers: null);
        if (method == null)
        {
            return false;
        }

        var result = method.Invoke(null, new object[] { modelRoot });
        if (result is bool value)
        {
            boolResult = value;
        }

        return true;
    }

    private static void EnsureAuxiliaryAssetExists(string assetPath)
    {
        var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
        if (asset == null)
        {
            throw new FileNotFoundException($"Required auxiliary asset was not found at '{assetPath}'.");
        }
    }

    private static void StripUnsupportedComponents(GameObject root)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(includeInactive: true))
        {
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transform.gameObject);

            foreach (var behaviour in transform.GetComponents<MonoBehaviour>())
            {
                if (behaviour == null)
                {
                    continue;
                }

                if (IsMmd4MecanimType(behaviour.GetType()))
                {
                    UnityEngine.Object.DestroyImmediate(behaviour, allowDestroyingAssets: true);
                }
            }
        }
    }

    private static bool IsMmd4MecanimType(Type type)
    {
        if (type == null)
        {
            return false;
        }

        var fullName = type.FullName ?? type.Name;
        var assemblyName = type.Assembly.GetName().Name ?? string.Empty;
        return fullName.IndexOf("MMD4Mecanim", StringComparison.OrdinalIgnoreCase) >= 0
            || assemblyName.IndexOf("MMD4Mecanim", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void StripAnimatorControllersForRuntimeVmd(GameObject root)
    {
        var animators = root.GetComponentsInChildren<Animator>(includeInactive: true)
            .Where(animator => animator != null)
            .ToArray();
        foreach (var animator in animators)
        {
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.updateMode = AnimatorUpdateMode.Normal;
        }

        Debug.Log($"[Codex] Cleared {animators.Length} Animator controller assignment(s) for runtime VMD legacy playback.");
    }

    private static void ValidatePlayableComponents(GameObject root, string displayAssetPath, string? runtimeVmdAssetPath)
    {
        if (!string.IsNullOrWhiteSpace(runtimeVmdAssetPath))
        {
            var renderer = root.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true)
                .Where(candidate => candidate != null && candidate.sharedMesh != null)
                .OrderByDescending(candidate => candidate.sharedMesh.blendShapeCount)
                .ThenByDescending(candidate => candidate.sharedMesh.vertexCount)
                .FirstOrDefault();
            if (renderer == null)
            {
                throw new InvalidOperationException($"Display asset '{displayAssetPath}' does not contain a SkinnedMeshRenderer for runtime VMD playback.");
            }

            var runtimeVmd = AssetDatabase.LoadAssetAtPath<TextAsset>(runtimeVmdAssetPath);
            if (runtimeVmd == null || runtimeVmd.bytes == null || runtimeVmd.bytes.Length == 0)
            {
                throw new InvalidOperationException($"Runtime VMD asset '{runtimeVmdAssetPath}' was not imported as readable bytes.");
            }

            Debug.Log(
                $"[Codex] Runtime VMD playback validated. vmd='{runtimeVmdAssetPath}', bytes={runtimeVmd.bytes.Length}, " +
                $"renderer='{BuildTransformPath(renderer.transform, root.transform)}', mesh='{renderer.sharedMesh.name}', " +
                $"blendShapes={renderer.sharedMesh.blendShapeCount}.");
            return;
        }

        var animators = root.GetComponentsInChildren<Animator>(includeInactive: true)
            .Where(animator => animator != null)
            .ToArray();
        var primaryAnimator = ResolvePrimaryAnimator(animators);
        if (primaryAnimator == null || primaryAnimator.runtimeAnimatorController == null)
        {
            throw new InvalidOperationException($"Display asset '{displayAssetPath}' does not contain a usable AnimatorController.");
        }

        var clips = primaryAnimator.runtimeAnimatorController.animationClips
            .Where(clip => clip != null)
            .OrderByDescending(clip => clip.length)
            .ToArray();
        var primaryClip = clips.FirstOrDefault();

        var audioSource = root.GetComponentInChildren<AudioSource>(includeInactive: true);
        var audioClip = audioSource?.clip ?? audioSource?.resource as AudioClip;
        if (primaryClip == null)
        {
            Debug.LogWarning(
                $"[Codex] Display asset '{displayAssetPath}' keeps AnimatorController '{primaryAnimator.runtimeAnimatorController.name}', " +
                "but Unity Editor did not enumerate a primary AnimationClip during bundle build. " +
                $"audioClip='{audioClip?.name ?? "(none)"}', audioLength={audioClip?.length ?? 0f:0.###}.");
            return;
        }

        Debug.Log(
            $"[Codex] Prefab playback validated. controller='{primaryAnimator.runtimeAnimatorController.name}', " +
            $"primaryClip='{primaryClip.name}', clipLength={primaryClip.length:0.###}, " +
            $"audioClip='{audioClip?.name ?? "(none)"}', audioLength={audioClip?.length ?? 0f:0.###}.");

        foreach (var animator in animators)
        {
            var controller = animator.runtimeAnimatorController;
            var clipNames = controller?.animationClips?
                .Where(clip => clip != null)
                .Select(clip => $"{clip.name}:{clip.length:0.###}")
                .ToArray() ?? Array.Empty<string>();
            Debug.Log(
                $"[Codex] Animator validation. object='{BuildTransformPath(animator.transform, root.transform)}', " +
                $"controller='{controller?.name ?? "(none)"}', clips=[{string.Join(", ", clipNames)}], " +
                $"facialCandidate={IsFacialAnimatorCandidate(animator)}.");

            if (!IsFacialAnimatorCandidate(animator) || controller == null)
            {
                continue;
            }

            foreach (var clip in controller.animationClips.Where(clip => clip != null))
            {
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                Debug.Log(
                    $"[Codex] Facial clip validation. clip='{clip.name}', length={clip.length:0.###}, " +
                    $"loopTime={settings.loopTime}, wrapMode={clip.wrapMode}.");
            }
        }
    }

    private static void AlignRendererMaterialSlots(GameObject root)
    {
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(includeInactive: true))
        {
            var expectedCount = GetExpectedMaterialCount(renderer);
            var originalMaterials = renderer.sharedMaterials;
            if (expectedCount <= 0)
            {
                renderer.sharedMaterials = Array.Empty<Material>();
                Debug.Log($"[Codex] Renderer '{renderer.name}' has no valid submeshes. Cleared materials.");
                continue;
            }

            if (originalMaterials.Length == expectedCount && originalMaterials.All(material => material != null))
            {
                Debug.Log(
                    $"[Codex] Renderer '{renderer.name}' material slots already aligned. mesh='{GetSharedMeshName(renderer)}', " +
                    $"subMeshes={expectedCount}, materials={originalMaterials.Length}");
                continue;
            }

            var alignedMaterials = GetAlignedMaterialsForRenderer(renderer, originalMaterials);
            renderer.sharedMaterials = alignedMaterials;
            Debug.Log(
                $"[Codex] Renderer '{renderer.name}' material slots aligned. mesh='{GetSharedMeshName(renderer)}', " +
                $"subMeshes={expectedCount}, materialsBefore={originalMaterials.Length}, materialsAfter={alignedMaterials.Length}");
        }
    }

    private static Material[] GetAlignedMaterialsForRenderer(Renderer renderer, IReadOnlyList<Material> sourceMaterials)
    {
        var expectedCount = GetExpectedMaterialCount(renderer);
        if (expectedCount <= 0)
        {
            return Array.Empty<Material>();
        }

        var compactMaterials = (sourceMaterials ?? Array.Empty<Material>())
            .Where(material => material != null)
            .Take(expectedCount)
            .ToArray();

        if (compactMaterials.Length == expectedCount)
        {
            return compactMaterials;
        }

        var alignedMaterials = new Material[expectedCount];
        Array.Copy(compactMaterials, alignedMaterials, compactMaterials.Length);

        var fallbackMaterial = compactMaterials.LastOrDefault()
            ?? (sourceMaterials ?? Array.Empty<Material>()).FirstOrDefault(material => material != null);

        for (var index = compactMaterials.Length; index < expectedCount; index++)
        {
            alignedMaterials[index] = fallbackMaterial;
        }

        return alignedMaterials;
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

        return Mathf.Max(1, renderer.sharedMaterials.Length);
    }

    private static string GetSharedMeshName(Renderer renderer)
    {
        if (renderer is SkinnedMeshRenderer skinnedRenderer)
        {
            return skinnedRenderer.sharedMesh != null ? skinnedRenderer.sharedMesh.name : "(none)";
        }

        if (renderer.TryGetComponent<MeshFilter>(out var meshFilter) && meshFilter.sharedMesh != null)
        {
            return meshFilter.sharedMesh.name;
        }

        return "(none)";
    }

    private static void NormalizeWrapperPivot(GameObject wrapperRoot, GameObject modelRoot)
    {
        var originalBounds = CalculateRendererBounds(wrapperRoot);
        if (originalBounds.size.sqrMagnitude <= 0.000001f)
        {
            Debug.LogWarning("[Codex] Unable to normalize bundle prefab pivot because renderer bounds are empty.");
            return;
        }

        var floorAnchor = new Vector3(originalBounds.center.x, originalBounds.min.y, originalBounds.center.z);
        modelRoot.transform.localPosition -= floorAnchor;

        var normalizedBounds = CalculateRendererBounds(wrapperRoot);
        Debug.Log(
            $"[Codex] Normalized lobby prefab pivot. originalCenter={originalBounds.center}, originalSize={originalBounds.size}, " +
            $"floorAnchor={floorAnchor}, normalizedCenter={normalizedBounds.center}, normalizedSize={normalizedBounds.size}");
    }

    private static Bounds CalculateRendererBounds(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);
        if (renderers.Length == 0)
        {
            return new Bounds(Vector3.zero, Vector3.zero);
        }

        var bounds = renderers[0].bounds;
        for (var index = 1; index < renderers.Length; index++)
        {
            bounds.Encapsulate(renderers[index].bounds);
        }

        return bounds;
    }

    private static void UpdatePreparedCache(
        MikuBundleBuildProfile profile,
        BuildAssetPaths assetPaths,
        PreparedBundleAssets preparedAssets)
    {
        profile.PreparedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(preparedAssets.PrefabAssetPath);
        profile.PreparedBodyAnimationClip = string.IsNullOrWhiteSpace(preparedAssets.BodyClipAssetPath)
            ? null
            : AssetDatabase.LoadAssetAtPath<AnimationClip>(preparedAssets.BodyClipAssetPath);
        profile.PreparedFacialAnimationClip = string.IsNullOrWhiteSpace(preparedAssets.MorphClipAssetPath)
            ? null
            : AssetDatabase.LoadAssetAtPath<AnimationClip>(preparedAssets.MorphClipAssetPath);
        profile.PreparedPlaybackMetadata = string.IsNullOrWhiteSpace(preparedAssets.PlaybackMetadataAssetPath)
            ? null
            : AssetDatabase.LoadAssetAtPath<TextAsset>(preparedAssets.PlaybackMetadataAssetPath);
        profile.PreparedController = string.IsNullOrWhiteSpace(preparedAssets.ControllerAssetPath)
            ? null
            : AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(preparedAssets.ControllerAssetPath);
        profile.PreparedAssetFolderPath = assetPaths.RootFolderPath;
        profile.PreparedCacheSignature = BuildPreparedCacheSignature(profile);
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private sealed class MikuBundleBuildRequest
    {
        public string DisplayAssetPath { get; set; } = string.Empty;
        public string BodyControllerAssetPath { get; set; }
        public string BodyAnimationClipAssetPath { get; set; }
        public string FacialAnimationClipAssetPath { get; set; }
        public string BackgroundMusicAssetPath { get; set; }
        public bool PreferPreparedAssets { get; set; }
        public string PreparedPrefabAssetPath { get; set; }
        public string PreparedBodyAnimationClipAssetPath { get; set; }
        public string PreparedFacialAnimationClipAssetPath { get; set; }
        public string PreparedPlaybackMetadataAssetPath { get; set; }
        public string PreparedRuntimeVmdAssetPath { get; set; }
        public string PreparedControllerAssetPath { get; set; }
        public string PreparedAssetFolderPath { get; set; }
        public string PreparedCacheSignature { get; set; }
        public string OutputDirectory { get; set; } = string.Empty;
        public string OutputBundleName { get; set; } = DefaultOutputBundleName;
        public MikuBundleTextureSampling TextureSampling { get; set; } = MikuBundleTextureSampling.Original;
        public MikuBundleMaterialHandling MaterialHandling { get; set; } = MikuBundleMaterialHandling.AutoCompatible;
        public int MotionStartFrame { get; set; } = 0;
        public bool HasMotionEndFrame { get; set; }
        public int MotionEndFrame { get; set; }
        public IReadOnlyList<MikuBundleBuildMaterialOverrideRequest> MaterialOverrides { get; set; } =
            Array.Empty<MikuBundleBuildMaterialOverrideRequest>();
    }

    private sealed class PreparedBundleAssets
    {
        public PreparedBundleAssets(
            string prefabAssetPath,
            string? bodyClipAssetPath,
            string? morphClipAssetPath,
            string? playbackMetadataAssetPath,
            string? runtimeVmdAssetPath,
            string? controllerAssetPath)
        {
            PrefabAssetPath = prefabAssetPath;
            BodyClipAssetPath = bodyClipAssetPath;
            MorphClipAssetPath = morphClipAssetPath;
            PlaybackMetadataAssetPath = playbackMetadataAssetPath;
            RuntimeVmdAssetPath = runtimeVmdAssetPath;
            ControllerAssetPath = controllerAssetPath;
        }

        public string PrefabAssetPath { get; }
        public string? BodyClipAssetPath { get; }
        public string? MorphClipAssetPath { get; }
        public string? PlaybackMetadataAssetPath { get; }
        public string? RuntimeVmdAssetPath { get; }
        public string? ControllerAssetPath { get; }
        public bool HasRuntimeVmdMotion => !string.IsNullOrWhiteSpace(RuntimeVmdAssetPath);
    }

    private sealed class BuildAssetPaths
    {
        private BuildAssetPaths(
            string rootFolderPath,
            string prefabPath,
            string bodyClipPath,
            string morphClipPath,
            string facialClipPath,
            string playbackMetadataPath,
            string runtimeVmdPath,
            string controllerPath,
            string facialControllerPath,
            string materialFolderPath)
        {
            RootFolderPath = rootFolderPath;
            PrefabPath = prefabPath;
            BodyClipPath = bodyClipPath;
            MorphClipPath = morphClipPath;
            FacialClipPath = facialClipPath;
            PlaybackMetadataPath = playbackMetadataPath;
            RuntimeVmdPath = runtimeVmdPath;
            ControllerPath = controllerPath;
            FacialControllerPath = facialControllerPath;
            MaterialFolderPath = materialFolderPath;
        }

        public string RootFolderPath { get; }
        public string PrefabPath { get; }
        public string BodyClipPath { get; }
        public string MorphClipPath { get; }
        public string FacialClipPath { get; }
        public string PlaybackMetadataPath { get; }
        public string RuntimeVmdPath { get; }
        public string ControllerPath { get; }
        public string FacialControllerPath { get; }
        public string MaterialFolderPath { get; }

        public static BuildAssetPaths CreateTemporary()
        {
            return new BuildAssetPaths(
                TemporaryAssetFolder,
                TemporaryPrefabPath,
                TemporaryBodyClipPath,
                TemporaryMorphClipPath,
                TemporaryFacialClipPath,
                TemporaryPlaybackMetadataPath,
                TemporaryRuntimeVmdPath,
                TemporaryControllerPath,
                TemporaryFacialControllerPath,
                TemporaryMaterialFolder);
        }

        public static BuildAssetPaths CreatePrepared(MikuBundleBuildProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            var profileAssetPath = AssetDatabase.GetAssetPath(profile);
            if (string.IsNullOrWhiteSpace(profileAssetPath))
            {
                throw new InvalidOperationException("The selected build profile must be saved as an asset before preparing bundle assets.");
            }

            var profileName = SanitizeAssetSegment(Path.GetFileNameWithoutExtension(profileAssetPath));
            var rootFolderPath = $"{TemporaryAssetFolder}/Prepared/{profileName}";
            return new BuildAssetPaths(
                rootFolderPath,
                $"{rootFolderPath}/MikuLobby.prefab",
                $"{rootFolderPath}/MikuLobbyBody.anim",
                $"{rootFolderPath}/MikuLobbyMorph.anim",
                $"{rootFolderPath}/MikuLobbyFacial.anim",
                $"{rootFolderPath}/MikuLobbyPlayback.json",
                $"{rootFolderPath}/MikuLobbyMotion.vmd.bytes",
                $"{rootFolderPath}/MikuLobby.controller",
                $"{rootFolderPath}/MikuLobbyFace.controller",
                $"{rootFolderPath}/GeneratedMaterials");
        }

        private static string SanitizeAssetSegment(string value)
        {
            var candidate = string.IsNullOrWhiteSpace(value) ? "Profile" : value.Trim();
            foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
            {
                candidate = candidate.Replace(invalidCharacter, '_');
            }

            return string.IsNullOrWhiteSpace(candidate) ? "Profile" : candidate;
        }
    }

    private sealed class MikuBundleBuildMaterialOverrideRequest
    {
        public string RendererPath { get; set; } = string.Empty;
        public string[] MaterialAssetPaths { get; set; } = Array.Empty<string>();
    }

    private sealed class ResolvedMaterialOverride
    {
        public ResolvedMaterialOverride(string rendererPath, Material[] materials)
        {
            RendererPath = rendererPath;
            Materials = materials;
        }

        public string RendererPath { get; }
        public Material[] Materials { get; }
    }

    private sealed class TextureImportSettings
    {
        public TextureImportSettings(
            int maxTextureSize,
            TextureImporterFormat format,
            int compressionQuality,
            TextureImporterCompression textureCompression,
            bool crunchedCompression)
        {
            MaxTextureSize = maxTextureSize;
            Format = format;
            CompressionQuality = compressionQuality;
            TextureCompression = textureCompression;
            CrunchedCompression = crunchedCompression;
        }

        public int MaxTextureSize { get; }
        public TextureImporterFormat Format { get; }
        public int CompressionQuality { get; }
        public TextureImporterCompression TextureCompression { get; }
        public bool CrunchedCompression { get; }
    }

    private sealed class PreparedBodyMotion
    {
        public PreparedBodyMotion(
            AnimationClip? sourceClip,
            AnimationClip? playbackClip,
            string? playbackClipAssetPath,
            float sourceFrameRate,
            int motionStartFrame,
            bool hasMotionEndFrame,
            int motionEndFrame,
            float startSeconds,
            float? endSeconds,
            bool trimRangeActive)
        {
            SourceClip = sourceClip;
            PlaybackClip = playbackClip;
            PlaybackClipAssetPath = playbackClipAssetPath;
            SourceFrameRate = sourceFrameRate;
            MotionStartFrame = motionStartFrame;
            HasMotionEndFrame = hasMotionEndFrame;
            MotionEndFrame = motionEndFrame;
            StartSeconds = startSeconds;
            EndSeconds = endSeconds;
            TrimRangeActive = trimRangeActive;
        }

        public AnimationClip? SourceClip { get; }
        public AnimationClip? PlaybackClip { get; }
        public string? PlaybackClipAssetPath { get; }
        public float SourceFrameRate { get; }
        public int MotionStartFrame { get; }
        public bool HasMotionEndFrame { get; }
        public int MotionEndFrame { get; }
        public float StartSeconds { get; }
        public float? EndSeconds { get; }
        public bool TrimRangeActive { get; }
    }

    [Serializable]
    private sealed class PlaybackMetadataJson
    {
        public float SourceFrameRate = 30f;
        public int MotionStartFrame;
        public bool HasMotionEndFrame;
        public int MotionEndFrame;
        public float AudioSegmentStartSeconds;
        public float AudioSegmentDurationSeconds;
    }
}
