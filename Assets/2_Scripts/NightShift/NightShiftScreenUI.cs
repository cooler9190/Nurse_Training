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
    public bool Done;
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
    Label summaryScore;
    VisualElement summaryList;
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

    /// <summary>Shows the end-of-shift summary.</summary>
    public void ShowSummary(string shiftName, int percent, ScoreLevel level, IReadOnlyList<NightCallCard> cards)
    {
        pendingView = () => ApplySummary(shiftName, percent, level, cards);
        Apply();
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

    void ApplySummary(string shiftName, int percent, ScoreLevel level, IReadOnlyList<NightCallCard> cards)
    {
        summaryScore.text = $"{shiftName}: {percent}% - {level}";
        summaryList.Clear();
        foreach (NightCallCard card in cards)
        {
            VisualElement row = BuildCard(card);
            Label result = new Label(card.Done ? $"{card.PatientName}: {card.Percent}% - {card.Level}" : "Not attended");
            result.AddToClassList("nightshift-call-result");
            row.Add(result);
            summaryList.Add(row);
        }

        panel.style.display = DisplayStyle.None;
        summary.style.display = DisplayStyle.Flex;
    }

    VisualElement BuildCall(int index, NightCallCard card)
    {
        VisualElement element = BuildCard(card);
        if (card.Done)
        {
            Label result = new Label($"{card.PatientName}: {card.Percent}% - {card.Level}");
            result.AddToClassList("nightshift-call-result");
            element.Add(result);
        }

        Button go = new Button(() => OnCallClicked?.Invoke(index)) { text = card.Done ? "Attend again" : "Attend" };
        go.AddToClassList("nightshift-button");
        if (!card.Done) { go.AddToClassList("nightshift-button--primary"); }
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

        panel = root.Q<VisualElement>("nightshift-panel");
        summary = root.Q<VisualElement>("nightshift-summary");
        title = root.Q<Label>("nightshift-title");
        clock = root.Q<Label>("nightshift-clock");
        progress = root.Q<Label>("nightshift-progress");
        callList = root.Q<ScrollView>("nightshift-calls")?.contentContainer;
        summaryScore = root.Q<Label>("nightshift-summary-score");
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
        pendingView?.Invoke();
    }
}
