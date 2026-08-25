#nullable enable annotations
#nullable disable warnings

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(fileName = "MikuBundleBuildProfile", menuName = "Miku Showcase/Bundle Build Profile")]
public sealed class MikuBundleBuildProfile : ScriptableObject
{
    [Header("Core Assets")]
    public GameObject DisplayAsset;
    public RuntimeAnimatorController BodyController;
    public AnimationClip BodyAnimationClip;
    public AnimationClip FacialAnimationClip;
    public AudioClip BackgroundMusic;

    [Header("Playback Range")]
    public int MotionStartFrame = 0;
    public bool LimitMotionEndFrame;
    public int MotionEndFrame;

    [Header("Bundle Output")]
    public string BundleName = "miku_lobby_display.bundle";
    public string OutputDirectory = string.Empty;

    [Header("Texture Sampling")]
    public MikuBundleTextureSampling TextureSampling = MikuBundleTextureSampling.Original;

    [Header("Material Handling")]
    public MikuBundleMaterialHandling MaterialHandling = MikuBundleMaterialHandling.AutoCompatible;

    [Header("Optional Material Overrides")]
    public List<MikuBundleMaterialOverride> MaterialOverrides = new List<MikuBundleMaterialOverride>();

    [Header("Prepared Build Cache")]
    public bool PreferPreparedAssets = true;

    [HideInInspector] public GameObject PreparedPrefab;
    [HideInInspector] public AnimationClip PreparedBodyAnimationClip;
    [HideInInspector] public AnimationClip PreparedFacialAnimationClip;
    [HideInInspector] public RuntimeAnimatorController PreparedController;
    [HideInInspector] public TextAsset PreparedPlaybackMetadata;
    [HideInInspector] public string PreparedAssetFolderPath = string.Empty;
    [HideInInspector] public string PreparedCacheSignature = string.Empty;

#if UNITY_EDITOR
    private void OnEnable()
    {
        MikuBundleBuildProfileAutoSync.TrySyncLoadedProfile(this);
    }
#endif

    public string ResolveOutputDirectory()
    {
        if (!string.IsNullOrWhiteSpace(OutputDirectory))
        {
            return Path.GetFullPath(OutputDirectory);
        }

        var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.Combine(projectRoot, "BundleOutput");
    }
}

[Serializable]
public sealed class MikuBundleMaterialOverride
{
    public string RendererPath = string.Empty;
    public List<Material> Materials = new List<Material>();
}

public enum MikuBundleTextureSampling
{
    Original,
    High,
    Balanced,
    Compact,
}

public enum MikuBundleMaterialHandling
{
    AutoCompatible,
    PreserveProjectMaterials,
    ForceUrpCompatible,
}

#if UNITY_EDITOR
internal static class MikuBundleBuildProfileAutoSync
{
    private sealed class BlendShapeNameEntry
    {
        public string Name;
        public string Normalized;
    }

    private const string ProfileAssetPath = "Assets/MikuBundleBuildProfile.asset";
    private const string DefaultBundleName = "miku_lobby_display.bundle";
    private const string BundleRelativeModelRootName = "MikuLobbyModel";
    private const string DisplayAssetGuid = "7dc7d8f635b1c8d4584e10e882420aa0";
    private const string BodyControllerGuid = "0465593a6873b3e4084d7a782a56da79";
    private const string FacialAnimationClipGuid = "4c3dee400fa8bfc4bad704dea3c3463f";
    private const string BackgroundMusicGuid = "100b4f0e80dde444190d41d9db4f2e38";
    private const string ExportedBodyClipAssetPath = "Assets/CodexBuild/Source/MikuLobbyBodySource.anim";
    private const string WorkspaceBuildBundleSourceRelativePath = @"MikuDanceProject\tools\Unity\BuildMikuLobbyAssetBundle.cs";
    private const string WorkspaceMorphExportSourceRelativePath = @"MikuDanceProject\tools\Unity\ExportMikuLobbyMorphClip.cs";
    private const string WorkspaceInspectAnimationSourceRelativePath = @"MikuDanceProject\tools\Unity\InspectMikuAnimation.cs";
    private const string WorkspaceAnimationTrimSourceRelativePath = @"MikuDanceProject\tools\Unity\MikuAnimationClipTrimUtility.cs";
    private const string WorkspaceBuilderWindowSourceRelativePath = @"MikuDanceProject\tools\Unity\MikuBundleBuilderWindow.cs";
    private const string WorkspaceBuildProfileSourceRelativePath = @"MikuDanceProject\tools\Unity\MikuBundleBuildProfile.cs";
    private const string WorkspaceMaterialCompatibilitySourceRelativePath = @"MikuDanceProject\tools\Unity\MikuBundleMaterialCompatibilityUtility.cs";
    private const string WorkspaceImportedModelAutomationSourceRelativePath = @"MikuDanceProject\tools\Unity\MikuImportedModelAutomation.cs";
    private const string WorkspaceToonShaderSharedSourceRelativePath = @"MikuDanceProject\tools\UnityShaderSync\SimpleURPToonLitOutlineExample_Shared.hlsl";
    private const string WorkspaceToonShaderLightingSourceRelativePath = @"MikuDanceProject\tools\UnityShaderSync\SimpleURPToonLitOutlineExample_LightingEquation.hlsl";
    private const string WorkspacePowerBearConvertSourceRelativePath = @"temp\PowerBear\Editor\Mmd\ConvertVMD.cs";
    private const string WorkspacePowerBearMorphGeneratorSourceRelativePath = @"temp\PowerBear\Editor\MmdMorphGenerate.cs";
    private const string ProjectBuildBundleAssetPath = "Assets/Editor/BuildMikuLobbyAssetBundle.cs";
    private const string ProjectMorphExportAssetPath = "Assets/Editor/ExportMikuLobbyMorphClip.cs";
    private const string ProjectInspectAnimationAssetPath = "Assets/Editor/InspectMikuAnimation.cs";
    private const string ProjectAnimationTrimAssetPath = "Assets/Editor/MikuAnimationClipTrimUtility.cs";
    private const string ProjectBuilderWindowAssetPath = "Assets/Editor/MikuBundleBuilderWindow.cs";
    private const string ProjectBuildProfileAssetPath = "Assets/Editor/MikuBundleBuildProfile.cs";
    private const string ProjectMaterialCompatibilityAssetPath = "Assets/Editor/MikuBundleMaterialCompatibilityUtility.cs";
    private const string ProjectImportedModelAutomationAssetPath = "Assets/Editor/MikuImportedModelAutomation.cs";
    private const string ProjectToonShaderSharedAssetPath = "Assets/UnityURPToonLitShaderExample-master/UnityURPToonLitShaderExample-master/SimpleURPToonLitOutlineExample_Shared.hlsl";
    private const string ProjectToonShaderLightingAssetPath = "Assets/UnityURPToonLitShaderExample-master/UnityURPToonLitShaderExample-master/SimpleURPToonLitOutlineExample_LightingEquation.hlsl";
    private const string ProjectPowerBearConvertAssetPath = "Assets/Plugins/PowerBear/Editor/Mmd/ConvertVMD.cs";
    private const string ProjectPowerBearMorphGeneratorAssetPath = "Assets/Plugins/PowerBear/Editor/MmdMorphGenerate.cs";
    private const string SelectedProfileStateRelativePath = @"ProjectSettings\CodexMikuActiveProfile.txt";
    private static bool _isSyncing;
    private static bool _didAttemptWorkspacePowerBearSync;

    [InitializeOnLoadMethod]
    private static void InitializeOnEditorLoad()
    {
        TrySyncWorkspacePowerBearEditorScripts();
    }

    internal static void SaveSelectedProfileAssetPath(MikuBundleBuildProfile? profile)
    {
        var profileAssetPath = NormalizeAssetPath(profile != null ? AssetDatabase.GetAssetPath(profile) : string.Empty);
        if (string.IsNullOrWhiteSpace(profileAssetPath) || !profileAssetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var stateFilePath = ResolveSelectedProfileStatePath();
        var stateDirectory = Path.GetDirectoryName(stateFilePath);
        if (string.IsNullOrWhiteSpace(stateDirectory))
        {
            return;
        }

        Directory.CreateDirectory(stateDirectory);
        var currentValue = File.Exists(stateFilePath) ? File.ReadAllText(stateFilePath).Trim() : string.Empty;
        if (string.Equals(currentValue, profileAssetPath, StringComparison.Ordinal))
        {
            return;
        }

        File.WriteAllText(stateFilePath, profileAssetPath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    internal static string? TryResolveSelectedProfileAssetPath()
    {
        var stateFilePath = ResolveSelectedProfileStatePath();
        if (!File.Exists(stateFilePath))
        {
            return null;
        }

        var assetPath = NormalizeAssetPath(File.ReadAllText(stateFilePath).Trim());
        if (string.IsNullOrWhiteSpace(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return AssetDatabase.LoadAssetAtPath<MikuBundleBuildProfile>(assetPath) != null
            ? assetPath
            : null;
    }

    internal static void TrySyncLoadedProfile(MikuBundleBuildProfile profile)
    {
        if (_isSyncing || profile == null)
        {
            return;
        }

        var assetPath = AssetDatabase.GetAssetPath(profile);
        if (!string.Equals(assetPath, ProfileAssetPath, StringComparison.Ordinal))
        {
            return;
        }

        ForceSyncProfile(profile);
    }

    internal static bool ForceSyncProfile()
    {
        var profile = AssetDatabase.LoadAssetAtPath<MikuBundleBuildProfile>(ProfileAssetPath);
        if (profile == null)
        {
            Debug.LogWarning("[Codex] Skipped Miku bundle profile sync because the profile asset could not be resolved.");
            return false;
        }

        return ForceSyncProfile(profile);
    }

    private static bool ForceSyncProfile(MikuBundleBuildProfile profile)
    {
        if (_isSyncing)
        {
            return false;
        }

        if (TrySyncWorkspacePowerBearEditorScripts())
        {
            Debug.Log("[Codex] Synced PowerBear editor scripts from workspace. Unity will reimport scripts before the next profile sync.");
            return false;
        }

        _isSyncing = true;
        try
        {
            var displayAsset = LoadAssetByGuid<GameObject>(DisplayAssetGuid);
            var bodyController = LoadAssetByGuid<RuntimeAnimatorController>(BodyControllerGuid);
            var facialAnimationClip = LoadAssetByGuid<AnimationClip>(FacialAnimationClipGuid);
            var backgroundMusic = LoadAssetByGuid<AudioClip>(BackgroundMusicGuid);
            if (displayAsset == null || bodyController == null || facialAnimationClip == null || backgroundMusic == null)
            {
                Debug.LogWarning("[Codex] Skipped Miku bundle profile sync because one or more Sour Cherry assets could not be resolved.");
                return false;
            }

            var sourceBodyClip = ResolveBodyClip(displayAsset, bodyController);

            var changed = false;
            changed |= AssignIfMissing(ref profile.DisplayAsset, displayAsset);
            changed |= AssignIfMissing(ref profile.BodyController, bodyController);
            changed |= AssignIfMissing(ref profile.FacialAnimationClip, facialAnimationClip);
            changed |= AssignIfMissing(ref profile.BackgroundMusic, backgroundMusic);

            if (string.IsNullOrWhiteSpace(profile.BundleName))
            {
                profile.BundleName = DefaultBundleName;
                changed = true;
            }

            if (!changed)
            {
                Debug.Log(
                    $"[Codex] Miku bundle profile reference sync found no missing fields. " +
                    $"profile='{AssetDatabase.GetAssetPath(profile)}', bodySource='{AssetDatabase.GetAssetPath(sourceBodyClip) ?? "(none)"}', " +
                    $"facialSource='{AssetDatabase.GetAssetPath(facialAnimationClip) ?? "(none)"}'.");
                return true;
            }

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            Debug.Log(
                $"[Codex] Filled missing references in Miku bundle profile. " +
                $"profile='{AssetDatabase.GetAssetPath(profile)}', bodySource='{AssetDatabase.GetAssetPath(sourceBodyClip) ?? "(none)"}', " +
                $"facialSource='{AssetDatabase.GetAssetPath(facialAnimationClip) ?? "(none)"}'.");
            return true;
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private static bool TrySyncWorkspacePowerBearEditorScripts()
    {
        if (_didAttemptWorkspacePowerBearSync)
        {
            return false;
        }

        _didAttemptWorkspacePowerBearSync = true;
        var workspaceRoot = TryResolveWorkspaceRoot();
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return false;
        }

        var changed = false;
        changed |= TrySyncWorkspaceFileToProject(
            Path.Combine(workspaceRoot, WorkspaceBuildBundleSourceRelativePath),
            ProjectBuildBundleAssetPath);
        changed |= TrySyncWorkspaceFileToProject(
            Path.Combine(workspaceRoot, WorkspaceMorphExportSourceRelativePath),
            ProjectMorphExportAssetPath);
        changed |= TrySyncWorkspaceFileToProject(
            Path.Combine(workspaceRoot, WorkspaceInspectAnimationSourceRelativePath),
            ProjectInspectAnimationAssetPath);
        changed |= TrySyncWorkspaceFileToProject(
            Path.Combine(workspaceRoot, WorkspaceAnimationTrimSourceRelativePath),
            ProjectAnimationTrimAssetPath);
        changed |= TrySyncWorkspaceFileToProject(
            Path.Combine(workspaceRoot, WorkspaceBuilderWindowSourceRelativePath),
            ProjectBuilderWindowAssetPath);
        changed |= TrySyncWorkspaceFileToProject(
            Path.Combine(workspaceRoot, WorkspaceBuildProfileSourceRelativePath),
            ProjectBuildProfileAssetPath);
        changed |= TrySyncWorkspaceFileToProject(
            Path.Combine(workspaceRoot, WorkspaceMaterialCompatibilitySourceRelativePath),
            ProjectMaterialCompatibilityAssetPath);
        changed |= TrySyncWorkspaceFileToProject(
            Path.Combine(workspaceRoot, WorkspaceImportedModelAutomationSourceRelativePath),
            ProjectImportedModelAutomationAssetPath);
        changed |= TrySyncWorkspaceFileToProject(
            Path.Combine(workspaceRoot, WorkspaceToonShaderSharedSourceRelativePath),
            ProjectToonShaderSharedAssetPath);
        changed |= TrySyncWorkspaceFileToProject(
            Path.Combine(workspaceRoot, WorkspaceToonShaderLightingSourceRelativePath),
            ProjectToonShaderLightingAssetPath);
        changed |= TrySyncWorkspaceFileToProject(
            Path.Combine(workspaceRoot, WorkspacePowerBearConvertSourceRelativePath),
            ProjectPowerBearConvertAssetPath);
        changed |= TrySyncWorkspaceFileToProject(
            Path.Combine(workspaceRoot, WorkspacePowerBearMorphGeneratorSourceRelativePath),
            ProjectPowerBearMorphGeneratorAssetPath);

        if (changed)
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        }

        return changed;
    }

    internal static string? TryResolveWorkspaceRoot()
    {
        return EnumerateWorkspaceRootCandidates()
            .Select(NormalizeWorkspaceRootCandidate)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(path => Directory.Exists(path));
    }

    private static IEnumerable<string> EnumerateWorkspaceRootCandidates()
    {
        var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        var projectName = Path.GetFileName(projectRoot);
        var environmentRoot = Environment.GetEnvironmentVariable("CODEX_MIKU_WORKSPACE_ROOT");
        if (!string.IsNullOrWhiteSpace(environmentRoot))
        {
            yield return environmentRoot;
        }

        if (!string.IsNullOrWhiteSpace(projectName))
        {
            var projectDriveRoot = Path.GetPathRoot(projectRoot);
            if (!string.IsNullOrWhiteSpace(projectDriveRoot))
            {
                yield return Path.Combine(projectDriveRoot, "Mod Projects", projectName);
            }

            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), projectName);
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop", projectName);
            yield return Path.Combine(@"D:\Mod Projects", projectName);
            yield return Path.Combine(@"D:\UnityProjects", projectName);
        }
    }

    private static string? NormalizeWorkspaceRootCandidate(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(candidate);
        if (File.Exists(Path.Combine(fullPath, "MikuDanceProject", "MikuDanceProject.csproj")))
        {
            return fullPath;
        }

        if (File.Exists(Path.Combine(fullPath, "MikuDanceProject.csproj")))
        {
            return Directory.GetParent(fullPath)?.FullName;
        }

        return null;
    }

    private static bool TrySyncWorkspaceFileToProject(string sourcePath, string targetAssetPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(targetAssetPath) || !File.Exists(sourcePath))
        {
            return false;
        }

        var targetAbsolutePath = ResolveAbsoluteAssetPath(targetAssetPath);
        var targetDirectory = Path.GetDirectoryName(targetAbsolutePath);
        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            return false;
        }

        Directory.CreateDirectory(targetDirectory);

        var sourceText = File.ReadAllText(sourcePath);
        var targetText = File.Exists(targetAbsolutePath) ? File.ReadAllText(targetAbsolutePath) : null;
        if (string.Equals(sourceText, targetText, StringComparison.Ordinal))
        {
            return false;
        }

        File.WriteAllText(targetAbsolutePath, sourceText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        AssetDatabase.ImportAsset(targetAssetPath, ImportAssetOptions.ForceUpdate);
        Debug.Log($"[Codex] Synced workspace editor script to '{targetAssetPath}'.");
        return true;
    }

    private static bool AssignIfMissing<T>(ref T field, T value)
        where T : UnityEngine.Object
    {
        if (field != null || value == null)
        {
            return false;
        }

        field = value;
        return true;
    }

    private static string ResolveSelectedProfileStatePath()
    {
        var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.Combine(projectRoot, SelectedProfileStateRelativePath);
    }

    private static string NormalizeAssetPath(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return string.Empty;
        }

        return assetPath.Replace("\\", "/").Trim();
    }

    private static void ConfigureSourceModelImporter(AnimationClip sourceBodyClip)
    {
        if (sourceBodyClip == null)
        {
            return;
        }

        var assetPath = AssetDatabase.GetAssetPath(sourceBodyClip);
        if (string.IsNullOrWhiteSpace(assetPath) || AssetImporter.GetAtPath(assetPath) is not ModelImporter importer)
        {
            return;
        }

        var changed = false;
        changed |= SetIfDifferent(() => importer.meshCompression, value => importer.meshCompression = value, ModelImporterMeshCompression.Off);
        changed |= SetIfDifferent(() => importer.isReadable, value => importer.isReadable = value, true);
        changed |= SetIfDifferent(() => importer.importCameras, value => importer.importCameras = value, false);
        changed |= SetIfDifferent(() => importer.importLights, value => importer.importLights = value, false);
        changed |= SetIfDifferent(() => importer.importVisibility, value => importer.importVisibility = value, false);
        changed |= SetIfDifferent(() => importer.resampleCurves, value => importer.resampleCurves = value, true);
        changed |= SetIfDifferent(() => importer.animationCompression, value => importer.animationCompression = value, ModelImporterAnimationCompression.Off);
        changed |= SetIfDifferent(() => importer.animationRotationError, value => importer.animationRotationError = value, 0.01f);
        changed |= SetIfDifferent(() => importer.animationPositionError, value => importer.animationPositionError = value, 0.01f);
        changed |= SetIfDifferent(() => importer.animationScaleError, value => importer.animationScaleError = value, 0.01f);

        if (!changed)
        {
            return;
        }

        Debug.Log(
            $"[Codex] Optimizing source model importer '{assetPath}'. " +
            $"meshCompression={importer.meshCompression}, animationCompression={importer.animationCompression}, " +
            $"rotationError={importer.animationRotationError}, positionError={importer.animationPositionError}, scaleError={importer.animationScaleError}.");
        importer.SaveAndReimport();
    }

    private static AnimationClip TryExportStandaloneBodyClip(AnimationClip sourceBodyClip)
    {
        if (sourceBodyClip == null)
        {
            return null;
        }

        try
        {
            return ExportStandaloneBodyClipAsset(
                sourceBodyClip,
                ExportedBodyClipAssetPath,
                "MikuLobbyBody");
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"[Codex] Failed to export standalone body clip from '{AssetDatabase.GetAssetPath(sourceBodyClip)}'. " +
                $"Falling back to the source controller. Reason: {exception.Message}");
            return null;
        }
    }

    private static AnimationClip ExportStandaloneBodyClipAsset(
        AnimationClip sourceClip,
        string outputAssetPath,
        string clipName)
    {
        if (TryExtractEmbeddedBodyClip(sourceClip, outputAssetPath, clipName, out var extractedClip))
        {
            return extractedClip;
        }

        if (TryCloneStandaloneBodyClip(sourceClip, outputAssetPath, clipName, out var clonedClip))
        {
            return clonedClip;
        }

        var outputFolderPath = Path.GetDirectoryName(outputAssetPath)?.Replace("\\", "/");
        if (string.IsNullOrWhiteSpace(outputFolderPath))
        {
            throw new InvalidOperationException($"Output clip asset path is invalid: '{outputAssetPath}'.");
        }

        EnsureFolderExists(outputFolderPath);

        var outputClip = new AnimationClip
        {
            name = clipName,
            frameRate = sourceClip.frameRate,
            legacy = sourceClip.legacy,
            wrapMode = sourceClip.wrapMode,
        };

        var clipLength = Mathf.Max(0f, sourceClip.length);
        var floatBindings = AnimationUtility.GetCurveBindings(sourceClip);
        var copiedFloatBindings = 0;
        var compactedFloatBindings = 0;
        var skippedBlendShapeBindings = 0;
        var skippedObjectReferenceBindings = AnimationUtility.GetObjectReferenceCurveBindings(sourceClip).Length;
        Debug.Log(
            $"[Codex] Falling back to manual standalone body clip export for '{AssetDatabase.GetAssetPath(sourceClip)}'. " +
            $"floatBindings={floatBindings.Length}, objectReferenceBindings={skippedObjectReferenceBindings}.");

        foreach (var binding in floatBindings)
        {
            if (IsBlendShapeBinding(binding))
            {
                skippedBlendShapeBindings++;
                continue;
            }

            var sourceCurve = AnimationUtility.GetEditorCurve(sourceClip, binding);
            if (sourceCurve == null || sourceCurve.length == 0)
            {
                continue;
            }

            var optimizedCurve = CloneOrCompactCurve(sourceCurve, clipLength, out var compacted);
            if (optimizedCurve == null || optimizedCurve.length == 0)
            {
                continue;
            }

            AnimationUtility.SetEditorCurve(outputClip, binding, optimizedCurve);
            copiedFloatBindings++;
            if (compacted)
            {
                compactedFloatBindings++;
            }
        }

        AnimationUtility.SetAnimationEvents(outputClip, AnimationUtility.GetAnimationEvents(sourceClip));
        var settings = AnimationUtility.GetAnimationClipSettings(sourceClip);
        AnimationUtility.SetAnimationClipSettings(outputClip, settings);
        outputClip.EnsureQuaternionContinuity();

        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(outputAssetPath) != null)
        {
            AssetDatabase.DeleteAsset(outputAssetPath);
        }

        var exportedClip = PersistStandaloneClipAsset(
            sourceClip,
            outputClip,
            outputAssetPath,
            clipName,
            "manual-curve-copy")
            ?? throw new InvalidOperationException($"Failed to load exported body clip '{outputAssetPath}'.");

        Debug.Log(
            $"[Codex] Exported standalone body clip '{outputAssetPath}' from '{AssetDatabase.GetAssetPath(sourceClip)}'. " +
            $"copiedFloatBindings={copiedFloatBindings}, compactedFloatBindings={compactedFloatBindings}, " +
            $"skippedBlendShapeBindings={skippedBlendShapeBindings}, skippedObjectReferenceBindings={skippedObjectReferenceBindings}, length={exportedClip.length:0.###}.");
        return exportedClip;
    }

    private static bool TryCloneStandaloneBodyClip(
        AnimationClip sourceClip,
        string outputAssetPath,
        string clipName,
        out AnimationClip exportedClip)
    {
        exportedClip = null;

        try
        {
            var instantiatedClip = UnityEngine.Object.Instantiate(sourceClip);
            exportedClip = PersistStandaloneClipAsset(
                sourceClip,
                instantiatedClip,
                outputAssetPath,
                clipName,
                "instantiate");
            if (exportedClip != null)
            {
                return true;
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"[Codex] Fast standalone body clip clone via instantiate failed for '{AssetDatabase.GetAssetPath(sourceClip)}'. " +
                $"Reason: {exception.Message}");
        }

        try
        {
            var copiedClip = new AnimationClip();
            EditorUtility.CopySerialized(sourceClip, copiedClip);
            exportedClip = PersistStandaloneClipAsset(
                sourceClip,
                copiedClip,
                outputAssetPath,
                clipName,
                "copy-serialized");
            return exportedClip != null;
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"[Codex] Fast standalone body clip clone via CopySerialized failed for '{AssetDatabase.GetAssetPath(sourceClip)}'. " +
                $"Reason: {exception.Message}");
            exportedClip = null;
            return false;
        }
    }

    private static AnimationClip PersistStandaloneClipAsset(
        AnimationClip sourceClip,
        AnimationClip candidateClip,
        string outputAssetPath,
        string clipName,
        string strategy)
    {
        if (candidateClip == null)
        {
            return null;
        }

        candidateClip.name = string.IsNullOrWhiteSpace(clipName) ? sourceClip.name : clipName;
        candidateClip.hideFlags = HideFlags.None;

        var outputFolderPath = Path.GetDirectoryName(outputAssetPath)?.Replace("\\", "/");
        if (string.IsNullOrWhiteSpace(outputFolderPath))
        {
            return null;
        }

        EnsureFolderExists(outputFolderPath);
        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(outputAssetPath) != null)
        {
            AssetDatabase.DeleteAsset(outputAssetPath);
        }

        AssetDatabase.CreateAsset(candidateClip, outputAssetPath);
        EditorUtility.SetDirty(candidateClip);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(outputAssetPath, ImportAssetOptions.ForceUpdate);

        var exportedClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(outputAssetPath);
        if (exportedClip == null)
        {
            Debug.LogWarning($"[Codex] Persisted standalone clip '{outputAssetPath}' could not be loaded after strategy '{strategy}'.");
            AssetDatabase.DeleteAsset(outputAssetPath);
            return null;
        }

        var dependencyPaths = AssetDatabase.GetDependencies(outputAssetPath, true)
            .Where(path => !string.Equals(path, outputAssetPath, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var sourceAssetPath = AssetDatabase.GetAssetPath(sourceClip);
        if (dependencyPaths.Any(path => string.Equals(path, sourceAssetPath, StringComparison.OrdinalIgnoreCase)))
        {
            Debug.LogWarning(
                $"[Codex] Strategy '{strategy}' produced '{outputAssetPath}', but it still depends on '{sourceAssetPath}'. " +
                "Discarding the exported clip.");
            AssetDatabase.DeleteAsset(outputAssetPath);
            AssetDatabase.SaveAssets();
            return null;
        }

        var bindingCount = AnimationUtility.GetCurveBindings(exportedClip).Length;
        if (bindingCount <= 0)
        {
            Debug.LogWarning(
                $"[Codex] Strategy '{strategy}' produced '{outputAssetPath}', but the exported clip does not contain any float curve bindings.");
            AssetDatabase.DeleteAsset(outputAssetPath);
            AssetDatabase.SaveAssets();
            return null;
        }

        Debug.Log(
            $"[Codex] Exported standalone clip '{outputAssetPath}' using strategy '{strategy}'. " +
            $"bindings={bindingCount}, dependencies={dependencyPaths.Length}, length={exportedClip.length:0.###}.");
        return exportedClip;
    }

    private static bool TryExtractEmbeddedBodyClip(
        AnimationClip sourceClip,
        string outputAssetPath,
        string clipName,
        out AnimationClip extractedClip)
    {
        extractedClip = null;
        var sourceAssetPath = AssetDatabase.GetAssetPath(sourceClip);
        if (string.IsNullOrWhiteSpace(sourceAssetPath)
            || AssetImporter.GetAtPath(sourceAssetPath) is not ModelImporter)
        {
            return false;
        }

        var outputFolderPath = Path.GetDirectoryName(outputAssetPath)?.Replace("\\", "/");
        if (string.IsNullOrWhiteSpace(outputFolderPath))
        {
            return false;
        }

        EnsureFolderExists(outputFolderPath);
        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(outputAssetPath) != null)
        {
            AssetDatabase.DeleteAsset(outputAssetPath);
        }

        var extractResult = AssetDatabase.ExtractAsset(sourceClip, outputAssetPath);
        if (!string.IsNullOrWhiteSpace(extractResult))
        {
            Debug.LogWarning(
                $"[Codex] Failed to extract embedded body clip from '{sourceAssetPath}' into '{outputAssetPath}'. " +
                $"Reason: {extractResult}");
            return false;
        }

        AssetDatabase.ImportAsset(outputAssetPath, ImportAssetOptions.ForceUpdate);
        extractedClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(outputAssetPath);
        if (extractedClip == null)
        {
            Debug.LogWarning(
                $"[Codex] Extracted embedded body clip into '{outputAssetPath}', but Unity could not load the asset afterwards.");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(clipName) && !string.Equals(extractedClip.name, clipName, StringComparison.Ordinal))
        {
            extractedClip.name = clipName;
            EditorUtility.SetDirty(extractedClip);
            AssetDatabase.SaveAssets();
        }

        Debug.Log(
            $"[Codex] Extracted embedded body clip '{outputAssetPath}' from '{sourceAssetPath}'. length={extractedClip.length:0.###}.");
        return true;
    }

    private static AnimationCurve CloneOrCompactCurve(AnimationCurve sourceCurve, float clipLength, out bool compacted)
    {
        compacted = false;
        if (sourceCurve == null || sourceCurve.length == 0)
        {
            return null;
        }

        if (!IsConstantCurve(sourceCurve))
        {
            return new AnimationCurve(sourceCurve.keys)
            {
                preWrapMode = sourceCurve.preWrapMode,
                postWrapMode = sourceCurve.postWrapMode,
            };
        }

        compacted = true;
        var constantValue = sourceCurve.keys[0].value;
        var compactKeys = clipLength > 0.0001f
            ? new[]
            {
                new Keyframe(0f, constantValue),
                new Keyframe(clipLength, constantValue),
            }
            : new[]
            {
                new Keyframe(0f, constantValue),
            };
        return new AnimationCurve(compactKeys)
        {
            preWrapMode = sourceCurve.preWrapMode,
            postWrapMode = sourceCurve.postWrapMode,
        };
    }

    private static bool IsConstantCurve(AnimationCurve sourceCurve)
    {
        if (sourceCurve == null || sourceCurve.length <= 1)
        {
            return true;
        }

        var referenceValue = sourceCurve.keys[0].value;
        for (var index = 1; index < sourceCurve.length; index++)
        {
            if (Mathf.Abs(sourceCurve.keys[index].value - referenceValue) > 0.0001f)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsBlendShapeBinding(EditorCurveBinding binding)
    {
        return binding.type == typeof(SkinnedMeshRenderer)
            && binding.propertyName.IndexOf("blendShape.", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static AnimationClip TryPrepareFacialAnimationClip(
        GameObject displayAsset,
        string vmdAssetPath,
        string outputAssetPath)
    {
        if (displayAsset == null || string.IsNullOrWhiteSpace(vmdAssetPath))
        {
            return null;
        }

        var absoluteVmdPath = ResolveAbsoluteAssetPath(vmdAssetPath);
        if (!File.Exists(absoluteVmdPath))
        {
            Debug.LogWarning($"[Codex] Facial VMD source file was not found: '{absoluteVmdPath}'.");
            return null;
        }

        var morphFrames = ReadMorphFramesFromVmd(absoluteVmdPath, out var vmdModelName);
        if (morphFrames.Count == 0)
        {
            Debug.LogWarning($"[Codex] VMD '{vmdAssetPath}' does not contain any readable morph frames.");
            return null;
        }

        var renderer = ResolvePreferredMorphRenderer(displayAsset, morphFrames.Keys);
        if (renderer == null || renderer.sharedMesh == null || renderer.sharedMesh.blendShapeCount <= 0)
        {
            Debug.LogWarning(
                $"[Codex] Failed to resolve a facial SkinnedMeshRenderer for '{AssetDatabase.GetAssetPath(displayAsset)}'.");
            return null;
        }

        var animationRoot = ResolveMorphAnimationRoot(displayAsset, renderer);
        var rendererPathWithinModel = AnimationUtility.CalculateTransformPath(renderer.transform, animationRoot);
        var rendererPathWithinBundle = string.IsNullOrWhiteSpace(rendererPathWithinModel)
            ? BundleRelativeModelRootName
            : $"{BundleRelativeModelRootName}/{rendererPathWithinModel}";
        var blendShapeNames = Enumerable.Range(0, renderer.sharedMesh.blendShapeCount)
            .Select(renderer.sharedMesh.GetBlendShapeName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var clip = new AnimationClip
        {
            name = Path.GetFileNameWithoutExtension(outputAssetPath),
            legacy = false,
            frameRate = 30f,
            wrapMode = WrapMode.Loop,
        };
        var metadataAliasMap = BuildMorphMetadataAliasMap(renderer, blendShapeNames);

        var matchedBindingCount = 0;
        var unmatchedMorphNames = new List<string>();
        foreach (var item in morphFrames.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var resolvedBlendShapeName = ResolveBlendShapeName(blendShapeNames, item.Key, metadataAliasMap);
            if (resolvedBlendShapeName == null)
            {
                if (unmatchedMorphNames.Count < 20)
                {
                    unmatchedMorphNames.Add(item.Key);
                }

                continue;
            }

            var keyframes = BuildSortedDistinctKeyframes(item.Value);
            if (keyframes.Length == 0)
            {
                continue;
            }

            clip.SetCurve(
                rendererPathWithinBundle,
                typeof(SkinnedMeshRenderer),
                $"blendShape.{resolvedBlendShapeName}",
                new AnimationCurve(keyframes));
            matchedBindingCount++;
        }

        if (matchedBindingCount <= 0)
        {
            Debug.LogWarning(
                $"[Codex] Failed to generate a facial clip from '{vmdAssetPath}' because no morph names matched renderer '{renderer.name}'. " +
                $"vmdModel='{vmdModelName}', blendShapes={blendShapeNames.Length}, unmatchedSamples=[{string.Join(", ", unmatchedMorphNames)}].");
            return null;
        }

        if (AnimationUtility.GetCurveBindings(clip).Length <= 0)
        {
            Debug.LogWarning(
                $"[Codex] Failed to generate a facial clip from '{vmdAssetPath}' because no curve bindings were written. " +
                $"renderer='{renderer.name}', unmatchedSamples=[{string.Join(", ", unmatchedMorphNames)}].");
            return null;
        }

        var outputFolderPath = Path.GetDirectoryName(outputAssetPath)?.Replace("\\", "/");
        if (string.IsNullOrWhiteSpace(outputFolderPath))
        {
            return null;
        }

        EnsureFolderExists(outputFolderPath);
        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(outputAssetPath) != null)
        {
            AssetDatabase.DeleteAsset(outputAssetPath);
        }

        AssetDatabase.CreateAsset(clip, outputAssetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(outputAssetPath, ImportAssetOptions.ForceUpdate);

        var exportedClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(outputAssetPath);
        if (exportedClip == null)
        {
            Debug.LogWarning($"[Codex] Generated facial clip asset '{outputAssetPath}' could not be loaded.");
            return null;
        }

        Debug.Log(
            $"[Codex] Generated VMD facial clip '{outputAssetPath}' from '{vmdAssetPath}'. " +
            $"vmdModel='{vmdModelName}', renderer='{renderer.name}', rendererPath='{rendererPathWithinBundle}', " +
            $"matchedBindings={matchedBindingCount}, unmatchedSamples=[{string.Join(", ", unmatchedMorphNames)}].");
        return exportedClip;
    }

    private static SkinnedMeshRenderer ResolvePreferredMorphRenderer(
        GameObject displayAsset,
        IEnumerable<string> morphNames)
    {
        return displayAsset.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true)
            .Where(renderer => renderer != null && renderer.sharedMesh != null && renderer.sharedMesh.blendShapeCount > 0)
            .Select(renderer => new
            {
                Renderer = renderer,
                MatchCount = EstimateMorphMatchCount(renderer, morphNames),
                Score = ResolveMorphRendererScore(renderer),
            })
            .OrderByDescending(item => item.MatchCount)
            .ThenByDescending(item => item.Score)
            .ThenByDescending(item => item.Renderer.sharedMesh.blendShapeCount)
            .Select(item => item.Renderer)
            .FirstOrDefault();
    }

    private static int EstimateMorphMatchCount(
        SkinnedMeshRenderer renderer,
        IEnumerable<string> morphNames)
    {
        if (renderer == null || renderer.sharedMesh == null || morphNames == null)
        {
            return 0;
        }

        var distinctMorphNames = morphNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (distinctMorphNames.Length == 0)
        {
            return 0;
        }

        var blendShapeNames = Enumerable.Range(0, renderer.sharedMesh.blendShapeCount)
            .Select(renderer.sharedMesh.GetBlendShapeName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var metadataAliasMap = BuildMorphMetadataAliasMap(renderer, blendShapeNames);
        var matchCount = 0;
        foreach (var morphName in distinctMorphNames)
        {
            if (ResolveBlendShapeName(blendShapeNames, morphName, metadataAliasMap) != null)
            {
                matchCount++;
            }
        }

        return matchCount;
    }

    private static int ResolveMorphRendererScore(SkinnedMeshRenderer renderer)
    {
        var mesh = renderer.sharedMesh;
        var score = mesh != null ? mesh.blendShapeCount * 10 : 0;
        var rendererName = renderer.name ?? string.Empty;
        var meshName = mesh != null ? mesh.name ?? string.Empty : string.Empty;

        if (rendererName.IndexOf("U_Char_1", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            score += 2000;
        }
        else if (rendererName.IndexOf("U_Char_2", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            score += 1800;
        }

        if (meshName.IndexOf("U_CharMesh_1", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            score += 1600;
        }

        if (rendererName.IndexOf("face", StringComparison.OrdinalIgnoreCase) >= 0
            || meshName.IndexOf("face", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            score += 1200;
        }

        if (rendererName.IndexOf("char", StringComparison.OrdinalIgnoreCase) >= 0
            || meshName.IndexOf("char", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            score += 400;
        }

        return score;
    }

    private static Transform ResolveMorphAnimationRoot(GameObject displayAsset, SkinnedMeshRenderer renderer)
    {
        if (displayAsset == null)
        {
            return renderer.transform;
        }

        var animator = displayAsset.GetComponentInChildren<Animator>(includeInactive: true);
        if (animator != null)
        {
            return animator.transform;
        }

        var animation = displayAsset.GetComponentInChildren<Animation>(includeInactive: true);
        if (animation != null)
        {
            return animation.transform;
        }

        return displayAsset.transform;
    }

    private static IReadOnlyDictionary<string, string> BuildMorphMetadataAliasMap(
        SkinnedMeshRenderer renderer,
        IEnumerable<string> blendShapeNames)
    {
        var blendShapeEntries = MaterializeBlendShapeNames(blendShapeNames);
        if (blendShapeEntries.Length == 0)
        {
            return null;
        }

        var meshAssetPath = AssetDatabase.GetAssetPath(renderer.sharedMesh);
        if (string.IsNullOrWhiteSpace(meshAssetPath))
        {
            return null;
        }

        var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        var directoryPath = Path.GetDirectoryName(meshAssetPath);
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return null;
        }

        var absoluteDirectoryPath = Path.GetFullPath(Path.Combine(projectRoot, directoryPath));
        if (!Directory.Exists(absoluteDirectoryPath))
        {
            return null;
        }

        var fileStem = Path.GetFileNameWithoutExtension(meshAssetPath);
        var candidateFiles = Directory.GetFiles(absoluteDirectoryPath, "*.xml", SearchOption.TopDirectoryOnly)
            .OrderBy(path => ResolveMorphMetadataPriority(path, fileStem))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (candidateFiles.Length == 0)
        {
            return null;
        }

        var aliasMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var candidateFile in candidateFiles)
        {
            TryAppendMorphMetadataAliases(candidateFile, blendShapeEntries, aliasMap);
        }

        return aliasMap.Count > 0 ? aliasMap : null;
    }

    private static int ResolveMorphMetadataPriority(string absoluteXmlPath, string fileStem)
    {
        var fileName = Path.GetFileNameWithoutExtension(absoluteXmlPath);
        var score = 0;
        if (string.Equals(fileName, fileStem, StringComparison.OrdinalIgnoreCase))
        {
            score -= 3000;
        }

        if (fileName.IndexOf("mmd4mecanim", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            score -= 2000;
        }

        return score;
    }

    private static void TryAppendMorphMetadataAliases(
        string absoluteXmlPath,
        IReadOnlyList<BlendShapeNameEntry> blendShapeEntries,
        IDictionary<string, string> aliasMap)
    {
        try
        {
            var document = XDocument.Load(absoluteXmlPath);
            foreach (var element in document.Descendants())
            {
                var blendShapeName = element.Element("blendShapeName")?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(blendShapeName))
                {
                    continue;
                }

                var resolvedBlendShape = ResolveBlendShapeNameFromEntries(blendShapeEntries, blendShapeName);
                if (string.IsNullOrWhiteSpace(resolvedBlendShape))
                {
                    continue;
                }

                foreach (var alias in EnumerateMetadataAliases(element))
                {
                    foreach (var candidate in ExpandMorphNameCandidates(alias))
                    {
                        var normalizedCandidate = NormalizeMorphName(candidate);
                        if (string.IsNullOrWhiteSpace(normalizedCandidate) || aliasMap.ContainsKey(normalizedCandidate))
                        {
                            continue;
                        }

                        aliasMap.Add(normalizedCandidate, resolvedBlendShape);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Codex] Failed to read morph metadata from '{absoluteXmlPath}'. Reason: {exception.Message}");
        }
    }

    private static IEnumerable<string> EnumerateMetadataAliases(XElement element)
    {
        foreach (var name in new[] { "nameJp", "nameEn", "translatedName", "name" })
        {
            var value = element.Element(name)?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value;
            }
        }
    }

    private static Dictionary<string, List<Keyframe>> ReadMorphFramesFromVmd(string absoluteVmdPath, out string modelName)
    {
        modelName = string.Empty;
        var morphKeyframes = new Dictionary<string, List<Keyframe>>(StringComparer.Ordinal);
        var shiftJisEncoding = ResolveShiftJisEncoding();
        using var stream = File.Open(absoluteVmdPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream, shiftJisEncoding, leaveOpen: false);

        var header = ReadFixedLengthText(reader, 30, shiftJisEncoding);
        modelName = ReadFixedLengthText(
            reader,
            string.Equals(header, "Vocaloid Motion Data 0002", StringComparison.Ordinal) ? 20 : 10,
            shiftJisEncoding);

        var boneFrameCount = reader.ReadUInt32();
        SkipBytes(reader, checked((long)boneFrameCount * 111L));

        var morphFrameCount = reader.ReadUInt32();
        for (var index = 0; index < morphFrameCount; index++)
        {
            var morphName = ReadFixedLengthText(reader, 15, shiftJisEncoding);
            var frameTime = reader.ReadUInt32();
            var weight = reader.ReadSingle();
            if (string.IsNullOrWhiteSpace(morphName))
            {
                continue;
            }

            if (!morphKeyframes.TryGetValue(morphName, out var keyframes))
            {
                keyframes = new List<Keyframe>();
                morphKeyframes.Add(morphName, keyframes);
            }

            keyframes.Add(new Keyframe(frameTime / 30f, weight * 100f));
        }

        return morphKeyframes;
    }

    private static Keyframe[] BuildSortedDistinctKeyframes(List<Keyframe> keyframes)
    {
        if (keyframes == null || keyframes.Count == 0)
        {
            return Array.Empty<Keyframe>();
        }

        keyframes.Sort((left, right) => left.time.CompareTo(right.time));

        var writeIndex = 0;
        for (var readIndex = 1; readIndex < keyframes.Count; readIndex++)
        {
            if (Math.Abs(keyframes[readIndex].time - keyframes[writeIndex].time) <= 0.0001f)
            {
                keyframes[writeIndex] = keyframes[readIndex];
                continue;
            }

            writeIndex++;
            keyframes[writeIndex] = keyframes[readIndex];
        }

        var distinctKeyframes = new Keyframe[writeIndex + 1];
        keyframes.CopyTo(0, distinctKeyframes, 0, distinctKeyframes.Length);
        return distinctKeyframes;
    }

    private static string ResolveBlendShapeName(
        IEnumerable<string> blendShapeNames,
        string morphName,
        IReadOnlyDictionary<string, string> metadataAliasMap = null)
    {
        if (string.IsNullOrWhiteSpace(morphName))
        {
            return null;
        }

        var blendShapeEntries = MaterializeBlendShapeNames(blendShapeNames);
        if (blendShapeEntries.Length == 0)
        {
            return null;
        }

        foreach (var candidate in ExpandMorphNameCandidates(morphName))
        {
            var exactMatch = blendShapeEntries.FirstOrDefault(entry => string.Equals(entry.Name, candidate, StringComparison.Ordinal));
            if (exactMatch != null)
            {
                return exactMatch.Name;
            }
        }

        foreach (var candidate in ExpandMorphNameCandidates(morphName))
        {
            var normalizedCandidate = NormalizeMorphName(candidate);
            if (string.IsNullOrWhiteSpace(normalizedCandidate))
            {
                continue;
            }

            if (metadataAliasMap != null
                && metadataAliasMap.TryGetValue(normalizedCandidate, out var metadataMatch)
                && !string.IsNullOrWhiteSpace(metadataMatch))
            {
                return metadataMatch;
            }
        }

        var normalizedCandidates = ExpandMorphNameCandidates(morphName)
            .Select(NormalizeMorphName)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (normalizedCandidates.Length == 0)
        {
            return null;
        }

        return blendShapeEntries
            .Select(entry => new
            {
                entry.Name,
                Score = ResolveBlendShapeMatchScore(entry.Normalized, normalizedCandidates),
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Name.Length)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .Select(item => item.Name)
            .FirstOrDefault();
    }

    private static BlendShapeNameEntry[] MaterializeBlendShapeNames(IEnumerable<string> blendShapeNames)
    {
        return blendShapeNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .Select(name => new BlendShapeNameEntry
            {
                Name = name,
                Normalized = NormalizeMorphName(name),
            })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Normalized))
            .ToArray();
    }

    private static string ResolveBlendShapeNameFromEntries(
        IReadOnlyList<BlendShapeNameEntry> blendShapeEntries,
        string value)
    {
        foreach (var candidate in ExpandMorphNameCandidates(value))
        {
            var exact = blendShapeEntries.FirstOrDefault(entry => string.Equals(entry.Name, candidate, StringComparison.Ordinal));
            if (exact != null)
            {
                return exact.Name;
            }
        }

        var normalizedCandidates = ExpandMorphNameCandidates(value)
            .Select(NormalizeMorphName)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (normalizedCandidates.Length == 0)
        {
            return null;
        }

        return blendShapeEntries
            .Select(entry => new
            {
                entry.Name,
                Score = ResolveBlendShapeMatchScore(entry.Normalized, normalizedCandidates),
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Name.Length)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .Select(item => item.Name)
            .FirstOrDefault();
    }

    private static int ResolveBlendShapeMatchScore(string normalizedBlendShapeName, IReadOnlyList<string> normalizedCandidates)
    {
        if (string.IsNullOrWhiteSpace(normalizedBlendShapeName) || normalizedCandidates == null || normalizedCandidates.Count == 0)
        {
            return 0;
        }

        var bestScore = 0;
        foreach (var candidate in normalizedCandidates)
        {
            if (string.Equals(normalizedBlendShapeName, candidate, StringComparison.Ordinal))
            {
                return 1000;
            }

            if (normalizedBlendShapeName.EndsWith(candidate, StringComparison.Ordinal))
            {
                bestScore = Math.Max(bestScore, 800 - (normalizedBlendShapeName.Length - candidate.Length));
            }
            else if (normalizedBlendShapeName.StartsWith(candidate, StringComparison.Ordinal))
            {
                bestScore = Math.Max(bestScore, 760 - (normalizedBlendShapeName.Length - candidate.Length));
            }
            else if (normalizedBlendShapeName.IndexOf(candidate, StringComparison.Ordinal) >= 0)
            {
                bestScore = Math.Max(bestScore, 600 - (normalizedBlendShapeName.Length - candidate.Length));
            }
        }

        return bestScore;
    }

    private static IEnumerable<string> ExpandMorphNameCandidates(string morphName)
    {
        var candidates = new HashSet<string>(StringComparer.Ordinal);

        void AddCandidate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var trimmed = value.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                candidates.Add(trimmed);
            }

            var normalized = NormalizeMorphName(trimmed);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                candidates.Add(normalized);
            }
        }

        AddCandidate(morphName);

        var normalizedMorphName = NormalizeMorphName(morphName);
        switch (normalizedMorphName)
        {
            case "まばたき":
                AddCandidate("blink");
                break;
            case "blink":
                AddCandidate("まばたき");
                break;
            case "笑":
                AddCandidate("笑い");
                AddCandidate("smile");
                AddCandidate("にっこり");
                break;
            case "笑い":
            case "にっこり":
            case "smile":
                AddCandidate("笑");
                AddCandidate("笑い");
                AddCandidate("smile");
                break;
            case "ウィンク":
                AddCandidate("wink");
                AddCandidate("ウィンク2");
                AddCandidate("ウィンク２");
                break;
            case "wink":
                AddCandidate("ウィンク");
                break;
            case "真面目":
                AddCandidate("serious");
                break;
            case "困る":
                AddCandidate("sad");
                break;
            case "怒り":
                AddCandidate("angry");
                break;
            case "照れ":
                AddCandidate("shy");
                break;
            case "ワ":
                AddCandidate("ワE");
                break;
        }

        if (IsPhonemeMorphName(normalizedMorphName))
        {
            AddCandidate(normalizedMorphName + "E");
        }

        if (normalizedMorphName.EndsWith("e", StringComparison.Ordinal) && normalizedMorphName.Length > 1)
        {
            AddCandidate(normalizedMorphName.Substring(0, normalizedMorphName.Length - 1));
        }

        return candidates;
    }

    private static bool IsPhonemeMorphName(string normalizedMorphName)
    {
        return string.Equals(normalizedMorphName, "あ", StringComparison.Ordinal)
            || string.Equals(normalizedMorphName, "い", StringComparison.Ordinal)
            || string.Equals(normalizedMorphName, "う", StringComparison.Ordinal)
            || string.Equals(normalizedMorphName, "え", StringComparison.Ordinal)
            || string.Equals(normalizedMorphName, "お", StringComparison.Ordinal)
            || string.Equals(normalizedMorphName, "わ", StringComparison.Ordinal)
            || string.Equals(normalizedMorphName, "ワ", StringComparison.Ordinal)
            || normalizedMorphName.StartsWith("あ", StringComparison.Ordinal)
            || normalizedMorphName.StartsWith("い", StringComparison.Ordinal)
            || normalizedMorphName.StartsWith("う", StringComparison.Ordinal)
            || normalizedMorphName.StartsWith("え", StringComparison.Ordinal)
            || normalizedMorphName.StartsWith("お", StringComparison.Ordinal)
            || normalizedMorphName.StartsWith("ワ", StringComparison.Ordinal);
    }

    private static string NormalizeMorphName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        value = value.Normalize(NormalizationForm.FormKC);
        var buffer = new char[value.Length];
        var count = 0;
        for (var index = 0; index < value.Length; index++)
        {
            var character = char.ToLowerInvariant(value[index]);
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(character);
            if (char.IsWhiteSpace(character)
                || unicodeCategory == UnicodeCategory.Control
                || unicodeCategory == UnicodeCategory.PrivateUse
                || unicodeCategory == UnicodeCategory.OtherPunctuation
                || unicodeCategory == UnicodeCategory.MathSymbol
                || unicodeCategory == UnicodeCategory.ModifierSymbol
                || unicodeCategory == UnicodeCategory.OtherSymbol
                || character == '_'
                || character == '-'
                || character == '.'
                || character == '/'
                || character == '\\'
                || character == '('
                || character == ')'
                || character == '['
                || character == ']'
                || character == '{'
                || character == '}'
                || character == '・')
            {
                continue;
            }

            buffer[count++] = character;
        }

        if (count == 0)
        {
            return string.Empty;
        }

        var normalized = new string(buffer, 0, count);
        var firstNonDigitIndex = 0;
        while (firstNonDigitIndex < normalized.Length && char.IsDigit(normalized[firstNonDigitIndex]))
        {
            firstNonDigitIndex++;
        }

        return firstNonDigitIndex > 0 && firstNonDigitIndex < normalized.Length
            ? normalized.Substring(firstNonDigitIndex)
            : normalized;
    }

    private static Encoding ResolveShiftJisEncoding()
    {
        TryRegisterCodePagesProvider();

        foreach (var candidate in new object[] { 932, "shift_jis", "shift-jis", "cp932" })
        {
            try
            {
                return candidate is int codePage
                    ? Encoding.GetEncoding(codePage)
                    : Encoding.GetEncoding((string)candidate);
            }
            catch
            {
            }
        }

        Debug.LogWarning("[Codex] Failed to resolve Shift-JIS encoding. Falling back to UTF-8 for VMD morph decoding.");
        return Encoding.UTF8;
    }

    private static void TryRegisterCodePagesProvider()
    {
        try
        {
            var encodingProviderType = Type.GetType("System.Text.CodePagesEncodingProvider, System.Text.Encoding.CodePages", throwOnError: false);
            if (encodingProviderType == null)
            {
                return;
            }

            var instanceProperty = encodingProviderType.GetProperty("Instance");
            var providerInstance = instanceProperty?.GetValue(null, null);
            if (providerInstance is EncodingProvider encodingProvider)
            {
                Encoding.RegisterProvider(encodingProvider);
            }
        }
        catch
        {
        }
    }

    private static string ReadFixedLengthText(BinaryReader reader, int count, Encoding encoding)
    {
        var bytes = reader.ReadBytes(count);
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        var length = Array.IndexOf(bytes, (byte)0);
        if (length < 0)
        {
            length = bytes.Length;
        }

        while (length > 0 && (bytes[length - 1] == 0xFD || bytes[length - 1] == 0xFF || bytes[length - 1] == 0x20))
        {
            length--;
        }

        if (length <= 0)
        {
            return string.Empty;
        }

        return encoding.GetString(bytes, 0, length).Trim();
    }

    private static void SkipBytes(BinaryReader reader, long byteCount)
    {
        if (byteCount <= 0)
        {
            return;
        }

        if (reader.BaseStream.CanSeek)
        {
            reader.BaseStream.Seek(byteCount, SeekOrigin.Current);
            return;
        }

        const int bufferSize = 4096;
        var buffer = new byte[bufferSize];
        var remainingBytes = byteCount;
        while (remainingBytes > 0)
        {
            var bytesToRead = remainingBytes > buffer.Length ? buffer.Length : (int)remainingBytes;
            var bytesRead = reader.BaseStream.Read(buffer, 0, bytesToRead);
            if (bytesRead <= 0)
            {
                throw new EndOfStreamException("Unexpected end of file while skipping VMD sections.");
            }

            remainingBytes -= bytesRead;
        }
    }

    private static string ResolveAbsoluteAssetPath(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return string.Empty;
        }

        var normalizedAssetPath = assetPath.Replace("\\", "/");
        if (!normalizedAssetPath.StartsWith("Assets/", StringComparison.Ordinal))
        {
            return Path.GetFullPath(normalizedAssetPath);
        }

        var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        var relativePath = normalizedAssetPath.Substring("Assets/".Length);
        return Path.Combine(projectRoot, "Assets", relativePath);
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

    private static AnimationClip ResolveBodyClip(GameObject displayAsset, RuntimeAnimatorController bodyController)
    {
        var controllerClip = bodyController != null
            ? bodyController.animationClips
                .Where(IsCandidateBodyAnimationClip)
                .Distinct()
                .OrderByDescending(clip => clip.length)
                .FirstOrDefault()
            : null;
        if (controllerClip != null)
        {
            return controllerClip;
        }

        return displayAsset != null
            ? BuildMikuLobbyAssetBundle.TryResolveSuggestedBodyAnimationClip(displayAsset)
            : null;
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
        if (bindings == null || bindings.Length == 0)
        {
            return false;
        }

        return Array.Exists(bindings, binding =>
            binding.type != typeof(SkinnedMeshRenderer)
            || binding.propertyName.IndexOf("blendShape.", StringComparison.OrdinalIgnoreCase) < 0);
    }

    private static bool AssignIfDifferent<T>(ref T currentValue, T nextValue)
        where T : UnityEngine.Object
    {
        if (currentValue == nextValue)
        {
            return false;
        }

        currentValue = nextValue;
        return true;
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

    private static T LoadAssetByGuid<T>(string guid)
        where T : UnityEngine.Object
    {
        if (string.IsNullOrWhiteSpace(guid))
        {
            return null;
        }

        var assetPath = AssetDatabase.GUIDToAssetPath(guid);
        return string.IsNullOrWhiteSpace(assetPath)
            ? null
            : AssetDatabase.LoadAssetAtPath<T>(assetPath);
    }
}

public static class MikuBundleBuildProfileBatch
{
    public static void PrepareSpaBuildProfile()
    {
        if (!MikuBundleBuildProfileAutoSync.ForceSyncProfile())
        {
            throw new InvalidOperationException("Failed to prepare the spa bundle build profile.");
        }
    }
}
#endif
