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

    [Header("Tutor")]
    [Tooltip("Optional. When present, confirming a hand or the discard refreshes the advice.")]
    public TutorService tutor;

    /// <summary>
    /// Which pile the next scan targets. A game always starts by reading the discard pile, because
    /// the hand can only be judged against a known top card, so that is the default.
    /// </summary>
    public ScanTarget ActiveTarget { get; private set; } = ScanTarget.Discard;
    string TargetName => ActiveTarget == ScanTarget.Hand ? "Hand" : "Discard";

    [Header("Wild colour")]
    public GameObject wildColorControls;
    public TMP_Text wildColorText;

    string pendingWildColor;
    ScanState lastRenderedState = ScanState.Seeking;
    CardHandView cardFaces;

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

    [Tooltip("Canvas scale. 0.0007 makes the 700x1000 canvas 0.49m x 0.70m.")]
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

        if (tutor == null)
            tutor = FindFirstObjectByType<TutorService>();

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

        ApplyLayout();

        cardFaces = GetComponent<CardHandView>();
        if (cardFaces == null) cardFaces = gameObject.AddComponent<CardHandView>();
        cardFaces.Initialize(this);

        if (scanButton != null)
            scanButton.onClick.AddListener(OnScan);

        if (scanDiscardButton != null)
            scanDiscardButton.onClick.AddListener(OnScanDiscard);

        if (rescanButton != null)
            rescanButton.onClick.AddListener(OnRescan);

        if (confirmButton != null)
            confirmButton.onClick.AddListener(OnConfirm);
    }

    /// <summary>Height of the panel canvas in UI units. Every row below was laid out against it.</summary>
    public const float PanelHeight = 670f;

    /// <summary>
    /// Lays the panel out from code, top row first.
    ///
    /// Two reasons it does not live in the prefab: a prefab instance keeps its own copy of every
    /// value it was ever given (this one had been saved at 700x660 and beat every prefab change),
    /// and the rows only stay clear of each other while a single place owns the vertical budget.
    /// </summary>
    void ApplyLayout()
    {
        var rect = (RectTransform)transform;

        if (!Mathf.Approximately(rect.sizeDelta.y, PanelHeight))
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, PanelHeight);

        // Sit a little higher than the old 0.26 m drop: the bottom rows were below the eye line.
        if (Mathf.Approximately(localOffset.y, -0.26f))
            localOffset.y = -0.20f;

        // Vertical budget on a 670 tall canvas (335 at the top, -335 at the bottom):
        //   status        327 .. 255
        //   frame / list  248 ..  68
        //   colour picker 247 ..  82   right column, replaces the list while picking
        //   headline       59 ..   7
        //   reasons         1 .. -91
        //   blocked       -97 .. -165
        //   saved        -171 .. -255   text-only fallback
        //   buttons      -319 .. -255
        if (statusText != null)
        {
            statusText.rectTransform.sizeDelta = new Vector2(-40f, 72f);
            statusText.rectTransform.anchoredPosition = new Vector2(0f, -8f);
            statusText.fontSize = 28f;
            statusText.overflowMode = TextOverflowModes.Ellipsis;
        }

        // Frame hugs the left edge, card list hugs the right edge, so the middle stays free.
        Place(frameImage != null ? frameImage.rectTransform : null, 180f, 180f, -240f, 158f);
        Place(listText != null ? listText.rectTransform : null, 280f, 180f, 190f, 158f);

        // Three lines at 22pt: the box has to be tall enough for all of them, otherwise the last
        // one ("Active colour") runs down into the button row.
        if (savedStateText != null)
            savedStateText.overflowMode = TextOverflowModes.Ellipsis;

        Place(savedStateText != null ? savedStateText.rectTransform : null, 660f, 84f, 0f, -213f);

        var tutorPanel = GetComponent<TutorPanel>();

        if (tutorPanel != null)
        {
            Place(tutorPanel.reasonText != null ? tutorPanel.reasonText.rectTransform : null,
                660f, 92f, 0f, -45f);
            Place(tutorPanel.blockedText != null ? tutorPanel.blockedText.rectTransform : null,
                660f, 68f, 0f, -131f);
        }

        ApplyTutorReadability(tutorPanel);
        ApplyScanControlsLayout();

        // One line in logcat makes a wrong canvas size obvious without guessing from a screenshot.
        string framePos = frameImage != null
            ? frameImage.rectTransform.anchoredPosition.ToString()
            : "none";

        Debug.Log($"[ScanPanel] canvas {rect.sizeDelta.x:0}x{rect.sizeDelta.y:0}, " +
                  $"offset {localOffset.y:0.00}, frame {framePos}");
    }

    /// <summary>Shares the teaching colours and headline position with the prefab preview.</summary>
    public void ApplyTutorReadability(TutorPanel tutorPanel)
    {
        var background = transform.Find("Background")?.GetComponent<Image>();
        if (background != null)
            background.color = new Color(0f, 0f, 0f, 0.92f);

        if (tutorPanel == null)
            return;

        // The Wild colour chip sits to the left of the headline.
        Place(tutorPanel.legalText != null ? tutorPanel.legalText.rectTransform : null,
            540f, 52f, 60f, 33f);
        if (tutorPanel.blockedText != null)
            tutorPanel.blockedText.color = new Color32(209, 213, 219, 255);
    }

    /// <summary>Shares the control geometry and colours with the prefab preview.</summary>
    public void ApplyScanControlsLayout()
    {
        if (wildColorControls != null)
            Place((RectTransform)wildColorControls.transform, 120f, 120f, 252f, 142f);

        if (wildColorText != null)
        {
            Place(wildColorText.rectTransform, 156f, 34f, 0f, 88f);
            wildColorText.alignment = TextAlignmentOptions.Center;
            wildColorText.overflowMode = TextOverflowModes.Ellipsis;
        }

        if (scanButton != null && scanButton.transform.parent is RectTransform row)
        {
            row.anchorMin = row.anchorMax = row.pivot = new Vector2(0.5f, 0f);
            Place(row, 660f, 64f, 0f, 16f);
        }

        var scanBlue = new Color32(61, 112, 217, 255);
        TintButton(scanButton, scanBlue);
        TintButton(scanDiscardButton, scanBlue);
        TintButton(rescanButton, new Color32(69, 75, 84, 255));
        TintButton(confirmButton, new Color32(25, 125, 72, 255));
    }

    static void TintButton(Button button, Color colour)
    {
        if (button != null && button.targetGraphic != null)
            button.targetGraphic.color = colour;
    }

    static void Place(RectTransform rect, float width, float height, float x, float y)
    {
        if (rect == null)
            return;

        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, y);
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

        // The advice describes the position that was just saved, so refresh it after a successful
        // save rather than on every frame.
        if (tutor != null)
            tutor.Evaluate();
    }

    public void SelectWildColor(string color)
    {
        if (client == null || !NeedsWildColor(client.State, client.Cards) ||
            !GameSessionState.IsValidColor(color))
            return;

        pendingWildColor = color;
        RefreshWildColorControls(client.State, client.Cards);
        RefreshConfirmButton(client.State, client.Cards);
        if (statusText != null)
            statusText.text = $"Discard: {color} selected. Confirm to save.";
    }

    bool NeedsWildColor(ScanState state, IReadOnlyList<CardEntry> cards)
    {
        return state == ScanState.Reviewing && ActiveTarget == ScanTarget.Discard &&
            cards != null && cards.Count == 1 && GameSessionState.IsWild(cards[0]);
    }

    /// <summary>
    /// Confirm stays disabled until every precondition the save will be checked against is met,
    /// including the colour of a scanned Wild. Done here rather than inside Render so the colour
    /// buttons can refresh the button the moment a colour is picked.
    /// </summary>
    void RefreshConfirmButton(ScanState state, IReadOnlyList<CardEntry> cards)
    {
        if (confirmButton == null)
            return;

        bool needsColour = NeedsWildColor(state, cards);
        bool colourChosen = !needsColour || !string.IsNullOrEmpty(pendingWildColor);

        confirmButton.interactable =
            state == ScanState.Reviewing && cards != null && cards.Count > 0 && colourChosen;
    }

    void RefreshWildColorControls(ScanState state, IReadOnlyList<CardEntry> cards)
    {
        bool show = NeedsWildColor(state, cards);
        if (wildColorControls != null)
            wildColorControls.SetActive(show);

        // The wheel sits in the card list's column, so the list steps aside while it is up.
        // Two blocks in one rectangle is exactly the overlap we keep out of the panel.
        if (listText != null && listText.enabled == show)
            listText.enabled = !show;

        if (wildColorText != null)
            wildColorText.text = string.IsNullOrEmpty(pendingWildColor)
                ? "Choose colour" : $"Selected: {pendingWildColor}";
    }

    public void Render(ScanState state, Texture2D frame, IReadOnlyList<CardEntry> cards, string message)
    {
        // Clear a pending colour on every new round, including controller/HUD rescans.
        if (state == ScanState.Scanning && lastRenderedState != ScanState.Scanning)
            pendingWildColor = null;
        lastRenderedState = state;
        RefreshWildColorControls(state, cards);

        // Frozen results keep their photo. Update() supplies a live photo during a new scan;
        // the recognised card list itself still waits for the existing freeze rules.
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

        if (cardFaces != null && cardFaces.IsAvailable)
        {
            cardFaces.Render(state, ActiveTarget, cards);
            if (listText != null) listText.enabled = false;
        }

        // Scan means "look again now", so it stays pressable in Reviewing as well.
        if (scanButton != null)
            scanButton.interactable = state != ScanState.Scanning;

        if (scanDiscardButton != null)
            scanDiscardButton.interactable = state != ScanState.Scanning;

        if (rescanButton != null)
            rescanButton.interactable = state != ScanState.Scanning;

        // Confirm needs at least one card, and a scanned Wild needs its colour chosen first.
        RefreshConfirmButton(state, cards);

        RefreshSavedState();
    }

    /// <summary>
    /// Updates scan progress and the live photo/crop overlay without rebuilding card faces.
    /// </summary>
    void Update()
    {
        if (client == null)
            return;

        if (client.State == ScanState.Scanning)
        {
            if (statusText != null)
                statusText.text = $"Recognising {TargetName}... {client.ScanElapsedSeconds:F1}s";
            // Only the photo updates live; card faces still wait for the existing freeze rules.
            var capture = client.capture;
            if (frameImage != null && capture != null && client.LastCapturedFrameId > 0)
            {
                frameImage.texture = capture.LastTexture;
                frameImage.enabled = capture.LastTexture != null;
            }
        }
        if (cardFaces != null && cardFaces.IsAvailable) cardFaces.RefreshPhotoPanel();
    }

    void RefreshSavedState()
    {
        if (savedStateText == null)
            return;

        savedStateText.enabled = cardFaces == null || !cardFaces.IsAvailable;
        if (!savedStateText.enabled)
            return;

        if (session == null)
        {
            savedStateText.text = "Saved state unavailable";
            return;
        }

        var text = new StringBuilder("Saved hand: ");
        if (!session.HasHand)
            text.Append("not confirmed");
        else for (int i = 0; i < session.Hand.Count; i++)
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
                return cards.Count > 0
                    ? new TextRef(cards.Count == 1 ? "scan.detectedOne" : "scan.detectedMany",
                        new TextVar("n", cards.Count.ToString())).Resolve()
                    : "No cards detected";

            case ScanState.Confirmed:
                return new TextRef(cards.Count == 1 ? "scan.confirmedOne" : "scan.confirmedMany",
                    new TextVar("n", cards.Count.ToString())).Resolve();

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
