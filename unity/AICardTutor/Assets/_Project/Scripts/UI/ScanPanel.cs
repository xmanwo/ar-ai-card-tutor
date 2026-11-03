using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The three-page result panel. Pure view: it draws what CardScanClient hands it and forwards
/// button presses back. It never touches the camera or the network.
/// </summary>
public class ScanPanel : MonoBehaviour, IScanView
{
    [Header("Data")]
    public CardScanClient client;

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

    [Tooltip("Canvas scale. 0.0007 makes the 700x500 canvas 0.49m x 0.35m.")]
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

        var canvas = GetComponent<Canvas>();
        if (canvas != null)
        {
            canvas.overrideSorting = true;
            canvas.sortingOrder = canvasSortingOrder;
        }

        if (scanButton != null)
            scanButton.onClick.AddListener(OnScan);

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
        if (client != null && client.State == ScanState.Ready)
            Render(ScanState.Ready, null, client.Cards, string.Empty);
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

        if (rescanButton != null)
            rescanButton.onClick.RemoveListener(OnRescan);

        if (confirmButton != null)
            confirmButton.onClick.RemoveListener(OnConfirm);
    }

    void OnScan()
    {
        if (client != null)
            client.RequestScan();
    }

    void OnRescan()
    {
        if (client != null)
            client.Rescan();
    }

    void OnConfirm()
    {
        if (client != null)
            client.Confirm();
    }

    public void Render(ScanState state, Texture2D frame, IReadOnlyList<CardEntry> cards, string message)
    {
        if (frameImage != null)
        {
            frameImage.texture = frame;
            frameImage.enabled = frame != null && state != ScanState.Ready;
        }

        if (statusText != null)
            statusText.text = StatusLine(state, cards, message);

        if (listText != null)
            listText.text = CardList(state, cards);

        if (scanButton != null)
            scanButton.interactable = state == ScanState.Ready || state == ScanState.Error;

        if (rescanButton != null)
            rescanButton.interactable = state != ScanState.Scanning;

        // Confirm needs at least one card, per the spec.
        if (confirmButton != null)
            confirmButton.interactable = state == ScanState.Reviewing && cards.Count > 0;
    }

    static string StatusLine(ScanState state, IReadOnlyList<CardEntry> cards, string message)
    {
        switch (state)
        {
            case ScanState.Scanning:
                return "Scanning...";

            case ScanState.Reviewing:
                return cards.Count == 0 ? "No cards detected" : $"{cards.Count} card(s) detected";

            case ScanState.Confirmed:
                return $"Confirmed {cards.Count} cards.";

            case ScanState.Error:
                return string.IsNullOrEmpty(message) ? "Connection failed" : $"Connection failed\n{message}";

            default:
                return "Ready. Look at the cards, then tap Scan.";
        }
    }

    static string CardList(ScanState state, IReadOnlyList<CardEntry> cards)
    {
        if (state == ScanState.Scanning)
            return "Waiting for result...";

        if (cards == null || cards.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();

        for (int i = 0; i < cards.Count; i++)
        {
            var card = cards[i];

            if (i > 0)
                sb.Append('\n');

            sb.Append(card.DisplayName);

            // A user-added card has no model score, so do not invent one.
            if (card.source == CardSource.Model && card.confidence >= 0f)
                sb.Append($"   ({card.confidence:P0})");
        }

        return sb.ToString();
    }
}
