using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Full-screen hand-over after the interview: one multiple-choice step at a time (problems,
/// interventions, evaluation, SBAR...) followed by the results. Like <see cref="HypothesisScreenUI"/>
/// it holds no game logic; <see cref="PatientInterviewController"/> decides which step comes next
/// and what it is worth, and this class only shows it and reports what the player picked.
/// <para>
/// Flow: <see cref="ShowStep"/> lets the player pick and submit, <see cref="ShowStepFeedback"/>
/// then marks every option right or wrong with its explanation and offers Next, and
/// <see cref="ShowResults"/> closes the case with the score per category and the stars.
/// </para>
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class AssessmentScreenUI : MonoBehaviour
{
    /// <summary>Submit was pressed. Passes the indices of the selected options.</summary>
    public event Action<IReadOnlyList<int>> OnStepSubmitted;
    public event Action OnNextClicked;
    /// <summary>Back to the map was pressed on the results.</summary>
    public event Action OnResultsClosed;
    public event Action OnRetryClicked;

    UIDocument document;
    VisualElement stepPanel;
    VisualElement resultsPanel;
    Label stepSection;
    Label stepProgress;
    Label stepPrompt;
    Label stepHint;
    VisualElement stepOptions;
    Label stepRationale;
    Label stepPoints;
    Button submitButton;
    Button nextButton;
    Label resultsStars;
    Label resultsScore;
    VisualElement starRow;
    VisualElement resultsBreakdown;
    Button retryButton;
    Button closeButton;

    readonly List<VisualElement> optionElements = new List<VisualElement>();
    readonly List<int> selected = new List<int>();
    AssessmentStep currentStep;
    bool submitted;
    bool isOpen;

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
        Unbind();
    }

    /// <summary>Shows one step with nothing selected yet.</summary>
    public void ShowStep(AssessmentStep step, int number, int total)
    {
        EnsureBound();
        if (document == null || step == null)
        {
            return;
        }

        currentStep = step;
        submitted = false;
        selected.Clear();
        optionElements.Clear();

        stepSection.text = string.IsNullOrEmpty(step.framework)
            ? ScoreCategoryNames.Display(step.category)
            : $"{ScoreCategoryNames.Display(step.category)}  -  {step.framework}";
        stepProgress.text = $"Step {number} of {total}";
        stepPrompt.text = step.prompt;
        stepHint.text = step.kind == AssessmentKind.SingleChoice ? "Choose one answer." : "Choose all that apply. Wrong choices cost points.";
        stepRationale.text = string.Empty;
        stepRationale.style.display = DisplayStyle.None;
        stepPoints.text = string.Empty;

        stepOptions.Clear();
        for (int i = 0; i < step.options.Count; i++)
        {
            int index = i;
            VisualElement option = new VisualElement();
            option.AddToClassList("assessment-option");
            Label text = new Label(step.options[i].text);
            text.AddToClassList("assessment-option-text");
            option.Add(text);
            option.RegisterCallback<ClickEvent>(_ => ToggleOption(index));
            stepOptions.Add(option);
            optionElements.Add(option);
        }

        submitButton.style.display = DisplayStyle.Flex;
        submitButton.SetEnabled(false);
        nextButton.style.display = DisplayStyle.None;
        stepPanel.style.display = DisplayStyle.Flex;
        resultsPanel.style.display = DisplayStyle.None;
        SetVisible(true);
    }

    /// <summary>Locks the answer and shows, for every option, whether it was right and why.</summary>
    public void ShowStepFeedback(AssessmentStep step, IReadOnlyList<int> chosen, int points, bool isLast)
    {
        if (document == null || step == null)
        {
            return;
        }

        submitted = true;
        HashSet<int> chosenSet = new HashSet<int>(chosen);
        for (int i = 0; i < optionElements.Count; i++)
        {
            AssessmentOption option = step.options[i];
            VisualElement element = optionElements[i];
            element.AddToClassList("assessment-option--locked");
            element.EnableInClassList("assessment-option--selected", chosenSet.Contains(i));
            element.AddToClassList(option.points > 0 ? "assessment-option--good" : option.points < 0 ? "assessment-option--bad" : "assessment-option--neutral");

            Label chip = new Label(option.points > 0 ? $"+{option.points}" : option.points.ToString());
            chip.AddToClassList("assessment-option-points");
            element.Insert(0, chip);

            if (!string.IsNullOrWhiteSpace(option.feedback))
            {
                Label feedback = new Label(option.feedback);
                feedback.AddToClassList("assessment-option-feedback");
                element.Add(feedback);
            }
        }

        if (!string.IsNullOrWhiteSpace(step.rationale))
        {
            stepRationale.text = step.rationale;
            stepRationale.style.display = DisplayStyle.Flex;
        }

        stepPoints.text = $"You earned {points} of {step.MaxPoints} points";
        submitButton.style.display = DisplayStyle.None;
        nextButton.text = isLast ? "See results" : "Next";
        nextButton.style.display = DisplayStyle.Flex;
    }

    /// <summary>Shows the score per category, the stars, what cost points, and the learning goals.</summary>
    public void ShowResults(SO_Patient patient, CaseResult result, int bestStars)
    {
        EnsureBound();
        if (document == null)
        {
            return;
        }

        starRow.Clear();
        for (int i = 0; i < 3; i++)
        {
            VisualElement star = new VisualElement();
            star.AddToClassList("result-star");
            star.EnableInClassList("result-star--filled", i < result.Stars);
            starRow.Add(star);
        }

        string title = string.IsNullOrEmpty(patient.caseTitle) ? patient.patientDisplayName : patient.caseTitle;
        resultsStars.text = $"{title}  -  {RankName(result.Stars)}";
        resultsScore.text = $"{result.Earned} of {result.Max} points ({result.Percent}%)   Best: {bestStars} of 3 stars";

        resultsBreakdown.Clear();
        for (int i = 0; i < result.Lines.Count; i++)
        {
            resultsBreakdown.Add(BuildLine(result.Lines[i]));
        }

        if (result.Penalties.Count > 0)
        {
            AddHeading("Points lost");
            for (int i = 0; i < result.Penalties.Count; i++)
            {
                AddText(result.Penalties[i], "results-penalty");
            }
        }

        if (!string.IsNullOrWhiteSpace(patient.learningGoals))
        {
            AddHeading("Learning goals");
            AddText(patient.learningGoals, "results-text");
        }

        stepPanel.style.display = DisplayStyle.None;
        resultsPanel.style.display = DisplayStyle.Flex;
        SetVisible(true);
    }

    public void Hide()
    {
        SetVisible(false);
    }

    static string RankName(int stars)
    {
        switch (stars)
        {
            case 3: return "Excellent";
            case 2: return "Competent";
            case 1: return "Developing";
            default: return "Needs more practice";
        }
    }

    void ToggleOption(int index)
    {
        if (submitted || currentStep == null)
        {
            return;
        }

        if (currentStep.kind == AssessmentKind.SingleChoice)
        {
            selected.Clear();
            selected.Add(index);
        }
        else if (!selected.Remove(index))
        {
            selected.Add(index);
        }

        for (int i = 0; i < optionElements.Count; i++)
        {
            optionElements[i].EnableInClassList("assessment-option--selected", selected.Contains(i));
        }

        submitButton.SetEnabled(selected.Count > 0);
    }

    VisualElement BuildLine(ScoreLine line)
    {
        VisualElement row = new VisualElement();
        row.AddToClassList("results-line");

        VisualElement top = new VisualElement();
        top.AddToClassList("results-line-top");
        Label name = new Label(ScoreCategoryNames.Display(line.Category));
        name.AddToClassList("results-line-name");
        top.Add(name);
        Label value = new Label($"{line.Earned} / {line.Max}");
        value.AddToClassList("results-line-value");
        top.Add(value);
        row.Add(top);

        VisualElement track = new VisualElement();
        track.AddToClassList("results-bar");
        VisualElement fill = new VisualElement();
        fill.AddToClassList("results-bar-fill");
        fill.style.width = Length.Percent(line.Max > 0 ? 100f * line.Earned / line.Max : 0f);
        track.Add(fill);
        row.Add(track);
        return row;
    }

    void AddHeading(string text)
    {
        AddText(text, "results-heading");
    }

    void AddText(string text, string cssClass)
    {
        Label label = new Label(text);
        label.AddToClassList(cssClass);
        resultsBreakdown.Add(label);
    }

    void HandleSubmit()
    {
        if (!submitted && selected.Count > 0)
        {
            OnStepSubmitted?.Invoke(new List<int>(selected));
        }
    }

    void HandleNext()
    {
        OnNextClicked?.Invoke();
    }

    void HandleRetry()
    {
        OnRetryClicked?.Invoke();
    }

    void HandleClose()
    {
        OnResultsClosed?.Invoke();
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
        stepPanel = root.Q<VisualElement>("step-panel");
        resultsPanel = root.Q<VisualElement>("results-panel");
        stepSection = root.Q<Label>("step-section");
        stepProgress = root.Q<Label>("step-progress");
        stepPrompt = root.Q<Label>("step-prompt");
        stepHint = root.Q<Label>("step-hint");
        stepOptions = root.Q<VisualElement>("step-options");
        stepRationale = root.Q<Label>("step-rationale");
        stepPoints = root.Q<Label>("step-points");
        submitButton = root.Q<Button>("step-submit");
        nextButton = root.Q<Button>("step-next");
        resultsStars = root.Q<Label>("results-stars");
        resultsScore = root.Q<Label>("results-score");
        starRow = root.Q<VisualElement>("results-star-row");
        ScrollView breakdown = root.Q<ScrollView>("results-breakdown");
        resultsBreakdown = breakdown != null ? breakdown.contentContainer : null;
        retryButton = root.Q<Button>("results-retry");
        closeButton = root.Q<Button>("results-close");

        if (submitButton != null) { submitButton.clicked += HandleSubmit; }
        if (nextButton != null) { nextButton.clicked += HandleNext; }
        if (retryButton != null) { retryButton.clicked += HandleRetry; }
        if (closeButton != null) { closeButton.clicked += HandleClose; }

        SetVisible(isOpen);
    }

    void Unbind()
    {
        if (submitButton != null) { submitButton.clicked -= HandleSubmit; }
        if (nextButton != null) { nextButton.clicked -= HandleNext; }
        if (retryButton != null) { retryButton.clicked -= HandleRetry; }
        if (closeButton != null) { closeButton.clicked -= HandleClose; }
        document = null;
    }

    // The whole document root is hidden so that, when closed, it can't block clicks on the interview below.
    void SetVisible(bool visible)
    {
        isOpen = visible;
        if (document == null)
        {
            return;
        }

        document.rootVisualElement.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
