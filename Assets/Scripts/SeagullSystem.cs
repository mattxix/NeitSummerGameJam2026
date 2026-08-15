using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Spawns seagulls from the left and right, scales difficulty with the level,
/// handles punching (left mouse), and drives the punch indicator.
///
/// The punch indicator shows only when you're looking left or right AND a gull is
/// actually in punch range. Facing the sandwich shows nothing -- the spawn sound is
/// your only cue that a bird is coming.
///
/// A connecting punch bursts feathers at the gull. The arm spawns at its Arm Origin,
/// flies to the target gull's transform and back, then despawns.
///
/// Attach to an empty GameObject in the scene.
/// </summary>
public class SeagullSystem : MonoBehaviour
{
    [Header("References")]
    public SandwichEater eater;
    public SandwichLookController lookController;
    public Camera playerCamera;
    public GameObject seagullPrefab;
    public Transform leftSpawn;
    public Transform rightSpawn;

    [Header("Spawning")]
    public float firstSpawnDelay = 6f;
    public float spawnInterval = 9f;
    public float intervalReductionPerLevel = 0.8f;
    public float minSpawnInterval = 3f;
    [Min(1)] public int baseMaxConcurrent = 1;
    [Tooltip("Gain one extra concurrent gull every N levels. 0 = never.")]
    public int extraGullEveryNLevels = 3;

    [Header("Spawn Sound")]
    [Tooltip("Played at the spawn point when a gull appears. Positional, so you hear which side.")]
    public AudioClip spawnSound;
    [Range(0f, 1f)] public float spawnSoundVolume = 1f;

    [Header("Difficulty")]
    public float hoverDuration = 1.8f;
    public float hoverReductionPerLevel = 0.12f;
    public float minHoverDuration = 0.5f;

    [Header("Arms")]
    public GameObject leftArmPrefab;
    public GameObject rightArmPrefab;
    [Tooltip("Fallback start point, used when a side-specific origin is missing.")]
    public Transform armOrigin;
    public Transform leftArmOrigin;
    public Transform rightArmOrigin;
    [Tooltip("Cap on travel distance so the arm can't stretch across the map.")]
    public float maxReach = 3f;

    [Header("Arm Orientation")]
    [Tooltip("Spin the hand around the punch axis until the back of the hand faces up.")]
    public float leftArmRoll = 0f;
    public float rightArmRoll = 0f;
    [Tooltip("Rescale the arm to this length in metres, whatever scale the prefab was saved at.")]
    public bool autoFitArmLength = true;
    public float desiredArmLength = 0.55f;
    [Tooltip("Off = work the fist's forward axis out from the mesh. On = use the axis below.")]
    public bool overridePunchAxis = false;
    public Vector3 punchAxisOverride = Vector3.forward;

    [Header("Punching")]
    public float punchOutDuration = 0.09f;
    public float punchReturnDuration = 0.18f;
    public float extraPunchCooldown = 0f;
    public float punchRange = 3.5f;
    [Range(5f, 90f)] public float punchAngle = 45f;

    [Header("Punch Impact")]
    [Tooltip("Feather burst prefab. Spawned at the gull when a punch connects.")]
    public GameObject feathersPrefab;
    [Tooltip("Nudge the burst toward the player so it isn't buried inside the model.")]
    public float feathersOffsetTowardPlayer = 0.1f;
    [Tooltip("Fallback lifetime, used only if the prefab has no ParticleSystem to measure.")]
    public float feathersLifetime = 3f;

    [Header("Punch Events")]
    [Tooltip("Fires on every click that throws a punch, hit or miss.")]
    public UnityEngine.Events.UnityEvent onPunchThrown;
    [Tooltip("Fires only when the punch actually connects with a gull.")]
    public UnityEngine.Events.UnityEvent onPunchLanded;

    [Header("Punch Indicator")]
    [Tooltip("Screen-space object shown while a gull is punchable. Hidden otherwise.")]
    public GameObject punchLabel;
    [Tooltip("Only show it while looking left or right, never while facing the sandwich.")]
    public bool requireSideLook = true;

    public int ActiveGullCount => activeGulls.Count;
    public bool CanPunch => arm == null && cooldownTimer <= 0f;

    private readonly List<Seagull> activeGulls = new List<Seagull>();
    private float spawnTimer;
    private bool wasLevelOver;

    private struct ArmFit { public Vector3 axis; public float scale; }
    private readonly Dictionary<GameObject, ArmFit> armFits = new Dictionary<GameObject, ArmFit>();

    private Transform arm;
    private Vector3 punchStart;
    private Vector3 punchEnd;
    private Quaternion punchRotation;
    private float punchTimer;
    private float cooldownTimer;

    private void Start()
    {
        if (eater == null) eater = FindObjectOfType<SandwichEater>();
        if (playerCamera == null) playerCamera = Camera.main;
        if (lookController == null && playerCamera != null)
            lookController = playerCamera.GetComponent<SandwichLookController>();

        if (seagullPrefab == null)
            Debug.LogWarning("SeagullSystem: no seagull prefab assigned.", this);
        if (leftSpawn == null || rightSpawn == null)
            Debug.LogWarning("SeagullSystem: assign Left Spawn and Right Spawn.", this);
        if (leftArmPrefab == null || rightArmPrefab == null)
            Debug.LogWarning("SeagullSystem: assign both arm prefabs.", this);
        if (armOrigin == null && (leftArmOrigin == null || rightArmOrigin == null))
            Debug.LogWarning("SeagullSystem: assign arm origins.", this);

        ResetForLevel();
    }

    private void Update()
    {
        PruneDeadGulls();

        bool levelOver = eater != null && eater.LevelOver;
        if (wasLevelOver && !levelOver) ResetForLevel();
        wasLevelOver = levelOver;

        if (!levelOver)
        {
            TickSpawning();
            TickPunchInput();
            TickPunchIndicator();
        }
        else
        {
            SetActive(punchLabel, false);
        }

        TickArm();
    }

    // ---- Level control ----

    public void ResetForLevel()
    {
        for (int i = activeGulls.Count - 1; i >= 0; i--)
            if (activeGulls[i] != null) Destroy(activeGulls[i].gameObject);

        activeGulls.Clear();
        spawnTimer = firstSpawnDelay;
        cooldownTimer = 0f;

        DespawnArm();
        SetActive(punchLabel, false);
    }

    // ---- Difficulty curves ----

    private int Level => eater != null ? Mathf.Max(1, eater.currentLevel) : 1;

    private float CurrentSpawnInterval =>
        Mathf.Max(minSpawnInterval, spawnInterval - intervalReductionPerLevel * (Level - 1));

    private float CurrentHoverDuration =>
        Mathf.Max(minHoverDuration, hoverDuration - hoverReductionPerLevel * (Level - 1));

    private int CurrentMaxConcurrent =>
        baseMaxConcurrent + (extraGullEveryNLevels > 0 ? (Level - 1) / extraGullEveryNLevels : 0);

    // ---- Spawning ----

    private void TickSpawning()
    {
        if (seagullPrefab == null || leftSpawn == null || rightSpawn == null) return;

        spawnTimer -= Time.deltaTime;
        if (spawnTimer > 0f) return;

        spawnTimer = CurrentSpawnInterval;

        if (activeGulls.Count >= CurrentMaxConcurrent) return;

        bool fromLeft = Random.value < 0.5f;
        Transform spawn = fromLeft ? leftSpawn : rightSpawn;
        GameObject go = Instantiate(seagullPrefab, spawn.position, spawn.rotation);

        Seagull gull = go.GetComponent<Seagull>();
        if (gull == null)
        {
            Debug.LogError("SeagullSystem: the seagull prefab has no Seagull component.", this);
            Destroy(go);
            return;
        }

        gull.Init(eater, CurrentHoverDuration, fromLeft);
        activeGulls.Add(gull);

        if (spawnSound != null)
            AudioSource.PlayClipAtPoint(spawnSound, spawn.position, spawnSoundVolume);
    }

    private void PruneDeadGulls()
    {
        for (int i = activeGulls.Count - 1; i >= 0; i--)
            if (activeGulls[i] == null) activeGulls.RemoveAt(i);
    }

    // ---- Punch indicator ----

    private void TickPunchIndicator()
    {
        if (punchLabel == null) return;
        SetActive(punchLabel, HasPunchableTarget());
    }

    private bool HasPunchableTarget()
    {
        if (requireSideLook && !IsLookingSideways()) return false;
        return FindPunchableTarget() != null;
    }

    private bool IsLookingSideways()
    {
        if (lookController == null) return true;

        return lookController.CurrentState == SandwichLookController.LookState.Left ||
               lookController.CurrentState == SandwichLookController.LookState.Right;
    }

    // ---- Punch input ----

    private void TickPunchInput()
    {
        if (cooldownTimer > 0f) cooldownTimer -= Time.deltaTime;

        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
        if (!CanPunch) return;

        Seagull target = FindPunchableTarget();
        ThrowPunch(UseLeftArm(), target);
        onPunchThrown?.Invoke();

        if (target != null && playerCamera != null)
        {
            target.Punch(playerCamera.transform.position);
            SpawnFeathers(target.transform.position);
            onPunchLanded?.Invoke();
        }
    }

    private Seagull FindPunchableTarget()
    {
        if (playerCamera == null) return null;

        Vector3 origin = playerCamera.transform.position;
        Vector3 forward = playerCamera.transform.forward;

        Seagull best = null;
        float bestDistance = float.MaxValue;

        foreach (Seagull gull in activeGulls)
        {
            if (gull == null || !gull.IsPunchable) continue;

            Vector3 toGull = gull.transform.position - origin;
            float distance = toGull.magnitude;
            if (distance > punchRange) continue;
            if (Vector3.Angle(forward, toGull) > punchAngle) continue;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = gull;
            }
        }

        return best;
    }

    private bool UseLeftArm()
    {
        return lookController != null &&
               lookController.CurrentState == SandwichLookController.LookState.Left;
    }

    // ---- Feather burst ----

    private void SpawnFeathers(Vector3 gullPosition)
    {
        if (feathersPrefab == null) return;

        Vector3 position = gullPosition;

        if (playerCamera != null && feathersOffsetTowardPlayer != 0f)
        {
            Vector3 toPlayer = playerCamera.transform.position - gullPosition;
            if (toPlayer.sqrMagnitude > 0.0001f)
                position += toPlayer.normalized * feathersOffsetTowardPlayer;
        }

        GameObject fx = Instantiate(feathersPrefab, position, Quaternion.identity);

        // Unparented, so it stays where the hit happened instead of riding the gull away.
        float life = feathersLifetime;

        ParticleSystem ps = fx.GetComponent<ParticleSystem>();
        if (ps == null) ps = fx.GetComponentInChildren<ParticleSystem>();

        if (ps != null)
        {
            ps.Play(true);
            ParticleSystem.MainModule main = ps.main;
            life = main.duration + main.startLifetime.constantMax;
        }

        Destroy(fx, Mathf.Max(life, 0.1f));
    }

    // ---- Arm ----

    private Transform OriginFor(bool left)
    {
        Transform specific = left ? leftArmOrigin : rightArmOrigin;
        return specific != null ? specific : armOrigin;
    }

    private void ThrowPunch(bool left, Seagull target)
    {
        DespawnArm();

        GameObject prefab = left ? leftArmPrefab : rightArmPrefab;
        if (prefab == null) prefab = left ? rightArmPrefab : leftArmPrefab;

        Transform origin = OriginFor(left);
        if (prefab == null || origin == null) return;

        punchStart = origin.position;

        Vector3 toTarget = target != null
            ? target.transform.position - punchStart
            : origin.forward * Mathf.Min(1f, maxReach);

        if (toTarget.sqrMagnitude < 0.0001f) toTarget = origin.forward;
        toTarget = Vector3.ClampMagnitude(toTarget, maxReach);
        punchEnd = punchStart + toTarget;

        ArmFit fit = GetArmFit(prefab);

        Vector3 aim = toTarget.normalized;
        Vector3 up = Mathf.Abs(Vector3.Dot(aim, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;

        punchRotation = Quaternion.LookRotation(aim, up)
                        * Quaternion.AngleAxis(left ? leftArmRoll : rightArmRoll, Vector3.forward)
                        * Quaternion.FromToRotation(fit.axis, Vector3.forward);

        GameObject go = Instantiate(prefab, punchStart, punchRotation, origin);
        arm = go.transform;

        if (autoFitArmLength) arm.localScale = Vector3.one * fit.scale;

        foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        punchTimer = 0f;
    }

    private void TickArm()
    {
        if (arm == null) return;

        punchTimer += Time.deltaTime;
        float total = punchOutDuration + punchReturnDuration;

        if (punchTimer >= total)
        {
            DespawnArm();
            cooldownTimer = extraPunchCooldown;
            return;
        }

        float t = punchTimer <= punchOutDuration
            ? (punchOutDuration <= 0f ? 1f : punchTimer / punchOutDuration)
            : 1f - (punchTimer - punchOutDuration) / Mathf.Max(punchReturnDuration, 0.0001f);

        arm.position = Vector3.Lerp(punchStart, punchEnd, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)));
        arm.rotation = punchRotation;
    }

    private void DespawnArm()
    {
        if (arm != null) Destroy(arm.gameObject);
        arm = null;
        punchTimer = 0f;
    }

    // ---- Arm fitting (once per prefab) ----

    private ArmFit GetArmFit(GameObject prefab)
    {
        if (armFits.TryGetValue(prefab, out ArmFit cached)) return cached;

        ArmFit fit = new ArmFit { axis = Vector3.forward, scale = 1f };

        if (overridePunchAxis)
        {
            fit.axis = punchAxisOverride.sqrMagnitude > 0.0001f
                ? punchAxisOverride.normalized
                : Vector3.forward;
        }

        if (TryGetLocalBounds(prefab.transform, out Bounds bounds))
        {
            Vector3 size = bounds.size;
            int axis = 0;
            if (size.y > size.x) axis = 1;
            if (size.z > size[axis]) axis = 2;

            if (!overridePunchAxis)
            {
                Vector3 dir = Vector3.zero;
                dir[axis] = bounds.center[axis] < 0f ? -1f : 1f;
                fit.axis = dir;
            }

            if (size[axis] > 0.0001f && desiredArmLength > 0.0001f)
                fit.scale = desiredArmLength / size[axis];
        }
        else
        {
            Debug.LogWarning($"SeagullSystem: no mesh on {prefab.name}, punch axis defaulting to +Z.", this);
        }

        armFits[prefab] = fit;
        return fit;
    }

    private static bool TryGetLocalBounds(Transform root, out Bounds bounds)
    {
        bounds = new Bounds();
        bool found = false;

        foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            Encapsulate(root, mf.transform, mf.sharedMesh.bounds, ref bounds, ref found);
        }

        foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (smr.sharedMesh == null) continue;
            Encapsulate(root, smr.transform, smr.sharedMesh.bounds, ref bounds, ref found);
        }

        return found;
    }

    private static void Encapsulate(Transform root, Transform child, Bounds meshBounds,
                                    ref Bounds bounds, ref bool found)
    {
        Vector3 c = meshBounds.center;
        Vector3 e = meshBounds.extents;

        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = c + new Vector3(
                (i & 1) == 0 ? -e.x : e.x,
                (i & 2) == 0 ? -e.y : e.y,
                (i & 4) == 0 ? -e.z : e.z);

            Vector3 local = root.InverseTransformPoint(child.TransformPoint(corner));

            if (!found)
            {
                bounds = new Bounds(local, Vector3.zero);
                found = true;
            }
            else
            {
                bounds.Encapsulate(local);
            }
        }
    }

    private static void SetActive(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active) go.SetActive(active);
    }
}