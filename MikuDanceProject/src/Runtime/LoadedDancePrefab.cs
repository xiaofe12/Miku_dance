using UnityEngine;

namespace MikuDanceProject.Runtime;

internal sealed class LoadedDancePrefab
{
    public LoadedDancePrefab(
        GameObject template,
        AnimationClip? clip,
        AnimationClip? morphClip,
        DancePlaybackMetadata? playbackMetadata,
        float localMinY,
        float localHeight,
        Vector3 localBoundsCenter,
        LoadedPrefabSource source)
    {
        Template = template;
        Clip = clip;
        MorphClip = morphClip;
        PlaybackMetadata = playbackMetadata;
        LocalMinY = localMinY;
        LocalHeight = localHeight;
        LocalBoundsCenter = localBoundsCenter;
        Source = source;
    }

    public GameObject Template { get; }
    public AnimationClip? Clip { get; }
    public AnimationClip? MorphClip { get; }
    public DancePlaybackMetadata? PlaybackMetadata { get; }
    public float LocalMinY { get; }
    public float LocalHeight { get; }
    public Vector3 LocalBoundsCenter { get; }
    public LoadedPrefabSource Source { get; }
}

internal enum LoadedPrefabSource
{
    UnityAssetBundle = 1,
}
