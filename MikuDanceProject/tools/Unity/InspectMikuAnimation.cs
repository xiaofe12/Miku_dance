using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class InspectMikuAnimation
{
    private static readonly string[] PrefabAssetPaths =
    {
        "Assets/Kinsama式初音ミクV4C/Kinsama式初音ミクV4C.prefab",
        "Assets/CodexBuild/MikuLobby.prefab",
    };

    public static void Run()
    {
        Debug.Log("[Codex] ===== Begin Miku animation inspection =====");

        foreach (var prefabAssetPath in PrefabAssetPaths)
        {
            InspectPrefab(prefabAssetPath);
        }

        InspectAssetFromEnvironment();
        InspectBundleFromEnvironment();

        Debug.Log("[Codex] ===== End Miku animation inspection =====");
        EditorApplication.Exit(0);
    }

    public static void InspectGameObject(GameObject prefab)
    {
        if (prefab == null)
        {
            Debug.LogWarning("[Codex] No GameObject was provided for inspection.");
            return;
        }

        Debug.Log($"[Codex] Inspecting GameObject '{prefab.name}'.");
        InspectRuntimePrefab(prefab);
    }

    public static void InspectAssetPath(string prefabAssetPath)
    {
        InspectPrefab(prefabAssetPath);
    }

    private static void InspectBundleFromEnvironment()
    {
        var bundlePath = Environment.GetEnvironmentVariable("CODEX_MIKU_BUNDLE_INSPECT");
        if (string.IsNullOrWhiteSpace(bundlePath))
        {
            return;
        }

        bundlePath = Path.GetFullPath(bundlePath);
        if (!File.Exists(bundlePath))
        {
            Debug.LogWarning($"[Codex] Bundle file not found: '{bundlePath}'.");
            return;
        }

        var bundle = AssetBundle.LoadFromFile(bundlePath);
        if (bundle == null)
        {
            Debug.LogWarning($"[Codex] Failed to load bundle from '{bundlePath}'.");
            return;
        }

        try
        {
            var assetNames = bundle.GetAllAssetNames();
            Debug.Log($"[Codex] Inspecting bundle '{bundlePath}'. assets=[{string.Join(", ", assetNames)}].");

            var prefabAssetName = assetNames.FirstOrDefault(name =>
                string.Equals(name, "assets/codexbuild/mikulobby.prefab", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("/mikulobby.prefab", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(prefabAssetName))
            {
                Debug.LogWarning($"[Codex] Bundle '{bundlePath}' does not contain a MikuLobby prefab asset.");
                return;
            }

            var prefab = bundle.LoadAsset<GameObject>(prefabAssetName);
            if (prefab == null)
            {
                Debug.LogWarning($"[Codex] Failed to load bundle prefab '{prefabAssetName}'.");
                return;
            }

            Debug.Log($"[Codex] Inspecting bundle prefab '{prefabAssetName}'.");
            InspectRuntimePrefab(prefab);
        }
        finally
        {
            bundle.Unload(unloadAllLoadedObjects: false);
        }
    }

    private static void InspectAssetFromEnvironment()
    {
        var assetPath = Environment.GetEnvironmentVariable("CODEX_MIKU_INSPECT_ASSET");
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return;
        }

        assetPath = assetPath.Replace('\\', '/');
        var assetsIndex = assetPath.IndexOf("Assets/", StringComparison.OrdinalIgnoreCase);
        if (assetsIndex >= 0)
        {
            assetPath = assetPath.Substring(assetsIndex);
        }

        Debug.Log($"[Codex] Inspecting asset path '{assetPath}' from CODEX_MIKU_INSPECT_ASSET.");

        var importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
        if (importer != null)
        {
            Debug.Log(
                $"[Codex] ModelImporter animationType={importer.animationType}, avatarSetup={importer.avatarSetup}, " +
                $"sourceAvatar='{importer.sourceAvatar?.name ?? "(none)"}', optimizeGameObjects={importer.optimizeGameObjects}, " +
                $"importAnimation={importer.importAnimation}, animationCompression={importer.animationCompression}, " +
                $"rotationError={importer.animationRotationError:0.###}, positionError={importer.animationPositionError:0.###}, " +
                $"scaleError={importer.animationScaleError:0.###}.");

            foreach (var clip in importer.clipAnimations)
            {
                Debug.Log(
                    $"[Codex] ModelImporter clip name='{clip.name}', take='{clip.takeName}', frames={clip.firstFrame:0.###}-{clip.lastFrame:0.###}, " +
                    $"loopTime={clip.loopTime}, maskType={clip.maskType}.");
            }
        }

        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(assetPath).Where(asset => asset != null))
        {
            Debug.Log($"[Codex] Subasset type={asset.GetType().FullName}, name='{asset.name}'.");

            if (asset is Avatar avatar)
            {
                Debug.Log($"[Codex] Avatar '{avatar.name}' isValid={avatar.isValid}, isHuman={avatar.isHuman}, humanDescriptionImported={avatar.humanDescription.human?.Length ?? 0}.");
            }
            else if (asset is RuntimeAnimatorController controller)
            {
                var clips = controller.animationClips
                    .Where(clip => clip != null)
                    .OrderByDescending(clip => clip.length)
                    .ToArray();
                Debug.Log($"[Codex] Controller '{controller.name}' clips=[{string.Join(", ", clips.Select(clip => $"{clip.name}({clip.length:0.###}s)"))}].");
                foreach (var clip in clips)
                {
                    InspectClipBindings(clip);
                }
            }
            else if (asset is AnimationClip clip)
            {
                InspectClipBindings(clip);
            }
        }
    }

    private static void InspectPrefab(string prefabAssetPath)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabAssetPath);
        if (prefab == null)
        {
            Debug.LogWarning($"[Codex] Prefab not found: '{prefabAssetPath}'.");
            return;
        }

        Debug.Log($"[Codex] Inspecting prefab '{prefabAssetPath}'.");
        InspectRuntimePrefab(prefab);
    }

    private static void InspectRuntimePrefab(GameObject prefab)
    {
        var animator = prefab.GetComponentInChildren<Animator>(includeInactive: true);
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            Debug.LogWarning($"[Codex] Prefab '{prefab.name}' has no AnimatorController.");
            InspectRenderers(prefab.transform);
            return;
        }

        var clips = animator.runtimeAnimatorController.animationClips
            .Where(clip => clip != null)
            .OrderByDescending(clip => clip.length)
            .ToArray();
        var primaryClip = clips.FirstOrDefault();
        Debug.Log(
            $"[Codex] Animator controller='{animator.runtimeAnimatorController.name}', clips=[{string.Join(", ", clips.Select(clip => $"{clip.name}({clip.length:0.###}s)"))}].");

        InspectRenderers(prefab.transform);

        if (primaryClip == null)
        {
            Debug.LogWarning($"[Codex] Prefab '{prefab.name}' has no animation clips.");
            return;
        }

        InspectClipBindings(primaryClip);
    }

    private static void InspectRenderers(Transform root)
    {
        foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true))
        {
            var mesh = renderer.sharedMesh;
            if (mesh == null)
            {
                Debug.Log($"[Codex] Renderer path='{BuildPath(root, renderer.transform)}' has no shared mesh.");
                continue;
            }

            var blendShapeNames = new List<string>(mesh.blendShapeCount);
            for (var index = 0; index < mesh.blendShapeCount; index++)
            {
                blendShapeNames.Add(mesh.GetBlendShapeName(index));
            }

            Debug.Log(
                $"[Codex] Renderer path='{BuildPath(root, renderer.transform)}', mesh='{mesh.name}', blendShapeCount={mesh.blendShapeCount}, " +
                $"blendShapes=[{string.Join(", ", blendShapeNames)}].");
        }
    }

    private static void InspectClipBindings(AnimationClip clip)
    {
        var bindings = AnimationUtility.GetCurveBindings(clip);
        var blendShapeBindings = bindings
            .Where(binding => binding.type == typeof(SkinnedMeshRenderer)
                || binding.propertyName.IndexOf("blendShape.", StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(binding => binding.path, StringComparer.Ordinal)
            .ThenBy(binding => binding.propertyName, StringComparer.Ordinal)
            .ToArray();

        var objectReferenceBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        Debug.Log(
            $"[Codex] Clip '{clip.name}' length={clip.length:0.###}, frameRate={clip.frameRate:0.###}, legacy={clip.legacy}, " +
            $"humanMotion={clip.humanMotion}, loopTime={settings.loopTime}, curveBindings={bindings.Length}, " +
            $"objectReferenceBindings={objectReferenceBindings.Length}, blendShapeBindings={blendShapeBindings.Length}.");

        foreach (var group in bindings.GroupBy(binding => binding.type != null ? binding.type.Name : "(null)")
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Take(12))
        {
            Debug.Log($"[Codex] Clip '{clip.name}' bindingType='{group.Key}', count={group.Count()}.");
        }

        foreach (var group in blendShapeBindings.GroupBy(binding => binding.path, StringComparer.Ordinal))
        {
            Debug.Log(
                $"[Codex] Clip '{clip.name}' blendShape path='{group.Key}', properties=[{string.Join(", ", group.Select(binding => binding.propertyName))}].");
        }
    }

    private static string BuildPath(Transform root, Transform target)
    {
        if (target == root)
        {
            return string.Empty;
        }

        var stack = new Stack<string>();
        var current = target;
        while (current != null && current != root)
        {
            stack.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", stack.ToArray());
    }
}
