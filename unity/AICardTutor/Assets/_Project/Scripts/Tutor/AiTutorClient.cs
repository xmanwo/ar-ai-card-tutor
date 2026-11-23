using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>Explicit, one-shot requests. No cloud keys or camera images enter this component.</summary>
public class AiTutorClient : MonoBehaviour
{
    public enum Availability { MissingTutor, MissingSession, MissingBoth, MissingHand, MissingDiscard, MissingColor, Busy, Ready }

    public TutorService tutor;
    public HttpCardScanSource scanSource;
    public int timeoutSeconds = 30;
    public bool Busy => activeRequest != null;
    public string Answer { get; private set; } = string.Empty;
    public event Action Changed;

    public Availability State
    {
        get
        {
            if (tutor == null) return Availability.MissingTutor;
            var saved = tutor.session;
            if (saved == null) return Availability.MissingSession;
            if (!saved.HasHand && saved.Discard == null) return Availability.MissingBoth;
            if (!saved.HasHand) return Availability.MissingHand;
            if (saved.Discard == null) return Availability.MissingDiscard;
            if (string.IsNullOrEmpty(saved.ActiveColor)) return Availability.MissingColor;
            return Busy ? Availability.Busy : Availability.Ready;
        }
    }

    public string ReadinessMessage
    {
        get
        {
            switch (State)
            {
                case Availability.MissingTutor: return Local("Teaching service unavailable.", "教学服务未连接。");
                case Availability.MissingSession: return Local("Saved card state unavailable.", "无法读取已确认牌面。");
                case Availability.MissingBoth: return Local("Confirm the discard AND your hand.", "请分别确认桌顶与手牌。");
                case Availability.MissingHand: return Local("Discard saved. Confirm your hand.", "桌顶已保存，请确认手牌。");
                case Availability.MissingDiscard: return Local("Hand saved. Confirm the discard.", "手牌已保存，请确认桌顶。");
                case Availability.MissingColor: return Local("Choose and confirm the active colour.", "请选择并确认当前颜色。");
                case Availability.Busy: return Local("Waiting for DeepSeek...", "正在等待 DeepSeek……");
                default: return Local("Cards confirmed. AI is ready.", "牌面已确认，可以使用 AI。");
            }
        }
    }

    UnityWebRequest activeRequest;
    Coroutine pending;
    int generation;
    GameSessionState session;
    ConfirmedCard discard;
    object hand;
    string activeColor;
    bool drawFourRule;
    string language;

    [Serializable] public class CardData
    {
        public string id, color, value;
    }
    [Serializable] public class CheckedCardData
    {
        public string id, color, value, reason;
        public bool legal;
    }
    [Serializable] public class RequestData
    {
        public int snapshot_id;
        public string language, mode, active_color;
        public CheckedCardData[] hand;
        public CardData discard;
        public bool wild_draw_four_needs_no_active_colour;
    }
    [Serializable] public class ResponseData
    {
        public int snapshot_id;
        public string answer, suggested_card_id;
    }
    [Serializable] class ErrorData { public string detail; }

    void Start()
    {
        if (tutor == null) tutor = FindFirstObjectByType<TutorService>();
        if (scanSource == null) scanSource = FindFirstObjectByType<HttpCardScanSource>();
    }

    void Update()
    {
        if (session != null && !SnapshotMatches())
        {
            Cancel();
            Answer = Local("Position changed. Ask AI again after confirming the cards.",
                "牌面已变化，请确认后重新请求 AI。");
            session = null;
            Changed?.Invoke();
        }
    }

    bool SnapshotMatches()
    {
        return tutor != null && tutor.session == session && session.Hand == hand &&
            session.Discard == discard && session.ActiveColor == activeColor &&
            RuleFlag() == drawFourRule && CurrentLanguage() == language;
    }

    bool RuleFlag() => tutor.ruleSet == null || tutor.ruleSet.wildDrawFourNeedsNoActiveColour;
    static string CurrentLanguage() => Loc.Current == Language.Zh ? "zh" : "en";
    public static string Local(string en, string zh) => Loc.Current == Language.Zh ? zh : en;

    public void Ask(string mode)
    {
        if (Busy) return;
        if (tutor == null) tutor = FindFirstObjectByType<TutorService>();
        if (scanSource == null) scanSource = FindFirstObjectByType<HttpCardScanSource>();
        if (State != Availability.Ready)
        {
            Answer = ReadinessMessage;
            Changed?.Invoke();
            return;
        }
        if (mode != "explain" && mode != "hint") return;

        session = tutor.session;
        discard = session.Discard;
        hand = session.Hand;
        activeColor = session.ActiveColor;
        drawFourRule = RuleFlag();
        language = CurrentLanguage();
        int id = ++generation;
        var rules = new StandardUnoRules(tutor.ruleSet);
        var cards = new CheckedCardData[session.Hand.Count];
        for (int i = 0; i < cards.Length; i++)
        {
            ConfirmedCard card = session.Hand[i];
            PlayCheck check = rules.Check(card, session.Hand, discard, activeColor);
            cards[i] = new CheckedCardData {
                id = card.id, color = card.color, value = card.value,
                legal = check.Legal, reason = check.Reason.ToString()
            };
        }
        var context = new RequestData {
            snapshot_id = id, language = language, mode = mode, hand = cards,
            discard = new CardData { id = discard.id, color = discard.color, value = discard.value },
            active_color = activeColor, wild_draw_four_needs_no_active_colour = drawFourRule
        };
        string baseUrl = scanSource != null ? scanSource.ResolvedUrl : null;
        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = scanSource != null && scanSource.Transport == ServerTransport.Lan
                ? scanSource.serverUrl : scanSource != null ? scanSource.usbUrl : "http://127.0.0.1:8000";
        Answer = Local("Generating an explanation...", "正在生成解释……");
        pending = StartCoroutine(Send(context, baseUrl));
        Changed?.Invoke();
    }

    IEnumerator Send(RequestData context, string baseUrl)
    {
        using (var request = new UnityWebRequest(baseUrl.TrimEnd('/') + "/tutor/explain", "POST"))
        {
            activeRequest = request;
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(context)));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = timeoutSeconds;
            yield return request.SendWebRequest();
            activeRequest = null;
            pending = null;
            if (context.snapshot_id != generation || !SnapshotMatches()) yield break;

            if (request.result != UnityWebRequest.Result.Success)
            {
                string detail = string.Empty;
                try { detail = JsonUtility.FromJson<ErrorData>(request.downloadHandler.text)?.detail; }
                catch (Exception) { }
                Answer = Local("AI unavailable. The rule advice on the left still applies.",
                    "AI 暂时不可用，请参考左侧规则提示。");
                // Only sanitized server messages are logged; no request headers or keys.
                Debug.LogWarning($"[AI Tutor] HTTP {request.responseCode}: {detail}");
            }
            else
            {
                ResponseData response = null;
                try { response = JsonUtility.FromJson<ResponseData>(request.downloadHandler.text); }
                catch (Exception) { }
                bool valid = response != null && response.snapshot_id == context.snapshot_id &&
                    !string.IsNullOrWhiteSpace(response.answer) && response.answer.Length <= 1200;
                if (valid && !string.IsNullOrEmpty(response.suggested_card_id))
                    valid = context.mode == "hint" && Array.Exists(context.hand,
                        c => c.legal && c.id == response.suggested_card_id);
                Answer = valid ? response.answer : Local("AI returned an invalid answer. Use the rule advice.",
                    "AI 回答无效，请参考规则提示。");
            }
            Changed?.Invoke();
        }
    }

    void Cancel()
    {
        generation++;
        if (activeRequest != null) activeRequest.Abort();
        if (pending != null) StopCoroutine(pending);
        activeRequest = null;
        pending = null;
    }

    void OnDisable() { Cancel(); }
}
