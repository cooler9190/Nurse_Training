using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>One bell call as the end-of-shift summary shows it.</summary>
public class NightCallCard
{
    public string Time;
    public string Bed;
    public string Hint;
    public string PatientName;
    public bool Done;
    public int Percent;
    public ScoreLevel Level;
}

/// <summary>
/// The night shift screens: the ward clock, shown on the map and during an interview, and an end-of-shift summary.
/// Calls that are still to come are never listed, so each one is a surprise: the room that is calling blinks. It
/// holds no game logic: <see cref="NightShiftFlow"/> decides what to show and reacts to the clicks.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class NightShiftScreenUI : MonoBehaviour
{
    /// <summary>Start again was clicked on the summary.</summary>
    public event Action OnRestartClicked;
    /// <summary>Back to the ward was clicked on the summary.</summary>
    public event Action OnSummaryClosed;

    UIDocument document;
    VisualElement summary;
    Label summaryTitle;
    Label summaryScore;
    VisualElement summaryList;
    VisualElement clockBar;
    Label clockBarTime;
    VisualElement clockBarFill;
    string lastClock = string.Empty;
    float lastElapsed;
    Button restartButton;
    Button closeButton;
    // What to show, kept so a request made before the UIDocument is ready is not lost
    Action pendingView;

    void OnEnable()
    {
        EnsureBound();
    }

    // The UIDocument may build its visual tree after this OnEnable, so try again here.
    void Start()
    {
        EnsureBound();
    }

    void OnDisable()
    {
        if (restartButton != null) { restartButton.clicked -= HandleRestart; }
        if (closeButton != null) { closeButton.clicked -= HandleClose; }
        if (document != null) { GameAudio.UnregisterButtonClicks(document.rootVisualElement); }
        document = null;
    }

    /// <summary>Shows only the clock, in the corner beside the ward map.</summary>
    public void ShowMapClock()
    {
        pendingView = () => ApplyClockOnly(true);
        Apply();
    }

    /// <summary>Shows only the clock, in its place on the examination screens.</summary>
    public void ShowInterviewClock()
    {
        pendingView = () => ApplyClockOnly(false);
        Apply();
    }

    /// <summary>Shows the end-of-shift summary. <paramref name="timeUp"/> is true when the clock ran out.</summary>
    public void ShowSummary(string shiftName, int percent, ScoreLevel level, IReadOnlyList<NightCallCard> cards, bool timeUp = false)
    {
        pendingView = () => ApplySummary(shiftName, percent, level, cards, timeUp);
        Apply();
    }

    /// <summary>Updates the ward clock.</summary>
    /// <param name="elapsed01">How much of the shift has passed, from 0 to 1.</param>
    public void SetClock(string time, float elapsed01)
    {
        lastClock = time;
        lastElapsed = elapsed01;
        ApplyClock();
    }

    void ApplyClock()
    {
        if (document == null)
        {
            return;
        }

        if (clockBarTime != null) { clockBarTime.text = lastClock; }
        if (clockBarFill != null)
        {
            clockBarFill.style.width = Length.Percent(Mathf.Clamp01(lastElapsed) * 100f);
            clockBarFill.EnableInClassList("nightshift-clockbar-fill--late", lastElapsed > 0.9f);
        }
    }

    void Apply()
    {
        EnsureBound();
        if (document == null)
        {
            return;
        }

        pendingView?.Invoke();
    }

    void ApplyClockOnly(bool onMap)
    {
        summary.style.display = DisplayStyle.None;
        if (clockBar != null)
        {
            clockBar.EnableInClassList("nightshift-clockbar--map", onMap);
            clockBar.style.display = DisplayStyle.Flex;
        }
    }

    void ApplySummary(string shiftName, int percent, ScoreLevel level, IReadOnlyList<NightCallCard> cards, bool timeUp)
    {
        if (summaryTitle != null) { summaryTitle.text = timeUp ? "Shift over - 06:00" : "Shift complete"; }
        summaryScore.text = $"{shiftName}: {percent}% - {level}";
        summaryList.Clear();
        foreach (NightCallCard card in cards)
        {
            VisualElement row = BuildCard(card);
            Label result = new Label(card.Done ? $"{card.PatientName}: {card.Percent}% - {card.Level}" : "Missed - 0%");
            result.AddToClassList("nightshift-call-result");
            if (!card.Done) { result.AddToClassList("nightshift-call-result--missed"); }
            row.Add(result);
            summaryList.Add(row);
        }

        if (clockBar != null) { clockBar.style.display = DisplayStyle.None; }
        summary.style.display = DisplayStyle.Flex;
    }

    static VisualElement BuildCard(NightCallCard card)
    {
        VisualElement element = new VisualElement();
        element.AddToClassList("nightshift-call");
        if (card.Done) { element.AddToClassList("nightshift-call--done"); }

        VisualElement top = new VisualElement();
        top.AddToClassList("nightshift-call-top");
        Label time = new Label(card.Time);
        time.AddToClassList("nightshift-call-time");
        top.Add(time);
        Label bed = new Label(card.Bed);
        bed.AddToClassList("nightshift-call-bed");
        top.Add(bed);
        element.Add(top);

        Label hint = new Label(card.Hint);
        hint.AddToClassList("nightshift-call-hint");
        element.Add(hint);
        return element;
    }

    void HandleRestart()
    {
        OnRestartClicked?.Invoke();
    }

    void HandleClose()
    {
        OnSummaryClosed?.Invoke();
    }

    void EnsureBound()
    {
        if (document != null)
        {
            return;
        }

        UIDocument candidate = GetComponent<UIDocument>();
        VisualElement root = candidate != null ? candidate.rootVisualElement : null;
        if (root == null)
        {
            return;
        }

        summary = root.Q<VisualElement>("nightshift-summary");
        summaryTitle = root.Q<Label>("nightshift-summary-title");
        summaryScore = root.Q<Label>("nightshift-summary-score");
        clockBar = root.Q<VisualElement>("nightshift-clockbar");
        clockBarTime = root.Q<Label>("nightshift-clockbar-time");
        clockBarFill = root.Q<VisualElement>("nightshift-clockbar-fill");
        summaryList = root.Q<ScrollView>("nightshift-summary-list")?.contentContainer;
        restartButton = root.Q<Button>("nightshift-restart");
        closeButton = root.Q<Button>("nightshift-close");
        if (summary == null || summaryList == null)
        {
            Debug.LogError("NightShiftScreenUI: the UXML is missing elements.", this);
            return;
        }

        document = candidate;
        if (restartButton != null) { restartButton.clicked += HandleRestart; }
        if (closeButton != null) { closeButton.clicked += HandleClose; }
        GameAudio.RegisterButtonClicks(root);

        summary.style.display = DisplayStyle.None;
        if (clockBar != null) { clockBar.style.display = DisplayStyle.None; }
        ApplyClock();
        pendingView?.Invoke();
    }
}
