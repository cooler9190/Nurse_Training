using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The hand over: a floating Hand over button on the interview screen, and a full-screen comparison of the
/// player's priorities with the correct ones. Diagnoses and cues each get a row that says whether the player put
/// them in the right place, where they belong and why. It holds no game logic: <see cref="HandoverFlow"/> decides
/// when the button shows and builds the <see cref="HandoverReport"/> with <see cref="PriorityEvaluator"/> from the
/// patient's <see cref="SO_Patient"/>.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class HandoverScreenUI : MonoBehaviour
{
    /// <summary>The Hand over button was clicked.</summary>
    public event Action OnHandoverClicked;
    /// <summary>The Back button was clicked and the screen closed.</summary>
    public event Action OnClosed;
    /// <summary>Finish case was clicked: the player is done with this patient.</summary>
    public event Action OnFinishClicked;

    UIDocument document;
    VisualElement screen;
    Button handoverButton;
    Button backButton;
    Button finishButton;
    Label summary;
    Label explanation;
    VisualElement diagnosisList;
    VisualElement cueList;
    bool buttonVisible;

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
        if (handoverButton != null) { handoverButton.clicked -= HandleHandover; }
        if (backButton != null) { backButton.clicked -= Close; }
        if (finishButton != null) { finishButton.clicked -= HandleFinish; }
        document = null;
    }

    /// <summary>Shows or hides the floating Hand over button.</summary>
    public void SetButtonVisible(bool visible)
    {
        buttonVisible = visible;
        EnsureBound();
        if (handoverButton != null)
        {
            handoverButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    /// <summary>Opens the comparison screen for the given report.</summary>
    public void Show(HandoverReport report)
    {
        EnsureBound();
        if (document == null)
        {
            Debug.LogError("HandoverScreenUI: UIDocument is not ready.", this);
            return;
        }

        summary.text = report.Summary;
        explanation.text = report.Explanation ?? string.Empty;
        explanation.style.display = string.IsNullOrWhiteSpace(report.Explanation) ? DisplayStyle.None : DisplayStyle.Flex;

        diagnosisList.Clear();
        foreach (DiagnosisPriorityRow row in report.Diagnoses)
        {
            diagnosisList.Add(BuildRow(row.Verdict, row.Title, PlaceText(row.Expected, row.Yours, "your list"), row.Reason));
        }

        foreach (DistractorRow distractor in report.Distractors)
        {
            string note = distractor.RankedFirst
                ? "Your top priority, but the cues you ranked do not support it."
                : distractor.InSolutionSlot
                    ? "Ranked above a real diagnosis, but the cues you ranked do not support it."
                    : "On your list, but the cues you ranked do not support it.";
            diagnosisList.Add(BuildRow(PriorityVerdict.Wrong, distractor.Title, "Not a priority here", note, "Not supported"));
        }

        cueList.Clear();
        foreach (CuePriorityRow row in report.Cues)
        {
            string detail = !row.Found ? "You have not found this cue yet" : row.Yours == 0 ? "Found, but not ranked" : PlaceText(row.Expected, row.Yours, "your ranking");
            cueList.Add(BuildRow(row.Verdict, row.Title, detail, row.Reason, !row.Found ? "Not found" : null));
        }

        screen.style.display = DisplayStyle.Flex;
        handoverButton.style.display = DisplayStyle.None;
    }

    public void Hide()
    {
        if (screen != null)
        {
            screen.style.display = DisplayStyle.None;
        }

        if (handoverButton != null)
        {
            handoverButton.style.display = buttonVisible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    static string PlaceText(int expected, int yours, string where)
    {
        return yours > 0 ? $"Belongs at {expected}. You put it at {yours} in {where}." : $"Belongs at {expected}. It is not on {where}.";
    }

    static VisualElement BuildRow(PriorityVerdict verdict, string title, string detail, string reason, string tagOverride = null)
    {
        VisualElement row = new VisualElement();
        row.AddToClassList("handover-row");
        row.AddToClassList($"handover-row--{verdict.ToString().ToLowerInvariant()}");

        VisualElement top = new VisualElement();
        top.AddToClassList("handover-row-top");
        Label tag = new Label(tagOverride ?? TagText(verdict));
        tag.AddToClassList("handover-row-tag");
        top.Add(tag);
        Label name = new Label(title);
        name.AddToClassList("handover-row-title");
        top.Add(name);
        row.Add(top);

        Label detailLabel = new Label(detail);
        detailLabel.AddToClassList("handover-row-detail");
        row.Add(detailLabel);

        if (!string.IsNullOrWhiteSpace(reason))
        {
            Label reasonLabel = new Label(reason);
            reasonLabel.AddToClassList("handover-row-reason");
            row.Add(reasonLabel);
        }

        return row;
    }

    static string TagText(PriorityVerdict verdict)
    {
        switch (verdict)
        {
            case PriorityVerdict.Correct: return "Right place";
            case PriorityVerdict.NearlyThere: return "One place off";
            case PriorityVerdict.Wrong: return "Wrong place";
            default: return "Missing";
        }
    }

    void Close()
    {
        Hide();
        OnClosed?.Invoke();
    }

    void HandleHandover()
    {
        OnHandoverClicked?.Invoke();
    }

    void HandleFinish()
    {
        OnFinishClicked?.Invoke();
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

        document = candidate;
        screen = root.Q<VisualElement>("handover-screen");
        handoverButton = root.Q<Button>("handover-button");
        backButton = root.Q<Button>("handover-back");
        finishButton = root.Q<Button>("handover-finish");
        summary = root.Q<Label>("handover-summary");
        explanation = root.Q<Label>("handover-explanation");
        diagnosisList = root.Q<ScrollView>("handover-diagnoses")?.contentContainer;
        cueList = root.Q<ScrollView>("handover-cues")?.contentContainer;

        if (handoverButton != null) { handoverButton.clicked += HandleHandover; }
        if (backButton != null) { backButton.clicked += Close; }
        if (finishButton != null) { finishButton.clicked += HandleFinish; }

        screen.style.display = DisplayStyle.None;
        handoverButton.style.display = buttonVisible ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
