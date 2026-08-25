#nullable enable annotations
#nullable disable warnings

using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class ExtractBundleAnimationClip
{
    private const string SourceBundleEnvironmentVariable = "CODEX_MIKU_EXTRACT_SOURCE_BUNDLE";
    private const string ClipNameEnvironmentVariable = "CODEX_MIKU_EXTRACT_CLIP_NAME";
    private const string OutputClipEnvironmentVariable = "CODEX_MIKU_EXTRACT_OUTPUT_CLIP";
    private const string ProfileEnvironmentVariable = "CODEX_MIKU_EXTRACT_PROFILE";

    public static void Run()
    {
        var sourceBundlePath = Environment.GetEnvironmentVariable(SourceBundleEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(sourceBundlePath))
        {
            throw new InvalidOperationException($"{SourceBundleEnvironmentVariable} is not set.");
        }

        sourceBundlePath = Path.GetFullPath(sourceBundlePath);
        if (!File.Exists(sourceBundlePath))
        {
            throw new FileNotFoundException($"Source bundle was not found at '{sourceBundlePath}'.");
        }

        var outputClipPath = NormalizeAssetPath(Environment.GetEnvironmentVariable(OutputClipEnvironmentVariable));
        if (string.IsNullOrWhiteSpace(outputClipPath))
        {
            outputClipPath = "Assets/CodexBuild/Source/MikuLobbyHumanoidMotion.anim";
        }

        EnsureAssetFolderExists(Path.GetDirectoryName(outputClipPath)?.Replace('\\', '/'));
        AssetDatabase.DeleteAsset(outputClipPath);

        var bundle = AssetBundle.LoadFromFile(sourceBundlePath);
        if (bundle == null)
        {
            throw new InvalidOperationException($"AssetBundle.LoadFromFile returned null for '{sourceBundlePath}'.");
        }

        try
        {
            var desiredClipName = Environment.GetEnvironmentVariable(ClipNameEnvironmentVariable);
            var sourceClip = bundle
                .LoadAllAssets<AnimationClip>()
                .Concat(bundle
                    .LoadAllAssets<RuntimeAnimatorController>()
                    .Where(controller => controller != null)
                    .SelectMany(controller => controller.animationClips ?? Array.Empty<AnimationClip>()))
                .Where(clip => clip != null)
                .GroupBy(clip => clip.GetInstanceID())
                .Select(group => group.First())
                .OrderByDescending(clip => string.Equals(clip.name, desiredClipName, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(clip => clip.length)
                .FirstOrDefault();
            if (sourceClip == null)
            {
                throw new InvalidOperationException($"No AnimationClip was found in '{sourceBundlePath}'.");
            }

            var extractedClip = UnityEngine.Object.Instantiate(sourceClip);
            extractedClip.name = string.IsNullOrWhiteSpace(sourceClip.name) ? "MikuLobbyHumanoidMotion" : sourceClip.name;
            extractedClip.wrapMode = WrapMode.Loop;
            AssetDatabase.CreateAsset(extractedClip, outputClipPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(outputClipPath, ImportAssetOptions.ForceUpdate);

            var savedClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(outputClipPath);
            if (savedClip == null)
            {
                throw new InvalidOperationException($"Failed to save extracted AnimationClip at '{outputClipPath}'.");
            }

            UpdateProfileIfRequested(outputClipPath, savedClip);
            Debug.Log(
                $"[Codex] Extracted AnimationClip '{sourceClip.name}' from '{sourceBundlePath}' to '{outputClipPath}'. " +
                $"length={savedClip.length:0.###}, legacy={savedClip.legacy}, humanMotion={savedClip.humanMotion}.");
        }
        finally
        {
            bundle.Unload(unloadAllLoadedObjects: false);
        }
    }

    private static void UpdateProfileIfRequested(string outputClipPath, AnimationClip savedClip)
    {
        var profilePath = NormalizeAssetPath(Environment.GetEnvironmentVariable(ProfileEnvironmentVariable));
        if (string.IsNullOrWhiteSpace(profilePath))
        {
            return;
        }

        var profile = AssetDatabase.LoadAssetAtPath<MikuBundleBuildProfile>(profilePath);
        if (profile == null)
        {
            throw new FileNotFoundException($"MikuBundleBuildProfile was not found at '{profilePath}'.");
        }

        profile.BodyAnimationClip = savedClip;
        profile.BodyController = null;
        profile.PreferPreparedAssets = false;
        profile.PreparedBodyAnimationClip = null;
        profile.PreparedController = null;
        profile.PreparedPrefab = null;
        profile.PreparedCacheSignature = string.Empty;
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Codex] Updated '{profilePath}' to use extracted body clip '{outputClipPath}' and no explicit body controller.");
    }

    private static string NormalizeAssetPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().Replace('\\', '/');
        var assetsIndex = normalized.IndexOf("Assets/", StringComparison.OrdinalIgnoreCase);
        return assetsIndex >= 0 ? normalized.Substring(assetsIndex) : normalized;
    }

    private static void EnsureAssetFolderExists(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || AssetDatabase.IsValidFolder(folderPath))
        {
            return;
        }

        var segments = folderPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || !string.Equals(segments[0], "Assets", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Expected an Assets-relative folder path, got '{folderPath}'.", nameof(folderPath));
        }

        var current = "Assets";
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
}
