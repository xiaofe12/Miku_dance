using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using UnityEngine;

namespace MikuDanceProject.Runtime;

internal sealed class MikuFacialExpressionController : MonoBehaviour
{
    private const string MouthAGroupKey = "MouthA";
    private const string MouthIGroupKey = "MouthI";
    private const string MouthUGroupKey = "MouthU";
    private const string MouthEGroupKey = "MouthE";
    private const string MouthOGroupKey = "MouthO";
    private const string SmileGroupKey = "Smile";

    private static readonly string[] MouthShapePriority =
    {
        MouthAGroupKey,
        MouthIGroupKey,
        MouthUGroupKey,
        MouthEGroupKey,
        MouthOGroupKey,
    };

    private static readonly FacialShapeGroup[] ManagedShapeGroups =
    {
        new FacialShapeGroup(
            MouthAGroupKey,
            new[] { "あ", "あ２", "a", "aa", "A" },
            new[] { "moutha", "mouthaa", "phonemea", "visemeaa", "vaa", "jawopen", "mouthopen" }),
        new FacialShapeGroup(
            MouthIGroupKey,
            new[] { "い", "i", "I" },
            new[] { "mouthi", "phonemei", "visemeih", "vih" }),
        new FacialShapeGroup(
            MouthUGroupKey,
            new[] { "う", "u", "U" },
            new[] { "mouthu", "phonemeu", "visemeou", "vou" }),
        new FacialShapeGroup(
            MouthEGroupKey,
            new[] { "え", "e", "E" },
            new[] { "mouthe", "phonemee", "visemee", "ve" }),
        new FacialShapeGroup(
            MouthOGroupKey,
            new[] { "お", "o", "O" },
            new[] { "moutho", "phonemeo", "visemeoh", "voh", "mouthround" }),
        new FacialShapeGroup(
            SmileGroupKey,
            new[] { "笑い", "smile", "Smile", "口角上げ" },
            new[] { "warai", "happy", "grin", "mouthsmile" }),
    };

    private static readonly string[] JawBoneTokens =
    {
        "jaw",
        "chin",
        "顎",
        "あご",
        "下顎",
        "下あご",
    };

    private static readonly string[] LipBoneTokens =
    {
        "mouth",
        "lip",
        "口",
        "くち",
        "唇",
    };

    private static readonly string[] ExcludedBoneTokens =
    {
        "eye",
        "瞳",
        "眉",
        "brow",
        "nose",
        "鼻",
        "hair",
        "髪",
        "ear",
        "耳",
        "cheek",
        "頬",
    };

    private const float SmileWeight = 10f;
    private const float MinimumMouthWeight = 4f;
    private const float MouthWeightSmoothing = 10f;
    private const float AudioAmplitudeScale = 28f;
    private const float JawOpenAngleDegrees = 10f;
    private const float JawOpenOffsetMeters = 0.005f;
    private const float LipOpenOffsetMeters = 0.0035f;
    private const int LoggedBlendShapeNamesPerRenderer = 12;
    private const float BlendShapeWeightChangeEpsilon = 0.1f;

    private readonly Dictionary<string, List<BlendShapeBinding>> _bindingsByGroupKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<BonePoseBinding> _jawBindings = new();
    private readonly List<BonePoseBinding> _lipBindings = new();
    private readonly float[] _audioSamples = new float[256];

    private AudioSource? _audioSource;
    private ManualLogSource? _logger;
    private bool _initialized;
    private float _currentMouthWeight;
    private string? _lastPrimaryShapeGroupKey;
    private string? _appliedPrimaryShapeGroupKey;
    private float _appliedPrimaryShapeWeight;
    private bool _smileWeightApplied;

    public void Initialize(AudioSource? audioSource, Animator? animator, ManualLogSource? logger)
    {
        enabled = true;
        _audioSource = audioSource;
        _logger = logger;
        _bindingsByGroupKey.Clear();
        _jawBindings.Clear();
        _lipBindings.Clear();
        ResetAppliedManagedShapeState();

        var rendererSummaries = new List<string>();
        foreach (var renderer in GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true))
        {
            var mesh = renderer.sharedMesh;
            if (mesh == null)
            {
                continue;
            }

            var sampledBlendShapeNames = new List<string>(Mathf.Min(mesh.blendShapeCount, LoggedBlendShapeNamesPerRenderer));
            for (var index = 0; index < mesh.blendShapeCount; index++)
            {
                var blendShapeName = mesh.GetBlendShapeName(index);
                if (sampledBlendShapeNames.Count < LoggedBlendShapeNamesPerRenderer)
                {
                    sampledBlendShapeNames.Add(string.IsNullOrWhiteSpace(blendShapeName) ? $"#{index}" : blendShapeName);
                }

                if (string.IsNullOrWhiteSpace(blendShapeName) || !TryResolveManagedShapeGroup(blendShapeName, out var groupKey))
                {
                    continue;
                }

                if (!_bindingsByGroupKey.TryGetValue(groupKey, out var bindings))
                {
                    bindings = new List<BlendShapeBinding>();
                    _bindingsByGroupKey.Add(groupKey, bindings);
                }

                bindings.Add(new BlendShapeBinding(renderer, index, blendShapeName));
            }

            rendererSummaries.Add(
                $"{renderer.name}:{mesh.blendShapeCount}[{string.Join(", ", sampledBlendShapeNames)}{(mesh.blendShapeCount > sampledBlendShapeNames.Count ? ", ..." : string.Empty)}]");
        }

        DiscoverBoneBindings();
        _initialized = true;

        if (_bindingsByGroupKey.Count == 0 && _jawBindings.Count == 0 && _lipBindings.Count == 0)
        {
            enabled = false;
            LogVerbose(
                "Facial expression controller disabled because no supported blendshapes or mouth bones were found on the loaded model. " +
                $"Skinned renderers: {(rendererSummaries.Count == 0 ? "(none)" : string.Join(" | ", rendererSummaries))}.");
            return;
        }

        var discoveredGroups = _bindingsByGroupKey.Count == 0
            ? "(none)"
            : string.Join(", ", _bindingsByGroupKey.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
        var jawBoneSummary = _jawBindings.Count == 0
            ? "(none)"
            : string.Join(", ", _jawBindings.Select(binding => binding.Transform.name));
        var lipBoneSummary = _lipBindings.Count == 0
            ? "(none)"
            : string.Join(", ", _lipBindings.Select(binding => binding.Transform.name));
        LogVerbose(
            $"Facial expression controller initialized. blendshapeGroups=[{discoveredGroups}], jawBones=[{jawBoneSummary}], lipBones=[{lipBoneSummary}], " +
            $"skinnedRenderers={(rendererSummaries.Count == 0 ? "(none)" : string.Join(" | ", rendererSummaries))}.");

        ResetManagedWeights();
        ApplySmileWeight();
    }

    private void LateUpdate()
    {
        if (!_initialized)
        {
            return;
        }

        var mouthWeight = ResolveMouthWeight();
        var primaryShapeGroupKey = ResolvePrimaryMouthShapeGroupKey(mouthWeight);

        UpdateManagedWeights(primaryShapeGroupKey, mouthWeight);
        ApplySmileWeight();
        ApplyBoneMouthPose(mouthWeight);
        _lastPrimaryShapeGroupKey = primaryShapeGroupKey;
    }

    private float ResolveMouthWeight()
    {
        var targetWeight = 0f;
        if (_audioSource != null && _audioSource.clip != null && _audioSource.isPlaying)
        {
            try
            {
                _audioSource.GetOutputData(_audioSamples, 0);
                var sumSquares = 0f;
                for (var i = 0; i < _audioSamples.Length; i++)
                {
                    sumSquares += _audioSamples[i] * _audioSamples[i];
                }

                var rms = Mathf.Sqrt(sumSquares / _audioSamples.Length);
                targetWeight = Mathf.Clamp(rms * AudioAmplitudeScale * 100f, 0f, 100f);
            }
            catch (Exception exception)
            {
                _logger?.LogWarning($"Facial expression audio sampling failed: {exception.Message}");
            }
        }

        _currentMouthWeight = Mathf.Lerp(_currentMouthWeight, targetWeight, Time.deltaTime * MouthWeightSmoothing);
        return _currentMouthWeight;
    }

    private string? ResolvePrimaryMouthShapeGroupKey(float mouthWeight)
    {
        if (mouthWeight <= MinimumMouthWeight)
        {
            return null;
        }

        foreach (var candidate in MouthShapePriority)
        {
            if (_bindingsByGroupKey.ContainsKey(candidate))
            {
                return candidate;
            }
        }

        return _lastPrimaryShapeGroupKey;
    }

    private void ResetManagedWeights()
    {
        foreach (var bindings in _bindingsByGroupKey.Values)
        {
            foreach (var binding in bindings)
            {
                binding.Renderer.SetBlendShapeWeight(binding.Index, 0f);
            }
        }
    }

    private void ApplySmileWeight()
    {
        if (_smileWeightApplied || !_bindingsByGroupKey.ContainsKey(SmileGroupKey))
        {
            return;
        }

        ApplyWeight(SmileGroupKey, SmileWeight);
        _smileWeightApplied = true;
    }

    private void ApplyWeight(string groupKey, float weight)
    {
        if (!_bindingsByGroupKey.TryGetValue(groupKey, out var bindings))
        {
            return;
        }

        for (var index = 0; index < bindings.Count; index++)
        {
            bindings[index].Renderer.SetBlendShapeWeight(bindings[index].Index, weight);
        }
    }

    private void UpdateManagedWeights(string? primaryShapeGroupKey, float mouthWeight)
    {
        var targetGroupKey = !string.IsNullOrWhiteSpace(primaryShapeGroupKey) && mouthWeight > MinimumMouthWeight
            ? primaryShapeGroupKey
            : null;
        var targetWeight = targetGroupKey == null ? 0f : mouthWeight;

        if (!string.IsNullOrWhiteSpace(_appliedPrimaryShapeGroupKey)
            && !string.Equals(_appliedPrimaryShapeGroupKey, targetGroupKey, StringComparison.OrdinalIgnoreCase))
        {
            ApplyWeight(_appliedPrimaryShapeGroupKey!, 0f);
            _appliedPrimaryShapeGroupKey = null;
            _appliedPrimaryShapeWeight = 0f;
        }

        if (string.IsNullOrWhiteSpace(targetGroupKey))
        {
            if (!string.IsNullOrWhiteSpace(_appliedPrimaryShapeGroupKey) && _appliedPrimaryShapeWeight > 0.001f)
            {
                ApplyWeight(_appliedPrimaryShapeGroupKey!, 0f);
            }

            _appliedPrimaryShapeGroupKey = null;
            _appliedPrimaryShapeWeight = 0f;
            return;
        }

        if (!string.Equals(_appliedPrimaryShapeGroupKey, targetGroupKey, StringComparison.OrdinalIgnoreCase)
            || Mathf.Abs(_appliedPrimaryShapeWeight - targetWeight) > BlendShapeWeightChangeEpsilon)
        {
            ApplyWeight(targetGroupKey!, targetWeight);
            _appliedPrimaryShapeGroupKey = targetGroupKey;
            _appliedPrimaryShapeWeight = targetWeight;
        }
    }

    private void ResetAppliedManagedShapeState()
    {
        _appliedPrimaryShapeGroupKey = null;
        _appliedPrimaryShapeWeight = 0f;
        _smileWeightApplied = false;
    }

    private void ApplyBoneMouthPose(float mouthWeight)
    {
        var openAmount = Mathf.Clamp01((mouthWeight - MinimumMouthWeight) / (100f - MinimumMouthWeight));

        for (var index = 0; index < _jawBindings.Count; index++)
        {
            var binding = _jawBindings[index];
            binding.Transform.localRotation = binding.ClosedLocalRotation * Quaternion.Euler(-JawOpenAngleDegrees * openAmount, 0f, 0f);
            binding.Transform.localPosition = binding.ClosedLocalPosition + ResolveLocalDownDirection(binding.Transform) * (JawOpenOffsetMeters * openAmount);
        }

        for (var index = 0; index < _lipBindings.Count; index++)
        {
            var binding = _lipBindings[index];
            binding.Transform.localRotation = binding.ClosedLocalRotation;
            binding.Transform.localPosition = binding.ClosedLocalPosition + ResolveLocalDownDirection(binding.Transform) * (LipOpenOffsetMeters * openAmount);
        }
    }

    private void DiscoverBoneBindings()
    {
        var excludedTransforms = new HashSet<Transform>();
        AddTopBoneBindings(_jawBindings, JawBoneTokens, excludedTransforms, maxCount: 2);
        foreach (var binding in _jawBindings)
        {
            excludedTransforms.Add(binding.Transform);
        }

        AddTopBoneBindings(_lipBindings, LipBoneTokens, excludedTransforms, maxCount: 2);
    }

    private void AddTopBoneBindings(ICollection<BonePoseBinding> destination, IEnumerable<string> tokens, ISet<Transform> excludedTransforms, int maxCount)
    {
        var candidates = GetComponentsInChildren<Transform>(includeInactive: true)
            .Where(candidate => candidate != transform)
            .Where(candidate => !excludedTransforms.Contains(candidate))
            .Select(candidate => new BoneCandidate(candidate, ScoreBoneCandidate(candidate.name, tokens)))
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Transform.name.Length)
            .ThenBy(candidate => candidate.Transform.name, StringComparer.OrdinalIgnoreCase)
            .Take(maxCount)
            .ToArray();

        for (var index = 0; index < candidates.Length; index++)
        {
            destination.Add(new BonePoseBinding(
                candidates[index].Transform,
                candidates[index].Transform.localPosition,
                candidates[index].Transform.localRotation));
        }
    }

    private void LogVerbose(string message)
    {
        if (Core.VerboseLogState.Enabled)
        {
            _logger?.LogInfo(message);
        }
    }

    private static int ScoreBoneCandidate(string candidateName, IEnumerable<string> tokens)
    {
        var normalizedName = NormalizeName(candidateName);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return 0;
        }

        if (ExcludedBoneTokens.Any(token => normalizedName.IndexOf(NormalizeName(token), StringComparison.Ordinal) >= 0))
        {
            return 0;
        }

        var score = 0;
        foreach (var token in tokens)
        {
            var normalizedToken = NormalizeName(token);
            if (normalizedName == normalizedToken)
            {
                score = Mathf.Max(score, 300);
                continue;
            }

            if (normalizedName.EndsWith(normalizedToken, StringComparison.Ordinal))
            {
                score = Mathf.Max(score, 220);
                continue;
            }

            if (normalizedName.IndexOf(normalizedToken, StringComparison.Ordinal) >= 0)
            {
                score = Mathf.Max(score, 160);
            }
        }

        return score;
    }

    private static bool TryResolveManagedShapeGroup(string blendShapeName, out string groupKey)
    {
        var normalizedName = NormalizeName(blendShapeName);
        for (var index = 0; index < ManagedShapeGroups.Length; index++)
        {
            if (ManagedShapeGroups[index].Matches(blendShapeName, normalizedName))
            {
                groupKey = ManagedShapeGroups[index].Key;
                return true;
            }
        }

        groupKey = string.Empty;
        return false;
    }

    private static string NormalizeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var buffer = new char[value.Length];
        var count = 0;
        for (var index = 0; index < value.Length; index++)
        {
            var character = char.ToLowerInvariant(value[index]);
            if (char.IsWhiteSpace(character)
                || character == '_'
                || character == '-'
                || character == '.'
                || character == '/'
                || character == '\\'
                || character == '('
                || character == ')'
                || character == '['
                || character == ']'
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

    private static Vector3 ResolveLocalDownDirection(Transform transform)
    {
        if (transform.parent == null)
        {
            return Vector3.down;
        }

        var localDown = transform.parent.InverseTransformDirection(Vector3.down);
        return localDown.sqrMagnitude > 0.0001f
            ? localDown.normalized
            : Vector3.down;
    }

    private readonly struct BlendShapeBinding
    {
        public BlendShapeBinding(SkinnedMeshRenderer renderer, int index, string name)
        {
            Renderer = renderer;
            Index = index;
            Name = name;
        }

        public SkinnedMeshRenderer Renderer { get; }
        public int Index { get; }
        public string Name { get; }
    }

    private readonly struct FacialShapeGroup
    {
        public FacialShapeGroup(string key, string[] exactAliases, string[] partialAliases)
        {
            Key = key;
            ExactAliases = exactAliases;
            PartialAliases = partialAliases;
        }

        public string Key { get; }
        public string[] ExactAliases { get; }
        public string[] PartialAliases { get; }

        public bool Matches(string rawName, string normalizedName)
        {
            for (var index = 0; index < ExactAliases.Length; index++)
            {
                var normalizedAlias = NormalizeName(ExactAliases[index]);
                if (string.Equals(rawName, ExactAliases[index], StringComparison.OrdinalIgnoreCase)
                    || string.Equals(normalizedName, normalizedAlias, StringComparison.Ordinal)
                    || (!string.IsNullOrWhiteSpace(normalizedAlias)
                        && normalizedName.EndsWith(normalizedAlias, StringComparison.Ordinal)))
                {
                    return true;
                }
            }

            for (var index = 0; index < PartialAliases.Length; index++)
            {
                var token = NormalizeName(PartialAliases[index]);
                if (!string.IsNullOrWhiteSpace(token)
                    && normalizedName.IndexOf(token, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private readonly struct BoneCandidate
    {
        public BoneCandidate(Transform transform, int score)
        {
            Transform = transform;
            Score = score;
        }

        public Transform Transform { get; }
        public int Score { get; }
    }

    private readonly struct BonePoseBinding
    {
        public BonePoseBinding(Transform transform, Vector3 closedLocalPosition, Quaternion closedLocalRotation)
        {
            Transform = transform;
            ClosedLocalPosition = closedLocalPosition;
            ClosedLocalRotation = closedLocalRotation;
        }

        public Transform Transform { get; }
        public Vector3 ClosedLocalPosition { get; }
        public Quaternion ClosedLocalRotation { get; }
    }
}
