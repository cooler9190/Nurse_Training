using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// View layer for the patient interview, built on UI Toolkit. It holds no game logic: it shows
/// what it is told to (speaker, dialogue text, question buttons, patient folder, diagnosis hand,
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
    /// <summary>Prioritise hypotheses was clicked; <see cref="Patient"/> opens the hypothesis screen.</summary>
    public event Action OnCuesClicked;
    /// <summary>Shared decision making was clicked in the patient file.</summary>
    public event Action OnSharedDecisionClicked;
    /// <summary>A diagnosis card was dropped on the patient during shared decision making.</summary>
    public event Action<Diagnosis> OnDiagnosisDropped;
    /// <summary>A shared-decision choice in the question box was clicked.</summary>
    public event Action<int> OnShareChoiceClicked;
    /// <summary>The dialogue panel was clicked, meaning "skip / show the full line".</summary>
    public event Action OnSkipRequested;

    const int AbcdeCount = 5;
    // Same fan as the cue hand on the hypothesis screen.
    const float HandCardSpacing = 130f;
    const float MaxHandSpread = 1000f;
    const float HandTilt = 4f;
    const float HandArc = 8f;

    UIDocument document;
    VisualElement documentRoot;
    VisualElement root;
    VisualElement portraitFrame;
    VisualElement portrait;
    Sprite portraitSprite;
    Label speakerName;
    Label dialogueText;
    Label frustrationReadout;
    Label frustrationMood;
    VisualElement frustrationMeter;
    VisualElement frustrationFill;
    int lastFrustration = -1;
    ScrollView questionScroll;
    VisualElement questionList;
    ScrollView choiceScroll;
    VisualElement choiceList;
    VisualElement dialoguePanel;
    VisualElement patientFolder;
    VisualElement folderContent;
    VisualElement notesRoot;
    VisualElement folderHand;
    VisualElement diagnosisHand;
    VisualElement dragLayer;
    Button folderTab;
    Button cueInventoryButton;
    Button sharedDecisionButton;
    VisualElement abcdeBar;
    readonly Button[] abcdeButtons = new Button[AbcdeCount];
    readonly Action[] abcdeHandlers = new Action[AbcdeCount];
    readonly List<string> chartNotes = new List<string>();
    readonly List<RecognizedCue> cueNotes = new List<RecognizedCue>();
    readonly List<DiagnosisCard> diagnosisCards = new List<DiagnosisCard>();
    bool questionsEnabled = true;
    bool shareMode;
    bool shareChoicesOpen;
    bool shareLinePlaying;
    DiagnosisCard hoveredDiagnosis;
    DiagnosisCard diagnosisDrag;
    DiagnosisCard diagnosisGhost;
    int dragPointerId;
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
    /// folder (closed) and re-enables all ABCDE buttons. Diagnosis cards and shared decision making
    /// are cleared.
    /// </summary>
    public void BeginPatient(SO_Patient patient)
    {
        EnsureBound();
        foundHotspotIds.Clear();
        shareMode = false;
        shareChoicesOpen = false;
        shareLinePlaying = false;
        ApplyPortrait(patient != null ? patient.patientPortrait : null);
        RebuildHotspots(patient != null ? patient.visualHotspots : null);
        SetFolderOpen(false);
        BuildFolder(patient);
        HideShareChoices();
        SetFrustration(0, false);
        ClearDiagnosisCards();
        PlaceDiagnosisHand();
        UpdateSharedDecisionButton();
        if (abcdeBar != null)
        {
            abcdeBar.style.display = DisplayStyle.Flex;
        }

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

    /// <summary>Opens or closes the full-screen patient file. The tab reads Close while it is open.</summary>
    void ToggleFolder()
    {
        if (patientFolder == null)
        {
            return;
        }

        SetFolderOpen(!patientFolder.ClassListContains("patient-folder--open"));
    }

    void SetFolderOpen(bool open)
    {
        if (patientFolder == null)
        {
            return;
        }

        patientFolder.EnableInClassList("patient-folder--open", open);
        if (folderTab != null)
        {
            folderTab.text = open ? "Close" : "File";
        }
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
        notesRoot = null;
        if (patient == null)
        {
            chartNotes.Clear();
            cueNotes.Clear();
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

        chartNotes.Clear();
        cueNotes.Clear();
        if (chart.notes != null)
        {
            for (int i = 0; i < chart.notes.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(chart.notes[i]))
                {
                    chartNotes.Add(chart.notes[i].Trim());
                }
            }
        }

        notesRoot = new VisualElement { name = "folder-notes" };
        folderContent.Add(notesRoot);
        RefreshNotes();
    }

    /// <summary>Replaces the cue notes with <paramref name="cues"/>, after the chart notes.</summary>
    public void SetCueNotes(IReadOnlyList<RecognizedCue> cues)
    {
        EnsureBound();
        cueNotes.Clear();
        if (cues != null)
        {
            for (int i = 0; i < cues.Count; i++)
            {
                cueNotes.Add(cues[i]);
            }
        }

        RefreshNotes();
    }

    /// <summary>Appends one discovered cue to the Notes section.</summary>
    public void AddCueNote(RecognizedCue cue)
    {
        EnsureBound();
        cueNotes.Add(cue);
        RefreshNotes();
    }

    void RefreshNotes()
    {
        if (notesRoot == null)
        {
            return;
        }

        notesRoot.Clear();
        if (chartNotes.Count == 0 && cueNotes.Count == 0)
        {
            return;
        }

        Label notesTitle = new Label("Notes");
        notesTitle.AddToClassList("folder-section-title");
        notesRoot.Add(notesTitle);

        for (int i = 0; i < chartNotes.Count; i++)
        {
            AddNote(chartNotes[i]);
        }

        for (int i = 0; i < cueNotes.Count; i++)
        {
            AddNote(FormatCueNote(cueNotes[i]));
        }
    }

    void AddNote(string text)
    {
        Label note = new Label($"- {text}");
        note.AddToClassList("folder-note");
        notesRoot.Add(note);
    }

    static string FormatCueNote(RecognizedCue cue)
    {
        string title = string.IsNullOrWhiteSpace(cue.Title) ? cue.Id : cue.Title.Trim();
        if (string.IsNullOrWhiteSpace(cue.Description))
        {
            return title;
        }

        return $"{title} - {cue.Description.Trim()}";
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
    /// Enables or disables the question list, shared-decision choices and the ABCDE bar together.
    /// Used to block input while a line is playing. The value is remembered and re-applied when
    /// the questions are rebuilt. Hiding the ABCDE bar during shared decision making is separate.
    /// </summary>
    public void SetQuestionsEnabled(bool enabled)
    {
        questionsEnabled = enabled;
        if (questionList != null)
        {
            questionList.SetEnabled(enabled);
        }

        if (choiceScroll != null)
        {
            choiceScroll.SetEnabled(enabled);
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

        documentRoot = bindRoot;
        GameAudio.RegisterButtonClicks(documentRoot);
        root = bindRoot.Q<VisualElement>("patient-interview") ?? bindRoot;
        portraitFrame = bindRoot.Q<VisualElement>("portrait-frame");
        portrait = bindRoot.Q<VisualElement>("portrait");
        speakerName = bindRoot.Q<Label>("speaker-name");
        dialogueText = bindRoot.Q<Label>("dialogue-text");
        frustrationReadout = bindRoot.Q<Label>("frustration-readout");
        frustrationMood = bindRoot.Q<Label>("frustration-mood");
        frustrationMeter = bindRoot.Q<VisualElement>("frustration-meter");
        frustrationFill = bindRoot.Q<VisualElement>("frustration-fill");
        questionScroll =bindRoot.Q<ScrollView>("question-list");
        questionList = questionScroll != null ? questionScroll.contentContainer : bindRoot.Q<VisualElement>("question-list");
        choiceScroll = bindRoot.Q<ScrollView>("share-choice-list");
        choiceList = choiceScroll != null ? choiceScroll.contentContainer : null;
        cueInventoryButton = bindRoot.Q<Button>("cue-inventory-button");
        sharedDecisionButton = bindRoot.Q<Button>("shared-decision-button");

        abcdeBar = bindRoot.Q<VisualElement>("abcde-bar");
        for (int i = 0; i < AbcdeCount; i++)
        {
            abcdeButtons[i] = bindRoot.Q<Button>($"abcde-{i}");
            if (abcdeButtons[i] != null)
            {
                abcdeButtons[i].clicked += abcdeHandlers[i];
            }
        }

        dialoguePanel = bindRoot.Q<VisualElement>("dialogue-panel");
        patientFolder = bindRoot.Q<VisualElement>("patient-folder");
        ScrollView folderScroll = bindRoot.Q<ScrollView>("folder-content");
        folderContent = folderScroll != null ? folderScroll.contentContainer : null;
        folderHand = bindRoot.Q<VisualElement>("folder-hand");
        diagnosisHand = bindRoot.Q<VisualElement>("diagnosis-hand");
        dragLayer = bindRoot.Q<VisualElement>("interview-drag-layer");
        folderTab = bindRoot.Q<Button>("folder-tab");

        if (folderTab != null)
        {
            folderTab.clicked += ToggleFolder;
        }

        if (cueInventoryButton != null)
        {
            cueInventoryButton.clicked += HandleCuesClicked;
        }

        if (sharedDecisionButton != null)
        {
            sharedDecisionButton.clicked += HandleSharedDecisionClicked;
            sharedDecisionButton.SetEnabled(false);
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
        EndDiagnosisDrag();
        diagnosisCards.Clear();
        hoveredDiagnosis = null;

        if (folderTab != null)
        {
            folderTab.clicked -= ToggleFolder;
        }

        if (cueInventoryButton != null)
        {
            cueInventoryButton.clicked -= HandleCuesClicked;
        }

        if (sharedDecisionButton != null)
        {
            sharedDecisionButton.clicked -= HandleSharedDecisionClicked;
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

        GameAudio.UnregisterButtonClicks(documentRoot);
        documentRoot = null;
        root = null;
        portraitFrame = null;
        portrait = null;
        speakerName = null;
        dialogueText = null;
        questionList = null;
        questionScroll = null;
        choiceList = null;
        choiceScroll = null;
        dialoguePanel = null;
        patientFolder = null;
        folderContent = null;
        notesRoot = null;
        folderHand = null;
        diagnosisHand = null;
        dragLayer = null;
        folderTab = null;
        cueInventoryButton = null;
        sharedDecisionButton = null;
        frustrationReadout = null;
        frustrationMood = null;
        frustrationMeter = null;
        frustrationFill = null;
        lastFrustration = -1;
    }

    void HandleCuesClicked()
    {
        OnCuesClicked?.Invoke();
    }

    void HandleSharedDecisionClicked()
    {
        OnSharedDecisionClicked?.Invoke();
    }

    void HandleDialoguePointerDown(PointerDownEvent evt)
    {
        OnSkipRequested?.Invoke();
    }

    /// <summary>
    /// Shows the frustration meter while shared decision making is active: a bar that fills and changes colour,
    /// a mood word, and a small bump when the frustration goes up.
    /// </summary>
    public void SetFrustration(int percent, bool visible)
    {
        EnsureBound();
        if (frustrationReadout == null || frustrationMeter == null)
        {
            return;
        }

        percent = Mathf.Clamp(percent, 0, 100);
        frustrationReadout.text = $"Frustration {percent}%";

        string level = FrustrationLevel(percent);
        if (frustrationFill != null)
        {
            frustrationFill.style.width = Length.Percent(percent);
            foreach (string name in FrustrationLevels)
            {
                frustrationFill.EnableInClassList("fm-fill--" + name, name == level);
                frustrationMood?.EnableInClassList("fm-mood--" + name, name == level);
            }
        }

        if (frustrationMood != null) { frustrationMood.text = FrustrationMood(level); }

        // Bump the meter when the frustration rises while it is on screen
        bool rose = visible && lastFrustration >= 0 && percent > lastFrustration;
        lastFrustration = visible ? percent : -1;
        frustrationMeter.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        if (rose)
        {
            frustrationMeter.AddToClassList("fm-meter--bump");
            frustrationMeter.schedule.Execute(() => frustrationMeter?.RemoveFromClassList("fm-meter--bump")).StartingIn(220);
        }
    }

    static readonly string[] FrustrationLevels = { "calm", "uneasy", "upset", "leaving" };

    static string FrustrationLevel(int percent)
    {
        if (percent >= 85) { return "leaving"; }
        if (percent >= 60) { return "upset"; }
        if (percent >= 35) { return "uneasy"; }
        return "calm";
    }

    static string FrustrationMood(string level)
    {
        switch (level)
        {
            case "leaving": return "About to leave";
            case "upset": return "Frustrated";
            case "uneasy": return "Uneasy";
            default: return "Calm";
        }
    }

    /// <summary>
    /// Builds the diagnosis hand in the patient file, or in the ABCDE slot if shared decision making
    /// is already active. Cards are not draggable until <see cref="BeginSharedDecision"/>.
    /// </summary>
    public void ShowDiagnosisHand(
        IReadOnlyList<RankedDiagnosis> results,
        Func<string, bool> isShared,
        Func<string, TreatmentOption> chosenTreatment,
        Func<string, bool> isDecided)
    {
        EnsureBound();
        ClearDiagnosisCards();
        if (results != null)
        {
            for (int i = 0; i < results.Count; i++)
            {
                RankedDiagnosis result = results[i];
                Diagnosis diagnosis = result.Diagnosis;
                string id = diagnosis != null ? diagnosis.id : null;
                bool shared = isShared != null && !string.IsNullOrEmpty(id) && isShared(id);
                bool decided = isDecided != null && !string.IsNullOrEmpty(id) && isDecided(id);
                TreatmentOption chosen = chosenTreatment != null && !string.IsNullOrEmpty(id) ? chosenTreatment(id) : null;
                diagnosisCards.Add(CreateDiagnosisCard(result, shared, chosen, decided));
            }
        }

        VisualElement parent = HandParent();
        for (int i = 0; i < diagnosisCards.Count; i++)
        {
            parent?.Add(diagnosisCards[i]);
        }

        PlaceDiagnosisHand();
        LayoutDiagnosisHand();
        UpdateSharedDecisionButton();
    }

    /// <summary>
    /// Closes the file, hides ABCDE and moves the diagnosis hand into that spot. Returns false when
    /// there is nothing to share or sharing has already started.
    /// </summary>
    public bool BeginSharedDecision()
    {
        EnsureBound();
        if (shareMode || diagnosisCards.Count == 0 || diagnosisHand == null)
        {
            return false;
        }

        shareMode = true;
        SetFolderOpen(false);
        if (abcdeBar != null)
        {
            abcdeBar.style.display = DisplayStyle.None;
        }

        for (int i = 0; i < diagnosisCards.Count; i++)
        {
            diagnosisHand.Add(diagnosisCards[i]);
        }

        PlaceDiagnosisHand();
        LayoutDiagnosisHand();
        UpdateSharedDecisionButton();
        return true;
    }

    /// <summary>Writes a finished decision back onto the matching card and closes it to further talks.</summary>
    public void UpdateDiagnosisCard(string diagnosisId, bool shared, TreatmentOption chosen)
    {
        if (string.IsNullOrEmpty(diagnosisId))
        {
            return;
        }

        for (int i = 0; i < diagnosisCards.Count; i++)
        {
            Diagnosis diagnosis = diagnosisCards[i].Result.Diagnosis;
            if (diagnosis != null && diagnosis.id == diagnosisId)
            {
                diagnosisCards[i].SetShared(shared);
                diagnosisCards[i].SetChosenTreatment(chosen);
                diagnosisCards[i].SetDecided(true);
            }
        }
    }

    /// <summary>Blocks dragging a diagnosis while a patient line is being typed.</summary>
    public void SetShareLinePlaying(bool playing)
    {
        shareLinePlaying = playing;
    }

    /// <summary>
    /// Shows a shared-decision step. Choices replace the interview questions until the list is empty,
    /// which brings the remaining questions back.
    /// </summary>
    public void ShowSharePrompt(string speaker, string dialogue, IReadOnlyList<string> choices)
    {
        EnsureBound();
        if (speaker != null)
        {
            SetSpeakerName(speaker);
        }

        if (dialogue != null)
        {
            SetDialogueText(dialogue);
        }

        bool hasChoices = choices != null && choices.Count > 0;
        shareChoicesOpen = hasChoices;
        if (questionScroll != null)
        {
            questionScroll.style.display = hasChoices ? DisplayStyle.None : DisplayStyle.Flex;
        }

        if (choiceScroll != null)
        {
            choiceScroll.style.display = hasChoices ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (choiceList == null)
        {
            return;
        }

        choiceList.Clear();
        if (!hasChoices)
        {
            return;
        }

        for (int i = 0; i < choices.Count; i++)
        {
            int index = i;
            Button button = new Button(() => OnShareChoiceClicked?.Invoke(index))
            {
                text = choices[i]
            };
            button.AddToClassList("question-button");
            button.style.whiteSpace = WhiteSpace.Normal;
            button.style.height = StyleKeyword.Auto;
            button.style.minHeight = 70;
            choiceList.Add(button);
        }
    }

    /// <summary>Hides shared-decision choices and shows the interview questions again.</summary>
    public void HideShareChoices()
    {
        EnsureBound();
        shareChoicesOpen = false;
        if (questionScroll != null)
        {
            questionScroll.style.display = DisplayStyle.Flex;
        }

        if (choiceScroll != null)
        {
            choiceScroll.style.display = DisplayStyle.None;
        }

        choiceList?.Clear();
    }

    /// <summary>
    /// Clears the current choices without showing interview questions yet.
    /// Dragging stays blocked until the next step is shown.
    /// </summary>
    public void ClearShareChoices()
    {
        EnsureBound();
        shareChoicesOpen = true;
        choiceList?.Clear();
        if (questionScroll != null)
        {
            questionScroll.style.display = DisplayStyle.None;
        }

        if (choiceScroll != null)
        {
            choiceScroll.style.display = DisplayStyle.None;
        }
    }

    VisualElement HandParent()
    {
        return shareMode && diagnosisHand != null ? diagnosisHand : folderHand;
    }

    void PlaceDiagnosisHand()
    {
        bool hasCards = diagnosisCards.Count > 0;
        if (folderHand != null)
        {
            folderHand.style.display = !shareMode && hasCards ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (diagnosisHand != null)
        {
            diagnosisHand.style.display = shareMode && hasCards ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    void UpdateSharedDecisionButton()
    {
        sharedDecisionButton?.SetEnabled(diagnosisCards.Count > 0 && !shareMode);
    }

    void ClearDiagnosisCards()
    {
        EndDiagnosisDrag();
        hoveredDiagnosis = null;
        diagnosisCards.Clear();
        folderHand?.Clear();
        diagnosisHand?.Clear();
    }

    DiagnosisCard CreateDiagnosisCard(RankedDiagnosis result, bool shared, TreatmentOption chosen, bool decided)
    {
        DiagnosisCard card = new DiagnosisCard(result, shared);
        card.SetDecided(decided);
        if (chosen != null)
        {
            card.SetChosenTreatment(chosen);
        }

        card.RegisterCallback<PointerEnterEvent>(_ => SetDiagnosisHovered(card, true));
        card.RegisterCallback<PointerLeaveEvent>(_ => SetDiagnosisHovered(card, false));
        card.RegisterCallback<PointerDownEvent>(evt => BeginDiagnosisDrag(card, evt));
        card.RegisterCallback<PointerMoveEvent>(evt => UpdateDiagnosisDrag(card, evt));
        card.RegisterCallback<PointerUpEvent>(evt => DropDiagnosis(card, evt));
        card.RegisterCallback<PointerCaptureOutEvent>(_ => EndDiagnosisDrag());
        return card;
    }

    void LayoutDiagnosisHand()
    {
        int count = diagnosisCards.Count;
        float spacing = count > 1 ? Mathf.Min(HandCardSpacing, MaxHandSpread / (count - 1)) : 0f;
        for (int i = 0; i < count; i++)
        {
            DiagnosisCard card = diagnosisCards[i];
            float fromCentre = i - (count - 1) * 0.5f;
            card.style.left = Length.Percent(50f);
            card.style.marginLeft = fromCentre * spacing;
            card.style.top = Mathf.Abs(fromCentre) * HandArc;
            card.style.rotate = new Rotate(new Angle(fromCentre * HandTilt, AngleUnit.Degree));
            card.BringToFront();
        }

        if (hoveredDiagnosis != null && diagnosisCards.Contains(hoveredDiagnosis))
        {
            hoveredDiagnosis.style.rotate = new Rotate(new Angle(0f, AngleUnit.Degree));
            hoveredDiagnosis.BringToFront();
        }
    }

    void SetDiagnosisHovered(DiagnosisCard card, bool hovered)
    {
        if (diagnosisDrag != null || !diagnosisCards.Contains(card))
        {
            return;
        }

        if (hovered)
        {
            hoveredDiagnosis = card;
        }
        else if (hoveredDiagnosis == card)
        {
            hoveredDiagnosis = null;
        }

        card.EnableInClassList("diagnosis-card--hovered", hovered);
        LayoutDiagnosisHand();
    }

    void BeginDiagnosisDrag(DiagnosisCard card, PointerDownEvent evt)
    {
        if (evt.button != 0 || !shareMode || shareChoicesOpen || shareLinePlaying || card.Decided || diagnosisDrag != null || dragLayer == null)
        {
            return;
        }

        SetDiagnosisHovered(card, false);
        diagnosisDrag = card;
        dragPointerId = evt.pointerId;
        card.AddToClassList("diagnosis-card--dragging");
        card.CapturePointer(dragPointerId);

        diagnosisGhost = new DiagnosisCard(card.Result, card.Shared) { pickingMode = PickingMode.Ignore };
        diagnosisGhost.SetDecided(card.Decided);
        diagnosisGhost.SetChosenTreatment(card.ChosenTreatment);
        diagnosisGhost.AddToClassList("diagnosis-card--ghost");
        dragLayer.Add(diagnosisGhost);
        MoveDiagnosisGhost(evt.position);
        evt.StopPropagation();
    }

    void UpdateDiagnosisDrag(DiagnosisCard card, PointerMoveEvent evt)
    {
        if (card != diagnosisDrag)
        {
            return;
        }

        MoveDiagnosisGhost(evt.position);
        SetPortraitTargeted(IsOver(portraitFrame, evt.position));
    }

    void DropDiagnosis(DiagnosisCard card, PointerUpEvent evt)
    {
        if (card != diagnosisDrag)
        {
            return;
        }

        bool onPatient = IsOver(portraitFrame, evt.position);
        Diagnosis diagnosis = card.Result.Diagnosis;
        EndDiagnosisDrag();
        if (onPatient && diagnosis != null)
        {
            OnDiagnosisDropped?.Invoke(diagnosis);
        }
    }

    /// <summary>Clears the diagnosis drag and its ghost. Safe to call when no drag is active.</summary>
    void EndDiagnosisDrag()
    {
        if (diagnosisDrag == null)
        {
            return;
        }

        DiagnosisCard card = diagnosisDrag;
        diagnosisDrag = null;
        card.RemoveFromClassList("diagnosis-card--dragging");
        if (card.HasPointerCapture(dragPointerId))
        {
            card.ReleasePointer(dragPointerId);
        }

        diagnosisGhost?.RemoveFromHierarchy();
        diagnosisGhost = null;
        SetPortraitTargeted(false);
    }

    void MoveDiagnosisGhost(Vector2 panelPosition)
    {
        if (diagnosisGhost == null || dragLayer == null)
        {
            return;
        }

        Vector2 local = dragLayer.WorldToLocal(panelPosition);
        diagnosisGhost.style.left = local.x;
        diagnosisGhost.style.top = local.y;
    }

    void SetPortraitTargeted(bool targeted)
    {
        if (portraitFrame == null)
        {
            return;
        }

        float width = targeted ? 4f : 0f;
        portraitFrame.style.borderTopWidth = width;
        portraitFrame.style.borderRightWidth = width;
        portraitFrame.style.borderBottomWidth = width;
        portraitFrame.style.borderLeftWidth = width;
        if (!targeted)
        {
            return;
        }

        Color color = new Color(53f / 255f, 234f / 255f, 198f / 255f);
        portraitFrame.style.borderTopColor = color;
        portraitFrame.style.borderRightColor = color;
        portraitFrame.style.borderBottomColor = color;
        portraitFrame.style.borderLeftColor = color;
    }

    static bool IsOver(VisualElement zone, Vector2 panelPosition)
    {
        return zone != null && zone.resolvedStyle.display != DisplayStyle.None && zone.worldBound.Contains(panelPosition);
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
