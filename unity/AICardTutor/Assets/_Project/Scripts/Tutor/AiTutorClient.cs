using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>Explicit, one-shot requests. No cloud keys or camera images enter this component.</summary>
public class AiTutorClient : MonoBehaviour
{
    public enum Availability
    {
        MissingTutor, MissingSession, MissingBoth, MissingHand, MissingDiscard, MissingColor,
        Checking, Offline, Busy, Ready
    }

    const float HealthCheckInterval = 5f;

    public TutorService tutor;
    public HttpCardScanSource scanSource;
    public int timeoutSeconds = 30;
    public bool Busy => activeRequest != null;
    public bool CanAsk => State == Availability.Ready;
    public string Answer { get; private set; } = string.Empty;
    public event Action Changed;

    Availability BoardState
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
            return Availability.Ready;
        }
    }

    public Availability State
    {
        get
        {
            var board = BoardState;
            if (board != Availability.Ready) return board;
            if (Busy) return Availability.Busy;
            if (!serviceAvailable.HasValue) return Availability.Checking;
            return serviceAvailable.Value ? Availability.Ready : Availability.Offline;
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
                case Availability.Checking: return Local("Checking AI service…", "正在检查 AI 服务……");
                case Availability.Offline: return Local("AI offline · Local rules active", "AI 离线 · 本地规则可用");
                case Availability.Busy: return Local("Waiting for DeepSeek...", "正在等待 DeepSeek……");
                default: return retrySuggested
                    ? Local("AI reachable · Try again", "AI 服务可连接 · 可以重试")
                    : Local("Cards confirmed. AI is ready.", "牌面已确认，可以使用 AI。");
            }
        }
    }

    UnityWebRequest activeRequest;
    Coroutine pending;
    UnityWebRequest healthRequest;
    Coroutine pendingHealth;
    string checkedEndpoint;
    bool? serviceAvailable;
    bool retrySuggested;
    float nextHealthCheck;
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
        public bool uno_alert;
    }
    [Serializable] public class ResponseData
    {
        public int snapshot_id;
        public string answer, suggested_card_id;
    }
    [Serializable] class ErrorData { public string detail; }
    [Serializable] class HealthData { public bool configured; }

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
            Answer = string.Empty;
            session = null;
            Changed?.Invoke();
        }

        if (BoardState != Availability.Ready || Busy)
            return;

        string endpoint = ResolveBaseUrl();
        if (endpoint != checkedEndpoint)
        {
            CancelHealthCheck();
            checkedEndpoint = endpoint;
            serviceAvailable = null;
            nextHealthCheck = 0f;
            Answer = string.Empty;
        }

        if (pendingHealth == null && Time.unscaledTime >= nextHealthCheck)
            pendingHealth = StartCoroutine(CheckHealth(endpoint));
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

    string ResolveBaseUrl()
    {
        string url = scanSource != null ? scanSource.ResolvedUrl : null;
        if (string.IsNullOrWhiteSpace(url) && scanSource != null)
            url = scanSource.Transport == ServerTransport.Lan ? scanSource.serverUrl : scanSource.usbUrl;
        return string.IsNullOrWhiteSpace(url) ? "http://127.0.0.1:8000" : url.Trim().TrimEnd('/');
    }

    IEnumerator CheckHealth(string endpoint)
    {
        using (var request = UnityWebRequest.Get(endpoint + "/tutor/health"))
        {
            healthRequest = request;
            request.timeout = 3;
            yield return request.SendWebRequest();

            bool configured = false;
            if (request.result == UnityWebRequest.Result.Success)
            {
                try { configured = JsonUtility.FromJson<HealthData>(request.downloadHandler.text)?.configured == true; }
                catch (ArgumentException) { }
            }

            healthRequest = null;
            pendingHealth = null;
            nextHealthCheck = Time.unscaledTime + HealthCheckInterval;
            serviceAvailable = configured;
            if (!configured)
                Answer = string.Empty;
            Changed?.Invoke();
        }
    }

    void MarkUnavailable()
    {
        CancelHealthCheck();
        serviceAvailable = false;
        retrySuggested = true;
        nextHealthCheck = Time.unscaledTime + HealthCheckInterval;
        Answer = string.Empty;
    }

    public void Ask(string mode)
    {
        if (!CanAsk || ResolveBaseUrl() != checkedEndpoint) return;
        if (mode != "explain" && mode != "hint") return;

        var advice = tutor.Current;
        if (advice == null || !advice.Ready || advice.Verdicts.Count != tutor.session.Hand.Count) return;
        session = tutor.session;
        discard = session.Discard;
        hand = session.Hand;
        activeColor = session.ActiveColor;
        drawFourRule = RuleFlag();
        language = CurrentLanguage();
        int id = ++generation;
        var cards = new CheckedCardData[session.Hand.Count];
        for (int i = 0; i < cards.Length; i++)
        {
            ConfirmedCard card = session.Hand[i];
            PlayCheck check = advice.Verdicts[i];
            cards[i] = new CheckedCardData {
                id = card.id, color = card.color, value = card.value,
                legal = check.Legal, reason = check.Reason.ToString()
            };
        }
        var context = new RequestData {
            snapshot_id = id, language = language, mode = mode, hand = cards,
            discard = new CardData { id = discard.id, color = discard.color, value = discard.value },
            active_color = activeColor, wild_draw_four_needs_no_active_colour = drawFourRule,
            uno_alert = advice.UnoAlert
        };
        CancelHealthCheck();
        Answer = string.Empty;
        pending = StartCoroutine(Send(context, checkedEndpoint));
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
                MarkUnavailable();
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
                if (valid)
                {
                    Answer = response.answer;
                    retrySuggested = false;
                    nextHealthCheck = Time.unscaledTime + HealthCheckInterval;
                }
                else
                    MarkUnavailable();
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

    void CancelHealthCheck()
    {
        if (healthRequest != null) healthRequest.Abort();
        if (pendingHealth != null) StopCoroutine(pendingHealth);
        healthRequest = null;
        pendingHealth = null;
    }

    void OnDisable()
    {
        Cancel();
        CancelHealthCheck();
        checkedEndpoint = null;
        Answer = string.Empty;
    }
}
