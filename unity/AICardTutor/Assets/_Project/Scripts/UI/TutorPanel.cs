using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// Draws <see cref="TutorAdvice"/>. Pure view: it subscribes to <see cref="TutorService"/> and
/// never reads the rules, the camera or the session itself.
///
/// Three lines, each optional:
///   legalText   - the headline, e.g. "Can play (2): Blue 7, Red 3"
///   reasonText  - one sentence per playable card
///   blockedText - why the remaining cards cannot be played, dimmed
///
/// P0 has no card selection, so reasonText lists every playable card rather than the reason for a
/// single highlighted one. When selection arrives, pass the index in and render one line.
/// </summary>
public class TutorPanel : MonoBehaviour
{
    [Tooltip("Leave empty to find the TutorService automatically.")]
    public TutorService tutor;

    [Header("Display")]
    public TMP_Text legalText;
    public TMP_Text reasonText;
    public TMP_Text blockedText;

    [Tooltip("Longest list of reasons to print. Two fits the panel; more spills into the row below.")]
    public int maxReasonLines = 2;

    readonly StringBuilder builder = new StringBuilder(256);

    void Awake()
    {
        if (tutor == null)
            tutor = FindFirstObjectByType<TutorService>();

        if (GetComponent<AiTutorPanel>() == null)
            gameObject.AddComponent<AiTutorPanel>();
    }

    void OnEnable()
    {
        if (tutor != null)
        {
            tutor.Changed += Render;
            return;
        }

        // The service may appear after this object is enabled, so try once more on the first frame.
        StartCoroutine(BindWhenReady());
    }

    void OnDisable()
    {
        if (tutor != null)
            tutor.Changed -= Render;
    }

    System.Collections.IEnumerator BindWhenReady()
    {
        yield return null;

        if (tutor == null)
            tutor = FindFirstObjectByType<TutorService>();

        if (tutor == null)
            yield break;

        tutor.Changed += Render;

        if (tutor.Current != null)
            Render(tutor.Current);
    }

    public void Render(TutorAdvice advice)
    {
        if (advice == null)
            return;

        if (legalText != null)
        {
            var faces = GetComponent<CardHandView>();
            legalText.text = advice.Ready && faces != null && faces.IsAvailable
                ? new TextRef(advice.LegalCount == 1 ? "tutor.playableOne" : "tutor.playableMany",
                    new TextVar("n", advice.LegalCount.ToString())).Resolve()
                : advice.Summary.Resolve();
        }

        if (reasonText != null)
            reasonText.text = advice.Ready ? Join(advice.LegalLines, maxReasonLines) : string.Empty;

        if (blockedText != null)
            blockedText.text = advice.Ready
                ? Join(advice.BlockedLines, TutorAdvice.MaxBlockedLines)
                : string.Empty;
    }

    string Join(IReadOnlyList<TextRef> lines, int max)
    {
        if (lines == null || lines.Count == 0 || max <= 0)
            return string.Empty;

        builder.Length = 0;
        int count = Mathf.Min(lines.Count, max);

        for (int i = 0; i < count; i++)
        {
            if (i > 0)
                builder.Append('\n');

            builder.Append("- ").Append(lines[i].Resolve());
        }

        // Say so when the list was cut, otherwise a truncated list looks like the whole story.
        if (lines.Count > count)
            builder.Append('\n').Append("- ...");

        return builder.ToString();
    }
}
