using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class ExportMikuLobbyMorphClip
{
    private const string DefaultSourceClipAssetPath = "Assets/CodexBuild/MikuLobbyOptimized.anim";
    private const string OutputFolderAssetPath = "Assets/CodexBuild";
    private const string OutputClipAssetPath = OutputFolderAssetPath + "/MikuLobbyMorph.anim";
    private const string MenuItemPath = "Tools/Miku Showcase/Export Facial Clip";
    private const int LoggedBindingSampleCount = 10;

    [MenuItem(MenuItemPath)]
    public static void Export()
    {
        EnsureFolderExists(OutputFolderAssetPath);

        var sourceClip = ResolveSourceClip();
        if (sourceClip == null)
        {
            throw new InvalidOperationException(
                "No source AnimationClip was found. Select an AnimationClip in the Project window, or make sure " +
                $"'{DefaultSourceClipAssetPath}' exists.");
        }

        var exportedClip = ExportToAsset(sourceClip, OutputClipAssetPath, out var copiedBindings, out var copiedKeys, out var bindingSamples);
        EditorGUIUtility.PingObject(exportedClip);
        Selection.activeObject = exportedClip;

        Debug.Log(
            $"[Codex] Exported facial blendshape clip '{OutputClipAssetPath}' from '{AssetDatabase.GetAssetPath(sourceClip)}'. " +
            $"bindings={copiedBindings}, keys={copiedKeys}, samples=[{string.Join(", ", bindingSamples)}].");
    }

    public static AnimationClip TryResolveDefaultSourceClip()
    {
        return ResolveSourceClip();
    }

    public static bool ContainsBlendShapeCurves(AnimationClip sourceClip)
    {
        if (sourceClip == null)
        {
            return false;
        }

        return AnimationUtility.GetCurveBindings(sourceClip).Any(IsBlendShapeBinding);
    }

    public static AnimationClip ExportToAsset(
        AnimationClip sourceClip,
        string outputClipAssetPath,
        out int copiedBindingCount,
        out int copiedKeyCount,
        out List<string> bindingSamples)
    {
        if (sourceClip == null)
        {
            throw new ArgumentNullException(nameof(sourceClip));
        }

        if (string.IsNullOrWhiteSpace(outputClipAssetPath))
        {
            throw new ArgumentException("Output clip asset path must not be empty.", nameof(outputClipAssetPath));
        }

        var outputFolderPath = Path.GetDirectoryName(outputClipAssetPath)?.Replace("\\", "/");
        if (string.IsNullOrWhiteSpace(outputFolderPath))
        {
            throw new InvalidOperationException($"Output clip asset path is invalid: '{outputClipAssetPath}'.");
        }

        EnsureFolderExists(outputFolderPath);

        var outputClip = BuildMorphOnlyClip(sourceClip, Path.GetFileNameWithoutExtension(outputClipAssetPath), out copiedBindingCount, out copiedKeyCount, out bindingSamples);
        if (copiedBindingCount <= 0)
        {
            throw new InvalidOperationException(
                $"No blendShape curves were found in source clip '{AssetDatabase.GetAssetPath(sourceClip)}'.");
        }

        ReplaceClipAsset(outputClip, outputClipAssetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return AssetDatabase.LoadAssetAtPath<AnimationClip>(outputClipAssetPath)
            ?? throw new InvalidOperationException($"Failed to load exported facial clip '{outputClipAssetPath}'.");
    }

    [MenuItem(MenuItemPath, true)]
    private static bool ValidateExport()
    {
        return ResolveSourceClip() != null;
    }

    private static AnimationClip ResolveSourceClip()
    {
        if (Selection.activeObject is AnimationClip selectedClip)
        {
            return selectedClip;
        }

        var defaultClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(DefaultSourceClipAssetPath);
        if (defaultClip != null)
        {
            return defaultClip;
        }

        var candidateGuids = AssetDatabase.FindAssets("t:AnimationClip MikuLobbyOptimized");
        for (var index = 0; index < candidateGuids.Length; index++)
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(candidateGuids[index]);
            var candidateClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            if (candidateClip != null)
            {
                return candidateClip;
            }
        }

        return null;
    }

    private static AnimationClip BuildMorphOnlyClip(
        AnimationClip sourceClip,
        string clipName,
        out int copiedBindingCount,
        out int copiedKeyCount,
        out List<string> bindingSamples)
    {
        var outputClip = new AnimationClip
        {
            name = string.IsNullOrWhiteSpace(clipName) ? Path.GetFileNameWithoutExtension(OutputClipAssetPath) : clipName,
            frameRate = sourceClip.frameRate,
            legacy = true,
            wrapMode = WrapMode.Loop,
        };

        copiedBindingCount = 0;
        copiedKeyCount = 0;
        bindingSamples = new List<string>(LoggedBindingSampleCount);

        var floatBindings = AnimationUtility.GetCurveBindings(sourceClip)
            .Where(IsBlendShapeBinding)
            .OrderBy(binding => binding.path, StringComparer.Ordinal)
            .ThenBy(binding => binding.propertyName, StringComparer.Ordinal)
            .ToArray();

        for (var index = 0; index < floatBindings.Length; index++)
        {
            var binding = floatBindings[index];
            var sourceCurve = AnimationUtility.GetEditorCurve(sourceClip, binding);
            if (sourceCurve == null || sourceCurve.length == 0)
            {
                continue;
            }

            var copiedCurve = new AnimationCurve(sourceCurve.keys)
            {
                preWrapMode = sourceCurve.preWrapMode,
                postWrapMode = sourceCurve.postWrapMode,
            };

            AnimationUtility.SetEditorCurve(outputClip, binding, copiedCurve);
            copiedBindingCount++;
            copiedKeyCount += copiedCurve.length;

            if (bindingSamples.Count < LoggedBindingSampleCount)
            {
                bindingSamples.Add($"{binding.path}:{binding.propertyName}");
            }
        }

        var animationEvents = AnimationUtility.GetAnimationEvents(sourceClip);
        if (animationEvents != null && animationEvents.Length > 0)
        {
            AnimationUtility.SetAnimationEvents(outputClip, Array.Empty<AnimationEvent>());
        }

        return outputClip;
    }

    private static bool IsBlendShapeBinding(EditorCurveBinding binding)
    {
        if (binding.type != typeof(SkinnedMeshRenderer) || string.IsNullOrWhiteSpace(binding.propertyName))
        {
            return false;
        }

        return binding.propertyName.StartsWith("blendShape.", StringComparison.OrdinalIgnoreCase);
    }

    private static void ReplaceClipAsset(AnimationClip clip, string assetPath)
    {
        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath) != null)
        {
            AssetDatabase.DeleteAsset(assetPath);
        }

        AssetDatabase.CreateAsset(clip, assetPath);
    }

    private static void EnsureFolderExists(string assetFolderPath)
    {
        if (AssetDatabase.IsValidFolder(assetFolderPath))
        {
            return;
        }

        var folderParts = assetFolderPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (folderParts.Length == 0 || !string.Equals(folderParts[0], "Assets", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Asset folder path must start with 'Assets': '{assetFolderPath}'.");
        }

        var currentFolder = folderParts[0];
        for (var index = 1; index < folderParts.Length; index++)
        {
            var nextFolder = $"{currentFolder}/{folderParts[index]}";
            if (!AssetDatabase.IsValidFolder(nextFolder))
            {
                AssetDatabase.CreateFolder(currentFolder, folderParts[index]);
            }

            currentFolder = nextFolder;
        }
    }
}
