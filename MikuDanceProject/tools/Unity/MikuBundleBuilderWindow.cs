using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public sealed class MikuBundleBuilderWindow : EditorWindow
{
    private const string DefaultDisplayAssetPath = "Assets/Kinsama式初音ミクV4C/Kinsama式初音ミクV4C.prefab";
    private const string DefaultBundleName = "miku_lobby_display.bundle";

    private MikuBundleBuildProfile _profile;
    private SerializedObject _serializedProfile;
    private Vector2 _scrollPosition;
    private bool _showMaterialOverrides;
    private GUIStyle _titleStyle;
    private GUIStyle _subtitleStyle;
    private GUIStyle _summaryValueStyle;
    private GUIStyle _summaryLabelStyle;
    private GUIStyle _primaryButtonStyle;
    private bool _buildScheduled;

    [MenuItem("Tools/Miku Showcase/Toolkit")]
    public static void Open()
    {
        var window = GetWindow<MikuBundleBuilderWindow>("Miku Toolkit");
        window.minSize = new Vector2(620f, 720f);
    }

    [MenuItem("Tools/Miku Showcase/Bundle Builder")]
    public static void OpenLegacyMenu()
    {
        Open();
    }

    private void OnEnable()
    {
        EnsureStyles();
        TryRestoreSelectedProfile();
    }

    private void OnGUI()
    {
        EnsureStyles();
        EditorGUILayout.Space();
        DrawWindowBanner();

        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

        DrawProfileSection();
        if (_profile == null || _serializedProfile == null)
        {
            EditorGUILayout.EndScrollView();
            return;
        }

        _serializedProfile.Update();

        DrawQuickSetupSection();
        DrawCoreAssetsSection();
        DrawBuildSettingsSection();

        _serializedProfile.ApplyModifiedProperties();

        var analysis = AnalyzeProfile();

        DrawStatusSection(analysis);
        DrawValidationSection(analysis);
        DrawUtilitiesSection();
        DrawActionSection(analysis);

        EditorGUILayout.EndScrollView();
    }

    private void DrawProfileSection()
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("1. 配置文件", EditorStyles.boldLabel);

            var selectedProfile = EditorGUILayout.ObjectField("当前配置", _profile, typeof(MikuBundleBuildProfile), false) as MikuBundleBuildProfile;
            if (!ReferenceEquals(selectedProfile, _profile))
            {
                BindProfile(selectedProfile);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("新建配置", GUILayout.Height(24f)))
                {
                    CreateProfileAsset();
                }

                if (_profile != null && GUILayout.Button("定位配置", GUILayout.Height(24f)))
                {
                    EditorGUIUtility.PingObject(_profile);
                }
            }

            if (_profile == null || _serializedProfile == null)
            {
                EditorGUILayout.HelpBox("先创建或选择一个 Build Profile，下面的工具才会启用。", MessageType.Warning);
            }
        }
    }

    private void DrawQuickSetupSection()
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("2. 快速设置", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "推荐顺序：先选中模型 -> 指定为展示对象 -> 自动补全缺失项。这样会优先尝试修材质、补控制器并关联动作资源。",
                MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("选中模型作为展示对象", GUILayout.Height(24f)))
                {
                    AssignSelectedDisplayAsset();
                }

                if (GUILayout.Button("自动补全缺失项", GUILayout.Height(24f)))
                {
                    AutoFillProfile(overwriteExisting: false);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("用建议值覆盖", GUILayout.Height(24f)))
                {
                    AutoFillProfile(overwriteExisting: true);
                }

                if (GUILayout.Button("加载示例默认值", GUILayout.Height(24f)))
                {
                    LoadDemoDefaults();
                }
            }
        }
    }

    private void DrawCoreAssetsSection()
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("3. 核心资源", EditorStyles.boldLabel);

            DrawObjectProperty("DisplayAsset", "显示模型");
            DrawObjectProperty("BodyController", "动作控制器");
            DrawObjectProperty("BodyAnimationClip", "动作片段（可选）");
            DrawObjectProperty("FacialAnimationClip", "面部表情动作");
            DrawObjectProperty("BackgroundMusic", "背景音乐");
        }
    }

    private void DrawBuildSettingsSection()
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("4. 构建设置", EditorStyles.boldLabel);

            DrawStringProperty("BundleName", "Bundle 文件名");
            EditorGUILayout.HelpBox(
                "这里直接填写最终 AB 文件名即可，例如 my_miku_show.bundle。不会再强制你从文件名末尾删改后再额外补一个 .bundle。",
                MessageType.None);
            DrawOutputDirectoryProperty("OutputDirectory", "输出目录");
            DrawProperty("TextureSampling", "材质取样预设");
            DrawProperty("MaterialHandling", "材质兼容策略");
            DrawProperty("PreferPreparedAssets", "优先使用预处理缓存");

            EditorGUILayout.HelpBox(
                "AutoCompatible 会在打包前把非通用 shader 材质转换为更稳定的 URP 兼容材质，并优先保护脸部、眼睛和透明贴图，减少进游戏后发白、贴图变形或脸红变方块的问题。",
                MessageType.None);

            _showMaterialOverrides = EditorGUILayout.Foldout(_showMaterialOverrides, "材质覆盖（高级）", true);
            if (_showMaterialOverrides)
            {
                EditorGUILayout.HelpBox(
                    "只有在你需要替换指定 Renderer 的材质时才需要这里。RendererPath 使用相对于模型根节点的路径，例如 U_Char/U_Char_2。",
                    MessageType.None);

                var materialOverridesProperty = _serializedProfile.FindProperty("MaterialOverrides");
                EditorGUILayout.PropertyField(materialOverridesProperty, includeChildren: true);
            }
        }
    }

    private void DrawValidationSection(ProfileAnalysis analysis)
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("6. 检查结果", EditorStyles.boldLabel);

            if (analysis.Errors.Count == 0 && analysis.Warnings.Count == 0 && analysis.Infos.Count == 0)
            {
                EditorGUILayout.HelpBox("当前配置看起来可以直接构建。", MessageType.Info);
                return;
            }

            foreach (var error in analysis.Errors)
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }

            foreach (var warning in analysis.Warnings)
            {
                EditorGUILayout.HelpBox(warning, MessageType.Warning);
            }

            foreach (var info in analysis.Infos)
            {
                EditorGUILayout.HelpBox(info, MessageType.Info);
            }
        }
    }

    private void DrawUtilitiesSection()
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("7. 工具面板", EditorStyles.boldLabel);
            var hasUsablePreparedAssets = _profile != null && BuildMikuLobbyAssetBundle.HasUsablePreparedAssets(_profile);

            if (hasUsablePreparedAssets)
            {
                EditorGUILayout.HelpBox(
                    $"已存在预处理缓存，可直接用于完整打包。\nPrefab: {_profile.PreparedPrefab.name}\nMorph: {_profile.PreparedFacialAnimationClip.name}\nMetadata: {_profile.PreparedPlaybackMetadata.name}",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox("当前还没有完整打包预处理缓存。先执行一次“预处理完整打包”后，完整打包会快很多。", MessageType.None);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("预处理完整打包", GUILayout.Height(24f)))
                {
                    PrepareFullBuildAssets();
                }

                if (GUILayout.Button("清理预处理缓存", GUILayout.Height(24f)))
                {
                    ClearPreparedBuildCache();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("导出面部 Clip", GUILayout.Height(24f)))
                {
                    ExportFacialClip();
                }

                if (GUILayout.Button("检查展示模型", GUILayout.Height(24f)))
                {
                    InspectCurrentDisplay();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("修复导入模型", GUILayout.Height(24f)))
                {
                    RepairCurrentDisplay();
                }

                if (GUILayout.Button("重建动作控制器", GUILayout.Height(24f)))
                {
                    RebuildCurrentController();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("定位面部 Clip", GUILayout.Height(24f)))
                {
                    PingFacialClip();
                }

                if (GUILayout.Button("打开输出目录", GUILayout.Height(24f)))
                {
                    OpenOutputFolder();
                }
            }
        }
    }

    private void DrawActionSection(ProfileAnalysis analysis)
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("8. 构建", EditorStyles.boldLabel);

            if (analysis.Errors.Count > 0)
            {
                EditorGUILayout.HelpBox("上面还有阻塞问题，先修正后再构建。", MessageType.Warning);
            }

            if (analysis.Errors.Count == 0)
            {
                EditorGUILayout.HelpBox("默认会启动后台 batch 构建并关闭当前 Unity，避免编辑器在打包时长时间卡住。", MessageType.Info);
            }

            using (new EditorGUI.DisabledScope(analysis.Errors.Count > 0))
            {
                GUILayout.Space(2f);
                if (GUILayout.Button("构建 AB 包", _primaryButtonStyle))
                {
                    BuildSelectedProfile();
                }
            }
        }
    }

    private void DrawObjectProperty(string propertyName, string label)
    {
        var property = _serializedProfile.FindProperty(propertyName);
        EditorGUILayout.PropertyField(property, new GUIContent(label));
    }

    private void DrawStringProperty(string propertyName, string label)
    {
        var property = _serializedProfile.FindProperty(propertyName);
        EditorGUILayout.PropertyField(property, new GUIContent(label));
    }

    private void DrawProperty(string propertyName, string label)
    {
        var property = _serializedProfile.FindProperty(propertyName);
        EditorGUILayout.PropertyField(property, new GUIContent(label));
    }

    private void DrawOutputDirectoryProperty(string propertyName, string label)
    {
        var property = _serializedProfile.FindProperty(propertyName);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PropertyField(property, new GUIContent(label));
            if (GUILayout.Button("浏览", GUILayout.Width(80f)))
            {
                var startDirectory = string.IsNullOrWhiteSpace(property.stringValue)
                    ? _profile.ResolveOutputDirectory()
                    : property.stringValue;
                var selectedDirectory = EditorUtility.OpenFolderPanel("Select Bundle Output Directory", startDirectory, string.Empty);
                if (!string.IsNullOrWhiteSpace(selectedDirectory))
                {
                    property.stringValue = Path.GetFullPath(selectedDirectory);
                }
            }
        }
    }

    private void BindProfile(MikuBundleBuildProfile profile)
    {
        _profile = profile;
        _serializedProfile = _profile != null ? new SerializedObject(_profile) : null;
        MikuBundleBuildProfileAutoSync.SaveSelectedProfileAssetPath(_profile);
    }

    private void TryRestoreSelectedProfile()
    {
        if (_profile != null)
        {
            return;
        }

        var selectedProfileAssetPath = MikuBundleBuildProfileAutoSync.TryResolveSelectedProfileAssetPath();
        if (string.IsNullOrWhiteSpace(selectedProfileAssetPath))
        {
            return;
        }

        var profile = AssetDatabase.LoadAssetAtPath<MikuBundleBuildProfile>(selectedProfileAssetPath);
        if (profile != null)
        {
            BindProfile(profile);
        }
    }

    private void EnsureStyles()
    {
        if (_titleStyle != null)
        {
            return;
        }

        _titleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 15,
        };
        _subtitleStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
        {
            fontSize = 11,
        };
        _summaryValueStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 16,
        };
        _summaryLabelStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
        };
        _primaryButtonStyle = new GUIStyle(EditorStyles.miniButton)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            fixedHeight = 36f,
        };
    }

    private void DrawWindowBanner()
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("Miku Showcase Toolkit", _titleStyle);
            EditorGUILayout.LabelField(
                "把模型导入、材质修复、动作控制器补齐、面部 Clip 导出和 AB 构建集中到一个窗口里，减少来回切换工具的操作成本。",
                _subtitleStyle);

            if (_profile != null)
            {
                EditorGUILayout.Space(3f);
                EditorGUILayout.LabelField($"当前配置：{_profile.name}", EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField(
                    $"展示模型：{(_profile.DisplayAsset != null ? _profile.DisplayAsset.name : "未指定")}",
                    EditorStyles.miniLabel);
            }
        }
    }

    private void DrawStatusSection(ProfileAnalysis analysis)
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("5. 状态概览", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawStatusCard("错误", analysis.Errors.Count.ToString(), "需要先修复");
                DrawStatusCard("警告", analysis.Warnings.Count.ToString(), "建议确认");
                DrawStatusCard("提示", analysis.Infos.Count.ToString(), "额外信息");
            }
        }
    }

    private void DrawStatusCard(string title, string value, string description)
    {
        using (new EditorGUILayout.VerticalScope("box", GUILayout.MinHeight(70f)))
        {
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(value, _summaryValueStyle);
            EditorGUILayout.LabelField(title, _summaryLabelStyle);
            EditorGUILayout.LabelField(description, _summaryLabelStyle);
            GUILayout.FlexibleSpace();
        }
    }

    private void CreateProfileAsset()
    {
        var assetPath = EditorUtility.SaveFilePanelInProject(
            "Create Miku Bundle Build Profile",
            "MikuBundleBuildProfile",
            "asset",
            "选择一个保存位置来创建打包配置。");
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return;
        }

        var profile = CreateInstance<MikuBundleBuildProfile>();
        AssetDatabase.CreateAsset(profile, assetPath);
        AssetDatabase.SaveAssets();
        BindProfile(profile);
        EditorGUIUtility.PingObject(profile);
        LoadDemoDefaults();
    }

    private void AssignSelectedDisplayAsset()
    {
        if (Selection.activeObject is not GameObject selectedDisplay)
        {
            EditorUtility.DisplayDialog("No Display Selected", "先在 Project 窗口选中一个模型 prefab，再点这个按钮。", "OK");
            return;
        }

        _profile.DisplayAsset = selectedDisplay;
        _profile.BodyController = null;
        _profile.BodyAnimationClip = null;
        EditorUtility.SetDirty(_profile);
        RebindSerializedObject();
        AutoFillProfile(overwriteExisting: false);
    }

    private void AutoFillProfile(bool overwriteExisting)
    {
        if (_profile == null)
        {
            return;
        }

        if (_profile.DisplayAsset == null)
        {
            var defaultDisplay = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultDisplayAssetPath);
            if (defaultDisplay != null)
            {
                _profile.DisplayAsset = defaultDisplay;
            }
        }

        if (_profile.DisplayAsset == null)
        {
            EditorUtility.DisplayDialog("Missing Display Asset", "先指定一个显示模型，再自动补全其它资源。", "OK");
            return;
        }

        TryPrepareDisplayAsset();

        if (overwriteExisting || _profile.BodyController == null)
        {
            _profile.BodyController = BuildMikuLobbyAssetBundle.TryResolveSuggestedBodyController(_profile.DisplayAsset);
        }

        if (overwriteExisting || _profile.BodyAnimationClip == null)
        {
            _profile.BodyAnimationClip = BuildMikuLobbyAssetBundle.TryResolveSuggestedBodyAnimationClip(_profile.DisplayAsset);
        }

        if (overwriteExisting || _profile.FacialAnimationClip == null)
        {
            _profile.FacialAnimationClip = BuildMikuLobbyAssetBundle.TryResolveSuggestedFacialAnimationClip(
                _profile.DisplayAsset,
                _profile.BodyAnimationClip,
                _profile.BodyController,
                allowAutoGenerate: true);
        }

        if (overwriteExisting || _profile.BackgroundMusic == null)
        {
            _profile.BackgroundMusic = BuildMikuLobbyAssetBundle.TryResolveSuggestedBackgroundMusic();
        }

        if (string.IsNullOrWhiteSpace(_profile.BundleName))
        {
            _profile.BundleName = DefaultBundleName;
        }

        EditorUtility.SetDirty(_profile);
        RebindSerializedObject();
    }

    private void LoadDemoDefaults()
    {
        if (_profile == null)
        {
            return;
        }

        var defaultDisplay = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultDisplayAssetPath);
        if (defaultDisplay != null)
        {
            _profile.DisplayAsset = defaultDisplay;
        }

        _profile.BundleName = DefaultBundleName;
        _profile.TextureSampling = MikuBundleTextureSampling.Original;
        _profile.MaterialHandling = MikuBundleMaterialHandling.AutoCompatible;
        AutoFillProfile(overwriteExisting: true);
    }

    private ProfileAnalysis AnalyzeProfile()
    {
        var result = new ProfileAnalysis();
        if (_profile == null)
        {
            result.Errors.Add("还没有选择 Build Profile。");
            return result;
        }

        if (_profile.DisplayAsset == null)
        {
            result.Errors.Add("显示模型不能为空。");
            return result;
        }

        if (string.IsNullOrWhiteSpace(AssetDatabase.GetAssetPath(_profile.DisplayAsset)))
        {
            result.Errors.Add("显示模型不是有效的 Project 资源。");
            return result;
        }

        var renderers = _profile.DisplayAsset.GetComponentsInChildren<Renderer>(includeInactive: true);
        if (renderers.Length == 0)
        {
            result.Warnings.Add("显示模型里没有 Renderer，导出的 bundle 可能不会显示任何内容。");
        }

        var animator = _profile.DisplayAsset.GetComponentInChildren<Animator>(includeInactive: true);
        if (animator == null)
        {
            result.Warnings.Add("显示模型里没有 Animator。构建时会临时在根节点补一个 Animator。");
        }
        else if (animator.runtimeAnimatorController != null && _profile.BodyController == null && _profile.BodyAnimationClip == null)
        {
            result.Infos.Add($"会复用模型自带的 AnimatorController: {animator.runtimeAnimatorController.name}");
        }

        if (_profile.BodyController == null && _profile.BodyAnimationClip == null)
        {
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                result.Errors.Add("没有可用的动作控制器或动作片段，也无法从模型中复用控制器。");
            }
            else
            {
                result.Infos.Add("未单独指定动作资源，将直接使用模型自带控制器。");
            }
        }

        if (_profile.FacialAnimationClip == null)
        {
            result.Warnings.Add("还没有指定面部表情动作。构建时会尝试直接从当前动作里自动提取 blendShape 曲线，但如果动作本身没有表情曲线，脸部仍然不会自动变化。");
        }
        else
        {
            result.Infos.Add($"当前面部表情资源：{_profile.FacialAnimationClip.name}");
        }

        if (string.IsNullOrWhiteSpace(_profile.BundleName))
        {
            result.Errors.Add("Bundle 文件名不能为空。");
        }

        if (string.IsNullOrWhiteSpace(_profile.OutputDirectory))
        {
            result.Infos.Add($"未填写输出目录时，将使用默认输出目录：{_profile.ResolveOutputDirectory()}");
        }

        if (_profile.PreferPreparedAssets)
        {
            if (BuildMikuLobbyAssetBundle.HasUsablePreparedAssets(_profile))
            {
                result.Infos.Add("完整打包会优先使用当前 profile 的预处理缓存。");
            }
            else
            {
                result.Warnings.Add("已启用预处理缓存，但当前 profile 还没有可用缓存。建议先执行一次“预处理完整打包”。");
            }
        }

        return result;
    }

    private void RepairCurrentDisplay()
    {
        if (!EnsureDisplayAssetSelected(out var displayAsset))
        {
            return;
        }

        try
        {
            var changed = MikuImportedModelAutomation.ProcessImportedModelForObject(displayAsset);
            RefreshResolvedMotionAssets();

            var message = changed
                ? "已执行材质、物理和动作控制器修复，结果已同步到当前配置。"
                : "没有检测到需要变更的内容，或者当前模型目录下还没有可用动作。";
            EditorUtility.DisplayDialog("Repair Complete", message, "OK");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("Repair Failed", exception.ToString(), "OK");
        }
    }

    private void RebuildCurrentController()
    {
        if (!EnsureDisplayAssetSelected(out var displayAsset))
        {
            return;
        }

        try
        {
            var changed = MikuImportedModelAutomation.EnsureModelControllerForObject(displayAsset);
            RefreshResolvedMotionAssets();

            var message = changed
                ? "已根据当前模型目录下的动作资源重建 AnimatorController。"
                : "没有找到可用于重建控制器的动作文件。";
            EditorUtility.DisplayDialog("Controller Complete", message, "OK");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("Controller Failed", exception.ToString(), "OK");
        }
    }

    private void ExportFacialClip()
    {
        try
        {
            ExportMikuLobbyMorphClip.Export();
            var exportedClip = BuildMikuLobbyAssetBundle.TryResolveSuggestedFacialAnimationClip(
                _profile.DisplayAsset,
                _profile.BodyAnimationClip,
                _profile.BodyController,
                allowAutoGenerate: false);
            if (exportedClip != null)
            {
                _profile.FacialAnimationClip = exportedClip;
                EditorUtility.SetDirty(_profile);
                RebindSerializedObject();
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("Export Facial Clip Failed", exception.ToString(), "OK");
        }
    }

    private void InspectCurrentDisplay()
    {
        if (_profile == null || _profile.DisplayAsset == null)
        {
            EditorUtility.DisplayDialog("No Display Asset", "先指定一个显示模型，再执行检查。", "OK");
            return;
        }

        InspectMikuAnimation.InspectGameObject(_profile.DisplayAsset);
        EditorUtility.DisplayDialog("Inspect Complete", "检查结果已经输出到 Unity Console。", "OK");
    }

    private void PingFacialClip()
    {
        if (_profile == null || _profile.FacialAnimationClip == null)
        {
            EditorUtility.DisplayDialog("No Facial Clip", "当前配置还没有绑定面部表情动作。", "OK");
            return;
        }

        EditorGUIUtility.PingObject(_profile.FacialAnimationClip);
        Selection.activeObject = _profile.FacialAnimationClip;
    }

    private void PrepareFullBuildAssets()
    {
        if (_profile == null)
        {
            return;
        }

        try
        {
            var preparedPrefabPath = BuildMikuLobbyAssetBundle.PrepareProfileAssets(_profile);
            RebindSerializedObject();
            EditorUtility.DisplayDialog(
                "Preprocess Complete",
                $"完整打包预处理已完成。\n\n预处理 Prefab:\n{preparedPrefabPath}\n\n后续完整打包会优先复用这些缓存资产。",
                "OK");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("Preprocess Failed", exception.ToString(), "OK");
        }
    }

    private void ClearPreparedBuildCache()
    {
        if (_profile == null)
        {
            return;
        }

        try
        {
            BuildMikuLobbyAssetBundle.ClearPreparedProfileAssets(_profile);
            RebindSerializedObject();
            EditorUtility.DisplayDialog("Cache Cleared", "当前 Build Profile 的预处理缓存已清理。", "OK");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("Clear Cache Failed", exception.ToString(), "OK");
        }
    }

    private void BuildSelectedProfile()
    {
        if (_profile == null)
        {
            return;
        }

        if (_buildScheduled)
        {
            return;
        }

        var action = EditorUtility.DisplayDialogComplex(
            "Build AB Bundle",
            "推荐使用后台 batch 构建。继续后会保存当前资源、启动后台打包任务，并在你确认后关闭当前 Unity。这样可以避免编辑器长时间卡住。\n\n如果你仍想直接在当前编辑器里构建，也可以使用第三个按钮。",
            "后台构建并关闭 Unity",
            "取消",
            "当前编辑器内构建");

        if (action == 1)
        {
            return;
        }

        if (action == 2)
        {
            _buildScheduled = true;
            EditorApplication.delayCall += ExecuteScheduledBuild;
            return;
        }

        StartExternalBatchBuildAndExit();
    }

    private void ExecuteScheduledBuild()
    {
        EditorApplication.delayCall -= ExecuteScheduledBuild;
        _buildScheduled = false;

        if (_profile == null)
        {
            return;
        }

        try
        {
            var bundlePath = BuildMikuLobbyAssetBundle.BuildFromProfile(_profile);
            EditorUtility.DisplayDialog("Build Complete", $"AB 包生成完成：\n{bundlePath}", "OK");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("Build Failed", exception.ToString(), "OK");
        }
    }

    private void StartExternalBatchBuildAndExit()
    {
        try
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            AssetDatabase.SaveAssets();

            var launch = CreateExternalBatchBuildLaunch();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{launch.ScriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = launch.ProjectPath,
            });

            EditorUtility.DisplayDialog(
                "Batch Build Scheduled",
                $"后台打包任务已启动。\n\n输出目录：\n{launch.OutputDirectory}\n\n日志文件：\n{launch.LogPath}\n\n点击确定后将关闭当前 Unity，后台任务会在项目锁释放后自动开始。",
                "OK");

            EditorApplication.delayCall += ExitEditorAfterBatchBuildLaunch;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("Start Build Failed", exception.ToString(), "OK");
        }
    }

    private void ExitEditorAfterBatchBuildLaunch()
    {
        EditorApplication.delayCall -= ExitEditorAfterBatchBuildLaunch;
        EditorApplication.Exit(0);
    }

    private ExternalBatchBuildLaunch CreateExternalBatchBuildLaunch()
    {
        var profileAssetPath = AssetDatabase.GetAssetPath(_profile);
        if (string.IsNullOrWhiteSpace(profileAssetPath))
        {
            throw new InvalidOperationException("当前配置文件还没有保存到项目中，无法启动后台打包。");
        }

        var projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        var unityPath = EditorApplication.applicationPath;
        if (string.IsNullOrWhiteSpace(unityPath) || !File.Exists(unityPath))
        {
            throw new FileNotFoundException("无法定位当前 Unity Editor 可执行文件。", unityPath);
        }

        var outputDirectory = _profile.ResolveOutputDirectory();
        Directory.CreateDirectory(outputDirectory);

        var logDirectory = Path.Combine(projectPath, "Logs");
        Directory.CreateDirectory(logDirectory);

        var helperDirectory = Path.Combine(projectPath, "Temp", "CodexBuild");
        Directory.CreateDirectory(helperDirectory);

        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var logPath = Path.Combine(logDirectory, $"Codex-Editor-BatchBuild-{timestamp}.log");
        var scriptPath = Path.Combine(helperDirectory, $"Start-MikuBundleBuild-{timestamp}.ps1");
        var lockFilePath = Path.Combine(projectPath, "Temp", "UnityLockfile");
        var workspaceRoot = MikuBundleBuildProfileAutoSync.TryResolveWorkspaceRoot() ?? string.Empty;

        var scriptContents = BuildExternalBatchBuildScript(projectPath, unityPath, outputDirectory, profileAssetPath, logPath, lockFilePath, workspaceRoot);
        File.WriteAllText(scriptPath, scriptContents);

        return new ExternalBatchBuildLaunch(projectPath, outputDirectory, logPath, scriptPath);
    }

    private static string BuildExternalBatchBuildScript(
        string projectPath,
        string unityPath,
        string outputDirectory,
        string profileAssetPath,
        string logPath,
        string lockFilePath,
        string workspaceRoot)
    {
        var projectLiteral = ToPowerShellLiteral(projectPath);
        var unityLiteral = ToPowerShellLiteral(unityPath);
        var outputLiteral = ToPowerShellLiteral(outputDirectory);
        var profileLiteral = ToPowerShellLiteral(profileAssetPath);
        var logLiteral = ToPowerShellLiteral(logPath);
        var lockLiteral = ToPowerShellLiteral(lockFilePath);
        var workspaceLiteral = ToPowerShellLiteral(workspaceRoot);

        return
$@"$ErrorActionPreference = 'Stop'
$projectPath = {projectLiteral}
$unityPath = {unityLiteral}
$outputDirectory = {outputLiteral}
$profileAssetPath = {profileLiteral}
$logPath = {logLiteral}
$lockFilePath = {lockLiteral}
$workspaceRoot = {workspaceLiteral}

while (Test-Path -LiteralPath $lockFilePath) {{
    Start-Sleep -Milliseconds 500
}}

$env:CODEX_MIKU_BUNDLE_OUTPUT = $outputDirectory
$env:CODEX_MIKU_BUNDLE_PROFILE = $profileAssetPath
$env:CODEX_MIKU_RUN_MODEL_AUTOMATION = '0'
if (-not [string]::IsNullOrWhiteSpace($workspaceRoot)) {{
    $env:CODEX_MIKU_WORKSPACE_ROOT = $workspaceRoot
}}
Remove-Item Env:CODEX_MIKU_PLAYBACK_START_FRAME -ErrorAction SilentlyContinue
Remove-Item Env:CODEX_MIKU_PLAYBACK_END_FRAME -ErrorAction SilentlyContinue

& $unityPath -batchmode -nographics -projectPath $projectPath -executeMethod BuildMikuLobbyAssetBundle.Run -quit -logFile $logPath
exit $LASTEXITCODE
";
    }

    private static string ToPowerShellLiteral(string value)
    {
        return "'" + (value ?? string.Empty).Replace("'", "''") + "'";
    }

    private readonly struct ExternalBatchBuildLaunch
    {
        public ExternalBatchBuildLaunch(string projectPath, string outputDirectory, string logPath, string scriptPath)
        {
            ProjectPath = projectPath;
            OutputDirectory = outputDirectory;
            LogPath = logPath;
            ScriptPath = scriptPath;
        }

        public string ProjectPath { get; }
        public string OutputDirectory { get; }
        public string LogPath { get; }
        public string ScriptPath { get; }
    }

    private void OpenOutputFolder()
    {
        if (_profile == null)
        {
            return;
        }

        var outputDirectory = _profile.ResolveOutputDirectory();
        Directory.CreateDirectory(outputDirectory);
        EditorUtility.RevealInFinder(outputDirectory);
    }

    private void RebindSerializedObject()
    {
        if (_profile != null)
        {
            _serializedProfile = new SerializedObject(_profile);
        }
    }

    private void TryPrepareDisplayAsset()
    {
        if (_profile == null || _profile.DisplayAsset == null)
        {
            return;
        }

        try
        {
            MikuImportedModelAutomation.ProcessImportedModelForObject(_profile.DisplayAsset);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private bool EnsureDisplayAssetSelected(out GameObject displayAsset)
    {
        displayAsset = _profile != null ? _profile.DisplayAsset : null;
        if (displayAsset != null)
        {
            return true;
        }

        EditorUtility.DisplayDialog("No Display Asset", "先指定一个显示模型，再执行这个工具。", "OK");
        return false;
    }

    private void RefreshResolvedMotionAssets()
    {
        if (_profile == null || _profile.DisplayAsset == null)
        {
            return;
        }

        var resolvedController = BuildMikuLobbyAssetBundle.TryResolveSuggestedBodyController(_profile.DisplayAsset);
        if (resolvedController != null)
        {
            _profile.BodyController = resolvedController;
        }

        var resolvedBodyClip = BuildMikuLobbyAssetBundle.TryResolveSuggestedBodyAnimationClip(_profile.DisplayAsset);
        if (resolvedBodyClip != null)
        {
            _profile.BodyAnimationClip = resolvedBodyClip;
        }

        EditorUtility.SetDirty(_profile);
        RebindSerializedObject();
    }

    private sealed class ProfileAnalysis
    {
        public List<string> Errors { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public List<string> Infos { get; } = new List<string>();
    }
}
