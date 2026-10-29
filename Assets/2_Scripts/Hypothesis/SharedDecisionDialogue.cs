using System;
using UnityEngine.UIElements;

/// <summary>
/// The Three-talk overlay on the hypothesis screen. One diagnosis at a time: team talk, option talk,
/// then decision talk. Finishing decision talk raises <see cref="OnFinished"/> with the diagnosis id.
/// Leaving early does not. The same elements are reused for every diagnosis.
/// </summary>
public class SharedDecisionDialogue
{
    /// <summary>The player finished decision talk for this diagnosis id.</summary>
    public event Action<string> OnFinished;

    static readonly string[] StageNames = { "Team talk", "Option talk", "Decision talk" };
    static readonly string[] StagePurposes =
    {
        "Describe the choices, offer support, and ask about goals.",
        "Lay the options out, with the pros and cons.",
        "Let the patient's preferences drive the decision."
    };

    readonly VisualElement root;
    readonly Label stageLabel;
    readonly Label purposeLabel;
    readonly Label introduction;
    readonly VisualElement options;
    readonly VisualElement choices;
    readonly Label spoken;
    readonly Button continueButton;
    readonly Button leaveButton;

    Diagnosis diagnosis;
    string patientName = "Patient";
    int stage;
    bool open;

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

    /// <summary>Unsubscribes the buttons. Call this before the visual tree is released.</summary>
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

        OnFinished = null;
    }

    /// <summary>Opens the overlay on team talk for <paramref name="diagnosis"/>.</summary>
    public void Begin(Diagnosis diagnosis, string speakerName)
    {
        if (root == null || diagnosis == null)
        {
            return;
        }

        this.diagnosis = diagnosis;
        patientName = string.IsNullOrWhiteSpace(speakerName) ? "Patient" : speakerName;
        stage = 0;
        open = true;
        ShowStage();
        root.style.display = DisplayStyle.Flex;
    }

    /// <summary>Closes the overlay without marking the diagnosis shared.</summary>
    public void Hide()
    {
        diagnosis = null;
        open = false;
        if (root != null)
        {
            root.style.display = DisplayStyle.None;
        }
    }

    void ShowStage()
    {
        SharedDecisionTalk talk = diagnosis != null ? diagnosis.sharedDecision : null;
        TalkStage current = talk != null ? talk.StageAt(stage) : null;

        if (stageLabel != null)
        {
            stageLabel.text = StageNames[stage];
        }

        if (purposeLabel != null)
        {
            purposeLabel.text = StagePurposes[stage];
        }

        if (introduction != null)
        {
            introduction.text = current != null ? current.introduction : string.Empty;
            introduction.style.display = string.IsNullOrWhiteSpace(introduction.text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        if (options != null)
        {
            options.Clear();
            bool showOptions = stage == 1 && talk != null && talk.options != null;
            if (showOptions)
            {
                foreach (TreatmentOption option in talk.options)
                {
                    if (option != null && !string.IsNullOrWhiteSpace(option.title))
                    {
                        options.Add(BuildOption(option));
                    }
                }
            }

            options.style.display = options.childCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (choices != null)
        {
            choices.Clear();
            choices.style.display = DisplayStyle.Flex;
            if (current != null && current.choices != null)
            {
                foreach (TalkChoice choice in current.choices)
                {
                    if (choice == null || string.IsNullOrWhiteSpace(choice.playerLine))
                    {
                        continue;
                    }

                    TalkChoice captured = choice;
                    Button button = new Button(() => Choose(captured)) { text = captured.playerLine };
                    button.AddToClassList("share-choice");
                    button.style.whiteSpace = WhiteSpace.Normal;
                    button.style.height = StyleKeyword.Auto;
                    choices.Add(button);
                }
            }
        }

        if (spoken != null)
        {
            spoken.style.display = DisplayStyle.None;
        }

        if (continueButton != null)
        {
            continueButton.style.display = DisplayStyle.None;
        }
    }

    void Choose(TalkChoice choice)
    {
        if (choices != null)
        {
            choices.style.display = DisplayStyle.None;
        }

        if (spoken != null)
        {
            spoken.text = $"You: {choice.playerLine}\n\n{patientName}: {choice.patientResponse}";
            spoken.style.display = DisplayStyle.Flex;
        }

        if (continueButton != null)
        {
            continueButton.style.display = DisplayStyle.Flex;
        }
    }

    void Continue()
    {
        if (diagnosis == null)
        {
            return;
        }

        if (stage < 2)
        {
            stage++;
            ShowStage();
            return;
        }

        string id = diagnosis.id;
        Hide();
        if (!string.IsNullOrEmpty(id))
        {
            OnFinished?.Invoke(id);
        }
    }

    static VisualElement BuildOption(TreatmentOption option)
    {
        VisualElement box = new VisualElement();
        box.AddToClassList("share-option");

        Label title = new Label(option.title);
        title.AddToClassList("share-option-title");
        box.Add(title);

        if (!string.IsNullOrWhiteSpace(option.pros))
        {
            box.Add(OptionLine($"Pros: {option.pros}"));
        }

        if (!string.IsNullOrWhiteSpace(option.cons))
        {
            box.Add(OptionLine($"Cons: {option.cons}"));
        }

        return box;
    }

    static Label OptionLine(string text)
    {
        Label label = new Label(text);
        label.AddToClassList("share-option-text");
        return label;
    }
}
