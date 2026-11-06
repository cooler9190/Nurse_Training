using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

/// <summary>
/// The overlay used while one diagnosis is being shared. It does not own the rules: the hypothesis
/// screen decides what frustration and the Shared flag do. This only shows the current step.
/// </summary>
public class SharedDecisionDialogue
{
    public event Action OnDiscussedProsCons;
    public event Action OnSkippedProsCons;
    public event Action OnDecideWithPatient;
    public event Action OnDecideAlone;
    public event Action<int> OnTreatmentPicked;
    public event Action OnWalkoutAcknowledged;

    readonly VisualElement root;
    readonly Label stageLabel;
    readonly Label purposeLabel;
    readonly Label introduction;
    readonly VisualElement options;
    readonly VisualElement choices;
    readonly Label spoken;
    readonly Button continueButton;
    readonly Button leaveButton;

    Action continueAction;
    bool open;
    bool walkingOut;

    public bool IsOpen => open;

    public SharedDecisionDialogue(VisualElement root)
    {
        this.root = root;
        if (root == null)
        {
            return;
        }

        stageLabel = root.Q<Label>("share-stage");
        purposeLabel = root.Q<Label>("share-purpose");
        introduction = root.Q<Label>("share-introduction");
        options = root.Q<VisualElement>("share-options");
        choices = root.Q<VisualElement>("share-choices");
        spoken = root.Q<Label>("share-spoken");
        continueButton = root.Q<Button>("share-continue");
        leaveButton = root.Q<Button>("share-leave");

        if (leaveButton != null)
        {
            leaveButton.clicked += Hide;
        }

        if (continueButton != null)
        {
            continueButton.clicked += Continue;
        }

        Hide();
    }

    public void Dispose()
    {
        if (leaveButton != null)
        {
            leaveButton.clicked -= Hide;
        }

        if (continueButton != null)
        {
            continueButton.clicked -= Continue;
        }

        OnDiscussedProsCons = null;
        OnSkippedProsCons = null;
        OnDecideWithPatient = null;
        OnDecideAlone = null;
        OnTreatmentPicked = null;
        OnWalkoutAcknowledged = null;
    }

    public void Hide()
    {
        walkingOut = false;
        open = false;
        continueAction = null;
        if (root != null)
        {
            root.style.display = DisplayStyle.None;
        }
    }

    public void ShowGoals(string patientName, string goalsAndFears)
    {
        string text = string.IsNullOrWhiteSpace(goalsAndFears)
            ? "They are not sure what else matters to them right now."
            : goalsAndFears;
        ShowPanel("Their goals and fears", "Asking what matters to them brings their frustration down.", text, null);
        ShowSpoken($"You explain the diagnosis and ask what matters to them.\n\n{Name(patientName)}: {text}");
        continueAction = Hide;
    }

    public void ShowDiscuss(TreatmentOption treatment, string patientName)
    {
        string title = treatment != null ? treatment.title : "This treatment";
        ShowPanel(title, "Discuss the pros and cons with the patient, or skip them.", null, null);
        ClearSpoken();
        AddChoice("Discuss the pros and cons.", () => ShowProsCons(treatment, patientName));
        AddChoice("Skip the pros and cons.", () => ShowSkip(patientName));
    }

    public void ShowDecision()
    {
        ShowPanel("Decide on a treatment", "Let the patient's preference drive the choice, or decide alone.", null, null);
        ClearSpoken();
        AddChoice("Decide with the patient.", () => FinishChoice(OnDecideWithPatient));
        AddChoice("Decide yourself.", () => FinishChoice(OnDecideAlone));
    }

    public void ShowTreatmentPick(IReadOnlyList<TreatmentOption> treatments, bool withPatient)
    {
        ShowPanel(withPatient ? "Choose together" : "Your decision",
            withPatient ? "Ask which treatment fits what matters to them." : "Tell them which treatment they will have.",
            null, null);
        ClearSpoken();
        if (treatments == null)
        {
            return;
        }

        for (int i = 0; i < treatments.Count; i++)
        {
            TreatmentOption treatment = treatments[i];
            if (treatment == null || string.IsNullOrWhiteSpace(treatment.title))
            {
                continue;
            }

            int index = i;
            AddChoice(treatment.title, () =>
            {
                Hide();
                OnTreatmentPicked?.Invoke(index);
            });
        }
    }

    public void ShowWalkout(string patientName)
    {
        walkingOut = true;
        ShowPanel("The patient is leaving", "Their frustration reached the limit.", null, null);
        ShowSpoken($"{Name(patientName)}: I've had enough. I'm leaving.");
        if (leaveButton != null)
        {
            leaveButton.style.display = DisplayStyle.None;
        }

        continueAction = () =>
        {
            walkingOut = false;
            open = false;
            continueAction = null;
            if (root != null)
            {
                root.style.display = DisplayStyle.None;
            }

            OnWalkoutAcknowledged?.Invoke();
        };
    }

    void ShowProsCons(TreatmentOption treatment, string patientName)
    {
        string pros = treatment != null ? treatment.pros : string.Empty;
        string cons = treatment != null ? treatment.cons : string.Empty;
        options?.Clear();
        if (options != null)
        {
            if (!string.IsNullOrWhiteSpace(pros))
            {
                options.Add(Line($"Pros: {pros}"));
            }

            if (!string.IsNullOrWhiteSpace(cons))
            {
                options.Add(Line($"Cons: {cons}"));
            }

            options.style.display = options.childCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (choices != null)
        {
            choices.style.display = DisplayStyle.None;
        }

        ShowSpoken($"{Name(patientName)}: I understand the benefits and the drawbacks.");
        continueAction = () => FinishChoice(OnDiscussedProsCons);
    }

    void ShowSkip(string patientName)
    {
        if (choices != null)
        {
            choices.style.display = DisplayStyle.None;
        }

        ShowSpoken($"{Name(patientName)}: You have not told me what this treatment would mean for me.");
        continueAction = () => FinishChoice(OnSkippedProsCons);
    }

    void FinishChoice(Action callback)
    {
        Hide();
        callback?.Invoke();
    }

    void ShowPanel(string heading, string purpose, string introductionText, Action onContinue)
    {
        if (root == null)
        {
            return;
        }

        open = true;
        root.style.display = DisplayStyle.Flex;
        if (stageLabel != null)
        {
            stageLabel.text = heading ?? string.Empty;
        }

        if (purposeLabel != null)
        {
            purposeLabel.text = purpose ?? string.Empty;
        }

        if (introduction != null)
        {
            introduction.text = introductionText ?? string.Empty;
            introduction.style.display = string.IsNullOrWhiteSpace(introductionText) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        if (options != null)
        {
            options.Clear();
            options.style.display = DisplayStyle.None;
        }

        if (choices != null)
        {
            choices.Clear();
            choices.style.display = DisplayStyle.Flex;
        }

        ClearSpoken();
        if (continueButton != null)
        {
            continueButton.style.display = DisplayStyle.None;
        }

        if (leaveButton != null)
        {
            leaveButton.style.display = walkingOut ? DisplayStyle.None : DisplayStyle.Flex;
        }

        continueAction = onContinue;
    }

    void AddChoice(string text, Action onClick)
    {
        if (choices == null)
        {
            return;
        }

        Button button = new Button(onClick) { text = text };
        button.AddToClassList("share-choice");
        button.style.whiteSpace = WhiteSpace.Normal;
        button.style.height = StyleKeyword.Auto;
        choices.Add(button);
    }

    void ShowSpoken(string text)
    {
        if (spoken == null)
        {
            return;
        }

        spoken.text = text;
        spoken.style.display = DisplayStyle.Flex;
        if (continueButton != null)
        {
            continueButton.style.display = DisplayStyle.Flex;
        }
    }

    void ClearSpoken()
    {
        if (spoken != null)
        {
            spoken.text = string.Empty;
            spoken.style.display = DisplayStyle.None;
        }
    }

    void Continue()
    {
        Action action = continueAction;
        continueAction = null;
        action?.Invoke();
    }

    static string Name(string patientName)
    {
        return string.IsNullOrWhiteSpace(patientName) ? "Patient" : patientName;
    }

    static Label Line(string text)
    {
        Label label = new Label(text);
        label.AddToClassList("share-option-text");
        return label;
    }
}
