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

    void Awake()
    {
        if (client == null)
            client = FindFirstObjectByType<CardScanClient>();

        if (scanButton != null)
            scanButton.onClick.AddListener(OnScan);

        if (rescanButton != null)
            rescanButton.onClick.AddListener(OnRescan);

        if (confirmButton != null)
            confirmButton.onClick.AddListener(OnConfirm);
    }

    void Start()
    {
        // CardScanClient resolves its view in Awake, which may run before this component exists.
        if (client != null && client.State == ScanState.Ready)
            Render(ScanState.Ready, null, client.Cards, string.Empty);
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
