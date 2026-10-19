using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// View layer for the patient interview, built on UI Toolkit. It holds no game logic: it shows
/// what it is told to (speaker, dialogue text, question buttons, cue count, patient folder,
/// portrait hotspots) and reports player input through events, which
/// <see cref="PatientInterviewController"/> and <see cref="Patient"/> listen to.
/// <para>
/// How it works: in <c>OnEnable</c> it looks up named elements in the <see cref="UIDocument"/>'s
/// UXML (e.g. <c>dialogue-text</c>, <c>question-list</c>, <c>abcde-0</c>..<c>abcde-4</c>,
/// <c>portrait</c>) and wires up their callbacks. Any element that is missing is simply skipped,
/// so the layout can be changed without breaking the script. Visual states (asked, examined,
/// found, folder open) are applied as USS classes, so styling lives in the stylesheet.
/// </para>
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class PatientInterviewUI : MonoBehaviour
{
    /// <summary>A question button was clicked. Passes the question id.</summary>
    public event Action<string> OnQuestionClicked;
    /// <summary>A portrait hotspot was clicked. Passes the hotspot id.</summary>
    public event Action<string> OnHotspotClicked;
    /// <summary>One of the five ABCDE examination buttons was clicked.</summary>
    public event Action<AbcdeLetter> OnAbcdeClicked;
    public event Action OnDoneClicked;
    /// <summary>The Cues button was clicked; <see cref="Patient"/> opens the hypothesis screen.</summary>
    public event Action OnCuesClicked;
    /// <summary>The dialogue panel was clicked, meaning "skip / show the full line".</summary>
    public event Action OnSkipRequested;

    const int AbcdeCount = 5;

    UIDocument document;
    VisualElement root;
    VisualElement portraitFrame;
    VisualElement portrait;
    Sprite portraitSprite;
    Label speakerName;
    Label dialogueText;
    VisualElement questionList;
    Button doneButton;
    VisualElement dialoguePanel;
    VisualElement patientFolder;
    VisualElement folderContent;
    Button folderTab;
    Button cueInventoryButton;
    VisualElement abcdeBar;
    readonly Button[] abcdeButtons = new Button[AbcdeCount];
    readonly Action[] abcdeHandlers = new Action[AbcdeCount];
    bool questionsEnabled = true;
    readonly HashSet<string> foundHotspotIds = new HashSet<string>();

    /// <summary>
    /// Creates one click handler per ABCDE button up front, so the exact same delegate can be
    /// removed again in <see cref="Unbind"/>.
    /// </summary>
    void Awake()
    {
        for (int i = 0; i < AbcdeCount; i++)
        {
            AbcdeLetter letter = (AbcdeLetter)i;
            abcdeHandlers[i] = () => OnAbcdeClicked?.Invoke(letter);
        }
    }

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

    /// <summary>Shows or hides the whole interview screen.</summary>
    public void SetVisible(bool visible)
    {
        EnsureBound();
        if (root != null)
        {
            root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    /// <summary>
    /// Resets the screen for a new patient: sets the portrait and its hotspots, fills the patient
    /// folder (closed), resets the cue count and re-enables all ABCDE buttons.
    /// </summary>
    public void BeginPatient(SO_Patient patient)
    {
        EnsureBound();
        foundHotspotIds.Clear();
        ApplyPortrait(patient != null ? patient.patientPortrait : null);
        RebuildHotspots(patient != null ? patient.visualHotspots : null);
        patientFolder?.RemoveFromClassList("patient-folder--open");
        BuildFolder(patient);
        SetCueCount(0);

        for (int i = 0; i < AbcdeCount; i++)
        {
            if (abcdeButtons[i] != null)
            {
                abcdeButtons[i].RemoveFromClassList("abcde-button--examined");
                abcdeButtons[i].SetEnabled(true);
            }
        }
    }

    /// <summary>Greys out an ABCDE button once that step has been examined.</summary>
    public void SetAbcdeExamined(AbcdeLetter letter)
    {
        Button button = abcdeButtons[(int)letter];
        if (button != null)
        {
            button.AddToClassList("abcde-button--examined");
            button.SetEnabled(false);
        }
    }

    /// <summary>Opens/closes the slide-out patient folder via the <c>patient-folder--open</c> USS class.</summary>
    void ToggleFolder()
    {
        patientFolder?.ToggleInClassList("patient-folder--open");
    }

    /// <summary>
    /// Fills the patient folder with label/value rows from the patient's chart, followed by any notes.
    /// Pacemaker and allergies are flagged as alerts when present.
    /// </summary>
    void BuildFolder(SO_Patient patient)
    {
        if (folderContent == null)
        {
            return;
        }

        folderContent.Clear();
        if (patient == null)
        {
            return;
        }

        PatientChart chart = patient.chart ?? new PatientChart();
        AddFolderRow("Name", patient.patientDisplayName);
        AddFolderRow("Age", $"{chart.age} years");
        AddFolderRow("Gender", chart.gender.ToString());
        AddFolderRow("Weight", $"{chart.weightKg:0.#} kg");
        AddFolderRow("Height", $"{chart.heightCm:0.#} cm");
        AddFolderRow("Pacemaker", chart.hasPacemaker ? "Yes" : "No", chart.hasPacemaker);

        bool hasAllergies = chart.allergies != null && chart.allergies.Count > 0;
        AddFolderRow("Allergies", hasAllergies ? string.Join(", ", chart.allergies) : "None known", hasAllergies);

        if (chart.notes == null || chart.notes.Count == 0)
        {
            return;
        }

        Label notesTitle = new Label("Notes");
        notesTitle.AddToClassList("folder-section-title");
        folderContent.Add(notesTitle);

        for (int i = 0; i < chart.notes.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(chart.notes[i]))
            {
                continue;
            }

            Label note = new Label($"- {chart.notes[i].Trim()}");
            note.AddToClassList("folder-note");
            folderContent.Add(note);
        }
    }

    /// <summary>Adds one "Label: value" row to the folder. <paramref name="alert"/> highlights the value.</summary>
    void AddFolderRow(string label, string value, bool alert = false)
    {
        VisualElement row = new VisualElement();
        row.AddToClassList("folder-row");

        Label labelElement = new Label(label);
        labelElement.AddToClassList("folder-label");
        row.Add(labelElement);

        Label valueElement = new Label(value ?? string.Empty);
        valueElement.AddToClassList("folder-value");
        if (alert)
        {
            valueElement.AddToClassList("folder-value--alert");
        }

        row.Add(valueElement);
        folderContent.Add(row);
    }

    /// <summary>
    /// Marks a portrait hotspot as found. The id is also remembered so the state survives if the
    /// hotspot elements are rebuilt.
    /// </summary>
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

    /// <summary>Updates the "Cues (n)" button label.</summary>
    public void SetCueCount(int count)
    {
        EnsureBound();
        if (cueInventoryButton != null)
        {
            cueInventoryButton.text = $"Cues ({count})";
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

    /// <summary>
    /// Enables/disables the question list and the ABCDE bar together; used to block input while a
    /// line is playing. The value is remembered and re-applied when the questions are rebuilt.
    /// </summary>
    public void SetQuestionsEnabled(bool enabled)
    {
        questionsEnabled = enabled;
        if (questionList != null)
        {
            questionList.SetEnabled(enabled);
        }

        if (abcdeBar != null)
        {
            abcdeBar.SetEnabled(enabled);
        }
    }

    /// <summary>
    /// Replaces the question list with one button per entry. Asked questions get the
    /// <c>question-button--asked</c> class and are disabled.
    /// </summary>
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

    /// <summary>
    /// Binds to the UI document if that hasn't happened yet. Other scripts may call into this
    /// component before its own <c>OnEnable</c> has run (or before the UIDocument has built its
    /// visual tree), so public methods call this first.
    /// </summary>
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

    /// <summary>Sets the portrait image (or clears it) and re-fits it to the frame.</summary>
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

    /// <summary>
    /// Sizes the portrait element to the largest rectangle with the sprite's aspect ratio that fits
    /// inside the frame, and centers it. Re-run whenever the frame changes size.
    /// Hotspot rects are normalized to the sprite, so the portrait must keep the sprite's aspect ratio.
    /// </summary>
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

    /// <summary>
    /// Creates one absolutely positioned element per hotspot inside the portrait. Positions are
    /// percentages of the portrait size, so they follow the image when it is resized.
    /// </summary>
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

            // StopPropagation keeps the click from reaching parent elements underneath the hotspot.
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

    /// <summary>
    /// Looks up all named elements under <paramref name="bindRoot"/> and registers the button and
    /// pointer callbacks. For scroll views the inner content container is used, since that is where
    /// children must be added.
    /// </summary>
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
        cueInventoryButton = bindRoot.Q<Button>("cue-inventory-button");

        abcdeBar = bindRoot.Q<VisualElement>("abcde-bar");
        for (int i = 0; i < AbcdeCount; i++)
        {
            abcdeButtons[i] = bindRoot.Q<Button>($"abcde-{i}");
            if (abcdeButtons[i] != null)
            {
                abcdeButtons[i].clicked += abcdeHandlers[i];
            }
        }

        doneButton = bindRoot.Q<Button>("done-button");
        dialoguePanel = bindRoot.Q<VisualElement>("dialogue-panel");
        patientFolder = bindRoot.Q<VisualElement>("patient-folder");
        ScrollView folderScroll = bindRoot.Q<ScrollView>("folder-content");
        folderContent = folderScroll != null ? folderScroll.contentContainer : null;
        folderTab = bindRoot.Q<Button>("folder-tab");

        if (doneButton != null)
        {
            doneButton.clicked += HandleDoneClicked;
        }

        if (folderTab != null)
        {
            folderTab.clicked += ToggleFolder;
        }

        if (cueInventoryButton != null)
        {
            cueInventoryButton.clicked += HandleCuesClicked;
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

    /// <summary>Removes every callback added in <see cref="Bind"/> and drops all element references.</summary>
    void Unbind()
    {
        if (doneButton != null)
        {
            doneButton.clicked -= HandleDoneClicked;
        }

        if (folderTab != null)
        {
            folderTab.clicked -= ToggleFolder;
        }

        if (cueInventoryButton != null)
        {
            cueInventoryButton.clicked -= HandleCuesClicked;
        }

        for (int i = 0; i < AbcdeCount; i++)
        {
            if (abcdeButtons[i] != null)
            {
                abcdeButtons[i].clicked -= abcdeHandlers[i];
                abcdeButtons[i] = null;
            }
        }

        abcdeBar = null;

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
        doneButton = null;
        dialoguePanel = null;
        patientFolder = null;
        folderContent = null;
        folderTab = null;
        cueInventoryButton = null;
    }

    void HandleDoneClicked()
    {
        OnDoneClicked?.Invoke();
    }

    void HandleCuesClicked()
    {
        OnCuesClicked?.Invoke();
    }

    void HandleDialoguePointerDown(PointerDownEvent evt)
    {
        OnSkipRequested?.Invoke();
    }
}

/// <summary>
/// The minimal data the UI needs to draw one question button, passed from the controller to the view.
/// </summary>
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
