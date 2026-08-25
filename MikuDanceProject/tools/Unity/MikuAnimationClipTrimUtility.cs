#nullable enable annotations
#nullable disable warnings

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

internal static class MikuAnimationClipTrimUtility
{
    private const float KeyTimeEpsilon = 0.0001f;
    private const float ConstantCurveValueEpsilon = 0.00001f;
    private const float RotationCurveSimplifyTolerance = 0.0015f;
    private const float PositionCurveSimplifyTolerance = 0.001f;
    private const float GenericCurveSimplifyTolerance = 0.0005f;

    public static AnimationClip ExportTrimmedClipAsset(
        AnimationClip sourceClip,
        string outputAssetPath,
        float startSeconds,
        float? endSeconds,
        string? clipName = null)
    {
        if (sourceClip == null)
        {
            throw new ArgumentNullException(nameof(sourceClip));
        }

        if (string.IsNullOrWhiteSpace(outputAssetPath))
        {
            throw new ArgumentException("Output clip asset path must not be empty.", nameof(outputAssetPath));
        }

        var safeStartSeconds = Mathf.Max(0f, startSeconds);
        var sourceLength = Mathf.Max(0f, sourceClip.length);
        var safeEndSeconds = endSeconds.HasValue
            ? Mathf.Clamp(endSeconds.Value, safeStartSeconds, sourceLength)
            : sourceLength;
        var trimmedDuration = Mathf.Max(0f, safeEndSeconds - safeStartSeconds);

        var outputClip = new AnimationClip
        {
            name = string.IsNullOrWhiteSpace(clipName)
                ? $"{sourceClip.name}_Trimmed"
                : clipName,
            frameRate = sourceClip.frameRate,
            legacy = sourceClip.legacy,
            wrapMode = sourceClip.wrapMode,
        };

        CopyFloatCurves(sourceClip, outputClip, safeStartSeconds, safeEndSeconds, trimmedDuration);
        CopyObjectReferenceCurves(sourceClip, outputClip, safeStartSeconds, safeEndSeconds);
        CopyAnimationEvents(sourceClip, outputClip, safeStartSeconds, safeEndSeconds);
        CopyAnimationSettings(sourceClip, outputClip);

        var outputFolderPath = Path.GetDirectoryName(outputAssetPath)?.Replace("\\", "/");
        if (string.IsNullOrWhiteSpace(outputFolderPath))
        {
            throw new InvalidOperationException($"Output clip asset path is invalid: '{outputAssetPath}'.");
        }

        EnsureFolderExists(outputFolderPath);
        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(outputAssetPath) != null)
        {
            AssetDatabase.DeleteAsset(outputAssetPath);
        }

        AssetDatabase.CreateAsset(outputClip, outputAssetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(outputAssetPath, ImportAssetOptions.ForceUpdate);

        return AssetDatabase.LoadAssetAtPath<AnimationClip>(outputAssetPath)
            ?? throw new InvalidOperationException($"Failed to load trimmed AnimationClip '{outputAssetPath}'.");
    }

    public static AnimationClip ExportFilteredClipAsset(
        AnimationClip sourceClip,
        string outputAssetPath,
        float startSeconds,
        float? endSeconds,
        Func<EditorCurveBinding, bool> keepFloatBinding,
        string? clipName = null,
        bool simplifyFloatCurves = false)
    {
        if (sourceClip == null)
        {
            throw new ArgumentNullException(nameof(sourceClip));
        }

        if (keepFloatBinding == null)
        {
            throw new ArgumentNullException(nameof(keepFloatBinding));
        }

        if (string.IsNullOrWhiteSpace(outputAssetPath))
        {
            throw new ArgumentException("Output clip asset path must not be empty.", nameof(outputAssetPath));
        }

        var safeStartSeconds = Mathf.Max(0f, startSeconds);
        var sourceLength = Mathf.Max(0f, sourceClip.length);
        var safeEndSeconds = endSeconds.HasValue
            ? Mathf.Clamp(endSeconds.Value, safeStartSeconds, sourceLength)
            : sourceLength;
        var trimmedDuration = Mathf.Max(0f, safeEndSeconds - safeStartSeconds);

        var outputClip = new AnimationClip
        {
            name = string.IsNullOrWhiteSpace(clipName)
                ? $"{sourceClip.name}_Filtered"
                : clipName,
            frameRate = sourceClip.frameRate,
            legacy = sourceClip.legacy,
            wrapMode = sourceClip.wrapMode,
        };

        CopyFloatCurves(
            sourceClip,
            outputClip,
            safeStartSeconds,
            safeEndSeconds,
            trimmedDuration,
            keepFloatBinding,
            simplifyFloatCurves);
        CopyAnimationEvents(sourceClip, outputClip, safeStartSeconds, safeEndSeconds);
        CopyAnimationSettings(sourceClip, outputClip);
        outputClip.EnsureQuaternionContinuity();

        var outputFolderPath = Path.GetDirectoryName(outputAssetPath)?.Replace("\\", "/");
        if (string.IsNullOrWhiteSpace(outputFolderPath))
        {
            throw new InvalidOperationException($"Output clip asset path is invalid: '{outputAssetPath}'.");
        }

        EnsureFolderExists(outputFolderPath);
        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(outputAssetPath) != null)
        {
            AssetDatabase.DeleteAsset(outputAssetPath);
        }

        AssetDatabase.CreateAsset(outputClip, outputAssetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(outputAssetPath, ImportAssetOptions.ForceUpdate);

        return AssetDatabase.LoadAssetAtPath<AnimationClip>(outputAssetPath)
            ?? throw new InvalidOperationException($"Failed to load filtered AnimationClip '{outputAssetPath}'.");
    }

    private static void CopyFloatCurves(
        AnimationClip sourceClip,
        AnimationClip outputClip,
        float startSeconds,
        float endSeconds,
        float trimmedDuration,
        Func<EditorCurveBinding, bool>? keepBinding = null,
        bool simplifyFloatCurves = false)
    {
        var originalKeyCount = 0;
        var copiedKeyCount = 0;
        var simplifiedCurveCount = 0;
        foreach (var binding in AnimationUtility.GetCurveBindings(sourceClip))
        {
            if (keepBinding != null && !keepBinding(binding))
            {
                continue;
            }

            var sourceCurve = AnimationUtility.GetEditorCurve(sourceClip, binding);
            if (sourceCurve == null || sourceCurve.length == 0)
            {
                continue;
            }

            originalKeyCount += sourceCurve.length;
            AnimationCurve? outputCurve;
            if (startSeconds <= KeyTimeEpsilon
                && endSeconds >= Mathf.Max(0f, sourceClip.length) - KeyTimeEpsilon)
            {
                outputCurve = CloneFloatCurve(sourceCurve);
            }
            else
            {
                outputCurve = BuildTrimmedFloatCurve(sourceCurve, startSeconds, endSeconds, trimmedDuration);
            }

            if (outputCurve == null || outputCurve.length == 0)
            {
                continue;
            }

            if (simplifyFloatCurves)
            {
                var simplifiedCurve = SimplifyFloatCurve(binding, outputCurve, trimmedDuration);
                if (simplifiedCurve != null && simplifiedCurve.length > 0)
                {
                    if (simplifiedCurve.length < outputCurve.length)
                    {
                        simplifiedCurveCount++;
                    }

                    outputCurve = simplifiedCurve;
                }
            }

            copiedKeyCount += outputCurve.length;
            AnimationUtility.SetEditorCurve(outputClip, binding, outputCurve);
        }

        if (simplifyFloatCurves)
        {
            Debug.Log(
                $"[Codex] Simplified copied body curves for '{outputClip.name}'. " +
                $"originalKeys={originalKeyCount}, outputKeys={copiedKeyCount}, simplifiedCurves={simplifiedCurveCount}.");
        }
    }

    private static AnimationCurve CloneFloatCurve(AnimationCurve sourceCurve)
    {
        return new AnimationCurve(sourceCurve.keys)
        {
            preWrapMode = sourceCurve.preWrapMode,
            postWrapMode = sourceCurve.postWrapMode,
        };
    }

    private static AnimationCurve? SimplifyFloatCurve(EditorCurveBinding binding, AnimationCurve sourceCurve, float clipLength)
    {
        if (sourceCurve == null || sourceCurve.length == 0)
        {
            return null;
        }

        if (sourceCurve.length <= 2)
        {
            return CloneFloatCurve(sourceCurve);
        }

        if (IsConstantFloatCurve(sourceCurve))
        {
            var constantValue = sourceCurve.keys[0].value;
            var keys = clipLength > KeyTimeEpsilon
                ? new[]
                {
                    new Keyframe(0f, constantValue),
                    new Keyframe(clipLength, constantValue),
                }
                : new[] { new Keyframe(0f, constantValue) };
            return new AnimationCurve(keys)
            {
                preWrapMode = sourceCurve.preWrapMode,
                postWrapMode = sourceCurve.postWrapMode,
            };
        }

        var tolerance = ResolveSimplifyTolerance(binding);
        if (tolerance <= 0f)
        {
            return CloneFloatCurve(sourceCurve);
        }

        var keysToKeep = SelectRepresentativeKeys(sourceCurve.keys, tolerance);
        if (keysToKeep.Count >= sourceCurve.length)
        {
            return CloneFloatCurve(sourceCurve);
        }

        var simplifiedCurve = new AnimationCurve(keysToKeep.ToArray())
        {
            preWrapMode = sourceCurve.preWrapMode,
            postWrapMode = sourceCurve.postWrapMode,
        };
        return simplifiedCurve;
    }

    private static bool IsConstantFloatCurve(AnimationCurve sourceCurve)
    {
        var referenceValue = sourceCurve.keys[0].value;
        for (var index = 1; index < sourceCurve.length; index++)
        {
            if (Mathf.Abs(sourceCurve.keys[index].value - referenceValue) > ConstantCurveValueEpsilon)
            {
                return false;
            }
        }

        return true;
    }

    private static float ResolveSimplifyTolerance(EditorCurveBinding binding)
    {
        if (binding.propertyName.IndexOf("m_LocalRotation", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return RotationCurveSimplifyTolerance;
        }

        if (binding.propertyName.IndexOf("m_LocalPosition", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return PositionCurveSimplifyTolerance;
        }

        return GenericCurveSimplifyTolerance;
    }

    private static List<Keyframe> SelectRepresentativeKeys(IReadOnlyList<Keyframe> sourceKeys, float tolerance)
    {
        var keep = new bool[sourceKeys.Count];
        keep[0] = true;
        keep[sourceKeys.Count - 1] = true;

        var stack = new Stack<(int Start, int End)>();
        stack.Push((0, sourceKeys.Count - 1));
        while (stack.Count > 0)
        {
            var (start, end) = stack.Pop();
            if (end <= start + 1)
            {
                continue;
            }

            var startKey = sourceKeys[start];
            var endKey = sourceKeys[end];
            var duration = endKey.time - startKey.time;
            if (duration <= KeyTimeEpsilon)
            {
                continue;
            }

            var maxError = 0f;
            var maxErrorIndex = -1;
            for (var index = start + 1; index < end; index++)
            {
                var key = sourceKeys[index];
                var normalizedTime = Mathf.Clamp01((key.time - startKey.time) / duration);
                var interpolatedValue = Mathf.Lerp(startKey.value, endKey.value, normalizedTime);
                var error = Mathf.Abs(key.value - interpolatedValue);
                if (error > maxError)
                {
                    maxError = error;
                    maxErrorIndex = index;
                }
            }

            if (maxErrorIndex < 0 || maxError <= tolerance)
            {
                continue;
            }

            keep[maxErrorIndex] = true;
            stack.Push((start, maxErrorIndex));
            stack.Push((maxErrorIndex, end));
        }

        var selectedKeys = new List<Keyframe>();
        for (var index = 0; index < sourceKeys.Count; index++)
        {
            if (keep[index])
            {
                selectedKeys.Add(sourceKeys[index]);
            }
        }

        return selectedKeys;
    }

    private static AnimationCurve? BuildTrimmedFloatCurve(
        AnimationCurve sourceCurve,
        float startSeconds,
        float endSeconds,
        float trimmedDuration)
    {
        var keys = new List<Keyframe>(sourceCurve.length + 2);
        AppendOrReplaceKey(keys, new Keyframe(0f, sourceCurve.Evaluate(startSeconds)));

        foreach (var sourceKey in sourceCurve.keys)
        {
            if (sourceKey.time <= startSeconds + KeyTimeEpsilon)
            {
                continue;
            }

            if (sourceKey.time >= endSeconds - KeyTimeEpsilon)
            {
                continue;
            }

            var shiftedKey = sourceKey;
            shiftedKey.time -= startSeconds;
            AppendOrReplaceKey(keys, shiftedKey);
        }

        if (trimmedDuration > KeyTimeEpsilon)
        {
            AppendOrReplaceKey(keys, new Keyframe(trimmedDuration, sourceCurve.Evaluate(endSeconds)));
        }

        if (keys.Count == 0)
        {
            return null;
        }

        var trimmedCurve = new AnimationCurve(keys.ToArray())
        {
            preWrapMode = sourceCurve.preWrapMode,
            postWrapMode = sourceCurve.postWrapMode,
        };
        return trimmedCurve;
    }

    private static void AppendOrReplaceKey(List<Keyframe> keys, Keyframe keyframe)
    {
        if (keys.Count > 0 && Mathf.Abs(keys[keys.Count - 1].time - keyframe.time) <= KeyTimeEpsilon)
        {
            keys[keys.Count - 1] = keyframe;
            return;
        }

        keys.Add(keyframe);
    }

    private static void CopyObjectReferenceCurves(
        AnimationClip sourceClip,
        AnimationClip outputClip,
        float startSeconds,
        float endSeconds)
    {
        foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(sourceClip))
        {
            var sourceKeys = AnimationUtility.GetObjectReferenceCurve(sourceClip, binding);
            if (sourceKeys == null || sourceKeys.Length == 0)
            {
                continue;
            }

            var trimmedKeys = BuildTrimmedObjectReferenceCurve(sourceKeys, startSeconds, endSeconds);
            if (trimmedKeys.Length == 0)
            {
                continue;
            }

            AnimationUtility.SetObjectReferenceCurve(outputClip, binding, trimmedKeys);
        }
    }

    private static ObjectReferenceKeyframe[] BuildTrimmedObjectReferenceCurve(
        IReadOnlyList<ObjectReferenceKeyframe> sourceKeys,
        float startSeconds,
        float endSeconds)
    {
        var keys = new List<ObjectReferenceKeyframe>(sourceKeys.Count + 1);
        var firstActiveKey = sourceKeys
            .Where(key => key.time <= startSeconds + KeyTimeEpsilon)
            .OrderBy(key => key.time)
            .LastOrDefault();
        if (firstActiveKey.value != null)
        {
            keys.Add(new ObjectReferenceKeyframe
            {
                time = 0f,
                value = firstActiveKey.value,
            });
        }

        for (var index = 0; index < sourceKeys.Count; index++)
        {
            var sourceKey = sourceKeys[index];
            if (sourceKey.time <= startSeconds + KeyTimeEpsilon)
            {
                continue;
            }

            if (sourceKey.time >= endSeconds - KeyTimeEpsilon)
            {
                continue;
            }

            keys.Add(new ObjectReferenceKeyframe
            {
                time = sourceKey.time - startSeconds,
                value = sourceKey.value,
            });
        }

        return keys
            .OrderBy(key => key.time)
            .ToArray();
    }

    private static void CopyAnimationEvents(
        AnimationClip sourceClip,
        AnimationClip outputClip,
        float startSeconds,
        float endSeconds)
    {
        var sourceEvents = AnimationUtility.GetAnimationEvents(sourceClip);
        if (sourceEvents == null || sourceEvents.Length == 0)
        {
            return;
        }

        var trimmedEvents = sourceEvents
            .Where(animationEvent => animationEvent != null)
            .Where(animationEvent => animationEvent.time >= startSeconds - KeyTimeEpsilon)
            .Where(animationEvent => animationEvent.time <= endSeconds + KeyTimeEpsilon)
            .Select(animationEvent => new AnimationEvent
            {
                time = Mathf.Max(0f, animationEvent.time - startSeconds),
                functionName = animationEvent.functionName,
                stringParameter = animationEvent.stringParameter,
                floatParameter = animationEvent.floatParameter,
                intParameter = animationEvent.intParameter,
                objectReferenceParameter = animationEvent.objectReferenceParameter,
                messageOptions = animationEvent.messageOptions,
            })
            .ToArray();

        AnimationUtility.SetAnimationEvents(outputClip, trimmedEvents);
    }

    private static void CopyAnimationSettings(AnimationClip sourceClip, AnimationClip outputClip)
    {
        var settings = AnimationUtility.GetAnimationClipSettings(sourceClip);
        AnimationUtility.SetAnimationClipSettings(outputClip, settings);
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
