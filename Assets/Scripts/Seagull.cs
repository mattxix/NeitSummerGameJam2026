using UnityEngine;

/// <summary>
/// One seagull. Flies in from its spawn side, holds at punch range for a beat,
/// then dives at the sandwich. If it reaches the sandwich it steals it and flies off.
/// If punched first it gets knocked back and despawns.
///
/// Put this on the ROOT of your seagull prefab. The animated model should be a CHILD
/// of that root, so the idle animation and this script never fight over the transform.
/// Spawned and driven by SeagullSystem.
/// </summary>
public class Seagull : MonoBehaviour
{
    public enum GullState { Approach, Hover, Dive, Stealing, Knocked }

    [Header("Movement")]
    public float approachSpeed = 2.5f;
    public float diveSpeed = 4.5f;
    public float escapeSpeed = 6f;
    [Tooltip("How fast it swings around to face where it's going.")]
    public float turnSpeed = 6f;

    [Header("Behaviour")]
    [Tooltip("How far from the sandwich it holds before committing to the dive.")]
    public float hoverDistance = 2.5f;
    [Tooltip("Height above the sandwich it holds at.")]
    public float hoverHeight = 1.2f;
    [Tooltip("Seconds it waits in punch range. SeagullSystem overrides this per level.")]
    public float hoverDuration = 1.8f;
    [Tooltip("How close it must get to grab the sandwich.")]
    public float grabDistance = 0.3f;

    [Header("Hover Bob")]
    public float bobSpeed = 3f;
    public float bobAmount = 0.12f;

    [Header("Knockback")]
    public float knockbackSpeed = 8f;
    public float knockbackSpin = 540f;
    public float despawnAfterKnock = 1.5f;

    [Header("Carrying")]
    [Tooltip("Empty child transform at the beak. The stolen sandwich parents here. " +
             "Leave empty to use the root, which will look wrong on most models.")]
    public Transform carryPoint;

    public GullState State { get; private set; } = GullState.Approach;

    /// <summary>Which spawn point it came from. Drives which warning icon shows.</summary>
    public bool SpawnedOnLeft { get; private set; }

    /// <summary>Can still be punched out of the sky, and is still a threat.</summary>
    public bool IsPunchable =>
        State == GullState.Approach || State == GullState.Hover || State == GullState.Dive;

    private SandwichEater eater;
    private Transform sandwich;
    private Vector3 hoverPoint;
    private float hoverTimer;
    private Vector3 driftDir;
    private float driftTimer;
    private float currentSpin;

    /// <summary>Called by SeagullSystem right after Instantiate.</summary>
    public void Init(SandwichEater owner, float hoverSeconds, bool fromLeft)
    {
        eater = owner;
        hoverDuration = hoverSeconds;
        SpawnedOnLeft = fromLeft;
        sandwich = owner != null ? owner.SandwichPoint : null;

        hoverTimer = 0f;
        State = GullState.Approach;
        RecalculateHoverPoint();
    }

    private void Update()
    {
        // If the round ended for any other reason, clear off.
        if (IsPunchable && eater != null && eater.LevelOver)
        {
            Leave();
            return;
        }

        switch (State)
        {
            case GullState.Approach: TickApproach(); break;
            case GullState.Hover: TickHover(); break;
            case GullState.Dive: TickDive(); break;
            case GullState.Stealing: TickDrift(escapeSpeed, false); break;
            case GullState.Knocked: TickDrift(knockbackSpeed, true); break;
        }
    }

    // ---- States ----

    private void TickApproach()
    {
        MoveToward(hoverPoint, approachSpeed);

        if (Vector3.Distance(transform.position, hoverPoint) < 0.2f)
        {
            hoverTimer = 0f;
            State = GullState.Hover;
        }
    }

    private void TickHover()
    {
        Vector3 bobbed = hoverPoint + Vector3.up * (Mathf.Sin(Time.time * bobSpeed) * bobAmount);
        transform.position = Vector3.MoveTowards(transform.position, bobbed, approachSpeed * Time.deltaTime);

        if (sandwich != null) FaceTowards(sandwich.position - transform.position);

        hoverTimer += Time.deltaTime;
        if (hoverTimer >= hoverDuration) State = GullState.Dive;
    }

    private void TickDive()
    {
        if (sandwich == null)
        {
            Leave();
            return;
        }

        MoveToward(sandwich.position, diveSpeed);

        if (Vector3.Distance(transform.position, sandwich.position) <= grabDistance)
            TryGrab();
    }

    private void TickDrift(float speed, bool spin)
    {
        transform.position += driftDir * (speed * Time.deltaTime);

        if (spin)
        {
            transform.Rotate(Vector3.forward, currentSpin * Time.deltaTime, Space.Self);
            driftTimer += Time.deltaTime;
            if (driftTimer >= despawnAfterKnock) Destroy(gameObject);
        }
        else
        {
            FaceTowards(driftDir);
        }
    }

    // ---- Actions ----

    private void TryGrab()
    {
        if (eater != null && eater.SandwichIsStealable)
        {
            // Camera locks onto the root; the sandwich parents to the beak.
            eater.LoseSandwich(transform, carryPoint != null ? carryPoint : transform);
            BeginDrift(AwayFromSandwich(0.6f));
            currentSpin = 0f;
            State = GullState.Stealing;
        }
        else
        {
            // Nothing to take -- sandwich is at the player's mouth, or the round already ended.
            Leave();
        }
    }

    /// <summary>Knocked out of the sky. Called by SeagullSystem.</summary>
    public void Punch(Vector3 fromPosition)
    {
        if (!IsPunchable) return;

        Vector3 away = transform.position - fromPosition;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = transform.forward;

        BeginDrift((away.normalized + Vector3.up * 0.5f).normalized);
        currentSpin = knockbackSpin;
        State = GullState.Knocked;
    }

    /// <summary>Give up and fly off without stealing anything.</summary>
    public void Leave()
    {
        if (State == GullState.Stealing) return;

        BeginDrift(AwayFromSandwich(1f));
        currentSpin = 0f;
        State = GullState.Knocked;
    }

    // ---- Helpers ----

    private Vector3 AwayFromSandwich(float rise)
    {
        Vector3 away = sandwich != null ? transform.position - sandwich.position : transform.forward;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = transform.forward;
        return (away.normalized + Vector3.up * rise).normalized;
    }

    private void BeginDrift(Vector3 dir)
    {
        driftDir = dir;
        driftTimer = 0f;
    }

    private void RecalculateHoverPoint()
    {
        if (sandwich == null)
        {
            hoverPoint = transform.position;
            return;
        }

        Vector3 flat = transform.position - sandwich.position;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.01f) flat = Vector3.right;

        hoverPoint = sandwich.position + flat.normalized * hoverDistance + Vector3.up * hoverHeight;
    }

    private void MoveToward(Vector3 point, float speed)
    {
        Vector3 before = transform.position;
        transform.position = Vector3.MoveTowards(before, point, speed * Time.deltaTime);
        FaceTowards(point - before);
    }

    private void FaceTowards(Vector3 dir)
    {
        if (dir.sqrMagnitude < 0.0001f) return;
        Quaternion want = Quaternion.LookRotation(dir, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, want, turnSpeed * Time.deltaTime);
    }
}