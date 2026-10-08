using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class PatientInterviewUI : MonoBehaviour
{
    public event Action<string> OnQuestionClicked;
    public event Action<string> OnHotspotClicked;
    public event Action OnDoneClicked;
    public event Action OnSkipRequested;

    UIDocument document;
    VisualElement root;
    VisualElement portraitFrame;
    VisualElement portrait;
    Sprite portraitSprite;
    Label speakerName;
    Label dialogueText;
    VisualElement questionList;
    VisualElement cueList;
    Button doneButton;
    VisualElement dialoguePanel;
    bool questionsEnabled = true;
    readonly HashSet<string> foundHotspotIds = new HashSet<string>();

    void OnEnable()
    {
        document = GetComponent<UIDocument>();
        Bind(document != null ? document.rootVisualElement : null);
        SetVisible(false);
    }

    void OnDisable()
    {
        Unbind();
    }

    public void SetVisible(bool visible)
    {
        EnsureBound();
        if (root != null)
        {
            root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    public void BeginPatient(SO_Patient patient)
    {
        EnsureBound();
        foundHotspotIds.Clear();
        ApplyPortrait(patient != null ? patient.patientPortrait : null);
        RebuildHotspots(patient != null ? patient.visualHotspots : null);
    }

    public void MarkHotspotFound(string hotspotId)
    {
        if (string.IsNullOrEmpty(hotspotId))
        {
            return;
        }

        foundHotspotIds.Add(hotspotId);
        VisualElement element = portrait != null ? portrait.Q(HotspotElementName(hotspotId)) : null;
        if (element != null)
        {
            element.AddToClassList("portrait-hotspot--found");
        }
    }

    public void RebuildCues(IReadOnlyList<RecognizedCue> cues)
    {
        EnsureBound();
        if (cueList == null)
        {
            return;
        }

        cueList.Clear();
        if (cues == null)
        {
            return;
        }

        for (int i = 0; i < cues.Count; i++)
        {
            RecognizedCue cue = cues[i];
            VisualElement card = new VisualElement();
            card.AddToClassList("cue-card");

            Label title = new Label(string.IsNullOrEmpty(cue.Title) ? cue.Id : cue.Title);
            title.AddToClassList("cue-card-title");
            card.Add(title);

            if (!string.IsNullOrEmpty(cue.Description))
            {
                Label description = new Label(cue.Description);
                description.AddToClassList("cue-card-description");
                card.Add(description);
            }

            cueList.Add(card);
        }
    }

    public void SetSpeakerName(string name)
    {
        if (speakerName != null)
        {
            speakerName.text = name ?? string.Empty;
        }
    }

    public void SetDialogueText(string text)
    {
        if (dialogueText != null)
        {
            dialogueText.text = text ?? string.Empty;
        }
    }

    public void SetQuestionsEnabled(bool enabled)
    {
        questionsEnabled = enabled;
        if (questionList != null)
        {
            questionList.SetEnabled(enabled);
        }
    }

    public void RebuildQuestions(IReadOnlyList<InterviewQuestionView> questions)
    {
        if (questionList == null)
        {
            return;
        }

        questionList.Clear();
        if (questions == null)
        {
            return;
        }

        for (int i = 0; i < questions.Count; i++)
        {
            InterviewQuestionView view = questions[i];
            string questionId = view.Id;
            Button button = new Button(() => OnQuestionClicked?.Invoke(questionId))
            {
                text = view.Text,
                name = $"question-{questionId}"
            };
            button.AddToClassList("question-button");
            if (view.Asked)
            {
                button.AddToClassList("question-button--asked");
                button.SetEnabled(false);
            }

            questionList.Add(button);
        }

        questionList.SetEnabled(questionsEnabled);
    }

    void EnsureBound()
    {
        if (document == null)
        {
            document = GetComponent<UIDocument>();
        }

        if (document != null && document.rootVisualElement != null && (root == null || dialogueText == null))
        {
            Bind(document.rootVisualElement);
        }
    }

    void ApplyPortrait(Sprite sprite)
    {
        portraitSprite = sprite;
        if (portrait == null)
        {
            return;
        }

        portrait.style.backgroundImage = sprite != null
            ? new StyleBackground(sprite)
            : new StyleBackground(StyleKeyword.None);
        LayoutPortrait();
    }

    void HandlePortraitFrameGeometryChanged(GeometryChangedEvent evt)
    {
        LayoutPortrait();
    }

    // Hotspot rects are normalized to the sprite, so the portrait must keep the sprite's aspect ratio.
    void LayoutPortrait()
    {
        if (portrait == null || portraitFrame == null)
        {
            return;
        }

        Rect frame = portraitFrame.contentRect;
        if (float.IsNaN(frame.width) || float.IsNaN(frame.height) || frame.width <= 0f || frame.height <= 0f)
        {
            return;
        }

        float aspect = portraitSprite != null && portraitSprite.rect.height > 0f
            ? portraitSprite.rect.width / portraitSprite.rect.height
            : frame.width / frame.height;

        float width = frame.width;
        float height = width / aspect;
        if (height > frame.height)
        {
            height = frame.height;
            width = height * aspect;
        }

        portrait.style.width = width;
        portrait.style.height = height;
        portrait.style.left = (frame.width - width) * 0.5f;
        portrait.style.top = (frame.height - height) * 0.5f;
    }

    void RebuildHotspots(IReadOnlyList<VisualHotspot> hotspots)
    {
        if (portrait == null)
        {
            return;
        }

        portrait.Clear();
        if (hotspots == null)
        {
            return;
        }

        for (int i = 0; i < hotspots.Count; i++)
        {
            VisualHotspot hotspot = hotspots[i];
            if (hotspot == null || string.IsNullOrEmpty(hotspot.id))
            {
                continue;
            }

            string hotspotId = hotspot.id;
            Rect rect = hotspot.normalizedRect;
            VisualElement element = new VisualElement
            {
                name = HotspotElementName(hotspotId),
                pickingMode = PickingMode.Position
            };
            element.AddToClassList("portrait-hotspot");
            element.style.position = Position.Absolute;
            element.style.left = Length.Percent(rect.x * 100f);
            element.style.top = Length.Percent(rect.y * 100f);
            element.style.width = Length.Percent(rect.width * 100f);
            element.style.height = Length.Percent(rect.height * 100f);
            if (foundHotspotIds.Contains(hotspotId))
            {
                element.AddToClassList("portrait-hotspot--found");
            }

            element.RegisterCallback<PointerDownEvent>(evt =>
            {
                evt.StopPropagation();
                OnHotspotClicked?.Invoke(hotspotId);
            });
            portrait.Add(element);
        }
    }

    static string HotspotElementName(string hotspotId)
    {
        return $"hotspot-{hotspotId}";
    }

    void Bind(VisualElement bindRoot)
    {
        Unbind();
        if (bindRoot == null)
        {
            return;
        }

        root = bindRoot.Q<VisualElement>("patient-interview") ?? bindRoot;
        portraitFrame = bindRoot.Q<VisualElement>("portrait-frame");
        portrait = bindRoot.Q<VisualElement>("portrait");
        speakerName = bindRoot.Q<Label>("speaker-name");
        dialogueText = bindRoot.Q<Label>("dialogue-text");
        ScrollView questionScroll = bindRoot.Q<ScrollView>("question-list");
        questionList = questionScroll != null ? questionScroll.contentContainer : bindRoot.Q<VisualElement>("question-list");
        ScrollView cueScroll = bindRoot.Q<ScrollView>("cue-list");
        cueList = cueScroll != null ? cueScroll.contentContainer : bindRoot.Q<VisualElement>("cue-list");
        if (cueList != null)
        {
            cueList.style.flexDirection = FlexDirection.Row;
        }

        doneButton = bindRoot.Q<Button>("done-button");
        dialoguePanel = bindRoot.Q<VisualElement>("dialogue-panel");

        if (doneButton != null)
        {
            doneButton.clicked += HandleDoneClicked;
        }

        if (dialoguePanel != null)
        {
            dialoguePanel.RegisterCallback<PointerDownEvent>(HandleDialoguePointerDown);
        }

        if (portraitFrame != null)
        {
            portraitFrame.RegisterCallback<GeometryChangedEvent>(HandlePortraitFrameGeometryChanged);
        }
    }

    void Unbind()
    {
        if (doneButton != null)
        {
            doneButton.clicked -= HandleDoneClicked;
        }

        if (dialoguePanel != null)
        {
            dialoguePanel.UnregisterCallback<PointerDownEvent>(HandleDialoguePointerDown);
        }

        if (portraitFrame != null)
        {
            portraitFrame.UnregisterCallback<GeometryChangedEvent>(HandlePortraitFrameGeometryChanged);
        }

        root = null;
        portraitFrame = null;
        portrait = null;
        speakerName = null;
        dialogueText = null;
        questionList = null;
        cueList = null;
        doneButton = null;
        dialoguePanel = null;
    }

    void HandleDoneClicked()
    {
        OnDoneClicked?.Invoke();
    }

    void HandleDialoguePointerDown(PointerDownEvent evt)
    {
        OnSkipRequested?.Invoke();
    }
}

public readonly struct InterviewQuestionView
{
    public InterviewQuestionView(string id, string text, bool asked)
    {
        Id = id;
        Text = text;
        Asked = asked;
    }

    public string Id { get; }
    public string Text { get; }
    public bool Asked { get; }
}
