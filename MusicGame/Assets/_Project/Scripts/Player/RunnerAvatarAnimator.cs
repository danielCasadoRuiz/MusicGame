using UnityEngine;

/// <summary>
/// Presentation-only animation driver for the Runner player's MakeHuman avatar. PlayerController
/// stays the single authority over the transform (lane/path movement); this only reads its state
/// and sets Animator parameters — root motion is always off, so the avatar can never move the player.
///
/// Runner controller (RunnerPlayer.controller): Idle (default) ⇄ Run on bool "Running".
/// Only Run/Idle exist today; jump, fall, hit and death have no suitable Humanoid clip yet, so
/// while airborne/falling the avatar keeps its current state (PROVISIONAL — add states here
/// when the clips exist).
/// </summary>
public class RunnerAvatarAnimator : MonoBehaviour
{
    public const string RunningParameter = "Running";
    private static readonly int RunningHash = Animator.StringToHash(RunningParameter);

    private Animator _animator;
    private PlayerController _player;

    public void Bind(Animator animator, RuntimeAnimatorController controller, PlayerController player)
    {
        _animator = animator;
        _player = player;
        if (_animator == null) return;
        _animator.applyRootMotion = false;
        if (controller != null) _animator.runtimeAnimatorController = controller;
        if (!_animator.isHuman)
            Debug.LogWarning("[RunnerAvatarAnimator] Avatar Animator is not Humanoid — Runner clips won't retarget.");
        Update();
    }

    private void Update()
    {
        if (_animator == null || _player == null || _animator.runtimeAnimatorController == null) return;
        _animator.SetBool(RunningHash, _player.IsRunning && !_player.IsFalling);
    }
}
