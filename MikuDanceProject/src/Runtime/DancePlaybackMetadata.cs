using System;
using UnityEngine;

namespace MikuDanceProject.Runtime;

[Serializable]
internal sealed class DancePlaybackMetadata
{
    public float SourceFrameRate = 30f;
    public int MotionStartFrame;
    public bool HasMotionEndFrame;
    public int MotionEndFrame;
    public float AudioSegmentStartSeconds;
    public float AudioSegmentDurationSeconds;

    public static DancePlaybackMetadata CreateDefault()
    {
        return new DancePlaybackMetadata
        {
            SourceFrameRate = 30f,
            MotionStartFrame = 0,
            HasMotionEndFrame = false,
            MotionEndFrame = 0,
            AudioSegmentStartSeconds = 0f,
            AudioSegmentDurationSeconds = 0f,
        };
    }

    public float ResolveAudioSegmentStartSeconds()
    {
        return Mathf.Max(0f, AudioSegmentStartSeconds);
    }

    public float ResolveMotionStartSeconds(float animationClipLength)
    {
        var safeClipLength = Mathf.Max(0f, animationClipLength);
        var frameRate = SourceFrameRate > 0.01f ? SourceFrameRate : 30f;
        var configuredStartSeconds = Mathf.Max(0f, MotionStartFrame) / frameRate;
        return Mathf.Clamp(configuredStartSeconds, 0f, safeClipLength);
    }

    public float ResolveMotionEndSeconds(float animationClipLength)
    {
        var safeClipLength = Mathf.Max(0f, animationClipLength);
        var startSeconds = ResolveMotionStartSeconds(safeClipLength);
        if (!HasMotionEndFrame || MotionEndFrame <= MotionStartFrame)
        {
            return safeClipLength;
        }

        var frameRate = SourceFrameRate > 0.01f ? SourceFrameRate : 30f;
        var configuredEndSeconds = Mathf.Max(MotionStartFrame, MotionEndFrame) / frameRate;
        return Mathf.Clamp(configuredEndSeconds, startSeconds, safeClipLength);
    }

    public float ResolveMotionSegmentDurationSeconds(float animationClipLength)
    {
        var safeClipLength = Mathf.Max(0f, animationClipLength);
        var startSeconds = ResolveMotionStartSeconds(safeClipLength);
        var endSeconds = ResolveMotionEndSeconds(safeClipLength);
        var segmentDuration = Mathf.Max(0f, endSeconds - startSeconds);
        return segmentDuration > 0.01f ? segmentDuration : safeClipLength;
    }

    public float ResolveAudioSegmentDurationSeconds(float animationClipLength, float audioClipLength)
    {
        var audioStart = ResolveAudioSegmentStartSeconds();
        var remainingAudioLength = Mathf.Max(0f, audioClipLength - audioStart);
        var configuredDuration = Mathf.Max(0f, AudioSegmentDurationSeconds);
        if (configuredDuration > 0.01f)
        {
            return Mathf.Min(configuredDuration, remainingAudioLength);
        }

        if (animationClipLength > 0.01f)
        {
            return Mathf.Min(animationClipLength, remainingAudioLength);
        }

        return remainingAudioLength;
    }
}
