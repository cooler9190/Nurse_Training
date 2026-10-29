using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Full-screen prioritising step, then shared decision making. Collected cues are a fanned hand of
/// <see cref="CueCard"/>s. The player drags them into the ranking list (most urgent at the top) and
/// presses Diagnose. Cue cards then leave, and the resulting <see cref="DiagnosisCard"/>s can be
/// dragged onto the patient portrait to run that diagnosis's Three-talk.
/// <para>
/// How dragging works: the pressed card captures the pointer and stays where it is (dimmed) while
/// a ghost copy follows the pointer in the drag layer. A cue card dropped on the ranking list is
/// inserted there; a diagnosis card dropped on the patient starts the talk. Anything else is ignored.
/// </para>
/// The cue ranking and which diagnoses have been shared are remembered until <see cref="ResetRanking"/>.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class HypothesisScreenUI : MonoBehaviour
{
    public event Action OnClosed;
    /// <summary>Raised after Diagnose with the prioritised diagnoses (may be empty).</summary>
    public event Action<IReadOnlyList<RankedDiagnosis>> OnDiagnosed;
    /// <summary>Hand over was pressed from the share-decisions view. The screen is already closed.</summary>
    public event Action OnHandoverRequested;

    [Tooltip("Horizontal distance in pixels between neighbouring cards in the hand.")]
    [SerializeField] float handCardSpacing = 130f;
    [Tooltip("Maximum total width in pixels the hand may spread over before cards overlap more.")]
    [SerializeField] float maxHandSpread = 1000f;
    [Tooltip("Degrees of rotation per card away from the centre of the hand.")]
    [SerializeField] float handTilt = 4f;
    [Tooltip("Pixels each card drops per step away from the centre, giving the hand an arc.")]
    [SerializeField] float handArc = 8f;

    UIDocument document;
    VisualElement rankingZone;
    VisualElement rankingList;
    VisualElement diagnosisZone;
    VisualElement diagnosisList;
    VisualElement patientDrop;
    VisualElement patientPortrait;
    VisualElement hand;
    VisualElement dragLayer;
    Label titleLabel;
    Label rankingHint;
    Label diagnosisHint;
    Label shareHint;
    Button diagnoseButton;
    Button backButton;
    Button reviseButton;
    Button shareHandoverButton;
    SharedDecisionDialogue dialogue;

    readonly List<string> rankedIds = new List<string>();
    readonly HashSet<string> sharedIds = new HashSet<string>(StringComparer.Ordinal);
    readonly HashSet<string> cuesAtCommit = new HashSet<string>(StringComparer.Ordinal);
    readonly List<CueCard> rankedCards = new List<CueCard>();
    readonly List<CueCard> handCards = new List<CueCard>();
    readonly List<DiagnosisCard> diagnosisCards = new List<DiagnosisCard>();
    IReadOnlyList<RecognizedCue> openCues;
    IReadOnlyList<Diagnosis> diagnoses;
    SO_Patient profile;
    CueCard hoveredCard;
    CueCard dragCard;
    CueCard dragGhost;
    DiagnosisCard hoveredDiagnosis;
    DiagnosisCard diagnosisDrag;
    DiagnosisCard diagnosisGhost;
    int dragPointerId;
    bool isOpen;
    bool prioritiesCommitted;
    bool showingShare;

    /// <summary>Cue ids in the player's current order; index 0 is the highest priority.</summary>
    public IReadOnlyList<string> RankedCueIds => rankedIds;

    /// <summary>True when this diagnosis was given to the patient and the Three-talk was finished.</summary>
    public bool IsShared(string diagnosisId)
    {
        return !string.IsNullOrEmpty(diagnosisId) && sharedIds.Contains(diagnosisId);
    }

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
        EndCueDrag();
        EndDiagnosisDrag();

        if (diagnoseButton != null)
        {
            diagnoseButton.clicked -= Diagnose;
        }

        if (backButton != null)
        {
            backButton.clicked -= Close;
        }

        if (reviseButton != null)
        {
            reviseButton.clicked -= ShowRankView;
        }

        if (shareHandoverButton != null)
        {
            shareHandoverButton.clicked -= RequestHandover;
        }

        if (dialogue != null)
        {
            dialogue.OnFinished -= HandleTalkFinished;
            dialogue.Dispose();
            dialogue = null;
        }

        if (document != null)
        {
            GameAudio.UnregisterButtonClicks(document.rootVisualElement);
        }

        document = null;
    }

    /// <summary>
    /// Shows the screen. Before Diagnose, ranked cues keep their place and the rest go in the hand.
    /// After Diagnose, the screen opens on the diagnosis cards instead.
    /// </summary>
    public void Open(IReadOnlyList<RecognizedCue> cues, SO_Patient patient)
    {
        EnsureBound();
        if (document == null)
        {
            Debug.LogError("HypothesisScreenUI: UIDocument is not ready.", this);
            return;
        }

        profile = patient;
        diagnoses = patient != null ? patient.diagnoses : null;
        openCues = cues;
        ApplyPortrait(patient != null ? patient.patientPortrait : null);
        rankedCards.Clear();
        handCards.Clear();
        hoveredCard = null;
        hoveredDiagnosis = null;

        List<RecognizedCue> allCues = cues != null ? cues.ToList() : new List<RecognizedCue>();
        foreach (string id in rankedIds)
        {
            int index = allCues.FindIndex(cue => cue.Id == id);
            if (index >= 0)
            {
                rankedCards.Add(CreateCueCard(allCues[index]));
                allCues.RemoveAt(index);
            }
        }

        foreach (RecognizedCue cue in allCues)
        {
            handCards.Add(CreateCueCard(cue));
        }

        dialogue?.Hide();
        if (prioritiesCommitted)
        {
            ShowShareView();
        }
        else
        {
            ShowRankView();
        }

        SetVisible(true);
    }

    public void Close()
    {
        EndCueDrag();
        EndDiagnosisDrag();
        dialogue?.Hide();
        SetVisible(false);
        OnClosed?.Invoke();
    }

    /// <summary>Forgets the ranking and which diagnoses were shared, e.g. when a new patient starts.</summary>
    public void ResetRanking()
    {
        rankedIds.Clear();
        sharedIds.Clear();
        cuesAtCommit.Clear();
        prioritiesCommitted = false;
        showingShare = false;
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
        rankingZone = root.Q<VisualElement>("ranking-zone");
        rankingList = root.Q<ScrollView>("ranking-list")?.contentContainer;
        diagnosisZone = root.Q<VisualElement>("diagnosis-zone");
        diagnosisList = root.Q<ScrollView>("diagnosis-list")?.contentContainer;
        patientDrop = root.Q<VisualElement>("patient-drop");
        patientPortrait = root.Q<VisualElement>("patient-portrait");
        hand = root.Q<VisualElement>("hand");
        dragLayer = root.Q<VisualElement>("drag-layer");
        titleLabel = root.Q<Label>("hypothesis-title");
        rankingHint = root.Q<Label>("ranking-hint");
        diagnosisHint = root.Q<Label>("diagnosis-hint");
        shareHint = root.Q<Label>("share-hint");
        diagnoseButton = root.Q<Button>("diagnose-button");
        backButton = root.Q<Button>("back-button");
        reviseButton = root.Q<Button>("revise-button");
        shareHandoverButton = root.Q<Button>("share-handover-button");

        if (diagnoseButton != null)
        {
            diagnoseButton.clicked += Diagnose;
        }

        if (backButton != null)
        {
            backButton.clicked += Close;
        }

        if (reviseButton != null)
        {
            reviseButton.clicked += ShowRankView;
        }

        if (shareHandoverButton != null)
        {
            shareHandoverButton.clicked += RequestHandover;
        }

        dialogue = new SharedDecisionDialogue(root.Q<VisualElement>("share-dialogue"));
        dialogue.OnFinished += HandleTalkFinished;

        GameAudio.RegisterButtonClicks(root);
        SetVisible(isOpen);
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

    void ApplyPortrait(Sprite sprite)
    {
        if (patientPortrait == null)
        {
            return;
        }

        patientPortrait.style.backgroundImage = sprite != null
            ? new StyleBackground(sprite)
            : new StyleBackground(StyleKeyword.None);
    }

    void ShowRankView()
    {
        EndDiagnosisDrag();
        dialogue?.Hide();
        showingShare = false;
        ApplyChrome();
        RefreshCueCards();
    }

    void ShowShareView()
    {
        EndCueDrag();
        dialogue?.Hide();
        showingShare = true;
        ApplyChrome();
        // Drop cue cards left in the ranking list by an earlier opening. The card objects stay in the lists.
        rankingList?.Clear();
        ShowDiagnosisHand();
    }

    void ApplyChrome()
    {
        SetDisplayed(rankingZone, !showingShare);
        SetDisplayed(diagnosisZone, !showingShare);
        SetDisplayed(patientDrop, showingShare);
        SetDisplayed(diagnoseButton, !showingShare);
        SetDisplayed(reviseButton, showingShare);
        SetDisplayed(shareHandoverButton, showingShare && profile != null && profile.HasPriorities);

        if (titleLabel != null)
        {
            titleLabel.text = showingShare ? "Share decisions" : "Prioritise Hypotheses";
        }
    }

    static void SetDisplayed(VisualElement element, bool visible)
    {
        if (element != null)
        {
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    void RequestHandover()
    {
        Close();
        OnHandoverRequested?.Invoke();
    }

    void HandleTalkFinished(string diagnosisId)
    {
        sharedIds.Add(diagnosisId);
        foreach (DiagnosisCard card in diagnosisCards)
        {
            Diagnosis diagnosis = card.Result.Diagnosis;
            if (diagnosis != null && diagnosis.id == diagnosisId)
            {
                card.SetShared(true);
            }
        }
    }

    CueCard CreateCueCard(RecognizedCue cue)
    {
        CueCard card = new CueCard(cue);
        card.RegisterCallback<PointerEnterEvent>(_ => SetCueHovered(card, true));
        card.RegisterCallback<PointerLeaveEvent>(_ => SetCueHovered(card, false));
        card.RegisterCallback<PointerDownEvent>(evt => BeginCueDrag(card, evt));
        card.RegisterCallback<PointerMoveEvent>(evt => UpdateCueDrag(card, evt));
        card.RegisterCallback<PointerUpEvent>(evt => DropCue(card, evt));
        card.RegisterCallback<PointerCaptureOutEvent>(_ => EndCueDrag());
        return card;
    }

    /// <summary>
    /// Puts every cue card in the ranking list or the hand and saves the ranking.
    /// Shown diagnoses are cleared because they belong to the previous order.
    /// </summary>
    void RefreshCueCards()
    {
        rankingList?.Clear();
        hand?.Clear();
        diagnosisCards.Clear();

        for (int i = 0; i < rankedCards.Count; i++)
        {
            CueCard card = rankedCards[i];
            card.RemoveFromClassList("cue-card--in-hand");
            card.RemoveFromClassList("cue-card--hovered");
            card.AddToClassList("cue-card--ranked");
            card.SetOrientation(false);
            card.style.left = StyleKeyword.Null;
            card.style.top = StyleKeyword.Null;
            card.style.marginLeft = StyleKeyword.Null;
            card.style.rotate = StyleKeyword.Null;
            card.SetRank(i + 1);
            rankingList?.Add(card);
        }

        foreach (CueCard card in handCards)
        {
            card.RemoveFromClassList("cue-card--ranked");
            card.AddToClassList("cue-card--in-hand");
            card.SetOrientation(true);
            card.SetRank(0);
            hand?.Add(card);
        }

        LayoutCueHand();

        rankedIds.Clear();
        rankedIds.AddRange(rankedCards.Select(card => card.Cue.Id));

        if (rankingHint != null)
        {
            rankingHint.style.display = rankedCards.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        diagnoseButton?.SetEnabled(rankedCards.Count > 0);
        ShowDiagnosisHint();
    }

    void LayoutCueHand()
    {
        int count = handCards.Count;
        float spacing = count > 1 ? Mathf.Min(handCardSpacing, maxHandSpread / (count - 1)) : 0f;

        for (int i = 0; i < count; i++)
        {
            CueCard card = handCards[i];
            float fromCentre = i - (count - 1) * 0.5f;
            card.style.left = Length.Percent(50f);
            card.style.marginLeft = fromCentre * spacing;
            card.style.top = Mathf.Abs(fromCentre) * handArc;
            card.style.rotate = new Rotate(new Angle(fromCentre * handTilt, AngleUnit.Degree));
            card.BringToFront();
        }

        if (hoveredCard != null)
        {
            hoveredCard.style.rotate = new Rotate(new Angle(0f, AngleUnit.Degree));
            hoveredCard.BringToFront();
        }
    }

    void SetCueHovered(CueCard card, bool hovered)
    {
        if (dragCard != null)
        {
            return;
        }

        bool inHand = handCards.Contains(card);
        if (!inHand && !rankedCards.Contains(card))
        {
            return;
        }

        if (inHand)
        {
            if (hovered)
            {
                hoveredCard = card;
            }
            else if (hoveredCard == card)
            {
                hoveredCard = null;
            }

            LayoutCueHand();
        }

        card.EnableInClassList("cue-card--hovered", hovered);
    }

    void BeginCueDrag(CueCard card, PointerDownEvent evt)
    {
        if (evt.button != 0 || dragCard != null || showingShare || dragLayer == null)
        {
            return;
        }

        SetCueHovered(card, false);
        dragCard = card;
        dragPointerId = evt.pointerId;
        card.AddToClassList("cue-card--dragging");
        card.CapturePointer(dragPointerId);

        dragGhost = new CueCard(card.Cue) { pickingMode = PickingMode.Ignore };
        dragGhost.AddToClassList("cue-card--ghost");
        dragGhost.SetOrientation(card.ClassListContains("cue-card--vertical"));
        int rank = rankedCards.IndexOf(card);
        dragGhost.SetRank(rank >= 0 ? rank + 1 : 0);
        dragLayer.Add(dragGhost);
        MoveGhost(dragGhost, evt.position);
        evt.StopPropagation();
    }

    void UpdateCueDrag(CueCard card, PointerMoveEvent evt)
    {
        if (card != dragCard)
        {
            return;
        }

        MoveGhost(dragGhost, evt.position);
        rankingZone?.EnableInClassList("ranking-zone--drop-target", IsOver(rankingZone, evt.position));
    }

    void DropCue(CueCard card, PointerUpEvent evt)
    {
        if (card != dragCard)
        {
            return;
        }

        Vector2 position = evt.position;
        EndCueDrag();

        if (IsOver(rankingZone, position))
        {
            rankedCards.Remove(card);
            handCards.Remove(card);
            rankedCards.Insert(CueDropIndex(position.y), card);
        }
        else if (rankedCards.Remove(card))
        {
            handCards.Add(card);
        }
        else
        {
            return;
        }

        RefreshCueCards();
    }

    /// <summary>Clears the cue drag and its ghost. Safe to call when no drag is active.</summary>
    void EndCueDrag()
    {
        if (dragCard == null)
        {
            return;
        }

        // Cleared before releasing, because releasing raises PointerCaptureOutEvent which calls back into this method.
        CueCard card = dragCard;
        dragCard = null;
        card.RemoveFromClassList("cue-card--dragging");
        if (card.HasPointerCapture(dragPointerId))
        {
            card.ReleasePointer(dragPointerId);
        }

        dragGhost?.RemoveFromHierarchy();
        dragGhost = null;
        rankingZone?.RemoveFromClassList("ranking-zone--drop-target");
    }

    /// <summary>Index in the ranking where a cue dropped at this height should go.</summary>
    int CueDropIndex(float panelY)
    {
        for (int i = 0; i < rankedCards.Count; i++)
        {
            if (panelY < rankedCards[i].worldBound.center.y)
            {
                return i;
            }
        }

        return rankedCards.Count;
    }

    void Diagnose()
    {
        if (rankedCards.Count == 0)
        {
            return;
        }

        prioritiesCommitted = true;
        cuesAtCommit.Clear();
        foreach (CueCard card in rankedCards)
        {
            cuesAtCommit.Add(card.Cue.Id);
        }

        foreach (CueCard card in handCards)
        {
            cuesAtCommit.Add(card.Cue.Id);
        }

        List<RankedDiagnosis> results = DiagnosisRanker.Rank(RankedCues(), diagnoses);
        OnDiagnosed?.Invoke(results);
        ShowShareView();
    }

    void ShowDiagnosisHand()
    {
        hand?.Clear();
        diagnosisCards.Clear();
        hoveredDiagnosis = null;

        List<RankedDiagnosis> results = DiagnosisRanker.Rank(RankedCues(), diagnoses);
        foreach (RankedDiagnosis result in results)
        {
            DiagnosisCard card = CreateDiagnosisCard(result);
            diagnosisCards.Add(card);
            hand?.Add(card);
        }

        LayoutDiagnosisHand();
        UpdateShareHint(results.Count);
    }

    DiagnosisCard CreateDiagnosisCard(RankedDiagnosis result)
    {
        Diagnosis diagnosis = result.Diagnosis;
        DiagnosisCard card = new DiagnosisCard(result, diagnosis != null && IsShared(diagnosis.id));
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
        float spacing = count > 1 ? Mathf.Min(handCardSpacing, maxHandSpread / (count - 1)) : 0f;

        for (int i = 0; i < count; i++)
        {
            DiagnosisCard card = diagnosisCards[i];
            float fromCentre = i - (count - 1) * 0.5f;
            card.style.left = Length.Percent(50f);
            card.style.marginLeft = fromCentre * spacing;
            card.style.top = Mathf.Abs(fromCentre) * handArc;
            card.style.rotate = new Rotate(new Angle(fromCentre * handTilt, AngleUnit.Degree));
            card.BringToFront();
        }

        if (hoveredDiagnosis != null)
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
        if (evt.button != 0 || diagnosisDrag != null || !showingShare || dialogue == null || dialogue.IsOpen || dragLayer == null)
        {
            return;
        }

        SetDiagnosisHovered(card, false);
        diagnosisDrag = card;
        dragPointerId = evt.pointerId;
        card.AddToClassList("diagnosis-card--dragging");
        card.CapturePointer(dragPointerId);

        diagnosisGhost = new DiagnosisCard(card.Result, card.Shared) { pickingMode = PickingMode.Ignore };
        diagnosisGhost.AddToClassList("diagnosis-card--ghost");
        dragLayer.Add(diagnosisGhost);
        MoveGhost(diagnosisGhost, evt.position);
        evt.StopPropagation();
    }

    void UpdateDiagnosisDrag(DiagnosisCard card, PointerMoveEvent evt)
    {
        if (card != diagnosisDrag)
        {
            return;
        }

        MoveGhost(diagnosisGhost, evt.position);
        patientDrop?.EnableInClassList("patient-drop--target", IsOver(patientDrop, evt.position));
    }

    void DropDiagnosis(DiagnosisCard card, PointerUpEvent evt)
    {
        if (card != diagnosisDrag)
        {
            return;
        }

        bool onPatient = IsOver(patientDrop, evt.position);
        EndDiagnosisDrag();
        if (!onPatient)
        {
            return;
        }

        Diagnosis diagnosis = card.Result.Diagnosis;
        if (diagnosis != null && diagnosis.sharedDecision != null && diagnosis.sharedDecision.HasScript)
        {
            dialogue.Begin(diagnosis, profile != null ? profile.patientDisplayName : null);
            return;
        }

        if (shareHint != null)
        {
            shareHint.text = "No shared decision is written for this diagnosis yet.";
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
        patientDrop?.RemoveFromClassList("patient-drop--target");
    }

    void UpdateShareHint(int diagnosisCount)
    {
        if (shareHint == null)
        {
            return;
        }

        if (diagnosisCount == 0)
        {
            shareHint.text = "No diagnosis matches the ranked cues.";
            return;
        }

        shareHint.text = HasNewCues()
            ? "Drag each diagnosis onto the patient. New cues found. Revise priorities to include them."
            : "Drag each diagnosis onto the patient to decide together.";
    }

    bool HasNewCues()
    {
        if (openCues == null)
        {
            return false;
        }

        foreach (RecognizedCue cue in openCues)
        {
            if (!string.IsNullOrEmpty(cue.Id) && !cuesAtCommit.Contains(cue.Id))
            {
                return true;
            }
        }

        return false;
    }

    List<RecognizedCue> RankedCues()
    {
        return rankedCards.Select(card => card.Cue).ToList();
    }

    /// <summary>The right-hand panel before Diagnose only shows the instruction.</summary>
    void ShowDiagnosisHint()
    {
        diagnosisList?.Clear();
        if (diagnosisHint != null)
        {
            diagnosisHint.text = "Rank your cues, then press Diagnose.";
            diagnosisHint.style.display = DisplayStyle.Flex;
        }
    }

    void MoveGhost(VisualElement ghost, Vector2 panelPosition)
    {
        if (ghost == null || dragLayer == null)
        {
            return;
        }

        Vector2 local = dragLayer.WorldToLocal(panelPosition);
        ghost.style.left = local.x;
        ghost.style.top = local.y;
    }

    static bool IsOver(VisualElement zone, Vector2 panelPosition)
    {
        return zone != null && zone.resolvedStyle.display != DisplayStyle.None && zone.worldBound.Contains(panelPosition);
    }
}
