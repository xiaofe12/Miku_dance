using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.Rendering;

namespace MikuDanceProject.Runtime;

internal sealed class UnityAssetBundleLoader
{
    private const string PreferredPrefabAssetName = "assets/codexbuild/mikulobby.prefab";
    private const string PreferredMorphClipAssetName = "assets/codexbuild/mikulobbymorph.anim";
    private const string PreferredPlaybackMetadataAssetName = "assets/codexbuild/mikulobbyplayback.json";
    private const string PreferredRuntimeVmdAssetName = "assets/codexbuild/mikulobbymotion.vmd.bytes";
    private static readonly string[] FaceLikeTokens =
    {
        "face",
        "head",
        "skin",
        "cheek",
        "blush",
        "mouth",
        "lip",
        "eye",
        "brow",
        "lash",
        "kao",
        "hada",
    };

    private static readonly string[] TransparentTokens =
    {
        "transparent",
        "trans",
        "fade",
        "glass",
        "decal",
        "alpha",
    };

    private static readonly string[] CustomToonShaderTokens =
    {
        "toon",
        "outline",
        "anime",
        "cel",
        "ramp",
    };

    private static readonly string[] TexturePropertyCandidates =
    {
        "_BaseMap",
        "_MainTex",
        "_BaseColorMap",
        "_BaseColorTexture",
        "_MainTexture",
        "_ShadeTexture",
        "_ShadeMultiplyTexture",
        "_UnlitTexture",
        "_ShadingGradeTexture",
        "_SphereAddTex",
        "_SphereTex",
        "_MatCap",
    };

    private static readonly string[] ColorPropertyCandidates =
    {
        "_BaseColor",
        "_Color",
        "_LitColor",
        "_MainColor",
        "_ShadeColor",
        "_SColor",
        "_TintColor",
    };

    private static readonly string[] NormalPropertyCandidates =
    {
        "_BumpMap",
        "_NormalMap",
    };

    private static readonly string[] EmissionPropertyCandidates =
    {
        "_EmissionMap",
        "_EmissiveMap",
    };

    private static readonly string[] MetallicPropertyCandidates =
    {
        "_MetallicGlossMap",
        "_MetallicSpecGlossMap",
    };

    private static readonly string[] OcclusionPropertyCandidates =
    {
        "_OcclusionMap",
    };

    private static readonly string[] EmissionTogglePropertyCandidates =
    {
        "_UseEmission",
        "_EmissionEnabled",
    };
    private AssetBundle? _retainedAssetBundle;

    public LoadedDancePrefab Load(string bundlePath, ManualLogSource logger)
    {
        ValidateFile(bundlePath);

        UnloadRetainedBundle();
        var assetBundle = AssetBundle.LoadFromFile(bundlePath);
        if (assetBundle == null)
        {
            throw new InvalidOperationException($"AssetBundle.LoadFromFile returned null for '{bundlePath}'.");
        }

        var retainBundle = false;
        try
        {
            var assetNames = assetBundle.GetAllAssetNames();
            LogVerbose(logger, $"Inspecting Unity bundle '{bundlePath}'. Assets: {string.Join(", ", assetNames)}");

            var prefabAsset = LoadPreferredPrefab(assetBundle, assetNames, logger);
            var embeddedMorphClip = LoadPreferredMorphClip(assetBundle, assetNames, logger);
            var playbackMetadata = LoadPreferredPlaybackMetadata(assetBundle, assetNames, logger);
            var runtimeVmdBytes = LoadPreferredRuntimeVmdBytes(assetBundle, assetNames, logger);

            if (prefabAsset == null)
            {
                throw new InvalidOperationException($"No GameObject asset was found in '{bundlePath}'. Assets: {string.Join(", ", assetNames)}");
            }

            var templateRoot = UnityEngine.Object.Instantiate(prefabAsset);
            templateRoot.name = prefabAsset.name;
            templateRoot.hideFlags = HideFlags.HideAndDontSave;
            templateRoot.SetActive(false);
            NormalizeRenderers(templateRoot, logger);
            NormalizeAudioSources(templateRoot, logger);
            var runtimeMotionClip = runtimeVmdBytes != null
                ? BuildRuntimeVmdClip(templateRoot, runtimeVmdBytes.bytes, logger)
                : null;

            var bounds = CalculateRendererBounds(templateRoot);
            var rendererCount = templateRoot.GetComponentsInChildren<Renderer>(true).Length;
            var skinnedRendererCount = templateRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length;
            var materialCount = templateRoot
                .GetComponentsInChildren<Renderer>(true)
                .SelectMany(renderer => renderer.sharedMaterials)
                .Count(material => material != null);
            var animator = templateRoot.GetComponentInChildren<Animator>(true);
            var animation = templateRoot.GetComponentInChildren<Animation>(true);
            var audioSources = templateRoot.GetComponentsInChildren<AudioSource>(true);
            if (embeddedMorphClip == null)
            {
                logger.LogWarning(
                    $"The Unity bundle '{bundlePath}' does not contain a pre-baked facial AnimationClip. " +
                    "Runtime VMD morph decoding has been removed, so only the simplified fallback facial controller can be used.");
            }

            var clipSummary = string.Join(
                ", ",
                animator?.runtimeAnimatorController?.animationClips?
                    .Where(clip => clip != null)
                    .GroupBy(clip => clip.name, StringComparer.Ordinal)
                    .Select(group => group.First())
                    .OrderByDescending(clip => clip.length)
                    .Take(8)
                    .Select(clip => $"{clip.name}({clip.length:0.###}s)")
                ?? Array.Empty<string>());
            var shaderSummary = string.Join(
                ", ",
                templateRoot
                    .GetComponentsInChildren<Renderer>(true)
                    .SelectMany(renderer => renderer.sharedMaterials)
                    .Where(material => material != null)
                    .GroupBy(material => material!.shader != null ? material.shader.name : "(null)")
                    .OrderByDescending(group => group.Count())
                    .Take(8)
                    .Select(group => $"{group.Key} x{group.Count()}"));

            LogVerbose(
                logger,
                $"Loaded Unity bundle prefab '{prefabAsset.name}' from '{bundlePath}'. " +
                $"renderers={rendererCount}, skinnedRenderers={skinnedRendererCount}, materials={materialCount}, " +
                $"animator={(animator != null)}, animation={(animation != null)}, " +
                $"controller='{animator?.runtimeAnimatorController?.name ?? "(none)"}', avatar='{animator?.avatar?.name ?? "(none)"}', " +
                $"clips=[{clipSummary}], " +
                $"audioSources={audioSources.Length}, " +
                $"embeddedMorphClip={(embeddedMorphClip != null)}, " +
                $"runtimeVmdClip={(runtimeMotionClip != null)}, " +
                $"playbackMetadata={(playbackMetadata != null)}, " +
                $"boundsCenter={bounds.center}, boundsSize={bounds.size}, shaders=[{shaderSummary}]");

            retainBundle = true;
            _retainedAssetBundle = assetBundle;
            return new LoadedDancePrefab(
                templateRoot,
                runtimeMotionClip,
                embeddedMorphClip,
                playbackMetadata,
                bounds.min.y,
                bounds.size.y,
                bounds.center,
                LoadedPrefabSource.UnityAssetBundle);
        }
        finally
        {
            if (!retainBundle)
            {
                assetBundle.Unload(unloadAllLoadedObjects: false);
            }
        }
    }

    public void UnloadRetainedBundle()
    {
        if (_retainedAssetBundle == null)
        {
            return;
        }

        _retainedAssetBundle.Unload(unloadAllLoadedObjects: false);
        _retainedAssetBundle = null;
    }

    private static GameObject? LoadPreferredPrefab(AssetBundle assetBundle, string[] assetNames, ManualLogSource logger)
    {
        var preferredAssetName = assetNames.FirstOrDefault(name =>
            string.Equals(name, PreferredPrefabAssetName, StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("/mikulobby.prefab", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(preferredAssetName))
        {
            var preferredPrefab = assetBundle.LoadAsset<GameObject>(preferredAssetName);
            if (preferredPrefab != null)
            {
                LogVerbose(logger, $"Selected preferred bundle prefab asset '{preferredAssetName}'.");
                return preferredPrefab;
            }
        }

        return assetBundle
            .LoadAllAssets<GameObject>()
            .OrderByDescending(HasPlayableAnimation)
            .ThenByDescending(CountRenderers)
            .FirstOrDefault();
    }

    private static AnimationClip? LoadPreferredMorphClip(AssetBundle assetBundle, string[] assetNames, ManualLogSource logger)
    {
        var assetName = assetNames.FirstOrDefault(name =>
            string.Equals(name, PreferredMorphClipAssetName, StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("/mikulobbymorph.anim", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("/mikulobbyfacial.anim", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("/facialmorph.anim", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("/facial.anim", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(assetName))
        {
            return null;
        }

        var sourceClip = assetBundle.LoadAsset<AnimationClip>(assetName);
        if (sourceClip == null)
        {
            logger.LogWarning($"The bundle contains a facial clip asset entry '{assetName}', but it could not be loaded as an AnimationClip.");
            return null;
        }

        try
        {
            var runtimeClip = UnityEngine.Object.Instantiate(sourceClip);
            runtimeClip.name = string.IsNullOrWhiteSpace(sourceClip.name) ? "MikuLobbyMorph" : sourceClip.name;
            runtimeClip.legacy = true;
            runtimeClip.wrapMode = WrapMode.Loop;

            LogVerbose(
                logger,
                $"Loaded pre-baked facial AnimationClip '{assetName}' from the Unity bundle. " +
                $"clip='{runtimeClip.name}', length={runtimeClip.length:0.###}, legacy={runtimeClip.legacy}.");
            return runtimeClip;
        }
        catch (Exception exception)
        {
            logger.LogWarning($"Failed to prepare pre-baked facial AnimationClip '{assetName}' for runtime playback: {exception.Message}");
            return null;
        }
    }

    private static TextAsset? LoadPreferredRuntimeVmdBytes(AssetBundle assetBundle, string[] assetNames, ManualLogSource logger)
    {
        var assetName = assetNames.FirstOrDefault(name =>
            string.Equals(name, PreferredRuntimeVmdAssetName, StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("/mikulobbymotion.vmd.bytes", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".vmd.bytes", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(assetName))
        {
            return null;
        }

        var textAsset = assetBundle.LoadAsset<TextAsset>(assetName);
        if (textAsset == null || textAsset.bytes == null || textAsset.bytes.Length == 0)
        {
            logger.LogWarning($"The bundle contains runtime VMD asset entry '{assetName}', but it could not be loaded as non-empty bytes.");
            return null;
        }

        LogVerbose(logger, $"Loaded runtime VMD bytes '{assetName}', bytes={textAsset.bytes.Length}.");
        return textAsset;
    }

    private static AnimationClip? BuildRuntimeVmdClip(GameObject templateRoot, byte[] vmdBytes, ManualLogSource logger)
    {
        if (vmdBytes != null && vmdBytes.Length > 0)
        {
            logger.LogWarning("The bundle contains runtime VMD bytes, but runtime VMD decoding is disabled in the Thunderstore build. Use a pre-baked AnimationClip in the Unity bundle instead.");
        }

        return null;
    }

    private static DancePlaybackMetadata? LoadPreferredPlaybackMetadata(AssetBundle assetBundle, string[] assetNames, ManualLogSource logger)
    {
        var assetName = assetNames.FirstOrDefault(name =>
            string.Equals(name, PreferredPlaybackMetadataAssetName, StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("/mikulobbyplayback.json", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("/playback.json", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(assetName))
        {
            return null;
        }

        var textAsset = assetBundle.LoadAsset<TextAsset>(assetName);
        if (textAsset == null || string.IsNullOrWhiteSpace(textAsset.text))
        {
            logger.LogWarning($"The bundle contains playback metadata '{assetName}', but it could not be loaded as a TextAsset.");
            return null;
        }

        try
        {
            var metadata = JsonUtility.FromJson<DancePlaybackMetadata>(textAsset.text);
            if (metadata == null)
            {
                logger.LogWarning($"Failed to deserialize playback metadata '{assetName}' from the Unity bundle.");
                return null;
            }

            LogVerbose(
                logger,
                $"Loaded playback metadata '{assetName}' from the Unity bundle. " +
                $"startFrame={metadata.MotionStartFrame}, hasEndFrame={metadata.HasMotionEndFrame}, endFrame={metadata.MotionEndFrame}, " +
                $"audioStart={metadata.AudioSegmentStartSeconds:0.###}, audioDuration={metadata.AudioSegmentDurationSeconds:0.###}, " +
                $"frameRate={metadata.SourceFrameRate:0.###}.");
            return metadata;
        }
        catch (Exception exception)
        {
            logger.LogWarning($"Failed to parse playback metadata '{assetName}': {exception.Message}");
            return null;
        }
    }

    private static int HasPlayableAnimation(GameObject candidate)
    {
        var animator = candidate.GetComponentInChildren<Animator>(includeInactive: true);
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            return 2;
        }

        var animation = candidate.GetComponentInChildren<Animation>(includeInactive: true);
        if (animation != null)
        {
            return 1;
        }

        return 0;
    }

    private static int CountRenderers(GameObject candidate)
    {
        return candidate.GetComponentsInChildren<Renderer>(includeInactive: true).Length;
    }

    private static void NormalizeRenderers(GameObject root, ManualLogSource logger)
    {
        var fallbackShader = Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Unlit/Texture")
            ?? Shader.Find("Sprites/Default")
            ?? Shader.Find("Universal Render Pipeline/Simple Lit")
            ?? Shader.Find("Universal Render Pipeline/Lit")
            ?? Shader.Find("Standard")
            ?? Shader.Find("Legacy Shaders/Diffuse");

        foreach (var transform in root.GetComponentsInChildren<Transform>(includeInactive: true))
        {
            transform.gameObject.layer = 0;
        }

        foreach (var renderer in root.GetComponentsInChildren<Renderer>(includeInactive: true))
        {
            renderer.enabled = true;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.allowOcclusionWhenDynamic = true;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;

            var originalMaterials = renderer.sharedMaterials;
            var expectedMaterialCount = GetExpectedMaterialCount(renderer);
            var alignedMaterials = AlignMaterialSlots(originalMaterials, expectedMaterialCount);
            renderer.sharedMaterials = NormalizeMaterials(renderer, alignedMaterials, fallbackShader);

            LogVerbose(
                logger,
                $"Renderer '{renderer.name}' normalized. type={renderer.GetType().Name}, " +
                $"mesh='{GetSharedMeshName(renderer)}', subMeshes={expectedMaterialCount}, " +
                $"materialsBefore={originalMaterials.Length}, materialsAfter={renderer.sharedMaterials.Length}.");

            if (renderer is SkinnedMeshRenderer skinnedRenderer)
            {
                skinnedRenderer.updateWhenOffscreen = true;
            }
        }

        LogVerbose(logger, $"Normalized Unity bundle renderers. fallback shader='{fallbackShader?.name ?? "(none)"}'.");
    }

    private static void NormalizeAudioSources(GameObject root, ManualLogSource logger)
    {
        foreach (var audioSource in root.GetComponentsInChildren<AudioSource>(includeInactive: true))
        {
            var audioClip = ResolveAudioClip(audioSource);
            if (audioClip != null && audioSource.clip == null)
            {
                audioSource.clip = audioClip;
            }

            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0f;
            audioSource.dopplerLevel = 0f;

            LogVerbose(
                logger,
                $"AudioSource '{audioSource.name}' normalized. clip='{audioClip?.name ?? "(none)"}', " +
                $"length={audioClip?.length ?? 0f:0.###}, volume={audioSource.volume:0.###}, " +
                $"resource='{audioSource.resource?.name ?? "(none)"}'.");
        }
    }

    private static void LogVerbose(ManualLogSource logger, string message)
    {
        if (Core.VerboseLogState.Enabled)
        {
            logger.LogInfo(message);
        }
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

    private static Material[] AlignMaterialSlots(Material[] materials, int expectedMaterialCount)
    {
        if (expectedMaterialCount <= 0)
        {
            return Array.Empty<Material>();
        }

        if (materials.Length == expectedMaterialCount && materials.All(material => material != null))
        {
            return materials;
        }

        var alignedMaterials = new Material[expectedMaterialCount];
        var lastMaterial = materials.LastOrDefault(material => material != null);
        for (var index = 0; index < expectedMaterialCount; index++)
        {
            alignedMaterials[index] = index < materials.Length && materials[index] != null
                ? materials[index]
                : lastMaterial!;
        }

        return alignedMaterials;
    }

    private static Material[] NormalizeMaterials(Renderer renderer, Material[] materials, Shader? fallbackShader)
    {
        if (materials.Length == 0)
        {
            return materials;
        }

        var faceLikeRenderer = IsFaceLikeRenderer(renderer);
        var normalizedMaterials = new Material[materials.Length];
        var hasRuntimeReplacement = false;
        for (var index = 0; index < materials.Length; index++)
        {
            var sourceMaterial = materials[index];
            if (sourceMaterial != null && CanUseSourceShader(sourceMaterial))
            {
                normalizedMaterials[index] = sourceMaterial;
                continue;
            }

            if (fallbackShader == null)
            {
                normalizedMaterials[index] = sourceMaterial!;
                continue;
            }

            normalizedMaterials[index] = CreateCompatibleMaterial(renderer, sourceMaterial, fallbackShader, index, faceLikeRenderer);
            hasRuntimeReplacement = true;
        }

        return hasRuntimeReplacement ? normalizedMaterials : materials;
    }

    private static Material CreateCompatibleMaterial(
        Renderer renderer,
        Material? source,
        Shader fallbackShader,
        int index,
        bool faceLikeRenderer)
    {
        if (CanUseSourceShader(source))
        {
            return source!;
        }

        var compatibleShader = ResolveCompatibleFallbackShader(source, fallbackShader);
        var compatibleMaterial = new Material(compatibleShader)
        {
            name = source != null ? $"{source.name}_Runtime" : $"RuntimeMaterial_{index}",
        };

        var baseTextureSlot = ReadFirstTextureSlot(source, TexturePropertyCandidates);
        if (baseTextureSlot.Texture == null)
        {
            baseTextureSlot = ReadFallbackBaseTextureSlot(source);
        }

        var normalTextureSlot = ReadFirstTextureSlot(source, NormalPropertyCandidates);
        var emissionTextureSlot = ReadFirstTextureSlot(source, EmissionPropertyCandidates);
        var metallicTextureSlot = ReadFirstTextureSlot(source, MetallicPropertyCandidates);
        var occlusionTextureSlot = ReadFirstTextureSlot(source, OcclusionPropertyCandidates);

        var baseColor = ResolveCompatibleBaseColor(ReadFirstColor(source, ColorPropertyCandidates) ?? Color.white);
        var baseTexture = baseTextureSlot.Texture;
        var normalTexture = normalTextureSlot.Texture;
        var emissionTexture = emissionTextureSlot.Texture;
        var metallicTexture = metallicTextureSlot.Texture;
        var occlusionTexture = occlusionTextureSlot.Texture;
        var cutoff = source != null && source.HasProperty("_Cutoff") ? Mathf.Clamp01(source.GetFloat("_Cutoff")) : 0f;
        var smoothness = source != null && source.HasProperty("_Smoothness")
            ? Mathf.Clamp01(source.GetFloat("_Smoothness"))
            : source != null && source.HasProperty("_Glossiness")
                ? Mathf.Clamp01(source.GetFloat("_Glossiness"))
                : 0f;
        var metallic = source != null && source.HasProperty("_Metallic") ? Mathf.Clamp01(source.GetFloat("_Metallic")) : 0f;
        var emissionEnabled = IsEmissionEnabled(source, emissionTexture);
        var emissionColor = emissionEnabled && !faceLikeRenderer
            ? ClampColor(TryReadColor(source, "_EmissionColor") ?? Color.black, 1f)
            : Color.black;

        compatibleMaterial.color = baseColor;
        if (compatibleMaterial.HasProperty("_BaseColor"))
        {
            compatibleMaterial.SetColor("_BaseColor", baseColor);
        }

        if (compatibleMaterial.HasProperty("_Color"))
        {
            compatibleMaterial.SetColor("_Color", baseColor);
        }

        CopyTexture(compatibleMaterial, "_BaseMap", baseTextureSlot);
        CopyTexture(compatibleMaterial, "_MainTex", baseTextureSlot);
        CopyTexture(compatibleMaterial, "_BumpMap", normalTextureSlot);
        CopyTexture(compatibleMaterial, "_EmissionMap", emissionTextureSlot);
        CopyTexture(compatibleMaterial, "_MetallicGlossMap", metallicTextureSlot);
        CopyTexture(compatibleMaterial, "_OcclusionMap", occlusionTextureSlot);

        if (compatibleMaterial.HasProperty("_BumpScale"))
        {
            compatibleMaterial.SetFloat("_BumpScale", source != null && source.HasProperty("_BumpScale") ? source.GetFloat("_BumpScale") : 1f);
        }

        if (compatibleMaterial.HasProperty("_Metallic"))
        {
            compatibleMaterial.SetFloat("_Metallic", 0f);
        }

        if (compatibleMaterial.HasProperty("_Smoothness"))
        {
            compatibleMaterial.SetFloat("_Smoothness", Mathf.Min(smoothness, 0.15f));
        }

        if (compatibleMaterial.HasProperty("_Glossiness"))
        {
            compatibleMaterial.SetFloat("_Glossiness", Mathf.Min(smoothness, 0.15f));
        }

        if (compatibleMaterial.HasProperty("_OcclusionStrength"))
        {
            compatibleMaterial.SetFloat("_OcclusionStrength", 0f);
        }

        if (compatibleMaterial.HasProperty("_Cutoff"))
        {
            compatibleMaterial.SetFloat("_Cutoff", cutoff);
        }

        if (compatibleMaterial.HasProperty("_SpecularHighlights"))
        {
            compatibleMaterial.SetFloat("_SpecularHighlights", 0f);
        }

        if (compatibleMaterial.HasProperty("_EnvironmentReflections"))
        {
            compatibleMaterial.SetFloat("_EnvironmentReflections", 0f);
        }

        if (compatibleMaterial.HasProperty("_EmissionColor"))
        {
            compatibleMaterial.SetColor("_EmissionColor", emissionColor);
            if (emissionEnabled && (emissionTexture != null || emissionColor.maxColorComponent > 0.001f))
            {
                compatibleMaterial.EnableKeyword("_EMISSION");
            }
            else
            {
                compatibleMaterial.DisableKeyword("_EMISSION");
            }
        }

        if (compatibleMaterial.HasProperty("_Cull") && source != null && source.HasProperty("_Cull"))
        {
            compatibleMaterial.SetFloat("_Cull", source.GetFloat("_Cull"));
        }

        ConfigureSurfaceMode(compatibleMaterial, source, baseTexture, baseColor, cutoff, faceLikeRenderer);
        EnableShadowSupport(compatibleMaterial);
        return compatibleMaterial;
    }

    private static Color ResolveCompatibleBaseColor(Color sourceColor)
    {
        return ClampColor(sourceColor, 1f);
    }

    private static Shader ResolveCompatibleFallbackShader(Material? source, Shader defaultFallbackShader)
    {
        var shaderName = source?.shader != null ? source.shader.name ?? string.Empty : string.Empty;
        if (!string.IsNullOrWhiteSpace(shaderName)
            && CustomToonShaderTokens.Any(token => shaderName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0))
        {
            return Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                ?? Shader.Find("Standard")
                ?? Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Texture")
                ?? defaultFallbackShader;
        }

        return defaultFallbackShader;
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

    private static bool CanUseSourceShader(Material? source)
    {
        if (source == null || source.shader == null)
        {
            return false;
        }

        var shaderName = source.shader.name ?? string.Empty;
        if (string.IsNullOrWhiteSpace(shaderName))
        {
            return false;
        }

        if (shaderName.IndexOf("Hidden/InternalErrorShader", StringComparison.OrdinalIgnoreCase) >= 0
            || shaderName.IndexOf("FallbackError", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return false;
        }

        try
        {
            return source.shader.isSupported;
        }
        catch
        {
            return false;
        }
    }

    private static Color? TryReadColor(Material? source, string propertyName)
    {
        if (source == null || !source.HasProperty(propertyName))
        {
            return null;
        }

        return source.GetColor(propertyName);
    }

    private static Color? ReadFirstColor(Material? source, IEnumerable<string> propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var color = TryReadColor(source, propertyName);
            if (color.HasValue)
            {
                return color;
            }
        }

        return null;
    }

    private static TextureSlotInfo ReadFirstTextureSlot(Material? source, IEnumerable<string> propertyNames)
    {
        if (source == null)
        {
            return TextureSlotInfo.Empty;
        }

        foreach (var propertyName in propertyNames)
        {
            if (!source.HasProperty(propertyName))
            {
                continue;
            }

            var texture = source.GetTexture(propertyName);
            if (texture != null)
            {
                return new TextureSlotInfo(
                    texture,
                    source.GetTextureScale(propertyName),
                    source.GetTextureOffset(propertyName));
            }
        }

        return TextureSlotInfo.Empty;
    }

    private static TextureSlotInfo ReadFallbackBaseTextureSlot(Material? source)
    {
        if (source == null)
        {
            return TextureSlotInfo.Empty;
        }

        foreach (var propertyName in source.GetTexturePropertyNames())
        {
            if (propertyName.IndexOf("bump", StringComparison.OrdinalIgnoreCase) >= 0
                || propertyName.IndexOf("normal", StringComparison.OrdinalIgnoreCase) >= 0
                || propertyName.IndexOf("emission", StringComparison.OrdinalIgnoreCase) >= 0
                || propertyName.IndexOf("occlusion", StringComparison.OrdinalIgnoreCase) >= 0
                || propertyName.IndexOf("metal", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }

            var texture = source.GetTexture(propertyName);
            if (texture != null)
            {
                return new TextureSlotInfo(
                    texture,
                    source.GetTextureScale(propertyName),
                    source.GetTextureOffset(propertyName));
            }
        }

        return TextureSlotInfo.Empty;
    }

    private static bool IsTransparentMaterial(Material? source, Color color)
    {
        if (color.a < 0.999f)
        {
            return true;
        }

        if (source == null)
        {
            return false;
        }

        if (source.renderQueue >= (int)RenderQueue.Transparent)
        {
            return true;
        }

        if (source.HasProperty("_Surface") && source.GetFloat("_Surface") > 0.5f)
        {
            return true;
        }

        if (source.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"))
        {
            return true;
        }

        var shaderName = source.shader != null ? source.shader.name ?? string.Empty : string.Empty;
        return TransparentTokens.Any(token =>
            shaderName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0
            || source.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool TextureHasAlpha(Texture? texture)
    {
        if (texture is not Texture2D texture2D)
        {
            return false;
        }

        var formatName = texture2D.format.ToString();
        return formatName.IndexOf("RGBA", StringComparison.OrdinalIgnoreCase) >= 0
            || formatName.IndexOf("ARGB", StringComparison.OrdinalIgnoreCase) >= 0
            || formatName.IndexOf("BGRA", StringComparison.OrdinalIgnoreCase) >= 0
            || formatName.IndexOf("Alpha", StringComparison.OrdinalIgnoreCase) >= 0
            || formatName.IndexOf("DXT5", StringComparison.OrdinalIgnoreCase) >= 0
            || formatName.IndexOf("BC7", StringComparison.OrdinalIgnoreCase) >= 0
            || formatName.IndexOf("ETC2_RGBA", StringComparison.OrdinalIgnoreCase) >= 0
            || formatName.IndexOf("ASTC_RGBA", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void ConfigureSurfaceMode(
        Material material,
        Material? source,
        Texture? baseTexture,
        Color baseColor,
        float cutoff,
        bool faceLikeRenderer)
    {
        var transparent = IsTransparentMaterial(source, baseColor);
        var textureHasAlpha = TextureHasAlpha(baseTexture);
        var alphaClip = cutoff > 0.001f || (textureHasAlpha && IsLikelyDecalMaterial(source, faceLikeRenderer));

        if (transparent)
        {
            ConfigureTransparentMaterial(material, source);
            return;
        }

        if (alphaClip)
        {
            ConfigureAlphaClipMaterial(material, source);
            return;
        }

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 0f);
        }

        if (material.HasProperty("_AlphaClip"))
        {
            material.SetFloat("_AlphaClip", 0f);
        }

        if (material.HasProperty("_Cutoff"))
        {
            material.SetFloat("_Cutoff", 0f);
        }

        if (material.HasProperty("_SrcBlend"))
        {
            material.SetInt("_SrcBlend", (int)BlendMode.One);
        }

        if (material.HasProperty("_DstBlend"))
        {
            material.SetInt("_DstBlend", (int)BlendMode.Zero);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetInt("_ZWrite", 1);
        }

        material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.renderQueue = (int)RenderQueue.Geometry;
    }

    private static void ConfigureAlphaClipMaterial(Material material, Material? source)
    {
        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 0f);
        }

        if (material.HasProperty("_AlphaClip"))
        {
            material.SetFloat("_AlphaClip", 1f);
        }

        if (material.HasProperty("_Cutoff"))
        {
            material.SetFloat("_Cutoff", source != null && source.HasProperty("_Cutoff") ? source.GetFloat("_Cutoff") : 0.5f);
        }

        if (material.HasProperty("_SrcBlend"))
        {
            material.SetInt("_SrcBlend", (int)BlendMode.One);
        }

        if (material.HasProperty("_DstBlend"))
        {
            material.SetInt("_DstBlend", (int)BlendMode.Zero);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetInt("_ZWrite", 1);
        }

        material.EnableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)RenderQueue.AlphaTest;
    }

    private static void ConfigureTransparentMaterial(Material material, Material? source)
    {
        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
        }

        if (material.HasProperty("_Blend"))
        {
            material.SetFloat("_Blend", 0f);
        }

        if (material.HasProperty("_SrcBlend"))
        {
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        }

        if (material.HasProperty("_DstBlend"))
        {
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetInt("_ZWrite", 0);
        }

        if (source != null && source.HasProperty("_Cutoff") && source.GetFloat("_Cutoff") > 0.001f)
        {
            if (material.HasProperty("_AlphaClip"))
            {
                material.SetFloat("_AlphaClip", 1f);
            }

            if (material.HasProperty("_Cutoff"))
            {
                material.SetFloat("_Cutoff", source.GetFloat("_Cutoff"));
            }
        }
        else if (material.HasProperty("_AlphaClip"))
        {
            material.SetFloat("_AlphaClip", 0f);
        }

        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.renderQueue = (int)RenderQueue.Transparent;
    }

    private static bool IsLikelyDecalMaterial(Material? source, bool faceLikeRenderer)
    {
        if (faceLikeRenderer)
        {
            return true;
        }

        if (source == null)
        {
            return false;
        }

        return FaceLikeTokens.Any(token =>
            source.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool IsFaceLikeRenderer(Renderer renderer)
    {
        var path = BuildRendererPath(renderer.transform, renderer.transform.root);
        return FaceLikeTokens.Any(token =>
            renderer.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static string BuildRendererPath(Transform current, Transform root)
    {
        var parts = new Stack<string>();
        var cursor = current;
        while (cursor != null)
        {
            parts.Push(cursor.name);
            if (cursor == root)
            {
                break;
            }

            cursor = cursor.parent;
        }

        return string.Join("/", parts);
    }

    private static void CopyTexture(Material target, string targetPropertyName, TextureSlotInfo textureSlot)
    {
        if (textureSlot.Texture == null || !target.HasProperty(targetPropertyName))
        {
            return;
        }

        target.SetTexture(targetPropertyName, textureSlot.Texture);
        target.SetTextureScale(targetPropertyName, textureSlot.Scale);
        target.SetTextureOffset(targetPropertyName, textureSlot.Offset);
    }

    private static bool IsEmissionEnabled(Material? source, Texture? emissionTexture)
    {
        if (emissionTexture != null)
        {
            return true;
        }

        if (source == null)
        {
            return false;
        }

        foreach (var propertyName in EmissionTogglePropertyCandidates)
        {
            if (source.HasProperty(propertyName) && source.GetFloat(propertyName) > 0.5f)
            {
                return true;
            }
        }

        return source.IsKeywordEnabled("_EMISSION");
    }

    private static Color ClampColor(Color color, float maxChannel)
    {
        maxChannel = Mathf.Max(0f, maxChannel);
        return new Color(
            Mathf.Clamp(color.r, 0f, maxChannel),
            Mathf.Clamp(color.g, 0f, maxChannel),
            Mathf.Clamp(color.b, 0f, maxChannel),
            Mathf.Clamp01(color.a));
    }

    private readonly struct TextureSlotInfo
    {
        public static readonly TextureSlotInfo Empty = new(null, Vector2.one, Vector2.zero);

        public TextureSlotInfo(Texture? texture, Vector2 scale, Vector2 offset)
        {
            Texture = texture;
            Scale = scale;
            Offset = offset;
        }

        public Texture? Texture { get; }
        public Vector2 Scale { get; }
        public Vector2 Offset { get; }
    }

    private static Bounds CalculateRendererBounds(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);
        if (renderers.Length == 0)
        {
            return new Bounds(Vector3.zero, Vector3.zero);
        }

        var bounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        return bounds;
    }

    private static void ValidateFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Required Unity asset bundle file was not found.", path);
        }
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
}
