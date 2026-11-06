using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Full-screen cue ranking. Collected cues are a fanned hand of <see cref="CueCard"/>s. The player
/// drags them into the ranking list (most urgent at the top) and presses Diagnose. The screen then
/// closes and the interview shows the resulting diagnosis cards.
/// <para>
/// How dragging works: the pressed card captures the pointer and stays where it is (dimmed) while
/// a ghost copy follows the pointer in the drag layer. A card dropped on the ranking list is
/// inserted there. A ranked card dropped anywhere else returns to the hand.
/// </para>
/// The cue ranking is remembered until <see cref="ResetRanking"/>.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class HypothesisScreenUI : MonoBehaviour
{
    public event Action OnClosed;
    /// <summary>Raised after Diagnose with the prioritised diagnoses (may be empty). The screen then closes.</summary>
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
    Button backButton;

    readonly List<string> rankedIds = new List<string>();
    readonly List<CueCard> rankedCards = new List<CueCard>();
    readonly List<CueCard> handCards = new List<CueCard>();
    IReadOnlyList<Diagnosis> diagnoses;
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
        EndCueDrag();

        if (diagnoseButton != null)
        {
            diagnoseButton.clicked -= Diagnose;
        }

        if (backButton != null)
        {
            backButton.clicked -= Close;
        }

        if (document != null)
        {
            GameAudio.UnregisterButtonClicks(document.rootVisualElement);
        }

        document = null;
    }

    /// <summary>Shows the ranking screen. Previously ranked cues keep their place.</summary>
    public void Open(IReadOnlyList<RecognizedCue> cues, SO_Patient patient)
    {
        EnsureBound();
        if (document == null)
        {
            Debug.LogError("HypothesisScreenUI: UIDocument is not ready.", this);
            return;
        }

        diagnoses = patient != null ? patient.diagnoses : null;
        rankedCards.Clear();
        handCards.Clear();
        hoveredCard = null;

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

        RefreshCueCards();
        SetVisible(true);
    }

    public void Close()
    {
        EndCueDrag();
        SetVisible(false);
        OnClosed?.Invoke();
    }

    /// <summary>Forgets the cue ranking.</summary>
    public void ResetRanking()
    {
        rankedIds.Clear();
        rankedCards.Clear();
        handCards.Clear();
        EndCueDrag();
        if (isOpen)
        {
            RefreshCueCards();
        }
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
        backButton = root.Q<Button>("back-button");

        if (diagnoseButton != null)
        {
            diagnoseButton.clicked += Diagnose;
        }

        if (backButton != null)
        {
            backButton.clicked += Close;
        }

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

    /// <summary>Puts every cue card in the ranking list or the hand and saves the ranking.</summary>
    void RefreshCueCards()
    {
        rankingList?.Clear();
        hand?.Clear();

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
        if (evt.button != 0 || dragCard != null || dragLayer == null)
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

        List<RankedDiagnosis> results = DiagnosisRanker.Rank(RankedCues(), diagnoses);
        OnDiagnosed?.Invoke(results);
        Close();
    }

    List<RecognizedCue> RankedCues()
    {
        return rankedCards.Select(card => card.Cue).ToList();
    }

    /// <summary>The right-hand panel only shows the instruction. Diagnosis cards are shown in the patient file.</summary>
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
