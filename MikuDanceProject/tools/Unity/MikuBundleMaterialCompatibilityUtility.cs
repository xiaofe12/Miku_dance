using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

internal static class MikuBundleMaterialCompatibilityUtility
{
    private static readonly string[] PortableShaderPrefixes =
    {
        "Universal Render Pipeline/",
        "Sprites/",
        "Standard",
        "Legacy Shaders/",
        "Unlit/",
        "Particles/",
    };

    private static readonly string[] PreservedProjectShaderTokens = Array.Empty<string>();

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

    private static readonly string[] SkinToneMaterialTokens =
    {
        "skin",
        "face",
        "face_b",
        "face_l",
        "neck",
        "jaw",
        "nose",
        "head",
        "kao",
        "hada",
    };

    private static readonly string[] SkinToneExcludeTokens =
    {
        "face_ex",
        "face_blush",
        "face_blue",
        "face_dark",
        "face_black",
        "mouth",
        "lip",
        "tongue",
        "teeth",
        "eye",
        "brow",
        "lash",
        "tear",
        "hl",
    };

    private static readonly Color RuntimeSkinBaseColorCap = new(0.94f, 0.92f, 0.90f, 1f);

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

    private static readonly string[] TransparentTokens =
    {
        "transparent",
        "trans",
        "fade",
        "glass",
        "decal",
        "alpha",
    };

    private static readonly string[] EmissionTogglePropertyCandidates =
    {
        "_UseEmission",
        "_EmissionEnabled",
    };

    public static string NormalizeBundleFileName(string rawName, string fallbackFileName)
    {
        var candidate = string.IsNullOrWhiteSpace(rawName) ? fallbackFileName : rawName.Trim();
        candidate = Path.GetFileName(candidate);

        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = fallbackFileName;
        }

        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            candidate = candidate.Replace(invalidCharacter, '_');
        }

        return string.IsNullOrWhiteSpace(candidate) ? fallbackFileName : candidate;
    }

    public static IReadOnlyList<string> PrepareRuntimeCompatibleMaterials(
        GameObject root,
        string outputFolderAssetPath,
        MikuBundleMaterialHandling handling)
    {
        if (root == null)
        {
            throw new ArgumentNullException(nameof(root));
        }

        if (handling == MikuBundleMaterialHandling.PreserveProjectMaterials)
        {
            return Array.Empty<string>();
        }

        var compatibleShader = FindCompatibleShader();
        if (compatibleShader == null)
        {
            Debug.LogWarning("[Codex] Could not find a URP-compatible fallback shader while preparing bundle materials.");
            return Array.Empty<string>();
        }

        EnsureFolderExists(outputFolderAssetPath);
        var createdAssetPaths = new List<string>();

        foreach (var renderer in root.GetComponentsInChildren<Renderer>(includeInactive: true))
        {
            var sourceMaterials = renderer.sharedMaterials;
            if (sourceMaterials == null || sourceMaterials.Length == 0)
            {
                continue;
            }

            var faceLikeRenderer = IsFaceLikeRenderer(renderer);
            var convertedMaterials = new Material[sourceMaterials.Length];
            var changed = false;

            for (var materialIndex = 0; materialIndex < sourceMaterials.Length; materialIndex++)
            {
                var sourceMaterial = sourceMaterials[materialIndex];
                if (sourceMaterial == null)
                {
                    convertedMaterials[materialIndex] = null;
                    continue;
                }

                if (!ShouldConvertMaterial(sourceMaterial, handling))
                {
                    convertedMaterials[materialIndex] = sourceMaterial;
                    continue;
                }

                var assetName = $"{SanitizeFileName(BuildRendererPath(renderer.transform, root.transform))}_{materialIndex:D2}_{SanitizeFileName(sourceMaterial.name)}.mat";
                var assetPath = $"{outputFolderAssetPath}/{assetName}";

                AssetDatabase.DeleteAsset(assetPath);
                var compatibleMaterial = CreateCompatibleMaterial(sourceMaterial, compatibleShader, faceLikeRenderer);
                AssetDatabase.CreateAsset(compatibleMaterial, assetPath);
                createdAssetPaths.Add(assetPath);

                convertedMaterials[materialIndex] = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
                changed = true;
            }

            if (!changed)
            {
                continue;
            }

            renderer.sharedMaterials = convertedMaterials;
            EditorUtility.SetDirty(renderer);
            Debug.Log(
                $"[Codex] Prepared runtime-compatible materials for renderer '{renderer.name}'. " +
                $"materialCount={convertedMaterials.Length}, handling={handling}.");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return createdAssetPaths;
    }

    private static Shader FindCompatibleShader()
    {
        return Shader.Find("Universal Render Pipeline/Lit")
            ?? Shader.Find("Universal Render Pipeline/Simple Lit")
            ?? Shader.Find("Standard");
    }

    private static bool ShouldConvertMaterial(Material material, MikuBundleMaterialHandling handling)
    {
        return handling switch
        {
            MikuBundleMaterialHandling.ForceUrpCompatible => true,
            MikuBundleMaterialHandling.AutoCompatible => !CanPreserveMaterial(material),
            _ => false,
        };
    }

    private static bool CanPreserveMaterial(Material material)
    {
        if (material == null || material.shader == null)
        {
            return false;
        }

        return IsPortableShader(material.shader)
            || IsPreservedProjectShader(material.shader);
    }

    private static bool IsPortableShader(Shader shader)
    {
        if (shader == null)
        {
            return false;
        }

        var shaderName = shader.name ?? string.Empty;
        if (string.IsNullOrWhiteSpace(shaderName))
        {
            return false;
        }

        if (shaderName.IndexOf("Hidden/InternalErrorShader", StringComparison.OrdinalIgnoreCase) >= 0
            || shaderName.IndexOf("FallbackError", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return false;
        }

        return PortableShaderPrefixes.Any(prefix => shaderName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPreservedProjectShader(Shader shader)
    {
        if (shader == null)
        {
            return false;
        }

        var shaderName = shader.name ?? string.Empty;
        var shaderAssetPath = AssetDatabase.GetAssetPath(shader);
        if (string.IsNullOrWhiteSpace(shaderName)
            || string.IsNullOrWhiteSpace(shaderAssetPath)
            || !shaderAssetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return PreservedProjectShaderTokens.Any(token =>
            shaderName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0
            || shaderAssetPath.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static Material CreateCompatibleMaterial(Material source, Shader compatibleShader, bool faceLikeRenderer)
    {
        var target = new Material(compatibleShader)
        {
            name = $"{source.name}_Compatible",
            enableInstancing = false,
            doubleSidedGI = source.doubleSidedGI,
            renderQueue = source.renderQueue,
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

        var skinToneMaterial = IsSkinToneMaterial(source);
        var baseColor = ClampColor(ReadFirstColor(source, ColorPropertyCandidates) ?? Color.white, 1f);
        if (skinToneMaterial)
        {
            baseColor = ClampSkinBaseColor(baseColor);
        }

        var baseTexture = baseTextureSlot.Texture;
        var normalTexture = normalTextureSlot.Texture;
        var emissionTexture = emissionTextureSlot.Texture;
        var metallicTexture = metallicTextureSlot.Texture;
        var occlusionTexture = occlusionTextureSlot.Texture;
        var cutoff = source.HasProperty("_Cutoff") ? Mathf.Clamp01(source.GetFloat("_Cutoff")) : 0f;
        var smoothness = source.HasProperty("_Smoothness")
            ? Mathf.Clamp01(source.GetFloat("_Smoothness"))
            : source.HasProperty("_Glossiness")
                ? Mathf.Clamp01(source.GetFloat("_Glossiness"))
                : 0f;
        var metallic = source.HasProperty("_Metallic") ? Mathf.Clamp01(source.GetFloat("_Metallic")) : 0f;
        var emissionEnabled = IsEmissionEnabled(source, emissionTexture);
        var emissionColor = emissionEnabled && !faceLikeRenderer
            ? ClampColor(source.HasProperty("_EmissionColor") ? source.GetColor("_EmissionColor") : Color.black, 1f)
            : Color.black;

        if (target.HasProperty("_BaseColor"))
        {
            target.SetColor("_BaseColor", baseColor);
        }

        if (target.HasProperty("_Color"))
        {
            target.SetColor("_Color", baseColor);
        }

        CopyTexture(target, "_BaseMap", baseTexture, baseTextureSlot.Scale, baseTextureSlot.Offset);
        CopyTexture(target, "_MainTex", baseTexture, baseTextureSlot.Scale, baseTextureSlot.Offset);
        CopyTexture(target, "_BumpMap", normalTexture, normalTextureSlot.Scale, normalTextureSlot.Offset);
        CopyTexture(target, "_EmissionMap", emissionTexture, emissionTextureSlot.Scale, emissionTextureSlot.Offset);
        CopyTexture(target, "_MetallicGlossMap", metallicTexture, metallicTextureSlot.Scale, metallicTextureSlot.Offset);
        CopyTexture(target, "_OcclusionMap", occlusionTexture, occlusionTextureSlot.Scale, occlusionTextureSlot.Offset);

        if (target.HasProperty("_BumpScale"))
        {
            target.SetFloat("_BumpScale", source.HasProperty("_BumpScale") ? source.GetFloat("_BumpScale") : 1f);
        }

        if (target.HasProperty("_Metallic"))
        {
            target.SetFloat("_Metallic", faceLikeRenderer ? 0f : metallic);
        }

        if (target.HasProperty("_Smoothness"))
        {
            target.SetFloat("_Smoothness", faceLikeRenderer ? Mathf.Min(smoothness, 0.08f) : smoothness);
        }

        if (target.HasProperty("_OcclusionStrength"))
        {
            target.SetFloat("_OcclusionStrength", source.HasProperty("_OcclusionStrength") ? source.GetFloat("_OcclusionStrength") : 1f);
        }

        if (target.HasProperty("_EmissionColor"))
        {
            target.SetColor("_EmissionColor", emissionColor);
            if (emissionEnabled && (emissionTexture != null || emissionColor.maxColorComponent > 0.001f))
            {
                target.EnableKeyword("_EMISSION");
            }
            else
            {
                target.DisableKeyword("_EMISSION");
            }
        }

        if (target.HasProperty("_Cull"))
        {
            var sourceCull = source.HasProperty("_Cull") ? source.GetFloat("_Cull") : (float)CullMode.Back;
            target.SetFloat("_Cull", sourceCull);
        }

        ConfigureSurfaceMode(target, source, baseTexture, baseColor, cutoff, faceLikeRenderer);
        EnableShadowSupport(target);

        if (skinToneMaterial)
        {
            Debug.Log(
                $"[Codex] Applied runtime skin tone cap to material '{source.name}'. " +
                $"baseColor={baseColor}.");
        }

        return target;
    }

    private static void ConfigureSurfaceMode(
        Material target,
        Material source,
        Texture baseTexture,
        Color baseColor,
        float cutoff,
        bool faceLikeRenderer)
    {
        var transparent = IsTransparentMaterial(source, baseColor);
        var textureHasAlpha = TextureHasAlpha(baseTexture);
        var alphaClip = cutoff > 0.001f || (textureHasAlpha && IsLikelyDecalMaterial(source, faceLikeRenderer));

        if (target.HasProperty("_Surface"))
        {
            target.SetFloat("_Surface", transparent ? 1f : 0f);
        }

        if (target.HasProperty("_AlphaClip"))
        {
            target.SetFloat("_AlphaClip", alphaClip ? 1f : 0f);
        }

        if (target.HasProperty("_Cutoff"))
        {
            target.SetFloat("_Cutoff", alphaClip ? Mathf.Max(0.02f, cutoff) : 0f);
        }

        if (transparent)
        {
            if (target.HasProperty("_SrcBlend"))
            {
                target.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            }

            if (target.HasProperty("_DstBlend"))
            {
                target.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            }

            if (target.HasProperty("_ZWrite"))
            {
                target.SetInt("_ZWrite", 0);
            }

            target.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            target.renderQueue = (int)RenderQueue.Transparent;
        }
        else
        {
            if (target.HasProperty("_SrcBlend"))
            {
                target.SetInt("_SrcBlend", (int)BlendMode.One);
            }

            if (target.HasProperty("_DstBlend"))
            {
                target.SetInt("_DstBlend", (int)BlendMode.Zero);
            }

            if (target.HasProperty("_ZWrite"))
            {
                target.SetInt("_ZWrite", 1);
            }

            target.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            target.renderQueue = alphaClip ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;
        }

        if (alphaClip)
        {
            target.EnableKeyword("_ALPHATEST_ON");
        }
        else
        {
            target.DisableKeyword("_ALPHATEST_ON");
        }
    }

    private static bool IsTransparentMaterial(Material source, Color color)
    {
        if (color.a < 0.999f)
        {
            return true;
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
        return TransparentTokens.Any(token => shaderName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool IsLikelyDecalMaterial(Material source, bool faceLikeRenderer)
    {
        if (faceLikeRenderer)
        {
            return true;
        }

        var normalizedName = NormalizeText(source.name);
        return FaceLikeTokens.Any(token => normalizedName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool IsFaceLikeRenderer(Renderer renderer)
    {
        var path = BuildRendererPath(renderer.transform, renderer.transform.root);
        var normalized = NormalizeText($"{renderer.name} {path}");
        return FaceLikeTokens.Any(token => normalized.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool IsSkinToneMaterial(Material material)
    {
        if (material == null)
        {
            return false;
        }

        var normalizedName = NormalizeText(material.name);
        if (SkinToneExcludeTokens.Any(token => normalizedName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0))
        {
            return false;
        }

        return SkinToneMaterialTokens.Any(token => normalizedName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static Color ClampSkinBaseColor(Color color)
    {
        return new Color(
            Mathf.Min(color.r, RuntimeSkinBaseColorCap.r),
            Mathf.Min(color.g, RuntimeSkinBaseColorCap.g),
            Mathf.Min(color.b, RuntimeSkinBaseColorCap.b),
            color.a);
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

        return string.Join("_", parts);
    }

    private static string NormalizeText(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "material";
        }

        var result = value;
        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            result = result.Replace(invalidCharacter, '_');
        }

        return result.Replace('/', '_').Replace('\\', '_').Replace(' ', '_');
    }

    private static void EnsureFolderExists(string assetFolderPath)
    {
        if (AssetDatabase.IsValidFolder(assetFolderPath))
        {
            return;
        }

        var parts = assetFolderPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !string.Equals(parts[0], "Assets", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Asset folder path must start with 'Assets': '{assetFolderPath}'.");
        }

        var current = parts[0];
        for (var index = 1; index < parts.Length; index++)
        {
            var next = $"{current}/{parts[index]}";
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[index]);
            }

            current = next;
        }
    }

    private static void CopyTexture(Material target, string targetPropertyName, Texture texture, Vector2 scale, Vector2 offset)
    {
        if (texture == null || !target.HasProperty(targetPropertyName))
        {
            return;
        }

        target.SetTexture(targetPropertyName, texture);
        target.SetTextureScale(targetPropertyName, scale);
        target.SetTextureOffset(targetPropertyName, offset);
    }

    private static TextureSlotInfo ReadFirstTextureSlot(Material source, IEnumerable<string> propertyNames)
    {
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

    private static TextureSlotInfo ReadFallbackBaseTextureSlot(Material source)
    {
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

    private static Color? ReadFirstColor(Material source, IEnumerable<string> propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!source.HasProperty(propertyName))
            {
                continue;
            }

            return source.GetColor(propertyName);
        }

        return null;
    }

    private static bool TextureHasAlpha(Texture texture)
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

    private static void EnableShadowSupport(Material material)
    {
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

    private static bool IsEmissionEnabled(Material source, Texture emissionTexture)
    {
        if (emissionTexture != null)
        {
            return true;
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

        public TextureSlotInfo(Texture texture, Vector2 scale, Vector2 offset)
        {
            Texture = texture;
            Scale = scale;
            Offset = offset;
        }

        public Texture Texture { get; }
        public Vector2 Scale { get; }
        public Vector2 Offset { get; }
    }
}
