using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MikuDancePackager;

internal sealed record PackagingOptions(
    string ProjectRoot,
    string BundlePath,
    string Version,
    string OutputDirectory,
    string IconPath,
    string ManifestJson,
    string ReadmeMarkdown,
    int MotionStartFrame,
    int? MotionEndFrame);

internal sealed record ProjectLayout(
    string ProjectRoot,
    string ProjectFilePath,
    string PluginConstantsPath,
    string ManifestPath,
    string ReadmePath,
    string IconPath,
    string PackageDirectory,
    string PackagePluginsDirectory,
    string BuiltDllPath,
    string DistDirectory,
    string PackageDllPath,
    string PackageBundlePath,
    string WorkspaceRoot,
    string PluginDllFileName,
    string BundleFileName,
    string PackageName);

internal sealed record PackagingProgressUpdate(
    int Percent,
    string Title,
    string Detail,
    bool IsIndeterminate = false);

internal static class PackagingService
{
    private const int DefaultMotionStartFrame = 0;
    private const string UnityBuildMethodName = "BuildMikuLobbyAssetBundle.Run";
    private const string UnityBuildOutputEnvironmentVariable = "CODEX_MIKU_BUNDLE_OUTPUT";
    private const string UnityBuildProfileEnvironmentVariable = "CODEX_MIKU_BUNDLE_PROFILE";
    private const string UnityBuildStartFrameEnvironmentVariable = "CODEX_MIKU_PLAYBACK_START_FRAME";
    private const string UnityBuildEndFrameEnvironmentVariable = "CODEX_MIKU_PLAYBACK_END_FRAME";
    private const string UnityWorkspaceRootEnvironmentVariable = "CODEX_MIKU_WORKSPACE_ROOT";
    private const string SelectedUnityProfileStateRelativePath = @"ProjectSettings\CodexMikuActiveProfile.txt";
    private const string ToonShaderProjectFolder =
        "Assets/UnityURPToonLitShaderExample-master/UnityURPToonLitShaderExample-master";

    private static readonly Regex PluginVersionRegex =
        new(@"public\s+const\s+string\s+Version\s*=\s*""(?<version>[^""]+)""\s*;", RegexOptions.Compiled);

    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly HashSet<string> IgnoredSearchDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".vs",
        "bin",
        "obj",
        "dist",
        "package",
        "_compare",
        "Library",
        "Logs",
        "Temp",
        "UserSettings",
        "ProjectSettings",
        "Packages",
        "PackageCache",
        "publish",
    };

    private static readonly string[] UnityEditorSearchRoots =
    {
        @"D:\UnityEditor",
        @"C:\Program Files\Unity\Hub\Editor",
        @"C:\Program Files\Unity\Editor",
    };

    public static string? TryDetectProjectRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            var projectFilePath = Path.Combine(current, "MikuDanceProject.csproj");
            if (File.Exists(projectFilePath))
            {
                return current;
            }

            var parent = Directory.GetParent(current);
            if (parent == null || string.Equals(parent.FullName, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = parent.FullName;
        }

        return null;
    }

    public static ProjectLayout CreateLayout(string projectRoot)
    {
        var normalizedProjectRoot = Path.GetFullPath(projectRoot);
        var workspaceRoot = Path.GetFullPath(Path.Combine(normalizedProjectRoot, ".."));
        var manifestPath = Path.Combine(normalizedProjectRoot, "package", "manifest.json");
        var manifestJson = JsonNode.Parse(File.ReadAllText(manifestPath))
            ?? throw new InvalidOperationException($"Unable to parse manifest: '{manifestPath}'.");
        var packageName = manifestJson["name"]?.GetValue<string>() ?? "MikuShowcase";

        return new ProjectLayout(
            normalizedProjectRoot,
            Path.Combine(normalizedProjectRoot, "MikuDanceProject.csproj"),
            Path.Combine(normalizedProjectRoot, "src", "Core", "PluginConstants.cs"),
            manifestPath,
            Path.Combine(normalizedProjectRoot, "package", "README.md"),
            Path.Combine(normalizedProjectRoot, "package", "icon.png"),
            Path.Combine(normalizedProjectRoot, "package"),
            Path.Combine(normalizedProjectRoot, "package", "plugins"),
            Path.Combine(normalizedProjectRoot, "bin", "Release", "com.github.Thanks.MikuShowcase.dll"),
            Path.Combine(normalizedProjectRoot, "dist"),
            Path.Combine(normalizedProjectRoot, "package", "plugins", "com.github.Thanks.MikuShowcase.dll"),
            Path.Combine(normalizedProjectRoot, "package", "plugins", "miku_lobby_display.bundle"),
            workspaceRoot,
            "com.github.Thanks.MikuShowcase.dll",
            "miku_lobby_display.bundle",
            packageName);
    }

    public static string ReadCurrentVersion(ProjectLayout layout)
    {
        var pluginConstantsText = File.ReadAllText(layout.PluginConstantsPath, Encoding.UTF8);
        var match = PluginVersionRegex.Match(pluginConstantsText);
        if (!match.Success)
        {
            throw new InvalidOperationException($"Could not find PluginConstants.Version in '{layout.PluginConstantsPath}'.");
        }

        return match.Groups["version"].Value;
    }

    public static string GetDefaultIconPath(ProjectLayout layout)
    {
        return File.Exists(layout.IconPath) ? layout.IconPath : string.Empty;
    }

    public static string? TryDetectLatestBundle(ProjectLayout layout)
    {
        var latestBundle = EnumerateBundleCandidates(layout)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault();

        return latestBundle?.FullName;
    }

    public static string ResolvePackageName(ProjectLayout layout)
    {
        var manifestText = File.ReadAllText(layout.ManifestPath, Encoding.UTF8);
        return ResolvePackageNameFromManifestText(manifestText, layout.PackageName);
    }

    public static string ReadManifestTemplate(ProjectLayout layout)
    {
        return File.ReadAllText(layout.ManifestPath, Encoding.UTF8);
    }

    public static string ReadReadmeTemplate(ProjectLayout layout)
    {
        return File.ReadAllText(layout.ReadmePath, Encoding.UTF8);
    }

    public static string UpdateManifestVersionText(string manifestText, string version)
    {
        if (string.IsNullOrWhiteSpace(manifestText) || string.IsNullOrWhiteSpace(version))
        {
            return manifestText;
        }

        return BuildManifestText(manifestText, "manifest editor", version);
    }

    public static string ResolvePackageNameFromManifestText(string? manifestText, string fallbackPackageName)
    {
        if (string.IsNullOrWhiteSpace(manifestText))
        {
            return fallbackPackageName;
        }

        try
        {
            var manifestNode = JsonNode.Parse(manifestText);
            var packageName = manifestNode?["name"]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(packageName) ? fallbackPackageName : packageName.Trim();
        }
        catch
        {
            return fallbackPackageName;
        }
    }

    public static string Package(
        PackagingOptions options,
        Action<string> log,
        Action<PackagingProgressUpdate>? reportProgress = null)
    {
        ReportProgress(reportProgress, 2, "检查参数", "正在验证项目配置。");

        var layout = CreateLayout(options.ProjectRoot);
        ValidateOptions(options, layout);

        log($"Project root: {layout.ProjectRoot}");
        log($"Version: {options.Version}");
        log($"Output directory: {options.OutputDirectory}");
        log($"Motion start frame: {options.MotionStartFrame}");
        log($"Motion end frame: {(options.MotionEndFrame.HasValue ? options.MotionEndFrame.Value.ToString() : "(none)")}");

        ReportProgress(reportProgress, 12, "同步版本", "正在更新插件版本号。");
        UpdatePluginVersion(layout.PluginConstantsPath, options.Version, log);

        ReportProgress(reportProgress, 24, "编译插件", "正在生成主插件 DLL。", isIndeterminate: true);
        RunDotnetBuild(layout, log);
        ReportProgress(reportProgress, 36, "编译插件", "主插件 DLL 已生成。");

        var resolvedBundle = ResolveBundleSource(options, layout, log, reportProgress);
        log($"Bundle source: {resolvedBundle.BundlePath}");

        var stageDirectory = Path.Combine(Path.GetTempPath(), "MikuDancePackager", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stageDirectory);
        log($"Created temporary package directory -> {stageDirectory}");

        try
        {
            ReportProgress(reportProgress, 80, "整理包内容", "正在复制模板和插件文件。");
            CopyDirectory(layout.PackageDirectory, stageDirectory);

            var stagePluginsDirectory = Path.Combine(stageDirectory, "plugins");
            Directory.CreateDirectory(stagePluginsDirectory);
            Directory.CreateDirectory(options.OutputDirectory);

            var stagedManifestPath = Path.Combine(stageDirectory, "manifest.json");
            var stagedReadmePath = Path.Combine(stageDirectory, "README.md");
            var stagedIconPath = Path.Combine(stageDirectory, "icon.png");
            var stagedDllPath = Path.Combine(stagePluginsDirectory, layout.PluginDllFileName);
            var stagedBundlePath = Path.Combine(stagePluginsDirectory, layout.BundleFileName);

            File.Copy(layout.BuiltDllPath, stagedDllPath, overwrite: true);
            log($"Copied DLL -> {stagedDllPath}");

            File.Copy(resolvedBundle.BundlePath, stagedBundlePath, overwrite: true);
            log($"Copied bundle -> {stagedBundlePath}");

            CleanupGeneratedManifestFiles(stagePluginsDirectory, log);

            ReportProgress(reportProgress, 88, "写入元数据", "正在生成 manifest 和 README。");
            var manifestText = BuildManifestText(options.ManifestJson, layout.ManifestPath, options.Version);
            File.WriteAllText(stagedManifestPath, manifestText, Utf8WithoutBom);
            log($"Wrote manifest -> {stagedManifestPath}");

            var readmeText = BuildReadmeText(options.ReadmeMarkdown, layout.ReadmePath);
            File.WriteAllText(stagedReadmePath, readmeText, Utf8WithoutBom);
            log($"Wrote README -> {stagedReadmePath}");

            ApplyIcon(stagedIconPath, options.IconPath, layout.IconPath, log);

            var packageName = ResolvePackageNameFromManifestText(manifestText, layout.PackageName);
            var packageDirectoryName = $"Thanks-{packageName}-{options.Version}";
            var packageOutputDirectory = Path.Combine(options.OutputDirectory, packageDirectoryName);
            if (Directory.Exists(packageOutputDirectory))
            {
                Directory.Delete(packageOutputDirectory, recursive: true);
                log($"Removed existing package folder -> {packageOutputDirectory}");
            }
            else if (File.Exists(packageOutputDirectory))
            {
                File.Delete(packageOutputDirectory);
            }

            ReportProgress(reportProgress, 96, "生成文件夹", "正在写入最终文件夹。", isIndeterminate: true);
            CopyDirectory(stageDirectory, packageOutputDirectory);
            log($"Created package folder -> {packageOutputDirectory}");
            ReportProgress(reportProgress, 100, "打包完成", $"已生成 {Path.GetFileName(packageOutputDirectory)}。");
            return packageOutputDirectory;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(resolvedBundle.TemporaryDirectory))
            {
                TryDeleteDirectory(resolvedBundle.TemporaryDirectory, log);
            }

            TryDeleteDirectory(stageDirectory, log);
        }
    }

    private static void ReportProgress(
        Action<PackagingProgressUpdate>? reportProgress,
        int percent,
        string title,
        string detail,
        bool isIndeterminate = false)
    {
        reportProgress?.Invoke(new PackagingProgressUpdate(
            Math.Clamp(percent, 0, 100),
            title,
            detail,
            isIndeterminate));
    }

    public static int GetDefaultMotionStartFrame()
    {
        return DefaultMotionStartFrame;
    }

    public static string? TryResolveUnityProjectRoot(ProjectLayout layout)
    {
        return TryResolveUnityProjectRootCore(layout);
    }

    private static IEnumerable<FileInfo> EnumerateBundleCandidates(ProjectLayout layout)
    {
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in GetBundleSearchRoots(layout))
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                continue;
            }

            foreach (var filePath in EnumerateBundleFiles(root))
            {
                if (!seenPaths.Add(filePath))
                {
                    continue;
                }

                if (IsPackagedBundle(filePath, layout))
                {
                    continue;
                }

                yield return new FileInfo(filePath);
            }
        }
    }

    private static IEnumerable<string> GetBundleSearchRoots(ProjectLayout layout)
    {
        yield return layout.ProjectRoot;
        yield return layout.WorkspaceRoot;

        var unityProjectRoot = TryResolveUnityProjectRoot(layout);
        if (!string.IsNullOrWhiteSpace(unityProjectRoot))
        {
            yield return unityProjectRoot;
        }
    }

    private static string? TryResolveUnityProjectRootCore(ProjectLayout layout)
    {
        var workspaceName = new DirectoryInfo(layout.WorkspaceRoot).Name;
        var candidates = new[]
        {
            Path.Combine(@"D:\unity\projects", workspaceName),
            Path.Combine(@"D:\UnityProjects", workspaceName),
        };

        return candidates.FirstOrDefault(Directory.Exists);
    }

    private static IEnumerable<string> EnumerateBundleFiles(string rootDirectory)
    {
        var pending = new Stack<string>();
        pending.Push(rootDirectory);

        while (pending.Count > 0)
        {
            var currentDirectory = pending.Pop();

            IEnumerable<string> subDirectories;
            try
            {
                subDirectories = Directory.EnumerateDirectories(currentDirectory);
            }
            catch
            {
                continue;
            }

            foreach (var subDirectory in subDirectories)
            {
                var name = Path.GetFileName(subDirectory);
                if (IgnoredSearchDirectoryNames.Contains(name))
                {
                    continue;
                }

                pending.Push(subDirectory);
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(currentDirectory, "*.bundle", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return Path.GetFullPath(file);
            }
        }
    }

    private static bool IsPackagedBundle(string bundlePath, ProjectLayout layout)
    {
        return bundlePath.StartsWith(layout.PackagePluginsDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || bundlePath.IndexOf(Path.DirectorySeparatorChar + "_compare" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static ResolvedBundleSource ResolveBundleSource(
        PackagingOptions options,
        ProjectLayout layout,
        Action<string> log,
        Action<PackagingProgressUpdate>? reportProgress)
    {
        var unityProjectRoot = TryResolveUnityProjectRootCore(layout);
        if (!string.IsNullOrWhiteSpace(unityProjectRoot))
        {
            if (IsUnityProjectOpen(unityProjectRoot))
            {
                var existingBundlePath = ResolveExistingBundlePath(options, layout);
                if (!string.IsNullOrWhiteSpace(existingBundlePath))
                {
                    ReportProgress(reportProgress, 52, "使用现有 AB", "Unity 工程已打开，改用最近一次生成的 bundle。");
                    log("Unity project is currently open. Falling back to the latest existing bundle instead of batch rebuilding.");
                    return new ResolvedBundleSource(existingBundlePath, null);
                }

                throw new InvalidOperationException(
                    $"Unity project '{unityProjectRoot}' is currently open, so batch build is unavailable, and no existing .bundle file could be found to use as a fallback.");
            }

            return BuildBundleFromUnityProject(options, layout, unityProjectRoot, log, reportProgress);
        }

        var fallbackBundlePath = ResolveExistingBundlePath(options, layout);
        if (!string.IsNullOrWhiteSpace(fallbackBundlePath))
        {
            ReportProgress(reportProgress, 64, "使用现有 AB", "未找到可用的 Unity 工程，改用现有 bundle。");
            return new ResolvedBundleSource(fallbackBundlePath, null);
        }

        throw new FileNotFoundException(
            "No usable bundle was found. Select an existing .bundle file, or place the Unity project under D:\\unity\\projects\\<workspace name> so the packager can rebuild it automatically.");
    }

    private static string? ResolveExistingBundlePath(PackagingOptions options, ProjectLayout layout)
    {
        if (!string.IsNullOrWhiteSpace(options.BundlePath) && File.Exists(options.BundlePath))
        {
            return Path.GetFullPath(options.BundlePath);
        }

        return TryDetectLatestBundle(layout);
    }

    private static bool IsUnityProjectOpen(string unityProjectRoot)
    {
        var lockFilePath = Path.Combine(unityProjectRoot, "Temp", "UnityLockfile");
        return File.Exists(lockFilePath);
    }

    private static ResolvedBundleSource BuildBundleFromUnityProject(
        PackagingOptions options,
        ProjectLayout layout,
        string unityProjectRoot,
        Action<string> log,
        Action<PackagingProgressUpdate>? reportProgress)
    {
        if (!Directory.Exists(unityProjectRoot))
        {
            throw new DirectoryNotFoundException($"Unity project root does not exist: '{unityProjectRoot}'.");
        }

        var unityEditorPath = TryResolveUnityEditorPath();
        if (string.IsNullOrWhiteSpace(unityEditorPath) || !File.Exists(unityEditorPath))
        {
            throw new FileNotFoundException(
                "Unity Editor executable was not found. Install Unity or place Unity.exe under D:\\UnityEditor\\<version>\\Editor\\Unity.exe.");
        }

        log($"Unity project: {unityProjectRoot}");
        log($"Unity editor: {unityEditorPath}");

        ReportProgress(reportProgress, 44, "准备 Unity 构建", "正在同步 Unity 编辑器脚本。", isIndeterminate: true);
        SyncUnityEditorScripts(layout, unityProjectRoot, log);

        var buildDirectory = Path.Combine(Path.GetTempPath(), "MikuDancePackager", "UnityBuild", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(buildDirectory);

        var unityLogPath = Path.Combine(buildDirectory, "unity-build.log");
        log($"Unity build output directory -> {buildDirectory}");

        var psi = new ProcessStartInfo
        {
            FileName = unityEditorPath,
            Arguments =
                $"-batchmode -nographics -projectPath \"{unityProjectRoot}\" -executeMethod {UnityBuildMethodName} -quit -logFile \"{unityLogPath}\"",
            WorkingDirectory = unityProjectRoot,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.Environment[UnityBuildOutputEnvironmentVariable] = buildDirectory;
        psi.Environment[UnityWorkspaceRootEnvironmentVariable] = layout.WorkspaceRoot;
        var selectedProfileAssetPath = TryResolveSelectedUnityProfileAssetPath(unityProjectRoot);
        if (!string.IsNullOrWhiteSpace(selectedProfileAssetPath))
        {
            psi.Environment[UnityBuildProfileEnvironmentVariable] = selectedProfileAssetPath;
            log($"Unity build profile: {selectedProfileAssetPath}");
        }

        psi.Environment[UnityBuildStartFrameEnvironmentVariable] = options.MotionStartFrame.ToString();
        psi.Environment[UnityBuildEndFrameEnvironmentVariable] = options.MotionEndFrame?.ToString() ?? string.Empty;

        using var process = new Process { StartInfo = psi };
        ReportProgress(reportProgress, 58, "构建 AB", "正在启动 Unity 批处理构建。", isIndeterminate: true);
        log("Running Unity batch build...");
        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start Unity batch build.");
        }

        WaitForUnityBatchBuild(process, unityLogPath, log);
        if (process.ExitCode != 0)
        {
            var unityLogTail = TryReadFileTail(unityLogPath, 120);
            throw new InvalidOperationException(
                $"Unity batch build failed with exit code {process.ExitCode}.{Environment.NewLine}{unityLogTail}");
        }

        var builtBundlePath = Directory.EnumerateFiles(buildDirectory, "*.bundle", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(builtBundlePath) || !File.Exists(builtBundlePath))
        {
            var unityLogTail = TryReadFileTail(unityLogPath, 120);
            throw new FileNotFoundException(
                $"Unity batch build completed but did not produce a .bundle file in '{buildDirectory}'.{Environment.NewLine}{unityLogTail}");
        }

        log($"Unity batch build completed -> {builtBundlePath}");
        ReportProgress(reportProgress, 76, "构建 AB", $"Unity 已生成 {Path.GetFileName(builtBundlePath)}。");
        return new ResolvedBundleSource(Path.GetFullPath(builtBundlePath), buildDirectory);
    }

    private static void WaitForUnityBatchBuild(Process process, string unityLogPath, Action<string> log)
    {
        var nextLineIndex = 0;
        while (!process.WaitForExit(400))
        {
            nextLineIndex = PumpUnityLog(unityLogPath, nextLineIndex, log);
        }

        PumpUnityLog(unityLogPath, nextLineIndex, log);
    }

    private static int PumpUnityLog(string unityLogPath, int nextLineIndex, Action<string> log)
    {
        if (string.IsNullOrWhiteSpace(unityLogPath) || !File.Exists(unityLogPath))
        {
            return nextLineIndex;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(unityLogPath, Encoding.UTF8);
        }
        catch
        {
            return nextLineIndex;
        }

        for (var index = nextLineIndex; index < lines.Length; index++)
        {
            var summary = TrySummarizeUnityLogLine(lines[index]);
            if (!string.IsNullOrWhiteSpace(summary))
            {
                log(summary);
            }
        }

        return lines.Length;
    }

    private static string? TrySummarizeUnityLogLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var trimmed = line.Trim();
        if (trimmed.StartsWith("[Codex]", StringComparison.OrdinalIgnoreCase))
        {
            return "[Unity] " + trimmed.Substring("[Codex]".Length).TrimStart();
        }

        if (trimmed.StartsWith("BuildPlayer:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("Bundle Name:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("***Player size statistics***", StringComparison.OrdinalIgnoreCase))
        {
            return "[Unity] " + trimmed;
        }

        if (trimmed.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0
            || trimmed.IndexOf("exception", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "[Unity] " + trimmed;
        }

        if (trimmed.IndexOf("Batchmode quit successfully", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "[Unity] Batchmode quit successfully.";
        }

        return null;
    }

    private static void SyncUnityEditorScripts(ProjectLayout layout, string unityProjectRoot, Action<string> log)
    {
        var sourceDirectory = Path.Combine(layout.ProjectRoot, "tools", "Unity");
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"Unity tool source directory does not exist: '{sourceDirectory}'.");
        }

        var destinationDirectory = Path.Combine(unityProjectRoot, "Assets", "Editor");
        Directory.CreateDirectory(destinationDirectory);

        var copiedCount = 0;
        foreach (var sourceFilePath in Directory.EnumerateFiles(sourceDirectory, "*.cs", SearchOption.TopDirectoryOnly))
        {
            var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(sourceFilePath));
            File.Copy(sourceFilePath, destinationPath, overwrite: true);
            copiedCount++;
        }

        log($"Synchronized {copiedCount} Unity editor scripts -> {destinationDirectory}");
        var shaderSupportFileCount = SyncUnityShaderSupportFiles(layout, unityProjectRoot);
        if (shaderSupportFileCount > 0)
        {
            log($"Synchronized {shaderSupportFileCount} Unity shader support files -> {ToonShaderProjectFolder}");
        }
    }

    private static int SyncUnityShaderSupportFiles(ProjectLayout layout, string unityProjectRoot)
    {
        var sourceDirectory = Path.Combine(layout.ProjectRoot, "tools", "UnityShaderSync");
        if (!Directory.Exists(sourceDirectory))
        {
            return 0;
        }

        var shaderProjectDirectory = Path.Combine(
            unityProjectRoot,
            ToonShaderProjectFolder.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(shaderProjectDirectory);

        var fileNames = new[]
        {
            "SimpleURPToonLitOutlineExample_Shared.hlsl",
            "SimpleURPToonLitOutlineExample_LightingEquation.hlsl",
        };

        var copiedCount = 0;
        foreach (var fileName in fileNames)
        {
            var sourcePath = Path.Combine(sourceDirectory, fileName);
            if (!File.Exists(sourcePath))
            {
                continue;
            }

            File.Copy(sourcePath, Path.Combine(shaderProjectDirectory, fileName), overwrite: true);
            copiedCount++;
        }

        return copiedCount;
    }

    private static string? TryResolveSelectedUnityProfileAssetPath(string unityProjectRoot)
    {
        if (string.IsNullOrWhiteSpace(unityProjectRoot) || !Directory.Exists(unityProjectRoot))
        {
            return null;
        }

        var stateFilePath = Path.Combine(unityProjectRoot, SelectedUnityProfileStateRelativePath);
        if (!File.Exists(stateFilePath))
        {
            return null;
        }

        var assetPath = File.ReadAllText(stateFilePath).Trim().Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var absoluteAssetPath = Path.Combine(unityProjectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(absoluteAssetPath) ? assetPath : null;
    }

    private static string? TryResolveUnityEditorPath()
    {
        var environmentPath = Environment.GetEnvironmentVariable("UNITY_EDITOR_PATH");
        if (!string.IsNullOrWhiteSpace(environmentPath) && File.Exists(environmentPath))
        {
            return Path.GetFullPath(environmentPath);
        }

        foreach (var searchRoot in UnityEditorSearchRoots)
        {
            if (!Directory.Exists(searchRoot))
            {
                continue;
            }

            var candidates = Directory.EnumerateDirectories(searchRoot)
                .Select(directory => Path.Combine(directory, "Editor", "Unity.exe"))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToArray();
            if (candidates.Length > 0)
            {
                return candidates[0];
            }
        }

        return null;
    }

    private static string TryReadFileTail(string filePath, int maxLineCount)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return "Unity log file was not found.";
        }

        try
        {
            var lines = File.ReadAllLines(filePath, Encoding.UTF8);
            return string.Join(Environment.NewLine, lines.Skip(Math.Max(0, lines.Length - maxLineCount)));
        }
        catch (Exception exception)
        {
            return $"Failed to read Unity log '{filePath}': {exception.Message}";
        }
    }

    private static void ValidateOptions(PackagingOptions options, ProjectLayout layout)
    {
        if (!Directory.Exists(layout.ProjectRoot))
        {
            throw new DirectoryNotFoundException($"Project root does not exist: '{layout.ProjectRoot}'.");
        }

        if (!File.Exists(layout.ProjectFilePath))
        {
            throw new FileNotFoundException($"Project file was not found: '{layout.ProjectFilePath}'.");
        }

        if (!Regex.IsMatch(options.Version, @"^\d+\.\d+\.\d+$"))
        {
            throw new InvalidOperationException("Version must use the format major.minor.patch, for example 0.5.0.");
        }

        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
        {
            throw new InvalidOperationException("Output directory must not be empty.");
        }

        if (options.MotionStartFrame < 0)
        {
            throw new InvalidOperationException("Motion start frame must not be negative.");
        }

        if (options.MotionEndFrame.HasValue)
        {
            if (options.MotionEndFrame.Value < 0)
            {
                throw new InvalidOperationException("Motion end frame must not be negative.");
            }

            if (options.MotionEndFrame.Value <= options.MotionStartFrame)
            {
                throw new InvalidOperationException("Motion end frame must be greater than motion start frame.");
            }
        }

        ValidateManifestText(options.ManifestJson);

        if (!string.IsNullOrWhiteSpace(options.IconPath))
        {
            if (!File.Exists(options.IconPath))
            {
                throw new FileNotFoundException($"Icon file was not found: '{options.IconPath}'.");
            }

            if (!string.Equals(Path.GetExtension(options.IconPath), ".png", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Icon file must be a .png image.");
            }
        }
    }

    private static void ValidateManifestText(string manifestText)
    {
        if (string.IsNullOrWhiteSpace(manifestText))
        {
            throw new InvalidOperationException("manifest.json content must not be empty.");
        }

        JsonNode? manifestNode;
        try
        {
            manifestNode = JsonNode.Parse(manifestText);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"manifest.json is not valid JSON. {exception.Message}", exception);
        }

        if (manifestNode is not JsonObject)
        {
            throw new InvalidOperationException("manifest.json must contain a JSON object at the root.");
        }
    }

    private static void UpdatePluginVersion(string pluginConstantsPath, string version, Action<string> log)
    {
        var originalText = File.ReadAllText(pluginConstantsPath, Encoding.UTF8);
        if (!PluginVersionRegex.IsMatch(originalText))
        {
            throw new InvalidOperationException($"Could not find PluginConstants.Version in '{pluginConstantsPath}'.");
        }

        var updatedText = PluginVersionRegex.Replace(
            originalText,
            match => match.Value.Replace(match.Groups["version"].Value, version));
        File.WriteAllText(pluginConstantsPath, updatedText, Utf8WithoutBom);
        log($"Updated PluginConstants.Version -> {version}");
    }

    private static string BuildManifestText(string? manifestOverrideText, string manifestPath, string version)
    {
        var sourceText = string.IsNullOrWhiteSpace(manifestOverrideText)
            ? File.ReadAllText(manifestPath, Encoding.UTF8)
            : manifestOverrideText;

        JsonNode? manifestNode;
        try
        {
            manifestNode = JsonNode.Parse(sourceText);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Failed to read manifest template '{manifestPath}'. {exception.Message}", exception);
        }

        if (manifestNode is not JsonObject manifestObject)
        {
            throw new InvalidOperationException("manifest.json must contain a JSON object at the root.");
        }

        manifestObject["version_number"] = version;

        if (manifestObject["dependencies"] == null)
        {
            manifestObject["dependencies"] = new JsonArray();
        }

        var serializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
        };
        return manifestObject.ToJsonString(serializerOptions);
    }

    private static string BuildReadmeText(string? readmeOverrideText, string readmePath)
    {
        if (readmeOverrideText != null)
        {
            return readmeOverrideText;
        }

        return File.Exists(readmePath)
            ? File.ReadAllText(readmePath, Encoding.UTF8)
            : string.Empty;
    }

    private static void ApplyIcon(string stagedIconPath, string selectedIconPath, string fallbackIconPath, Action<string> log)
    {
        var iconToUse = !string.IsNullOrWhiteSpace(selectedIconPath)
            ? selectedIconPath
            : fallbackIconPath;

        if (!string.IsNullOrWhiteSpace(iconToUse) && File.Exists(iconToUse))
        {
            File.Copy(iconToUse, stagedIconPath, overwrite: true);
            log($"Copied icon -> {stagedIconPath}");
        }
    }

    private static void RunDotnetBuild(ProjectLayout layout, Action<string> log)
    {
        var dotnetCliHome = Path.Combine(layout.WorkspaceRoot, ".dotnet-home");
        var nugetPackages = Path.Combine(layout.WorkspaceRoot, ".nuget", "packages");
        var appData = Path.Combine(layout.WorkspaceRoot, ".appdata");
        var localAppData = Path.Combine(layout.WorkspaceRoot, ".localappdata");
        Directory.CreateDirectory(dotnetCliHome);
        Directory.CreateDirectory(nugetPackages);
        Directory.CreateDirectory(appData);
        Directory.CreateDirectory(localAppData);

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{layout.ProjectFilePath}\" -c Release",
            WorkingDirectory = layout.ProjectRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.Environment["DOTNET_CLI_HOME"] = dotnetCliHome;
        psi.Environment["NUGET_PACKAGES"] = nugetPackages;
        psi.Environment["APPDATA"] = appData;
        psi.Environment["LOCALAPPDATA"] = localAppData;
        psi.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                log(args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                log(args.Data);
            }
        };

        log("Running dotnet build...");
        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start dotnet build.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"dotnet build failed with exit code {process.ExitCode}.");
        }

        if (!File.Exists(layout.BuiltDllPath))
        {
            throw new FileNotFoundException($"Build succeeded but DLL was not found: '{layout.BuiltDllPath}'.");
        }

        log("dotnet build completed successfully.");
    }

    private static void CleanupGeneratedManifestFiles(string packagePluginsDirectory, Action<string> log)
    {
        var candidates = new[]
        {
            Path.Combine(packagePluginsDirectory, "miku_lobby_display.bundle.manifest"),
            Path.Combine(packagePluginsDirectory, "plugins"),
            Path.Combine(packagePluginsDirectory, "plugins.manifest"),
        };

        foreach (var candidate in candidates.Where(File.Exists))
        {
            File.Delete(candidate);
            log($"Removed generated side file -> {candidate}");
        }
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var directoryPath in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, directoryPath);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relativePath));
        }

        foreach (var filePath in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, filePath);
            var destinationPath = Path.Combine(destinationDirectory, relativePath);
            var destinationParent = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationParent))
            {
                Directory.CreateDirectory(destinationParent);
            }

            File.Copy(filePath, destinationPath, overwrite: true);
        }
    }

    private static void TryDeleteDirectory(string directoryPath, Action<string> log, string description = "temporary package directory")
    {
        try
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
                log($"Removed {description} -> {directoryPath}");
            }
        }
        catch (Exception exception)
        {
            log($"[warn] Failed to remove {description} '{directoryPath}': {exception.Message}");
        }
    }

    private sealed record ResolvedBundleSource(string BundlePath, string? TemporaryDirectory);
}
