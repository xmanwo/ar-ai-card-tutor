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
    public ScanTarget ActiveTarget => client != null ? client.ActiveTarget : ScanTarget.Discard;
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

    [Header("Automatic Hand Scan")]
    public bool automaticHandScan = true;
    [Min(.1f)] public float handScanInterval = 4.5f;
    HandScanProgress handProgress;
    float handRemaining;
    int handClockResetFrame = -1;
    bool handLocked, discardPending, discardQueued, handFocused;

    internal bool HandScanCanRun => automaticHandScan && !discardPending && session != null && session.Discard != null;
    internal bool CanHoldHandScan => HandScanCanRun && (handFocused || handLocked);
    internal bool CanRescanCurrent => !automaticHandScan || (!discardPending && ActiveTarget == ScanTarget.Hand);

    [Header("Placement")]
    [Tooltip("Reparent to the head camera on start so the panel always sits in view, " +
             "wherever it was dropped in the hierarchy.")]
    public bool attachToCamera = true;

    [Tooltip("Local offset from the camera, in metres. X right, Y up, Z forward. " +
             "Kept nearer than the camera preview quad (z=1.0) so the two can never fight for depth.")]
    public Vector3 localOffset = new Vector3(0f, FloatingOffsetY, 0.85f);

    [Tooltip("World-space canvas scale, in metres per UI unit.")]
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
        if (rescanButton != null)
        {
            handProgress = rescanButton.gameObject.AddComponent<HandScanProgress>();
            handProgress.Initialize(this);
        }

        cardFaces = GetComponent<CardHandView>();
        if (cardFaces == null) cardFaces = gameObject.AddComponent<CardHandView>();
        cardFaces.Initialize(this);
        LayoutRuleLines(GetComponent<TutorPanel>());

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
    public const float PanelHeight = 600f;
    public const float FloatingOffsetY = -0.10f;
    public const float LeftPanelOffsetX = -0.34f;

    /// <summary>Right coach panel, measured in the left panel's local UI units.</summary>
    public const float RightPanelX = 970f;
    public const float RightPanelWidth = 460f;

    public const float ContentWidth = 620f;
    public const float CardRowOffset = 0f;
    const float FooterPadding = 16f;
    const float FooterHeight = 64f;
    const float SavedStateY = -42f;
    static readonly Color32 PanelTint = new Color32(12, 16, 22, 214);
    static Sprite floatingPanelSprite, unoPillSprite;

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

        rect.sizeDelta = new Vector2(700f, PanelHeight);

        // Left rail. The right rail is the AI panel; together they leave the middle of the view
        // free for the physical table and the player's hand.
        localOffset = new Vector3(LeftPanelOffsetX, FloatingOffsetY, 0.85f);

        if (statusText != null)
        {
            statusText.rectTransform.sizeDelta = new Vector2(ContentWidth, 48f);
            statusText.rectTransform.anchoredPosition = new Vector2(0f, 268f);
            statusText.fontSize = 24f;
            statusText.alignment = TextAlignmentOptions.Center;
            statusText.overflowMode = TextOverflowModes.Ellipsis;
        }

        // The photo panel owns frameImage at runtime; these fallbacks only matter if it is missing.
        Place(frameImage != null ? frameImage.rectTransform : null, 300f, 160f, 0f, 80f);
        Place(listText != null ? listText.rectTransform : null, 420f, 150f, 100f, 140f);

        // Text-only fallback when card textures are unavailable.
        if (savedStateText != null)
        {
            savedStateText.rectTransform.sizeDelta = new Vector2(ContentWidth, 96f);
            savedStateText.rectTransform.anchoredPosition = new Vector2(0f, SavedStateY);
            savedStateText.overflowMode = TextOverflowModes.Ellipsis;
        }

        var tutorPanel = GetComponent<TutorPanel>();

        ApplyTutorReadability(tutorPanel);
        var background = transform.Find("Background")?.GetComponent<Image>();
        if (background != null) StyleFloatingPanel(background);
        ApplyScanControlsLayout();

        // One line in logcat makes a wrong canvas size obvious without guessing from a screenshot.
        string framePos = frameImage != null
            ? frameImage.rectTransform.anchoredPosition.ToString()
            : "none";

        Debug.Log($"[ScanPanel] canvas {rect.sizeDelta.x:0}x{rect.sizeDelta.y:0}, " +
                  $"offset {localOffset.x:0.00},{localOffset.y:0.00}, frame {framePos}");
    }

    /// <summary>Shares the teaching colours and headline position with the prefab preview.</summary>
    public void ApplyTutorReadability(TutorPanel tutorPanel)
    {
        var background = transform.Find("Background")?.GetComponent<Image>();
        if (background != null)
            background.color = PanelTint;

        if (tutorPanel == null)
            return;

        bool runtime = Application.isPlaying;
        float legalX = runtime ? RightPanelX : 60f;
        float legalY = runtime ? 158f : 17f;
        float legalWidth = runtime ? 410f : 540f;

        // The local-rule headline sits at the top of the right coach rail.
        Place(tutorPanel.legalText != null ? tutorPanel.legalText.rectTransform : null,
            legalWidth, 32f, legalX, legalY);
        if (tutorPanel.legalText != null)
        {
            tutorPanel.legalText.fontSize = runtime ? 20f : 22f;
            tutorPanel.legalText.alignment = TextAlignmentOptions.TopLeft;
            tutorPanel.legalText.overflowMode = TextOverflowModes.Ellipsis;
        }
        if (tutorPanel.reasonText != null)
        {
            tutorPanel.reasonText.fontSize = runtime ? 18f : 20f;
            tutorPanel.reasonText.overflowMode = TextOverflowModes.Ellipsis;
        }
        if (tutorPanel.blockedText != null)
        {
            tutorPanel.blockedText.fontSize = runtime ? 17f : 20f;
            tutorPanel.blockedText.overflowMode = TextOverflowModes.Ellipsis;
            tutorPanel.blockedText.color = new Color32(209, 213, 219, 255);
        }
        if (!runtime)
        {
            Place(tutorPanel.reasonText?.rectTransform, 660f, 50f, 0f, -76f);
            Place(tutorPanel.blockedText?.rectTransform, 660f, 50f, 0f, -132f);
        }
    }

    internal void LayoutRuleLines(TutorPanel panel)
    {
        if (panel == null) return;
        const float top = 120f;
        const float bottom = 4f;
        const float width = 410f;
        float reason = RuleTextHeight(panel.reasonText);
        float blocked = RuleTextHeight(panel.blockedText);
        float gap = reason > 0f && blocked > 0f ? 6f : 0f;
        float available = top - bottom - gap;
        float reasonHeight = Mathf.Min(reason, available - Mathf.Min(blocked, available * .5f));
        float blockedHeight = Mathf.Min(blocked, available - reasonHeight);
        float y = top - (available - reasonHeight - blockedHeight) * .55f;
        Place(panel.reasonText?.rectTransform, width, reasonHeight, RightPanelX, y - reasonHeight * .5f);
        y -= reasonHeight + gap;
        Place(panel.blockedText?.rectTransform, width, blockedHeight, RightPanelX, y - blockedHeight * .5f);
    }

    static float RuleTextHeight(TMP_Text line) => line == null || string.IsNullOrEmpty(line.text)
        ? 0f : Mathf.Ceil(line.GetPreferredValues(line.text, 410f, 0f).y) + 3f;

    internal static void ApplyPhotoLayout(RectTransform rect, bool debug)
    {
        // The photo/debug page is an overlay on the left rail, opened from the Debug control.
        Place(rect, debug ? ContentWidth : 600f, debug ? 430f : 330f, 0f, 10f);
    }

    internal static void ApplyAiLayout(RectTransform rect)
    {
        Place(rect, RightPanelWidth, PanelHeight, RightPanelX, 0f);
    }

    internal static float FooterCentreY => -PanelHeight * .5f + FooterPadding + FooterHeight * .5f;

    internal static void StyleFloatingPanel(Image image)
    {
        if (floatingPanelSprite == null)
            floatingPanelSprite = RoundedSprite(42, 16f, PanelTint, new Color32(182, 194, 208, 178));
        image.sprite = floatingPanelSprite;
        image.type = Image.Type.Sliced;
        image.color = Color.white;
    }

    internal static void StyleUnoPill(Image image)
    {
        var yellow = new Color32(255, 217, 64, 255);
        if (unoPillSprite == null)
            unoPillSprite = RoundedSprite(34, 15f, yellow, yellow);
        image.sprite = unoPillSprite;
        image.type = Image.Type.Sliced;
    }

    // Keep the corner radius and outline uniform when the panels stretch to different sizes.
    static Sprite RoundedSprite(int size, float radius, Color32 fill, Color32 stroke)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        var pixels = new Color32[size * size];
        float half = size * .5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            var corner = new Vector2(Mathf.Abs(x + .5f - half), Mathf.Abs(y + .5f - half));
            corner -= Vector2.one * (half - radius);
            float distance = Vector2.Max(corner, Vector2.zero).magnitude +
                Mathf.Min(Mathf.Max(corner.x, corner.y), 0f) - radius;
            Color color = Color.Lerp(fill, stroke, Mathf.Clamp01(distance + 1.5f));
            color.a *= Mathf.Clamp01(.5f - distance);
            pixels[y * size + x] = color;
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0, 0, size, size), Vector2.one * .5f, 100f,
            0, SpriteMeshType.FullRect, Vector4.one * (radius + 1f));
    }

    /// <summary>Shares the control geometry and colours with the prefab preview.</summary>
    public void ApplyScanControlsLayout()
    {
        if (wildColorControls != null)
            Place((RectTransform)wildColorControls.transform, 110f, 110f, 210f, -10f);

        if (wildColorText != null)
        {
            Place(wildColorText.rectTransform, 160f, 28f, 210f, -82f);
            wildColorText.fontSize = 14f;
            wildColorText.alignment = TextAlignmentOptions.Center;
            wildColorText.overflowMode = TextOverflowModes.Ellipsis;
        }

        if (scanButton != null && scanButton.transform.parent is RectTransform row)
        {
            row.anchorMin = row.anchorMax = row.pivot = new Vector2(0.5f, 0.5f);
            Place(row, 660f, 60f, 0f, -220f);
        }

        PlaceButton(scanDiscardButton, 150f, 60f, -245f);
        PlaceButton(scanButton, 170f, 60f, -65f);
        PlaceButton(rescanButton, 145f, 60f, 95f);
        PlaceButton(confirmButton, 145f, 60f, 245f);

        SetButtonLabel(scanButton, "Scan hand", "扫描手牌");
        SetButtonLabel(scanDiscardButton, "Scan discard", "扫描桌顶");
        SetButtonLabel(confirmButton, "Confirm", "确认");

        var scanBlue = new Color32(61, 112, 217, 255);
        TintButton(scanButton, scanBlue);
        TintButton(scanDiscardButton, new Color32(52, 59, 72, 255));
        TintButton(rescanButton, new Color32(69, 75, 84, 255));
        TintButton(confirmButton, new Color32(25, 125, 72, 255));
    }

    static void PlaceButton(Button button, float width, float height, float x)
    {
        if (button == null) return;
        var rect = (RectTransform)button.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        Place(rect, width, height, x, 0f);
    }

    static void SetButtonLabel(Button button, string english, string chinese)
    {
        if (button == null) return;
        var label = button.GetComponentInChildren<TMP_Text>();
        if (label != null) label.text = Loc.Current == Language.Zh ? chinese : english;
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
        if (client == null || (automaticHandScan && discardPending)) return;
        handLocked = false;
        ResetHandClock(0f);
        if (!HandScanCanRun)
            client.TryRescan(ScanTarget.Hand);
    }

    void OnScanDiscard()
    {
        if (client == null) return;
        if (!automaticHandScan)
        {
            client.TryRescan(ScanTarget.Discard);
            return;
        }

        discardPending = discardQueued = true;
        client.CancelAutomaticHandRound();
        if (client.TryRescan(ScanTarget.Discard)) discardQueued = false;
        RefreshScanButtons(client.State);
        RefreshConfirmButton(client.State, client.Cards);
    }

    void OnRescan()
    {
        if (!automaticHandScan && client != null)
            client.Rescan();
    }

    internal void FreezeHandScanForHover()
    {
        if (client != null) client.CancelAutomaticHandRound();
    }

    internal void ToggleHandScanLock()
    {
        handLocked = !handLocked;
        if (handLocked) FreezeHandScanForHover();
        Debug.Log($"[HandScan] {(handLocked ? "locked" : "resumed")}");
    }

    void OnConfirm()
    {
        if (client == null || client.State != ScanState.Reviewing || discardQueued)
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

        if (ActiveTarget == ScanTarget.Discard)
        {
            discardPending = discardQueued = false;
            ResetHandClock(0f);
            RefreshScanButtons(client.State);
        }
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
            !discardQueued && state == ScanState.Reviewing && cards != null && cards.Count > 0 && colourChosen;
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
        {
            pendingWildColor = null;
            if (automaticHandScan && ActiveTarget == ScanTarget.Discard) discardPending = true;
        }
        if (ActiveTarget == ScanTarget.Hand && lastRenderedState == ScanState.Scanning && state != ScanState.Scanning)
            ResetHandClock(Mathf.Max(.1f, handScanInterval));
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
            statusText.text = StateLabel(state, cards, message);

        if (listText != null)
            listText.text = CardList(state, cards, message);

        if (cardFaces != null && cardFaces.IsAvailable)
        {
            cardFaces.Render(state, ActiveTarget, cards);
            if (listText != null) listText.enabled = false;
        }

        RefreshScanButtons(state);

        // Confirm needs at least one card, and a scanned Wild needs its colour chosen first.
        RefreshConfirmButton(state, cards);

        RefreshSavedState();
    }

    string StateLabel(ScanState state, IReadOnlyList<CardEntry> cards, string message)
    {
        if (state == ScanState.Seeking && ActiveTarget == ScanTarget.Discard)
            return new TextRef(client != null && client.StartupDiscardPending
                ? "scan.showDiscard" : "scan.startDiscard").Resolve();
        return $"{TargetName}: {StatusLine(state, cards, message)}";
    }

    void ResetHandClock(float seconds)
    {
        handRemaining = seconds;
        handClockResetFrame = Time.frameCount;
    }

    internal static float AdvanceHandClock(float remaining, float elapsed, bool ready, bool holding)
        => ready && !holding ? Mathf.Max(0f, remaining - elapsed) : remaining;

    void RefreshScanButtons(ScanState state)
    {
        bool scanning = state == ScanState.Scanning;
        if (scanButton != null)
            scanButton.interactable = !scanning && !(automaticHandScan && discardPending);

        if (scanDiscardButton != null)
            scanDiscardButton.interactable = !discardQueued &&
                (!scanning || (client != null && client.AutomaticHandRound));

        if (rescanButton != null)
            rescanButton.interactable = automaticHandScan ? CanHoldHandScan : !scanning;
    }

    // Pointer events and the finger ray run in Update. Accept them before advancing the clock.
    void LateUpdate()
    {
        if (client == null || handProgress == null) return;
        float interval = Mathf.Max(.1f, handScanInterval);
        if (!automaticHandScan)
        {
            discardPending = discardQueued = handLocked = false;
            client.CancelAutomaticHandRound();
        }
        else if (discardQueued && client.TryRescan(ScanTarget.Discard))
            discardQueued = false;

        handFocused = HandScanCanRun && client.CanFocusHand;
        if (client.AutomaticHandRound && !handFocused) client.CancelAutomaticHandRound();
        bool holding = handProgress.Holding;
        if (holding) FreezeHandScanForHover();
        handProgress.Tick(Time.unscaledTime);
        bool canCount = handFocused && !handLocked && !client.IsBusy && client.State != ScanState.Scanning;
        float elapsed = handClockResetFrame == Time.frameCount ? 0f : Time.unscaledDeltaTime;
        handRemaining = AdvanceHandClock(handRemaining, elapsed, canCount, holding);
        if (canCount && !holding && handRemaining == 0f)
            client.TryRescan(ScanTarget.Hand, automaticHand: true);
        RefreshScanButtons(client.State);
        handProgress.Display(automaticHandScan, handLocked, handFocused,
            HandScanCanRun && ActiveTarget == ScanTarget.Hand && client.State == ScanState.Scanning,
            handRemaining, interval);
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
