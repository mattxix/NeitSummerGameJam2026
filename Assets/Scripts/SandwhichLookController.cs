using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Three-position first-person look controller, plus a cinematic lock-on mode.
/// A = turn fully left, D = turn fully right, S = look back down at the sandwich.
/// LockOnTo(target) overrides input and tracks a moving transform (used when a seagull
/// steals the sandwich).
/// Attach to the object holding the Camera (or the Camera itself).
/// </summary>
public class SandwichLookController : MonoBehaviour
{
    public enum LookState { Sandwich, Left, Right, LockedOn }

    [Header("View Angles (local Euler)")]
    [Tooltip("Head pointed at the sandwich. X = pitch down.")]
    public Vector3 sandwichAngles = new Vector3(55f, 0f, 0f);
    public Vector3 leftAngles = new Vector3(0f, -90f, 0f);
    public Vector3 rightAngles = new Vector3(0f, 90f, 0f);

    [Header("Motion")]
    [Tooltip("Degrees per second for normal A/D/S turns.")]
    public float turnSpeed = 220f;
    [Tooltip("Degrees per second for the lock-on pan. Slower reads as cinematic.")]
    public float lockOnTurnSpeed = 110f;

    [Header("Input")]
    public Key lookLeftKey = Key.A;
    public Key lookRightKey = Key.D;
    public Key lookAtSandwichKey = Key.S;

    public LookState CurrentState { get; private set; } = LookState.Sandwich;
    public bool IsTurning { get; private set; }
    public bool IsLockedOn => lockTarget != null;

    /// <summary>True only when settled on the sandwich and not locked onto anything.</summary>
    public bool IsLookingAtSandwich =>
        CurrentState == LookState.Sandwich && !IsTurning && !IsLockedOn;

    private Quaternion targetRotation;
    private Transform lockTarget;

    private void Awake()
    {
        targetRotation = Quaternion.Euler(sandwichAngles);
        transform.localRotation = targetRotation;
        CurrentState = LookState.Sandwich;
        IsTurning = false;
        lockTarget = null;
    }

    private void Update()
    {
        if (IsLockedOn)
        {
            TickLockOn();
            return;
        }

        ReadInput();
        RotateTowardTarget();
    }

    private void ReadInput()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb[lookLeftKey].wasPressedThisFrame)
            LookLeft();
        else if (kb[lookRightKey].wasPressedThisFrame)
            LookRight();
        else if (kb[lookAtSandwichKey].wasPressedThisFrame)
            LookAtSandwich();
    }

    // ---- Public API ----

    public void LookLeft()
    {
        if (IsLockedOn) return;
        SetTarget(LookState.Left, leftAngles);
    }

    public void LookRight()
    {
        if (IsLockedOn) return;
        SetTarget(LookState.Right, rightAngles);
    }

    public void LookAtSandwich()
    {
        if (IsLockedOn) return;
        if (CurrentState == LookState.Sandwich) return;
        SetTarget(LookState.Sandwich, sandwichAngles);
    }

    /// <summary>Pans to the target and keeps tracking it. Ignores A/D/S until released.</summary>
    public void LockOnTo(Transform target)
    {
        if (target == null) return;
        lockTarget = target;
        CurrentState = LookState.LockedOn;
        IsTurning = true;
    }

    /// <summary>Drops the lock and returns to the sandwich view.</summary>
    public void ReleaseLock()
    {
        lockTarget = null;
        SetTarget(LookState.Sandwich, sandwichAngles);
    }

    /// <summary>Direction the head is facing, for punch aiming.</summary>
    public Vector3 LookDirection => transform.forward;

    // ---- Internals ----

    private void SetTarget(LookState state, Vector3 euler)
    {
        CurrentState = state;
        targetRotation = Quaternion.Euler(euler);
        IsTurning = true;
    }

    private void RotateTowardTarget()
    {
        if (!IsTurning) return;

        transform.localRotation = Quaternion.RotateTowards(
            transform.localRotation,
            targetRotation,
            turnSpeed * Time.deltaTime);

        if (Quaternion.Angle(transform.localRotation, targetRotation) < 0.01f)
        {
            transform.localRotation = targetRotation;
            IsTurning = false;
        }
    }

    private void TickLockOn()
    {
        Vector3 toTarget = lockTarget.position - transform.position;
        if (toTarget.sqrMagnitude < 0.0001f) return;

        Quaternion world = Quaternion.LookRotation(toTarget, Vector3.up);
        Quaternion local = transform.parent != null
            ? Quaternion.Inverse(transform.parent.rotation) * world
            : world;

        transform.localRotation = Quaternion.RotateTowards(
            transform.localRotation,
            local,
            lockOnTurnSpeed * Time.deltaTime);
    }
}