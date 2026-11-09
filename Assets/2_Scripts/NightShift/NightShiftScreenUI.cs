using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>One bell call as the screen shows it.</summary>
public class NightCallCard
{
    public string Time;
    public string Bed;
    public string Hint;
    /// <summary>Only shown once the call is done, so the name does not give the case away beforehand.</summary>
    public string PatientName;
    /// <summary>The clock has reached the time of the call.</summary>
    public bool Ringing;
    public bool Done;
    /// <summary>The shift ended before the call was done.</summary>
    public bool Missed;
    public int Percent;
    public ScoreLevel Level;
}

/// <summary>
/// The night shift screens: a call list beside the ward map with the clock, and an end-of-shift summary. It holds
/// no game logic: <see cref="NightShiftFlow"/> decides what to show and reacts to the clicks.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class NightShiftScreenUI : MonoBehaviour
{
    /// <summary>The player chose a call; the value is its index in the shift.</summary>
    public event Action<int> OnCallClicked;
    /// <summary>Start again was clicked on the summary.</summary>
    public event Action OnRestartClicked;
    /// <summary>Back to the ward was clicked on the summary.</summary>
    public event Action OnSummaryClosed;

    UIDocument document;
    VisualElement panel;
    VisualElement summary;
    Label title;
    Label clock;
    Label progress;
    VisualElement callList;
    Label summaryTitle;
    Label summaryScore;
    VisualElement summaryList;
    VisualElement clockBar;
    Label clockBarTime;
    VisualElement clockBarFill;
    string lastClock = string.Empty;
    float lastElapsed;
    bool clockBarVisible;
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

    /// <summary>Shows the call list beside the map.</summary>
    public void ShowCalls(string shiftName, string clockTime, IReadOnlyList<NightCallCard> cards)
    {
        pendingView = () => ApplyCalls(shiftName, clockTime, cards);
        Apply();
    }

    /// <summary>Shows the end-of-shift summary. <paramref name="timeUp"/> is true when the clock ran out.</summary>
    public void ShowSummary(string shiftName, int percent, ScoreLevel level, IReadOnlyList<NightCallCard> cards, bool timeUp = false)
    {
        pendingView = () => ApplySummary(shiftName, percent, level, cards, timeUp);
        Apply();
    }

    /// <summary>Updates the clock on the call list and the small clock shown during an interview.</summary>
    /// <param name="elapsed01">How much of the shift has passed, from 0 to 1.</param>
    public void SetClock(string time, float elapsed01)
    {
        lastClock = time;
        lastElapsed = elapsed01;
        ApplyClock();
    }

    /// <summary>Shows or hides the small clock that stays on screen during an interview.</summary>
    public void SetClockBarVisible(bool visible)
    {
        clockBarVisible = visible;
        EnsureBound();
        if (clockBar != null)
        {
            clockBar.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    void ApplyClock()
    {
        if (document == null)
        {
            return;
        }

        if (clock != null) { clock.text = lastClock; }
        if (clockBarTime != null) { clockBarTime.text = lastClock; }
        if (clockBarFill != null)
        {
            clockBarFill.style.width = Length.Percent(Mathf.Clamp01(lastElapsed) * 100f);
            clockBarFill.EnableInClassList("nightshift-clockbar-fill--late", lastElapsed > 0.9f);
        }
    }

    public void Hide()
    {
        pendingView = null;
        if (panel != null) { panel.style.display = DisplayStyle.None; }
        if (summary != null) { summary.style.display = DisplayStyle.None; }
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

    void ApplyCalls(string shiftName, string clockTime, IReadOnlyList<NightCallCard> cards)
    {
        title.text = shiftName;
        clock.text = clockTime;

        int done = 0;
        foreach (NightCallCard card in cards)
        {
            if (card.Done) { done++; }
        }

        progress.text = $"{done} of {cards.Count} calls done";
        callList.Clear();
        for (int i = 0; i < cards.Count; i++)
        {
            callList.Add(BuildCall(i, cards[i]));
        }

        summary.style.display = DisplayStyle.None;
        panel.style.display = DisplayStyle.Flex;
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

        panel.style.display = DisplayStyle.None;
        summary.style.display = DisplayStyle.Flex;
    }

    VisualElement BuildCall(int index, NightCallCard card)
    {
        VisualElement element = BuildCard(card);
        if (!card.Done && !card.Ringing && !card.Missed)
        {
            element.AddToClassList("nightshift-call--waiting");
        }

        // A call is attended once, so a finished call only shows its result
        if (card.Done)
        {
            Label result = new Label($"{card.PatientName}: {card.Percent}% - {card.Level}");
            result.AddToClassList("nightshift-call-result");
            element.Add(result);
            return element;
        }

        if (card.Missed)
        {
            Label missed = new Label("Missed - the shift ended - 0%");
            missed.AddToClassList("nightshift-call-result");
            missed.AddToClassList("nightshift-call-result--missed");
            element.Add(missed);
            return element;
        }

        Button go = new Button(() => OnCallClicked?.Invoke(index)) { text = card.Ringing ? "Attend" : "Not ringing yet" };
        go.AddToClassList("nightshift-button");
        if (card.Ringing) { go.AddToClassList("nightshift-button--primary"); }
        go.SetEnabled(card.Ringing);
        element.Add(go);
        return element;
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

        // A call that has not rung yet gives nothing away
        Label hint = new Label(card.Ringing || card.Done || card.Missed ? card.Hint : "Quiet for now.");
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

        panel = root.Q<VisualElement>("nightshift-panel");
        summary = root.Q<VisualElement>("nightshift-summary");
        title = root.Q<Label>("nightshift-title");
        clock = root.Q<Label>("nightshift-clock");
        progress = root.Q<Label>("nightshift-progress");
        callList = root.Q<ScrollView>("nightshift-calls")?.contentContainer;
        summaryTitle = root.Q<Label>("nightshift-summary-title");
        summaryScore = root.Q<Label>("nightshift-summary-score");
        clockBar = root.Q<VisualElement>("nightshift-clockbar");
        clockBarTime = root.Q<Label>("nightshift-clockbar-time");
        clockBarFill = root.Q<VisualElement>("nightshift-clockbar-fill");
        summaryList = root.Q<ScrollView>("nightshift-summary-list")?.contentContainer;
        restartButton = root.Q<Button>("nightshift-restart");
        closeButton = root.Q<Button>("nightshift-close");
        if (panel == null || summary == null || callList == null || summaryList == null)
        {
            Debug.LogError("NightShiftScreenUI: the UXML is missing elements.", this);
            return;
        }

        document = candidate;
        if (restartButton != null) { restartButton.clicked += HandleRestart; }
        if (closeButton != null) { closeButton.clicked += HandleClose; }
        GameAudio.RegisterButtonClicks(root);

        panel.style.display = DisplayStyle.None;
        summary.style.display = DisplayStyle.None;
        if (clockBar != null) { clockBar.style.display = clockBarVisible ? DisplayStyle.Flex : DisplayStyle.None; }
        ApplyClock();
        pendingView?.Invoke();
    }
}
