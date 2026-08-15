using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Spawns seagulls from the left and right, scales difficulty with the level,
/// handles punching (left mouse) with a script-driven arms prefab, and drives the
/// left/right screen-space warning icons.
///
/// A punch lands only if you're facing the gull (within Punch Angle of your view)
/// and it's within Punch Range -- so A/D is your aim.
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
    [Tooltip("Seconds shaved off the interval per level.")]
    public float intervalReductionPerLevel = 0.8f;
    public float minSpawnInterval = 3f;
    [Tooltip("Gulls allowed in the air at once on level 1.")]
    [Min(1)] public int baseMaxConcurrent = 1;
    [Tooltip("Gain one extra concurrent gull every N levels. 0 = never.")]
    public int extraGullEveryNLevels = 3;

    [Header("Difficulty")]
    [Tooltip("Seconds a gull hovers in punch range before diving, on level 1.")]
    public float hoverDuration = 1.8f;
    public float hoverReductionPerLevel = 0.12f;
    public float minHoverDuration = 0.5f;

    [Header("Punching")]
    public GameObject armsPrefab;
    public Vector3 armsRestLocalPosition = new Vector3(0f, -0.4f, 0.3f);
    public Vector3 armsPunchLocalPosition = new Vector3(0f, -0.15f, 0.85f);
    public float punchOutDuration = 0.07f;
    public float punchReturnDuration = 0.16f;
    public float punchCooldown = 0.3f;
    public float punchRange = 3.5f;
    [Tooltip("Half-angle of the punch cone, in degrees, measured from your view direction.")]
    [Range(5f, 90f)] public float punchAngle = 45f;

    [Header("Warning Icons")]
    [Tooltip("Screen-space icon pinned to the middle-left edge.")]
    public GameObject leftWarningIcon;
    [Tooltip("Screen-space icon pinned to the middle-right edge.")]
    public GameObject rightWarningIcon;
    [Tooltip("How close a gull must get before its warning icon appears.")]
    public float warningDistance = 8f;
    [Tooltip("Hide the icon once you're already looking that way -- it's done its job.")]
    public bool hideWarningWhenFacing = true;
    [Tooltip("Pulses per second. 0 = no pulse.")]
    public float warningPulseSpeed = 3f;
    [Tooltip("How much the icon grows at the top of the pulse. 0 = no pulse.")]
    public float warningPulseAmount = 0.15f;

    [Header("Optional UI")]
    [Tooltip("Object holding a \"Punch!\" prompt. Shown while a gull is in punch range. Can be left empty.")]
    public GameObject punchLabel;

    public int ActiveGullCount => activeGulls.Count;

    private readonly List<Seagull> activeGulls = new List<Seagull>();
    private float spawnTimer;
    private bool wasLevelOver;

    private Transform armsInstance;
    private float punchTimer = -1f;
    private float cooldownTimer;

    private Vector3 leftIconBaseScale = Vector3.one;
    private Vector3 rightIconBaseScale = Vector3.one;

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

        if (leftWarningIcon != null) leftIconBaseScale = leftWarningIcon.transform.localScale;
        if (rightWarningIcon != null) rightIconBaseScale = rightWarningIcon.transform.localScale;

        SpawnArms();
        ResetForLevel();
    }

    private void Update()
    {
        PruneDeadGulls();

        bool levelOver = eater != null && eater.LevelOver;
        if (wasLevelOver && !levelOver) ResetForLevel(); // a new level just started
        wasLevelOver = levelOver;

        if (!levelOver)
        {
            TickSpawning();
            TickPunchInput();
            TickPunchPrompt();
            TickWarnings();
        }
        else
        {
            SetActive(punchLabel, false);
            SetActive(leftWarningIcon, false);
            SetActive(rightWarningIcon, false);
        }

        TickArms();
    }

    // ---- Level control ----

    public void ResetForLevel()
    {
        for (int i = activeGulls.Count - 1; i >= 0; i--)
            if (activeGulls[i] != null) Destroy(activeGulls[i].gameObject);

        activeGulls.Clear();
        spawnTimer = firstSpawnDelay;
        cooldownTimer = 0f;
        punchTimer = -1f;

        SetActive(punchLabel, false);
        SetActive(leftWarningIcon, false);
        SetActive(rightWarningIcon, false);
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
    }

    private void PruneDeadGulls()
    {
        for (int i = activeGulls.Count - 1; i >= 0; i--)
            if (activeGulls[i] == null) activeGulls.RemoveAt(i);
    }

    // ---- Warning icons ----

    private void TickWarnings()
    {
        if (leftWarningIcon == null && rightWarningIcon == null) return;
        if (playerCamera == null) return;

        bool warnLeft = false;
        bool warnRight = false;

        Vector3 origin = playerCamera.transform.position;

        foreach (Seagull gull in activeGulls)
        {
            if (gull == null || !gull.IsPunchable) continue;
            if (Vector3.Distance(origin, gull.transform.position) > warningDistance) continue;

            if (gull.SpawnedOnLeft) warnLeft = true;
            else warnRight = true;
        }

        if (hideWarningWhenFacing && lookController != null)
        {
            if (lookController.CurrentState == SandwichLookController.LookState.Left)
                warnLeft = false;
            else if (lookController.CurrentState == SandwichLookController.LookState.Right)
                warnRight = false;
        }

        ShowWarning(leftWarningIcon, leftIconBaseScale, warnLeft);
        ShowWarning(rightWarningIcon, rightIconBaseScale, warnRight);
    }

    private void ShowWarning(GameObject icon, Vector3 baseScale, bool show)
    {
        if (icon == null) return;

        SetActive(icon, show);
        if (!show) return;

        float pulse = 1f;
        if (warningPulseSpeed > 0f && warningPulseAmount > 0f)
            pulse = 1f + Mathf.Abs(Mathf.Sin(Time.time * Mathf.PI * warningPulseSpeed)) * warningPulseAmount;

        icon.transform.localScale = baseScale * pulse;
    }

    // ---- Punching ----

    private void TickPunchInput()
    {
        if (cooldownTimer > 0f) cooldownTimer -= Time.deltaTime;

        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
        if (cooldownTimer > 0f) return;

        cooldownTimer = punchCooldown;
        punchTimer = 0f; // start the arm swing whether or not it connects

        Seagull target = FindPunchableTarget();
        if (target != null && playerCamera != null)
            target.Punch(playerCamera.transform.position);
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

    private void TickPunchPrompt()
    {
        if (punchLabel == null) return;
        SetActive(punchLabel, FindPunchableTarget() != null);
    }

    // ---- Arms ----

    private void SpawnArms()
    {
        if (armsPrefab == null || playerCamera == null) return;

        GameObject go = Instantiate(armsPrefab, playerCamera.transform);
        armsInstance = go.transform;
        armsInstance.localPosition = armsRestLocalPosition;
        armsInstance.localRotation = Quaternion.identity;
    }

    private void TickArms()
    {
        if (armsInstance == null || punchTimer < 0f) return;

        punchTimer += Time.deltaTime;

        if (punchTimer <= punchOutDuration)
        {
            float t = punchOutDuration <= 0f ? 1f : punchTimer / punchOutDuration;
            armsInstance.localPosition = Vector3.Lerp(
                armsRestLocalPosition, armsPunchLocalPosition, Mathf.SmoothStep(0f, 1f, t));
        }
        else if (punchTimer <= punchOutDuration + punchReturnDuration)
        {
            float t = punchReturnDuration <= 0f
                ? 1f
                : (punchTimer - punchOutDuration) / punchReturnDuration;
            armsInstance.localPosition = Vector3.Lerp(
                armsPunchLocalPosition, armsRestLocalPosition, Mathf.SmoothStep(0f, 1f, t));
        }
        else
        {
            armsInstance.localPosition = armsRestLocalPosition;
            punchTimer = -1f;
        }
    }

    private static void SetActive(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active) go.SetActive(active);
    }
}