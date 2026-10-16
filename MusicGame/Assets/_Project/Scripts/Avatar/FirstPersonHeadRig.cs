using System.Linq;
using UnityEngine;

/// <summary>
/// First-person EYE PIVOT of a Humanoid avatar, shared by the Runner camera (CameraFollow) and the Fight camera
/// (FightCameraController): a child Transform of the avatar's Head bone ("FirstPersonEyePivot"), so the view
/// moves and turns EXACTLY with the head — every step, lunge, spin, hit and fall — with no follow lag.
///
/// Position: the midpoint of the Humanoid LeftEye/RightEye bones when the rig maps them; otherwise (the
/// MakeHuman game-engine rig has no eye bones — see MakeHumanHumanoid.BoneMap) the Head bone pivot plus an
/// offset UP/FORWARD (metres for the canonical 1.75 m body, scaled with the avatar: composers are scaled by
/// heightMeters/1.75). The forward offset puts the lens slightly IN FRONT of the eyes/face, so with the
/// camera's short near plane nothing of the own head is ever in view — the head is NOT hidden or collapsed:
/// it keeps rendering for every other view and its shadow stays, and body/arms are untouched.
/// Orientation: the Head bone axes closest to the avatar's facing/up when the pivot is created (MakeHuman
/// bone axes are arbitrary), snapped to whole axes so the animated pose at that moment never bakes a tilt.
///
/// Resolution is lazy (avatars build asynchronously) and self-healing: when the avatar is rebuilt (Animator
/// destroyed) the next Resolve finds the new one and creates its pivot. Presentation only.
/// </summary>
public sealed class FirstPersonHeadRig
{
    /// <summary>Eye height above the Head bone pivot for the canonical MakeHuman body (head pivot ≈ 1.58 m,
    /// eyes ≈ 1.64 m), metres before the avatar's own scale.</summary>
    public const float DefaultEyeUp = 0.06f;
    /// <summary>Lens in front of the Head bone pivot (canonical body): the eyes are ≈ 0.09 m forward, the lens
    /// sits just in front of the face (nose ≈ 0.12 m) so the camera never sees the inside of the head.</summary>
    public const float DefaultEyeForward = 0.14f;

    public const string PivotName = "FirstPersonEyePivot";

    public Animator Animator { get; private set; }
    public Transform Head { get; private set; }
    /// <summary>The eye pivot (child of Head): the first-person camera takes its position (and, in the Runner,
    /// its rotation) every frame.</summary>
    public Transform Pivot { get; private set; }
    public bool IsValid => Animator != null && Head != null && Pivot != null;
    /// <summary>True when the rig maps LeftEye/RightEye (pivot placed between them), false = head + offset.</summary>
    public bool UsesEyeBones { get; private set; }

    private float _eyeUp = DefaultEyeUp, _eyeForward = DefaultEyeForward;

    /// <summary>Finds the Humanoid Animator under <paramref name="searchRoot"/>, its Head bone, and creates the eye
    /// pivot. Cheap when already resolved; re-resolves if the avatar was destroyed. Changed offsets re-place
    /// the pivot. Returns IsValid.</summary>
    public bool Resolve(Transform searchRoot, float eyeUp = DefaultEyeUp, float eyeForward = DefaultEyeForward)
    {
        if (IsValid)
        {
            if (!Mathf.Approximately(eyeUp, _eyeUp) || !Mathf.Approximately(eyeForward, _eyeForward))
            {
                _eyeUp = eyeUp; _eyeForward = eyeForward;
                PlacePivot();
            }
            return true;
        }
        Release();
        _eyeUp = eyeUp; _eyeForward = eyeForward;
        if (searchRoot == null) return false;

        var animator = searchRoot.GetComponentInChildren<Animator>();
        if (animator == null || !animator.isHuman) return false;
        var head = animator.GetBoneTransform(HumanBodyBones.Head);
        if (head == null) return false;

        Animator = animator;
        Head = head;
        var existing = head.Find(PivotName);
        Pivot = existing != null ? existing : new GameObject(PivotName).transform;
        Pivot.SetParent(head, false);
        PlacePivot();
        return true;
    }

    /// <summary>World-space eye point (the pivot) — valid only when IsValid.</summary>
    public Vector3 EyePosition => IsValid ? Pivot.position : Vector3.zero;

    /// <summary>World Y of the avatar's feet (its Animator root sits on the floor).</summary>
    public float FeetY => IsValid ? Animator.transform.position.y : 0f;

    /// <summary>Destroys the pivot (if the avatar still exists) and forgets the avatar.</summary>
    public void Release()
    {
        if (Pivot != null) { if (Application.isPlaying) Object.Destroy(Pivot.gameObject); else Object.DestroyImmediate(Pivot.gameObject); }
        Pivot = null;
        Animator = null;
        Head = null;
        UsesEyeBones = false;
    }

    private void PlacePivot()
    {
        Transform root = Animator.transform;
        Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward, Vector3.left, Vector3.down, Vector3.back };
        Vector3 fwdLocal = axes.OrderByDescending(a => Vector3.Dot(Head.rotation * a, root.forward)).First();
        Vector3 upLocal = axes.Where(a => Mathf.Abs(Vector3.Dot(a, fwdLocal)) < 0.5f)
                              .OrderByDescending(a => Vector3.Dot(Head.rotation * a, root.up)).First();
        Pivot.localRotation = Quaternion.LookRotation(fwdLocal, upLocal);

        float scale = root.lossyScale.y;
        var leftEye = Animator.GetBoneTransform(HumanBodyBones.LeftEye);
        var rightEye = Animator.GetBoneTransform(HumanBodyBones.RightEye);
        UsesEyeBones = leftEye != null && rightEye != null;
        Vector3 forwardWorld = Head.rotation * fwdLocal, upWorld = Head.rotation * upLocal;
        Pivot.position = UsesEyeBones
            ? (leftEye.position + rightEye.position) * 0.5f + forwardWorld * ((_eyeForward - 0.09f) * scale)
            : Head.position + (upWorld * _eyeUp + forwardWorld * _eyeForward) * scale;
    }
}
