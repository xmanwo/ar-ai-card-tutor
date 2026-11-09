using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// Displays scan drafts and commits a validated snapshot to GameSessionState on confirmation.
/// Camera capture and networking remain owned by CardScanClient.
/// </summary>
[RequireComponent(typeof(Canvas), typeof(TrackedDeviceGraphicRaycaster))]
public class ScanPanel : MonoBehaviour, IScanView
{
    [Header("Data")]
    public CardScanClient client;

    [Header("Session")]
    public GameSessionState session;
    public Button scanDiscardButton;
    public TMP_Text savedStateText;

    public ScanTarget ActiveTarget { get; private set; } = ScanTarget.Hand;
    string TargetName => ActiveTarget == ScanTarget.Hand ? "Hand" : "Discard";

    [Header("Wild colour")]
    public GameObject wildColorControls;
    public TMP_Text wildColorText;

    string pendingWildColor;
    ScanState lastRenderedState = ScanState.Seeking;

    [Header("Display")]
    public RawImage frameImage;
    public TMP_Text statusText;
    public TMP_Text listText;

    [Header("Buttons")]
    public Button scanButton;
    public Button rescanButton;
    public Button confirmButton;

    [Header("Placement")]
    [Tooltip("Reparent to the head camera on start so the panel always sits in view, " +
             "wherever it was dropped in the hierarchy.")]
    public bool attachToCamera = true;

    [Tooltip("Local offset from the camera, in metres. X right, Y up, Z forward. " +
             "Kept nearer than the camera preview quad (z=1.0) so the two can never fight for depth.")]
    public Vector3 localOffset = new Vector3(0f, -0.26f, 0.85f);

    [Tooltip("Canvas scale. 0.0007 makes the 700x660 canvas 0.49m x 0.462m.")]
    public float panelScale = 0.0007f;

    [Tooltip("Draw the panel after other world-space canvases. Belt and braces against " +
             "a same-depth quad covering the text.")]
    public int canvasSortingOrder = 100;

    [Tooltip("The camera preview quad duplicates the passthrough view and sits right in front " +
             "of the panel, so it is switched off on start.")]
    public bool hideCameraPreview = true;

    void Awake()
    {
        if (client == null)
            client = FindFirstObjectByType<CardScanClient>();

        if (session == null)
            session = FindFirstObjectByType<GameSessionState>();

        // Existing prefabs also need the XR receiver, not only newly created canvases.
        if (GetComponent<TrackedDeviceGraphicRaycaster>() == null)
            gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();

        var canvas = GetComponent<Canvas>();
        if (canvas != null)
        {
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            canvas.overrideSorting = true;
            canvas.sortingOrder = canvasSortingOrder;
        }

        if (frameImage != null) frameImage.raycastTarget = false;
        if (statusText != null) statusText.raycastTarget = false;
        if (listText != null) listText.raycastTarget = false;
        if (savedStateText != null) savedStateText.raycastTarget = false;

        if (scanButton != null)
            scanButton.onClick.AddListener(OnScan);

        if (scanDiscardButton != null)
            scanDiscardButton.onClick.AddListener(OnScanDiscard);

        if (rescanButton != null)
            rescanButton.onClick.AddListener(OnRescan);

        if (confirmButton != null)
            confirmButton.onClick.AddListener(OnConfirm);
    }

    void Start()
    {
        if (hideCameraPreview)
            HideCameraPreview();

        if (attachToCamera)
            AttachToCamera();

        // CardScanClient resolves its view in Awake, which may run before this component exists.
        if (client != null && client.State == ScanState.Seeking)
            Render(ScanState.Seeking, null, client.Cards, string.Empty);

        RefreshSavedState();
    }

    void AttachToCamera()
    {
        var cameraManager = FindFirstObjectByType<UnityEngine.XR.ARFoundation.ARCameraManager>();
        var cam = cameraManager != null ? cameraManager.GetComponentInChildren<Camera>() : null;

        if (cam == null)
            cam = Camera.main;

        if (cam == null)
        {
            Debug.LogWarning("[ScanPanel] no head camera found; leaving the panel where it is.");
            return;
        }

        // Tracked-device raycasts must use the same head camera as the displayed panel.
        GetComponent<Canvas>().worldCamera = cam;
        transform.SetParent(cam.transform, false);
        transform.localPosition = localOffset;
        transform.localRotation = Quaternion.identity;

        // Cancel out any scale on the rig, otherwise the panel comes out the wrong size.
        var rigScale = cam.transform.lossyScale;
        transform.localScale = new Vector3(
            panelScale / (rigScale.x == 0f ? 1f : rigScale.x),
            panelScale / (rigScale.y == 0f ? 1f : rigScale.y),
            panelScale / (rigScale.z == 0f ? 1f : rigScale.z));

        Debug.Log($"[ScanPanel] attached to {cam.name}, local pos {localOffset}, " +
                  $"scale {panelScale}, world pos {transform.position}");
    }

    /// <summary>
    /// The MR template's camera preview quad sits at z=1.0 on the same camera, which is exactly
    /// where the panel used to sit. Coplanar transparent quads fight for depth and the preview won
    /// over the text. The preview is redundant next to passthrough anyway - the panel shows the
    /// captured frame itself - so its picture is switched off.
    /// </summary>
    void HideCameraPreview()
    {
        var preview = FindFirstObjectByType<QuestCameraPreview>(FindObjectsInactive.Include);

        if (preview == null)
            return;

        preview.SetPreviewVisible(false);
        Debug.Log($"[ScanPanel] hid the picture of '{preview.name}' so it cannot cover the panel.");
    }

    void OnDestroy()
    {
        if (scanButton != null)
            scanButton.onClick.RemoveListener(OnScan);

        if (scanDiscardButton != null)
            scanDiscardButton.onClick.RemoveListener(OnScanDiscard);

        if (rescanButton != null)
            rescanButton.onClick.RemoveListener(OnRescan);

        if (confirmButton != null)
            confirmButton.onClick.RemoveListener(OnConfirm);
    }

    void OnScan()
    {
        StartTarget(ScanTarget.Hand);
    }

    void OnScanDiscard()
    {
        StartTarget(ScanTarget.Discard);
    }

    void StartTarget(ScanTarget target)
    {
        if (client == null || !client.TryRescan())
            return;

        // A rejected request must never retarget the round already in progress.
        ActiveTarget = target;
        Render(client.State, null, client.Cards, string.Empty);
    }

    void OnRescan()
    {
        if (client != null)
            client.Rescan();
    }

    void OnConfirm()
    {
        if (client == null || client.State != ScanState.Reviewing)
            return;

        if (session == null)
        {
            if (statusText != null)
                statusText.text = "Saved state unavailable";
            return;
        }

        if (!session.TrySave(ActiveTarget, client.Cards, pendingWildColor, out string error))
        {
            if (statusText != null)
                statusText.text = error;
            return;
        }

        // No state transition is allowed until validation and snapshot creation succeed.
        Debug.Log($"[Session] saved {TargetName}: hand={session.Hand.Count}, " +
                  $"discard={session.Discard?.DisplayName ?? "none"}, colour={session.ActiveColor ?? "not set"}");
        client.Confirm();
    }

    public void SelectWildColor(string color)
    {
        if (client == null || !NeedsWildColor(client.State, client.Cards) ||
            !GameSessionState.IsValidColor(color))
            return;

        pendingWildColor = color;
        RefreshWildColorControls(client.State, client.Cards);
        if (statusText != null)
            statusText.text = $"Discard: {color} selected. Confirm to save.";
    }

    bool NeedsWildColor(ScanState state, IReadOnlyList<CardEntry> cards)
    {
        return state == ScanState.Reviewing && ActiveTarget == ScanTarget.Discard &&
            cards != null && cards.Count == 1 && GameSessionState.IsWild(cards[0]);
    }

    void RefreshWildColorControls(ScanState state, IReadOnlyList<CardEntry> cards)
    {
        bool show = NeedsWildColor(state, cards);
        if (wildColorControls != null)
            wildColorControls.SetActive(show);

        if (wildColorText != null)
            wildColorText.text = string.IsNullOrEmpty(pendingWildColor)
                ? "Choose\ncolour" : $"Selected:\n{pendingWildColor}";
    }

    public void Render(ScanState state, Texture2D frame, IReadOnlyList<CardEntry> cards, string message)
    {
        // Clear a pending colour on every new round, including controller/HUD rescans.
        if (state == ScanState.Scanning && lastRenderedState != ScanState.Scanning)
            pendingWildColor = null;
        lastRenderedState = state;
        RefreshWildColorControls(state, cards);

        // The thumbnail is only shown once the result is frozen. During a round every frame is a
        // half-finished picture, and showing them is exactly what made the panel flicker.
        if (frameImage != null)
        {
            bool showFrame = frame != null && (state == ScanState.Reviewing || state == ScanState.Confirmed);
            frameImage.texture = showFrame ? frame : null;
            frameImage.enabled = showFrame;
        }

        if (statusText != null)
            statusText.text = $"{TargetName}: {StatusLine(state, cards, message)}";

        if (listText != null)
            listText.text = CardList(state, cards, message);

        // Scan means "look again now", so it stays pressable in Reviewing as well.
        if (scanButton != null)
            scanButton.interactable = state != ScanState.Scanning;

        if (scanDiscardButton != null)
            scanDiscardButton.interactable = state != ScanState.Scanning;

        if (rescanButton != null)
            rescanButton.interactable = state != ScanState.Scanning;

        // Confirm needs at least one card, per the spec.
        if (confirmButton != null)
            confirmButton.interactable = state == ScanState.Reviewing && cards.Count > 0;

        RefreshSavedState();
    }

    /// <summary>
    /// The only per-frame work in the view: while a round runs, the status line counts the seconds
    /// so the player can tell "still looking" from "stuck".
    /// </summary>
    void Update()
    {
        if (statusText == null || client == null)
            return;

        if (client.State != ScanState.Scanning)
            return;

        statusText.text = $"Recognising {TargetName}... {client.ScanElapsedSeconds:F1}s";
    }

    void RefreshSavedState()
    {
        if (savedStateText == null)
            return;

        if (session == null)
        {
            savedStateText.text = "Saved state unavailable";
            return;
        }

        var text = new StringBuilder("Saved hand: ");
        if (!session.HasHand)
            text.Append("not confirmed");

        for (int i = 0; i < session.Hand.Count; i++)
        {
            if (i > 0) text.Append(", ");
            text.Append(session.Hand[i].DisplayName);
        }

        text.Append("\nSaved discard: ");
        text.Append(session.Discard != null ? session.Discard.DisplayName : "not confirmed");
        text.Append("\nActive colour: ");
        text.Append(session.ActiveColor ?? "not set");
        savedStateText.text = text.ToString();
    }

    static string StatusLine(ScanState state, IReadOnlyList<CardEntry> cards, string message)
    {
        switch (state)
        {
            case ScanState.Seeking:
                return "Lay the cards out in front of you.";

            case ScanState.Scanning:
                return "Recognising...";

            case ScanState.Reviewing:
                return cards.Count > 0 ? $"{cards.Count} card(s) detected" : "No cards detected";

            case ScanState.Confirmed:
                return $"Confirmed {cards.Count} cards.";

            case ScanState.Failed:
                return "Nothing recognised";

            case ScanState.Error:
                return string.IsNullOrEmpty(message) ? "Connection failed" : $"Connection failed\n{message}";

            default:
                return "Lay the cards out in front of you.";
        }
    }

    static string CardList(ScanState state, IReadOnlyList<CardEntry> cards, string message)
    {
        if (state == ScanState.Scanning)
            return string.Empty;

        if (state == ScanState.Failed)
            return "Move closer, change the angle,\nor scan again.";

        if (state == ScanState.Error)
            return "Check that the detection service\nis running, then scan again.";

        if (cards == null || cards.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();

        for (int i = 0; i < cards.Count; i++)
        {
            var card = cards[i];

            if (i > 0)
                sb.Append('\n');

            string line = card.DisplayName;

            // A user-added card has no model score, so do not invent one.
            if (card.source == CardSource.Model && card.confidence >= 0f)
                line += $"   ({card.confidence:P0})";

            if (card.uncertain)
                line = $"<color=#F5B301>{line}  ?</color>";

            sb.Append(line);
        }

        // Preserve round warnings, such as a scan that stopped before becoming stable.
        if (state == ScanState.Reviewing && !string.IsNullOrEmpty(message))
            sb.Append($"\n<size=80%><color=#F5B301>{message}</color></size>");

        return sb.ToString();
    }
}
