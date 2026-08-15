using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Sandwich eating system with world-space UI.
///
/// Flow: "Bite!" floats above the sandwich -> Space (while settled on it) -> label hides,
/// sandwich rises to the mouth, model swaps, sandwich lowers back to the table ->
/// "Chew!" + chew bar appear -> press Space 13 times to fill it (presses only count while
/// looking at the sandwich) -> bar hides, "Bite!" returns. Last bite ends the level.
///
/// A seagull can call LoseSandwich() to steal the plate and end the level.
///
/// Attach to an empty GameObject in the scene.
/// </summary>
public class SandwichEater : MonoBehaviour
{
    public enum EatState { Idle, Raising, Lowering, Chewing, Finished, Lost }

    [Header("References")]
    public SandwichLookController lookController;
    [Tooltip("Empty transform that carries the sandwich model. This is what moves.")]
    public Transform sandwichHolder;
    [Tooltip("Resting spot on the table.")]
    public Transform tablePoint;
    [Tooltip("Bite spot in front of the camera. Make this a child of the camera.")]
    public Transform mouthPoint;

    [Header("Bite Stages")]
    [Tooltip("Element 0 = what sits on the table at the start. Each bite advances one element. " +
             "Total bites = array length; the final bite empties the plate.")]
    public GameObject[] bitePrefabs;

    [Header("World UI")]
    [Tooltip("Root of the world-space canvas above the sandwich. Billboards toward the camera.")]
    public Transform worldUiRoot;
    [Tooltip("Leave empty to use Camera.main.")]
    public Camera billboardCamera;
    [Tooltip("Object holding the \"Bite!\" text.")]
    public GameObject biteLabel;
    [Tooltip("Object holding the \"Chew!\" text and the bar.")]
    public GameObject chewGroup;
    [Tooltip("Image with Type = Filled, Fill Method = Horizontal, Fill Origin = Left.")]
    public Image chewFillImage;
    [Tooltip("Object holding the level-complete text.")]
    public GameObject winLabel;
    public TMP_Text winText;
    [Tooltip("Object holding the lose text. Put this on a SCREEN-space canvas -- the camera " +
             "pans away from the plate when you lose.")]
    public GameObject loseLabel;
    public TMP_Text loseText;

    [Header("Level")]
    public int currentLevel = 1;
    [Tooltip("{0} is replaced with the level number.")]
    public string winMessageFormat = "You Beat Level {0}";
    public string loseMessage = "The Seagull Got Your Sandwich!";

    [Header("Timing")]
    public float raiseDuration = 0.35f;
    public float lowerDuration = 0.35f;

    [Header("Chewing")]
    [Tooltip("Space presses required to finish one chew. The bar fills by 1/this per press.")]
    [Min(1)] public int pressesToChew = 13;

    [Header("Input")]
    public Key eatKey = Key.Space;

    [Header("Events")]
    public UnityEvent onBiteTaken;
    public UnityEvent onChewPress;
    public UnityEvent onLevelComplete;
    public UnityEvent onSandwichStolen;

    public EatState State { get; private set; } = EatState.Idle;
    public int BitesTaken { get; private set; }
    public int ChewPresses { get; private set; }
    public float ChewProgress => pressesToChew <= 0 ? 1f : (float)ChewPresses / pressesToChew;

    // ---- Hooks for the seagull system ----

    /// <summary>Where the sandwich currently sits. Seagulls dive at this.</summary>
    public Transform SandwichPoint => sandwichHolder;

    /// <summary>True when a seagull could actually take it -- on the table, not at your mouth.</summary>
    public bool SandwichIsStealable =>
        (State == EatState.Idle || State == EatState.Chewing) && currentSandwich != null;

    /// <summary>True once the level has been won or lost.</summary>
    public bool LevelOver => State == EatState.Finished || State == EatState.Lost;

    private GameObject currentSandwich;
    private float moveTimer;
    private Vector3 moveFromPos;
    private Quaternion moveFromRot;

    private void Start()
    {
        if (sandwichHolder == null || tablePoint == null || mouthPoint == null)
        {
            Debug.LogError("SandwichEater: assign Sandwich Holder, Table Point and Mouth Point.", this);
            enabled = false;
            return;
        }

        if (billboardCamera == null) billboardCamera = Camera.main;

        if (bitePrefabs == null || bitePrefabs.Length == 0)
            Debug.LogWarning("SandwichEater: no bite prefabs assigned.", this);

        BeginLevel();
    }

    private void Update()
    {
        bool pressed = Keyboard.current != null && Keyboard.current[eatKey].wasPressedThisFrame;

        switch (State)
        {
            case EatState.Idle:
                SetActive(biteLabel, CanBite());
                if (pressed && CanBite()) BeginRaise();
                break;

            case EatState.Raising:
                TickMove(mouthPoint, raiseDuration, OnReachedMouth);
                break;

            case EatState.Lowering:
                TickMove(tablePoint, lowerDuration, OnReachedTable);
                break;

            case EatState.Chewing:
                if (pressed) TryChewPress();
                break;

            case EatState.Finished:
            case EatState.Lost:
                break;
        }
    }

    private void LateUpdate()
    {
        if (worldUiRoot == null || billboardCamera == null) return;

        Vector3 away = worldUiRoot.position - billboardCamera.transform.position;
        if (away.sqrMagnitude > 0.0001f)
            worldUiRoot.rotation = Quaternion.LookRotation(away, Vector3.up);
    }

    // ---- Gating ----

    /// <summary>True only when settled on the sandwich with nothing else running.</summary>
    public bool CanBite()
    {
        if (State != EatState.Idle) return false;
        return IsLookingAtSandwich();
    }

    private bool IsLookingAtSandwich()
    {
        return lookController == null || lookController.IsLookingAtSandwich;
    }

    // ---- Level control ----

    /// <summary>Resets the plate for the current level.</summary>
    public void BeginLevel()
    {
        BitesTaken = 0;
        ChewPresses = 0;

        SetActive(biteLabel, false);
        SetActive(chewGroup, false);
        SetActive(winLabel, false);
        SetActive(loseLabel, false);
        RefreshChewBar();

        if (lookController != null)
        {
            lookController.enabled = true;
            lookController.ReleaseLock();
        }

        SnapToTable();
        SpawnStage(0);
        State = EatState.Idle;
    }

    /// <summary>Advances the level counter and resets the plate.</summary>
    public void StartNextLevel()
    {
        currentLevel++;
        BeginLevel();
    }

    /// <summary>Replays the current level.</summary>
    public void RestartLevel()
    {
        BeginLevel();
    }

    // ---- Losing ----

    /// <summary>
    /// Called by a seagull that reaches the sandwich. Parents the model to carryParent,
    /// locks the camera onto thief, and ends the level.
    /// </summary>
    public void LoseSandwich(Transform thief, Transform carryParent = null)
    {
        if (LevelOver) return;

        SetActive(biteLabel, false);
        SetActive(chewGroup, false);

        Transform parent = carryParent != null ? carryParent : thief;

        if (currentSandwich != null && parent != null)
        {
            currentSandwich.transform.SetParent(parent, false);
            currentSandwich.transform.localPosition = Vector3.zero;
            currentSandwich.transform.localRotation = Quaternion.identity;
            currentSandwich = null;
        }

        if (loseText != null) loseText.text = loseMessage;
        SetActive(loseLabel, true);

        State = EatState.Lost;

        if (lookController != null)
        {
            lookController.enabled = true; // in case a bite was mid-flight
            lookController.LockOnTo(thief);
        }

        onSandwichStolen?.Invoke();
    }

    // ---- State transitions ----

    private void BeginRaise()
    {
        SetActive(biteLabel, false);
        SetLookLocked(true); // the mouth point rides the camera, so freeze the head mid-move
        StartMove();
        State = EatState.Raising;
    }

    private void OnReachedMouth()
    {
        BitesTaken++;
        SpawnStage(BitesTaken); // past the last stage = empty plate
        onBiteTaken?.Invoke();

        StartMove();
        State = EatState.Lowering;
    }

    private void OnReachedTable()
    {
        SnapToTable();
        SetLookLocked(false);

        ChewPresses = 0;
        RefreshChewBar();
        SetActive(chewGroup, true);
        State = EatState.Chewing;
    }

    private void FinishChew()
    {
        SetActive(chewGroup, false);

        if (bitePrefabs != null && BitesTaken >= bitePrefabs.Length)
            FinishLevel();
        else
            State = EatState.Idle;
    }

    private void FinishLevel()
    {
        if (winText != null) winText.text = string.Format(winMessageFormat, currentLevel);
        SetActive(winLabel, true);
        State = EatState.Finished;
        onLevelComplete?.Invoke();
    }

    // ---- Chewing ----

    private void TryChewPress()
    {
        // Presses only count while you're actually looking at the sandwich.
        if (!IsLookingAtSandwich()) return;

        ChewPresses++;
        RefreshChewBar();
        onChewPress?.Invoke();

        if (ChewPresses >= pressesToChew) FinishChew();
    }

    private void RefreshChewBar()
    {
        if (chewFillImage != null) chewFillImage.fillAmount = Mathf.Clamp01(ChewProgress);
    }

    // ---- Movement ----

    private void StartMove()
    {
        moveFromPos = sandwichHolder.position;
        moveFromRot = sandwichHolder.rotation;
        moveTimer = 0f;
    }

    private void TickMove(Transform target, float duration, System.Action onArrive)
    {
        moveTimer += Time.deltaTime;
        float t = duration <= 0f ? 1f : Mathf.Clamp01(moveTimer / duration);
        float eased = Mathf.SmoothStep(0f, 1f, t);

        sandwichHolder.position = Vector3.Lerp(moveFromPos, target.position, eased);
        sandwichHolder.rotation = Quaternion.Slerp(moveFromRot, target.rotation, eased);

        if (t >= 1f) onArrive();
    }

    private void SnapToTable()
    {
        sandwichHolder.position = tablePoint.position;
        sandwichHolder.rotation = tablePoint.rotation;
    }

    private void SetLookLocked(bool locked)
    {
        if (lookController != null) lookController.enabled = !locked;
    }

    // ---- Model swapping ----

    private void SpawnStage(int index)
    {
        if (currentSandwich != null) Destroy(currentSandwich);
        currentSandwich = null;

        if (bitePrefabs == null) return;
        if (index < 0 || index >= bitePrefabs.Length) return; // plate is empty
        if (bitePrefabs[index] == null) return;

        currentSandwich = Instantiate(bitePrefabs[index], sandwichHolder);
        currentSandwich.transform.localPosition = Vector3.zero;
        currentSandwich.transform.localRotation = Quaternion.identity;
    }

    private static void SetActive(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active) go.SetActive(active);
    }
}