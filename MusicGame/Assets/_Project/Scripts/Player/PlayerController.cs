using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [SerializeField] private GameplayConfig config;
    [SerializeField] private AudioSource    audioSource;

    private CharacterController _cc;
    private float _verticalVel;
    private float _targetX;
    private float _zOffset;    // surge offset: builds to maxSurge, springs back to 0
    private float _surgeVel;   // for SmoothDamp spring return
    private float _startZ;
    private float _startTime;
    private bool  _running;

    public float CanonicalZ => _startZ + (SongTime * config.playerSpeed);

    private float SongTime => (audioSource != null && audioSource.isPlaying)
        ? config.warmupTime + audioSource.time
        : Time.time - _startTime;

    public bool IsRunning => _running;

    public void StartRunning(float startTime)
    {
        _startZ    = transform.position.z;
        _startTime = startTime;
        _running   = true;
    }

    public void StopRunning() => _running = false;

    private void Awake()
    {
        _cc = GetComponent<CharacterController>();
        BuildVisual();
    }

    private void BuildVisual()
    {
        var cap = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        cap.transform.SetParent(transform);
        cap.transform.localPosition = new Vector3(0f, 0.75f, 0f);
        cap.transform.localScale    = new Vector3(0.7f, 0.75f, 0.7f);

        // Remove the capsule collider — CharacterController handles physics
        Destroy(cap.GetComponent<CapsuleCollider>());

        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
            { color = new Color(0.25f, 0.65f, 1f) };
        cap.GetComponent<MeshRenderer>().material = mat;
        cap.name = "Visual";
    }

    private void Update()
    {
        if (!_running || config == null) return;
        HandleStrafe();
        HandleJump();
        Move();
    }

    private void HandleStrafe()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        float dir = 0f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  dir -= 1f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) dir += 1f;

        float halfTrack = config.laneWidth * (config.laneCount / 2f);
        _targetX = Mathf.Clamp(_targetX + dir * config.strafeSpeed * Time.deltaTime, -halfTrack, halfTrack);
    }

    private void HandleJump()
    {
        var kb = Keyboard.current;
        if (_cc.isGrounded)
        {
            _verticalVel = config.gravity * Time.deltaTime;
            if (kb != null && kb.spaceKey.wasPressedThisFrame)
                _verticalVel = config.jumpForce;
        }
        else
        {
            _verticalVel += config.gravity * Time.deltaTime;
        }
    }

    private void Move()
    {
        var  kb      = Keyboard.current;
        bool surging = kb != null && (kb.wKey.isPressed || kb.upArrowKey.isPressed);

        // Surge: build up on W held, spring back smoothly on release (rubber-band)
        if (surging)
        {
            _surgeVel = 0f;
            _zOffset  = Mathf.MoveTowards(_zOffset, config.maxSurge, config.surgeSpeed * Time.deltaTime);
        }
        else
        {
            _zOffset = Mathf.SmoothDamp(_zOffset, 0f, ref _surgeVel, 0.4f);
        }

        // Song-locked Z + surge offset
        float targetZ = CanonicalZ + _zOffset;
        float deltaZ  = targetZ - transform.position.z;

        // Smooth lateral movement
        float newX   = Mathf.Lerp(transform.position.x, _targetX, config.strafeLerp * Time.deltaTime);
        float deltaX = newX - transform.position.x;

        _cc.Move(new Vector3(deltaX, _verticalVel * Time.deltaTime, deltaZ));
    }
}
