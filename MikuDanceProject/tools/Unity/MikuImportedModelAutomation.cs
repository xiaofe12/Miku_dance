using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

[InitializeOnLoad]
internal static class MikuImportedModelAutomationScheduler
{
    private static readonly HashSet<string> PendingModelAssetPaths =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    static MikuImportedModelAutomationScheduler()
    {
        EditorApplication.delayCall += FlushPendingRepairs;
    }

    public static void Schedule(string assetPath)
    {
        if (!MikuImportedModelAutomation.TryResolveImportedModelAssetPath(assetPath, out var modelAssetPath))
        {
            return;
        }

        PendingModelAssetPaths.Add(modelAssetPath);
        EditorApplication.delayCall -= FlushPendingRepairs;
        EditorApplication.delayCall += FlushPendingRepairs;
    }

    private static void FlushPendingRepairs()
    {
        if (PendingModelAssetPaths.Count == 0)
        {
            return;
        }

        var modelAssetPaths = PendingModelAssetPaths.ToArray();
        PendingModelAssetPaths.Clear();
        foreach (var modelAssetPath in modelAssetPaths)
        {
            MikuImportedModelAutomation.ProcessImportedModel(modelAssetPath);
        }
    }
}

internal sealed class MikuImportedModelPostprocessor : AssetPostprocessor
{
    private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
    {
        if (importedAssets == null)
        {
            return;
        }

        foreach (var importedAsset in importedAssets)
        {
            if (!ShouldSchedule(importedAsset))
            {
                continue;
            }

            MikuImportedModelAutomationScheduler.Schedule(importedAsset);
        }
    }

    private static bool ShouldSchedule(string assetPath)
    {
        assetPath = assetPath?.Replace('\\', '/');
        return !string.IsNullOrWhiteSpace(assetPath)
            && (assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)
                || assetPath.EndsWith(".pmx", StringComparison.OrdinalIgnoreCase)
                || assetPath.EndsWith(".pmd", StringComparison.OrdinalIgnoreCase)
                || assetPath.EndsWith(".MMD4Mecanim.asset", StringComparison.OrdinalIgnoreCase)
                || assetPath.EndsWith(".MMD4Mecanim.xml", StringComparison.OrdinalIgnoreCase)
                || assetPath.EndsWith(".anim", StringComparison.OrdinalIgnoreCase));
    }
}

internal static class MikuImportedModelAutomation
{
    [MenuItem("Tools/Miku Showcase/Repair Selected Imported Model")]
    public static void RepairSelectedImportedModel()
    {
        var selectedAssetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
        if (!TryResolveImportedModelAssetPath(selectedAssetPath, out var modelAssetPath))
        {
            Debug.LogWarning("[Codex] 当前选择项无法解析到可修复的 MMD 模型 FBX。");
            return;
        }

        ProcessImportedModel(modelAssetPath);
    }

    [MenuItem("Tools/Miku Showcase/Repair Selected Imported Model", true)]
    private static bool ValidateRepairSelectedImportedModel()
    {
        return TryResolveImportedModelAssetPath(AssetDatabase.GetAssetPath(Selection.activeObject), out _);
    }

    [MenuItem("Tools/Miku Showcase/Repair All Imported MMD Models")]
    public static void RepairAllImportedMmdModels()
    {
        var modelAssetPaths = CollectImportedModelAssetPaths();
        var changedCount = 0;
        foreach (var modelAssetPath in modelAssetPaths)
        {
            if (ProcessImportedModel(modelAssetPath))
            {
                changedCount++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Codex] Repaired imported MMD models. processed={modelAssetPaths.Length}, changed={changedCount}.");
    }

    public static bool ProcessImportedModel(string assetPath)
    {
        if (!TryResolveImportedModelAssetPath(assetPath, out var modelAssetPath))
        {
            return false;
        }

        var changed = false;
        changed |= MMD4MecanimMaterialRepairer.RepairModelMaterials(modelAssetPath);
        changed |= MMD4MecanimPhysicsRepairer.RepairModelPhysics(modelAssetPath);
        changed |= MMD4MecanimClothAutoSetup.RemoveModelCloth(modelAssetPath);
        changed |= MMD4MecanimClothAutoSetup.RepairModelCloth(modelAssetPath);
        changed |= NormalizeRelatedPrefabs(modelAssetPath);
        changed |= EnsureModelController(modelAssetPath);
        return changed;
    }

    private static string[] CollectImportedModelAssetPaths()
    {
        var candidatePaths = new List<string>();
        foreach (var extension in new[] { "*.fbx", "*.pmx", "*.pmd", "*.MMD4Mecanim.asset", "*.MMD4Mecanim.xml" })
        {
            candidatePaths.AddRange(Directory.EnumerateFiles("Assets", extension, SearchOption.AllDirectories));
        }

        return candidatePaths
            .Select(NormalizeAssetPath)
            .Where(path => TryResolveImportedModelAssetPath(path, out _))
            .Select(path =>
            {
                TryResolveImportedModelAssetPath(path, out var modelAssetPath);
                return modelAssetPath;
            })
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool EnsureModelControllerForObject(UnityEngine.Object assetObject)
    {
        return TryResolveImportedModelAssetPath(assetObject, out var modelAssetPath)
            && EnsureModelController(modelAssetPath);
    }

    public static bool ProcessImportedModelForObject(UnityEngine.Object assetObject)
    {
        return TryResolveImportedModelAssetPath(assetObject, out var modelAssetPath)
            && ProcessImportedModel(modelAssetPath);
    }

    private static bool NormalizeRelatedPrefabs(string modelAssetPath)
    {
        var folderPath = NormalizeAssetPath(Path.GetDirectoryName(modelAssetPath));
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return false;
        }

        var changed = false;
        foreach (var prefabAssetPath in AssetDatabase.FindAssets("t:Prefab", new[] { folderPath })
                     .Select(AssetDatabase.GUIDToAssetPath)
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            changed |= NormalizePrefab(prefabAssetPath);
        }

        return changed;
    }

    private static bool NormalizePrefab(string prefabAssetPath)
    {
        var prefabRoot = PrefabUtility.LoadPrefabContents(prefabAssetPath);
        try
        {
            var changed = false;
            foreach (var renderer in prefabRoot.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                changed |= NormalizeRendererMaterialSlots(renderer);

                if (renderer is SkinnedMeshRenderer skinnedRenderer && !skinnedRenderer.updateWhenOffscreen)
                {
                    skinnedRenderer.updateWhenOffscreen = true;
                    EditorUtility.SetDirty(skinnedRenderer);
                    changed = true;
                }
            }

            if (!changed)
            {
                return false;
            }

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabAssetPath);
            Debug.Log($"[Codex] Normalized prefab renderer settings for '{prefabAssetPath}'.");
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private static bool NormalizeRendererMaterialSlots(Renderer renderer)
    {
        var expectedCount = GetExpectedMaterialCount(renderer);
        var sourceMaterials = renderer.sharedMaterials ?? Array.Empty<Material>();
        if (expectedCount <= 0)
        {
            if (sourceMaterials.Length == 0)
            {
                return false;
            }

            renderer.sharedMaterials = Array.Empty<Material>();
            EditorUtility.SetDirty(renderer);
            return true;
        }

        var compactMaterials = sourceMaterials
            .Where(material => material != null)
            .Take(expectedCount)
            .ToArray();

        if (compactMaterials.Length < expectedCount)
        {
            Array.Resize(ref compactMaterials, expectedCount);
            var fallbackMaterial = sourceMaterials.FirstOrDefault(material => material != null);
            for (var index = 0; index < compactMaterials.Length; index++)
            {
                compactMaterials[index] ??= fallbackMaterial;
            }
        }

        if (sourceMaterials.Length == compactMaterials.Length
            && sourceMaterials.SequenceEqual(compactMaterials))
        {
            return false;
        }

        renderer.sharedMaterials = compactMaterials;
        EditorUtility.SetDirty(renderer);
        return true;
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

    public static bool RepairModelMaterialsForObject(UnityEngine.Object assetObject)
    {
        if (!TryResolveImportedModelAssetPath(assetObject, out var modelAssetPath))
        {
            return false;
        }

        var changed = false;
        changed |= MMD4MecanimMaterialRepairer.RepairModelMaterials(modelAssetPath);
        changed |= MMD4MecanimPhysicsRepairer.RepairModelPhysics(modelAssetPath);
        changed |= MMD4MecanimClothAutoSetup.RemoveModelCloth(modelAssetPath);
        changed |= MMD4MecanimClothAutoSetup.RepairModelCloth(modelAssetPath);
        changed |= NormalizeRelatedPrefabs(modelAssetPath);
        return changed;
    }

    public static bool TryResolveImportedModelAssetPath(UnityEngine.Object assetObject, out string modelAssetPath)
    {
        return TryResolveImportedModelAssetPath(AssetDatabase.GetAssetPath(assetObject), out modelAssetPath);
    }

    public static bool TryResolveImportedModelAssetPath(string assetPath, out string modelAssetPath)
    {
        modelAssetPath = null;
        assetPath = NormalizeAssetPath(assetPath);
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return false;
        }

        if (MMD4MecanimMaterialRepairer.TryResolveMmdFbxAssetPath(assetPath, out modelAssetPath))
        {
            return true;
        }

        if (!assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
            && !assetPath.EndsWith(".controller", StringComparison.OrdinalIgnoreCase)
            && !assetPath.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var folderPath = NormalizeAssetPath(Path.GetDirectoryName(assetPath));
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return false;
        }

        foreach (var candidateFbxPath in Directory.EnumerateFiles(folderPath, "*.fbx", SearchOption.TopDirectoryOnly)
                     .Select(NormalizeAssetPath)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (MMD4MecanimMaterialRepairer.TryResolveMmdFbxAssetPath(candidateFbxPath, out modelAssetPath))
            {
                return true;
            }
        }

        return false;
    }

    private static bool EnsureModelController(string modelAssetPath)
    {
        modelAssetPath = NormalizeAssetPath(modelAssetPath);
        var motionClips = CollectMotionClips(modelAssetPath);
        if (motionClips.Length == 0)
        {
            return false;
        }

        var controllerAssetPath = Path.Combine(
                Path.GetDirectoryName(modelAssetPath) ?? string.Empty,
                Path.GetFileNameWithoutExtension(modelAssetPath) + ".controller")
            .Replace("\\", "/");

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerAssetPath);
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(controllerAssetPath);
        }

        if (controller == null)
        {
            throw new InvalidOperationException($"Failed to create or load AnimatorController '{controllerAssetPath}'.");
        }

        var changed = RebuildController(controller, motionClips);
        if (!changed)
        {
            return false;
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(controllerAssetPath, ImportAssetOptions.ForceUpdate);
        Debug.Log(
            $"[Codex] Rebuilt AnimatorController '{controllerAssetPath}' for model '{modelAssetPath}'. " +
            $"clips=[{string.Join(", ", motionClips.Select(clip => clip.name))}].");
        return true;
    }

    private static bool RebuildController(AnimatorController controller, AnimationClip[] motionClips)
    {
        var stateMachine = controller.layers[0].stateMachine;
        var childStates = stateMachine.states;
        for (var childStateIndex = 0; childStateIndex < childStates.Length; childStateIndex++)
        {
            stateMachine.RemoveState(childStates[childStateIndex].state);
        }

        var orderedClips = motionClips
            .Where(clip => clip != null)
            .Distinct()
            .OrderByDescending(clip => clip.length)
            .ThenBy(clip => clip.name, StringComparer.Ordinal)
            .ToArray();
        if (orderedClips.Length == 0)
        {
            return false;
        }

        AnimatorState defaultState = null;
        for (var index = 0; index < orderedClips.Length; index++)
        {
            var clip = orderedClips[index];
            var state = stateMachine.AddState(ResolveAnimatorStateName(clip, index));
            state.motion = clip;
            state.writeDefaultValues = true;

            if (defaultState == null)
            {
                defaultState = state;
            }
        }

        if (defaultState != null)
        {
            stateMachine.defaultState = defaultState;
        }

        return true;
    }

    private static string ResolveAnimatorStateName(AnimationClip clip, int index)
    {
        var candidate = clip != null && !string.IsNullOrWhiteSpace(clip.name)
            ? clip.name.Trim()
            : $"Motion_{index + 1}";
        foreach (var invalidCharacter in new[] { '.', '/', '\\', ':', '*', '?', '"', '<', '>', '|', '[', ']' })
        {
            candidate = candidate.Replace(invalidCharacter, '_');
        }

        return string.IsNullOrWhiteSpace(candidate)
            ? $"Motion_{index + 1}"
            : candidate;
    }

    private static AnimationClip[] CollectMotionClips(string modelAssetPath)
    {
        var folderPath = NormalizeAssetPath(Path.GetDirectoryName(modelAssetPath));
        var modelName = Path.GetFileNameWithoutExtension(modelAssetPath);
        var clips = new List<AnimationClip>();

        clips.AddRange(AssetDatabase.LoadAllAssetsAtPath(modelAssetPath)
            .OfType<AnimationClip>()
            .Where(IsMotionClip));

        if (!string.IsNullOrWhiteSpace(folderPath))
        {
            clips.AddRange(AssetDatabase.FindAssets("t:AnimationClip", new[] { folderPath })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !string.Equals(path, modelAssetPath, StringComparison.OrdinalIgnoreCase))
                .Select(AssetDatabase.LoadAssetAtPath<AnimationClip>)
                .Where(IsMotionClip)
                .Where(clip => IsLikelyRelatedMotionClip(clip, modelName, folderPath)));
        }

        return clips
            .Where(clip => clip != null)
            .Distinct()
            .ToArray();
    }

    private static bool IsLikelyRelatedMotionClip(AnimationClip clip, string modelName, string folderPath)
    {
        var clipPath = NormalizeAssetPath(AssetDatabase.GetAssetPath(clip));
        if (string.IsNullOrWhiteSpace(clipPath) || !clipPath.StartsWith(folderPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (clipPath.IndexOf("MikuLobbyMorph", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return false;
        }

        if (clip.name.IndexOf(modelName, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return true;
    }

    private static bool IsMotionClip(AnimationClip clip)
    {
        if (clip == null || string.IsNullOrWhiteSpace(clip.name))
        {
            return false;
        }

        if (clip.name.IndexOf("__preview__", StringComparison.OrdinalIgnoreCase) >= 0
            || clip.name.IndexOf("preview", StringComparison.OrdinalIgnoreCase) >= 0)
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

    private static string NormalizeAssetPath(string assetPath)
    {
        return assetPath?.Replace('\\', '/');
    }
}
