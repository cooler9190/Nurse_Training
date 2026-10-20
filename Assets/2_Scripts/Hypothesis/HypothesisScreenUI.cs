using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Full-screen "Prioritise Hypotheses" step. Collected cues are shown as a fanned hand of
/// <see cref="CueCard"/>s at the bottom of the screen. The player drags cards into the ranking list
/// on the left (most severe and urgent at the top) and presses Diagnose to get
/// <see cref="DiagnosisCard"/>s on the right, ordered by <see cref="DiagnosisRanker"/>.
/// <para>
/// How dragging works: the pressed card captures the pointer and stays where it is (dimmed) while
/// a ghost copy follows the pointer in the drag layer. On release the card is moved into the ranking
/// list at the drop position, or back to the hand if it was dropped anywhere else.
/// </para>
/// The ranking is remembered by cue id between openings and only cleared by <see cref="ResetRanking"/>.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class HypothesisScreenUI : MonoBehaviour
{
    public event Action OnClosed;
    /// <summary>Raised after Diagnose with the prioritised diagnoses (may be empty).</summary>
    public event Action<IReadOnlyList<RankedDiagnosis>> OnDiagnosed;

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
    VisualElement diagnosisList;
    VisualElement hand;
    VisualElement dragLayer;
    Label rankingHint;
    Label diagnosisHint;
    Button diagnoseButton;
    Button checkSolutionButton;
    Button backButton;

    readonly List<string> rankedIds = new List<string>();
    readonly List<CueCard> rankedCards = new List<CueCard>();
    readonly List<CueCard> handCards = new List<CueCard>();
    IReadOnlyList<Diagnosis> diagnoses;
    IReadOnlyList<string> solutionIds;
    CueCard hoveredCard;
    CueCard dragCard;
    CueCard dragGhost;
    int dragPointerId;
    bool isOpen;

    /// <summary>Cue ids in the player's current order; index 0 is the highest priority.</summary>
    public IReadOnlyList<string> RankedCueIds => rankedIds;

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
        if (diagnoseButton != null)
        {
            diagnoseButton.clicked -= Diagnose;
        }

        if (checkSolutionButton != null)
        {
            checkSolutionButton.clicked -= CheckSolution;
        }

        if (backButton != null)
        {
            backButton.clicked -= Close;
        }

        document = null;
    }

    /// <summary>
    /// Shows the screen with the given cues. Cues that were ranked before keep their place in the
    /// ranking; all others are dealt into the hand. <paramref name="solutionDiagnosisIds"/> is the
    /// correct diagnosis order used by the Check button.
    /// </summary>
    public void Open(IReadOnlyList<RecognizedCue> cues, IReadOnlyList<Diagnosis> patientDiagnoses,
        IReadOnlyList<string> solutionDiagnosisIds = null)
    {
        EnsureBound();
        if (document == null)
        {
            Debug.LogError("HypothesisScreenUI: UIDocument is not ready.", this);
            return;
        }

        diagnoses = patientDiagnoses;
        solutionIds = solutionDiagnosisIds;
        rankedCards.Clear();
        handCards.Clear();
        hoveredCard = null;

        List<RecognizedCue> allCues = cues != null ? cues.ToList() : new List<RecognizedCue>();
        foreach (string id in rankedIds)
        {
            int index = allCues.FindIndex(cue => cue.Id == id);
            if (index >= 0)
            {
                rankedCards.Add(CreateCard(allCues[index]));
                allCues.RemoveAt(index);
            }
        }

        foreach (RecognizedCue cue in allCues)
        {
            handCards.Add(CreateCard(cue));
        }

        RefreshCards();
        SetVisible(true);
    }

    public void Close()
    {
        EndDrag();
        SetVisible(false);
        OnClosed?.Invoke();
    }

    /// <summary>Forgets the ranking, e.g. when a new patient starts.</summary>
    public void ResetRanking()
    {
        rankedIds.Clear();
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
        diagnosisList = root.Q<ScrollView>("diagnosis-list")?.contentContainer;
        hand = root.Q<VisualElement>("hand");
        dragLayer = root.Q<VisualElement>("drag-layer");
        rankingHint = root.Q<Label>("ranking-hint");
        diagnosisHint = root.Q<Label>("diagnosis-hint");
        diagnoseButton = root.Q<Button>("diagnose-button");
        checkSolutionButton = root.Q<Button>("check-solution-button");
        backButton = root.Q<Button>("back-button");

        if (diagnoseButton != null)
        {
            diagnoseButton.clicked += Diagnose;
        }

        if (checkSolutionButton != null)
        {
            checkSolutionButton.clicked += CheckSolution;
        }

        if (backButton != null)
        {
            backButton.clicked += Close;
        }

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

        VisualElement root = document.rootVisualElement;
        root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    CueCard CreateCard(RecognizedCue cue)
    {
        CueCard card = new CueCard(cue);
        card.RegisterCallback<PointerEnterEvent>(_ => SetHovered(card, true));
        card.RegisterCallback<PointerLeaveEvent>(_ => SetHovered(card, false));
        card.RegisterCallback<PointerDownEvent>(evt => BeginDrag(card, evt));
        card.RegisterCallback<PointerMoveEvent>(evt => UpdateDrag(card, evt));
        card.RegisterCallback<PointerUpEvent>(evt => Drop(card, evt));
        card.RegisterCallback<PointerCaptureOutEvent>(_ => EndDrag());
        return card;
    }

    /// <summary>
    /// Puts every card in its container (ranking list or hand) with the matching style and rank
    /// number, and saves the ranking. Any shown diagnoses are cleared because they no longer match.
    /// </summary>
    void RefreshCards()
    {
        rankingList?.Clear();
        hand?.Clear();

        for (int i = 0; i < rankedCards.Count; i++)
        {
            CueCard card = rankedCards[i];
            card.RemoveFromClassList("cue-card--in-hand");
            card.RemoveFromClassList("cue-card--hovered");
            card.AddToClassList("cue-card--ranked");
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
            card.SetRank(0);
            hand?.Add(card);
        }

        LayoutHand();

        rankedIds.Clear();
        rankedIds.AddRange(rankedCards.Select(card => card.Cue.Id));

        if (rankingHint != null)
        {
            rankingHint.style.display = rankedCards.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        diagnoseButton?.SetEnabled(rankedCards.Count > 0);
        ShowDiagnoses(null);
    }

    /// <summary>
    /// Fans the hand cards out from the centre: each card is offset sideways, tilted and lowered a
    /// little more the further it is from the middle. The hovered card is straightened and drawn on top.
    /// </summary>
    void LayoutHand()
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

    void SetHovered(CueCard card, bool hovered)
    {
        if (dragCard != null || !handCards.Contains(card))
        {
            return;
        }

        if (hovered)
        {
            hoveredCard = card;
        }
        else if (hoveredCard == card)
        {
            hoveredCard = null;
        }

        card.EnableInClassList("cue-card--hovered", hovered);
        LayoutHand();
    }

    void BeginDrag(CueCard card, PointerDownEvent evt)
    {
        if (evt.button != 0 || dragCard != null || dragLayer == null)
        {
            return;
        }

        SetHovered(card, false);
        dragCard = card;
        dragPointerId = evt.pointerId;
        card.AddToClassList("cue-card--dragging");
        card.CapturePointer(dragPointerId);

        dragGhost = new CueCard(card.Cue) { pickingMode = PickingMode.Ignore };
        dragGhost.AddToClassList("cue-card--ghost");
        dragLayer.Add(dragGhost);
        MoveGhost(evt.position);
        evt.StopPropagation();
    }

    void UpdateDrag(CueCard card, PointerMoveEvent evt)
    {
        if (card != dragCard)
        {
            return;
        }

        MoveGhost(evt.position);
        rankingZone?.EnableInClassList("ranking-zone--drop-target", IsOverRankingZone(evt.position));
    }

    void Drop(CueCard card, PointerUpEvent evt)
    {
        if (card != dragCard)
        {
            return;
        }

        Vector2 position = evt.position;
        EndDrag();

        if (IsOverRankingZone(position))
        {
            rankedCards.Remove(card);
            handCards.Remove(card);
            rankedCards.Insert(DropIndex(position.y), card);
        }
        else if (rankedCards.Remove(card))
        {
            handCards.Add(card);
        }
        else
        {
            return;
        }

        RefreshCards();
    }

    /// <summary>Clears the drag state and ghost. Safe to call when no drag is active.</summary>
    void EndDrag()
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

    void MoveGhost(Vector2 panelPosition)
    {
        Vector2 local = dragLayer.WorldToLocal(panelPosition);
        dragGhost.style.left = local.x;
        dragGhost.style.top = local.y;
    }

    bool IsOverRankingZone(Vector2 panelPosition)
    {
        return rankingZone != null && rankingZone.worldBound.Contains(panelPosition);
    }

    /// <summary>Index in the ranking where a card dropped at this height should go (the dragged card is not in the list).</summary>
    int DropIndex(float panelY)
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
        List<RankedDiagnosis> results = DiagnosisRanker.Rank(rankedCards.Select(card => card.Cue).ToList(), diagnoses);
        ShowDiagnoses(results);
        OnDiagnosed?.Invoke(results);
    }

    /// <summary>Temporary: logs whether the current ranking yields the patient's solution diagnoses in the right order.</summary>
    void CheckSolution()
    {
        if (solutionIds == null || solutionIds.Count == 0)
        {
            Debug.LogWarning("HypothesisScreenUI: this patient has no solution diagnoses authored.", this);
            return;
        }

        List<RankedDiagnosis> results = DiagnosisRanker.Rank(rankedCards.Select(card => card.Cue).ToList(), diagnoses);
        DiagnosisCheckResult check = DiagnosisSolutionChecker.Check(results, solutionIds);
        if (check.IsCorrect)
        {
            Debug.Log(check.ToReport(), this);
        }
        else
        {
            Debug.LogWarning(check.ToReport(), this);
        }
    }

    /// <summary>Shows the diagnosis cards, or the hint text when <paramref name="results"/> is null.</summary>
    void ShowDiagnoses(IReadOnlyList<RankedDiagnosis> results)
    {
        if (diagnosisList == null)
        {
            return;
        }

        diagnosisList.Clear();
        if (results != null)
        {
            foreach (RankedDiagnosis result in results)
            {
                diagnosisList.Add(new DiagnosisCard(result));
            }
        }

        if (diagnosisHint != null)
        {
            diagnosisHint.text = results == null
                ? "Rank your cues, then press Diagnose."
                : "No diagnosis matches the ranked cues.";
            diagnosisHint.style.display = results == null || results.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
