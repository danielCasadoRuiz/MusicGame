using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Plays arbitrary AnimationClips on an avatar's existing Animator through a small PlayableGraph
/// (clip -> 2-input mixer for cross-fades -> Animator), with explicit time control: loop on/off
/// independent of the clip's own loop flag, speed (0.5x / 1x ...), pause and scrubbing.
///
/// Debug/browsing only (the combat animation library in the avatar debug scene) — no state machine.
/// It never rebuilds the avatar, never calls Animator.Rebind and never touches the body renderers, so
/// Gender / Weight / Muscle morphs keep working while a clip plays. Root motion is not applied.
/// </summary>
public sealed class AvatarClipPlayer : System.IDisposable
{
    private readonly Animator _animator;
    private PlayableGraph _graph;
    private AnimationMixerPlayable _mixer;
    private AnimationClipPlayable _current, _previous;
    private float _previousTime, _fade, _fadeDuration;

    public AnimationClip Clip { get; private set; }
    /// <summary>Current time inside the clip (seconds, 0..length).</summary>
    public float Time { get; private set; }
    /// <summary>Total playing time since Play (never wraps) — lets tests prove playback never restarted.</summary>
    public float TotalTime { get; private set; }
    public bool Loop { get; set; }
    public float Speed { get; set; } = 1f;
    public bool Paused { get; set; }
    public bool IsValid => _graph.IsValid();
    public float Length => Clip != null ? Clip.length : 0f;
    public float NormalizedTime => Length > 0f ? Time / Length : 0f;
    public bool IsFinished => Clip != null && !Loop && Time >= Length;

    public AvatarClipPlayer(Animator animator)
    {
        _animator = animator;
        _graph = PlayableGraph.Create($"AvatarClipPlayer_{animator.name}");
        _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var output = AnimationPlayableOutput.Create(_graph, "Animation", animator);
        _mixer = AnimationMixerPlayable.Create(_graph, 2);
        output.SetSourcePlayable(_mixer);
        _graph.Play();
    }

    public Animator Animator => _animator;

    /// <summary>Starts `clip` from its beginning, cross-fading from whatever was playing.</summary>
    public void Play(AnimationClip clip, bool loop, float crossFade = 0.15f, bool footIK = true)
    {
        if (!IsValid || clip == null) return;

        if (_previous.IsValid())
        {
            _graph.Disconnect(_mixer, 1);
            _previous.Destroy();
        }
        if (_current.IsValid())
        {
            _graph.Disconnect(_mixer, 0);
            _previous = _current;
            _previousTime = Time;
            _graph.Connect(_previous, 0, _mixer, 1);
        }

        _current = AnimationClipPlayable.Create(_graph, clip);
        _current.SetApplyFootIK(footIK);
        _graph.Connect(_current, 0, _mixer, 0);

        Clip = clip;
        Loop = loop;
        Time = 0f;
        TotalTime = 0f;
        _fade = 0f;
        _fadeDuration = _previous.IsValid() ? Mathf.Max(0f, crossFade) : 0f;
        Evaluate(0f);
    }

    /// <summary>Jumps to `time` (seconds) in the current clip.</summary>
    public void Seek(float time)
    {
        Time = Mathf.Clamp(time, 0f, Length);
        Evaluate(0f);
    }

    /// <summary>Advances by `deltaTime` * Speed (0 while paused) and poses the avatar.</summary>
    public void Tick(float deltaTime) => Evaluate(Paused ? 0f : deltaTime * Speed);

    private void Evaluate(float step)
    {
        if (!IsValid || !_current.IsValid()) return;

        float length = Length;
        TotalTime += step;
        Time += step;
        Time = Loop && length > 0f ? Mathf.Repeat(Time, length) : Mathf.Min(Time, length);
        _current.SetTime(Time);

        if (_previous.IsValid())
        {
            _previousTime += step;
            var previousClip = _previous.GetAnimationClip();
            if (previousClip != null) _previous.SetTime(Mathf.Repeat(_previousTime, Mathf.Max(previousClip.length, 1e-4f)));
            _fade += Mathf.Abs(step);
            float w = _fadeDuration > 0f ? Mathf.Clamp01(_fade / _fadeDuration) : 1f;
            _mixer.SetInputWeight(0, w);
            _mixer.SetInputWeight(1, 1f - w);
            if (w >= 1f)
            {
                _graph.Disconnect(_mixer, 1);
                _previous.Destroy();
            }
        }
        else
        {
            _mixer.SetInputWeight(0, 1f);
            _mixer.SetInputWeight(1, 0f);
        }

        _graph.Evaluate(0f);
    }

    public void Dispose()
    {
        if (_graph.IsValid()) _graph.Destroy();
        Clip = null;
    }
}
