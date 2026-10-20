using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Full-screen "Generate Solutions" step, the fourth step of clinical judgment. The diagnoses the player
/// arrived at are shown in priority order; the solution cards are in a hand at the bottom. The player drags
/// each card onto the diagnosis it is for (or clicks a card, then the diagnosis) and presses Check solutions.
/// Every card is then marked right or wrong with the reason, and the right solutions that were missed are listed.
/// <para>
/// It holds no game logic: <see cref="SolutionFlow"/> opens it, and <see cref="SolutionEvaluator"/> decides
/// what is right. The UXML also holds a small "Generate solutions" button, which <see cref="SolutionFlow"/>
/// shows next to the hypothesis screen's Diagnose button.
/// </para>
/// A click is a press and release without moving; moving further than a few pixels starts a drag.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class SolutionScreenUI : MonoBehaviour
{
    /// <summary>The Generate solutions button was clicked.</summary>
    public event Action OnEntryClicked;
    /// <summary>The Back button was clicked and the screen closed.</summary>
    public event Action OnClosed;

    const float ClickDistance = 6f;

    UIDocument document;
    VisualElement screen;
    Button entryButton;
    Button backButton;
    Button checkButton;
    Button retryButton;
    VisualElement rowList;
    VisualElement hand;
    VisualElement dragLayer;
    Label hint;

    readonly Dictionary<string, SolutionCard> cards = new Dictionary<string, SolutionCard>();
    readonly Dictionary<string, Row> rows = new Dictionary<string, Row>();
    readonly Dictionary<string, string> assignments = new Dictionary<string, string>();
    SO_Solutions set;
    IReadOnlyList<RankedDiagnosis> ranked;
    SolutionCard selectedCard;
    SolutionCard dragCard;
    SolutionCard dragGhost;
    Vector2 dragStart;
    bool dragMoved;
    int dragPointerId;
    bool locked;
    bool entryVisible;

    // One diagnosis row: its drop area for placed cards and the place where the result is written.
    class Row
    {
        public string DiagnosisId;
        public VisualElement Root;
        public VisualElement Drop;
        public VisualElement Result;
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
        EndDrag();
        Unbind();
    }

    /// <summary>Shows or hides the Generate solutions button.</summary>
    public void SetEntryVisible(bool visible)
    {
        entryVisible = visible;
        EnsureBound();
        if (entryButton != null)
        {
            entryButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    /// <summary>Opens the screen for the given diagnoses (most urgent first) with the cards for them in the hand.</summary>
    public void Open(SO_Solutions solutions, IReadOnlyList<RankedDiagnosis> diagnoses)
    {
        EnsureBound();
        if (document == null || solutions == null || diagnoses == null)
        {
            Debug.LogError("SolutionScreenUI: cannot open without a UI document, solutions and diagnoses.", this);
            return;
        }

        set = solutions;
        ranked = diagnoses;
        assignments.Clear();
        selectedCard = null;
        locked = false;
        BuildRows();
        BuildHand();
        Refresh();
        screen.style.display = DisplayStyle.Flex;
        entryButton.style.display = DisplayStyle.None;
    }

    public void Close()
    {
        EndDrag();
        if (screen != null)
        {
            screen.style.display = DisplayStyle.None;
        }

        if (entryButton != null)
        {
            entryButton.style.display = entryVisible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        OnClosed?.Invoke();
    }

    void BuildRows()
    {
        rowList.Clear();
        rows.Clear();
        foreach (RankedDiagnosis result in ranked)
        {
            Diagnosis diagnosis = result.Diagnosis;
            Row row = new Row { DiagnosisId = diagnosis.id, Root = new VisualElement() };
            row.Root.AddToClassList("solution-row");

            VisualElement header = new VisualElement();
            header.AddToClassList("solution-row-header");
            Label priority = new Label($"Priority {result.Priority}");
            priority.AddToClassList("solution-row-priority");
            header.Add(priority);
            Label title = new Label(string.IsNullOrEmpty(diagnosis.title) ? diagnosis.id : diagnosis.title);
            title.AddToClassList("solution-row-title");
            header.Add(title);
            row.Root.Add(header);

            if (!string.IsNullOrWhiteSpace(diagnosis.description))
            {
                Label description = new Label(diagnosis.description);
                description.AddToClassList("solution-row-description");
                row.Root.Add(description);
            }

            row.Drop = new VisualElement();
            row.Drop.AddToClassList("solution-row-drop");
            row.Root.Add(row.Drop);

            row.Result = new VisualElement();
            row.Result.AddToClassList("solution-row-result");
            row.Result.style.display = DisplayStyle.None;
            row.Root.Add(row.Result);

            // Clicking a row places the selected card there. Cards stop their own presses, so this only sees the row.
            Row captured = row;
            row.Root.RegisterCallback<PointerDownEvent>(_ => PlaceSelected(captured));
            rowList.Add(row.Root);
            rows[diagnosis.id] = row;
        }
    }

    void BuildHand()
    {
        cards.Clear();
        foreach (SolutionOption option in SolutionEvaluator.HandFor(set, ranked))
        {
            SolutionCard card = new SolutionCard(option);
            card.RegisterCallback<PointerDownEvent>(evt => BeginPress(card, evt));
            card.RegisterCallback<PointerMoveEvent>(evt => UpdatePress(card, evt));
            card.RegisterCallback<PointerUpEvent>(evt => EndPress(card, evt));
            card.RegisterCallback<PointerCaptureOutEvent>(_ => EndDrag());
            cards[option.id] = card;
        }
    }

    /// <summary>Puts every card where it belongs (hand or a diagnosis row) and updates the buttons and hint.</summary>
    void Refresh()
    {
        hand.Clear();
        foreach (Row row in rows.Values)
        {
            row.Drop.Clear();
        }

        foreach (KeyValuePair<string, SolutionCard> pair in cards)
        {
            SolutionCard card = pair.Value;
            bool placed = assignments.TryGetValue(pair.Key, out string diagnosisId) && rows.ContainsKey(diagnosisId);
            card.SetCompact(placed);
            card.EnableInClassList("solution-card--selected", card == selectedCard && !locked);
            card.EnableInClassList("solution-card--locked", locked);
            if (placed)
            {
                rows[diagnosisId].Drop.Add(card);
            }
            else
            {
                card.RemoveFromClassList("solution-card--right");
                card.RemoveFromClassList("solution-card--wrong");
                hand.Add(card);
            }
        }

        foreach (Row row in rows.Values)
        {
            row.Drop.EnableInClassList("solution-row-drop--empty", row.Drop.childCount == 0);
        }

        checkButton.SetEnabled(!locked && assignments.Count > 0);
        checkButton.style.display = locked ? DisplayStyle.None : DisplayStyle.Flex;
        retryButton.style.display = locked ? DisplayStyle.Flex : DisplayStyle.None;
        hint.text = locked
            ? "Right solutions are green, wrong ones red. Try again to rearrange them."
            : "Drag each solution onto the diagnosis it is for, or click a solution and then a diagnosis. Some cards do not belong anywhere.";
    }

    void BeginPress(SolutionCard card, PointerDownEvent evt)
    {
        if (evt.button != 0 || locked || dragCard != null || dragLayer == null)
        {
            return;
        }

        dragCard = card;
        dragStart = evt.position;
        dragMoved = false;
        dragPointerId = evt.pointerId;
        card.CapturePointer(dragPointerId);
        evt.StopPropagation();
    }

    void UpdatePress(SolutionCard card, PointerMoveEvent evt)
    {
        if (card != dragCard)
        {
            return;
        }

        if (!dragMoved && Vector2.Distance(dragStart, evt.position) > ClickDistance)
        {
            dragMoved = true;
            card.AddToClassList("solution-card--dragging");
            dragGhost = new SolutionCard(card.Option) { pickingMode = PickingMode.Ignore };
            dragGhost.AddToClassList("solution-card--ghost");
            dragLayer.Add(dragGhost);
        }

        if (dragMoved)
        {
            MoveGhost(evt.position);
            foreach (Row row in rows.Values)
            {
                row.Root.EnableInClassList("solution-row--drop-target", row.Root.worldBound.Contains(evt.position));
            }
        }
    }

    void EndPress(SolutionCard card, PointerUpEvent evt)
    {
        if (card != dragCard)
        {
            return;
        }

        Vector2 position = evt.position;
        bool wasDrag = dragMoved;
        EndDrag();

        if (!wasDrag)
        {
            Click(card);
            return;
        }

        Row target = rows.Values.FirstOrDefault(row => row.Root.worldBound.Contains(position));
        if (target != null)
        {
            assignments[card.Option.id] = target.DiagnosisId;
        }
        else
        {
            assignments.Remove(card.Option.id);
        }

        selectedCard = null;
        Refresh();
    }

    // A click on a placed card returns it to the hand; on a hand card it selects it for the next row click.
    void Click(SolutionCard card)
    {
        if (assignments.Remove(card.Option.id))
        {
            selectedCard = null;
        }
        else
        {
            selectedCard = selectedCard == card ? null : card;
        }

        Refresh();
    }

    void PlaceSelected(Row row)
    {
        if (locked || selectedCard == null)
        {
            return;
        }

        assignments[selectedCard.Option.id] = row.DiagnosisId;
        selectedCard = null;
        Refresh();
    }

    /// <summary>Clears the drag state and ghost. Safe to call when no drag is active.</summary>
    void EndDrag()
    {
        if (dragCard == null)
        {
            return;
        }

        // Cleared before releasing, because releasing raises PointerCaptureOutEvent which calls back into this method.
        SolutionCard card = dragCard;
        dragCard = null;
        dragMoved = false;
        card.RemoveFromClassList("solution-card--dragging");
        if (card.HasPointerCapture(dragPointerId))
        {
            card.ReleasePointer(dragPointerId);
        }

        dragGhost?.RemoveFromHierarchy();
        dragGhost = null;
        foreach (Row row in rows.Values)
        {
            row.Root.RemoveFromClassList("solution-row--drop-target");
        }
    }

    void MoveGhost(Vector2 panelPosition)
    {
        Vector2 local = dragLayer.WorldToLocal(panelPosition);
        dragGhost.style.left = local.x;
        dragGhost.style.top = local.y;
    }

    /// <summary>Locks the cards and writes, for each diagnosis, what was right, wrong and missed.</summary>
    void Check()
    {
        if (locked || assignments.Count == 0)
        {
            return;
        }

        locked = true;
        selectedCard = null;
        Refresh();

        foreach (DiagnosisReport report in SolutionEvaluator.Evaluate(set, ranked, assignments))
        {
            Row row = rows[report.Diagnosis.Diagnosis.id];
            row.Result.Clear();
            row.Result.style.display = DisplayStyle.Flex;

            foreach (PlacedSolution placed in report.Placed)
            {
                cards[placed.Option.id].AddToClassList(placed.Right ? "solution-card--right" : "solution-card--wrong");
                AddResultLine(row, placed.Right ? "Right" : "Not right", placed.Option.title, placed.Note, placed.Right ? "solution-result--right" : "solution-result--wrong");
            }

            foreach (SolutionOption missed in report.Missed)
            {
                AddResultLine(row, "Missed", missed.title, missed.description, "solution-result--missed");
            }

            if (report.Complete)
            {
                AddResultLine(row, "Complete", "Every expected solution is placed.", null, "solution-result--right");
            }
        }
    }

    static void AddResultLine(Row row, string tag, string title, string detail, string cssClass)
    {
        VisualElement line = new VisualElement();
        line.AddToClassList("solution-result");
        line.AddToClassList(cssClass);

        Label tagLabel = new Label(tag);
        tagLabel.AddToClassList("solution-result-tag");
        line.Add(tagLabel);

        Label text = new Label(string.IsNullOrWhiteSpace(detail) ? title : $"{title}: {detail}");
        text.AddToClassList("solution-result-text");
        line.Add(text);
        row.Result.Add(line);
    }

    void Retry()
    {
        assignments.Clear();
        selectedCard = null;
        locked = false;
        foreach (Row row in rows.Values)
        {
            row.Result.Clear();
            row.Result.style.display = DisplayStyle.None;
        }

        foreach (SolutionCard card in cards.Values)
        {
            card.RemoveFromClassList("solution-card--right");
            card.RemoveFromClassList("solution-card--wrong");
        }

        Refresh();
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
        screen = root.Q<VisualElement>("solution-screen");
        entryButton = root.Q<Button>("entry-button");
        backButton = root.Q<Button>("solution-back");
        checkButton = root.Q<Button>("solution-check");
        retryButton = root.Q<Button>("solution-retry");
        hint = root.Q<Label>("solution-hint");
        dragLayer = root.Q<VisualElement>("solution-drag-layer");
        rowList = root.Q<ScrollView>("solution-rows")?.contentContainer;
        ScrollView handScroll = root.Q<ScrollView>("solution-hand");
        hand = handScroll?.contentContainer;
        if (hand != null)
        {
            hand.style.flexDirection = FlexDirection.Row;
            hand.style.flexWrap = Wrap.Wrap;
        }

        if (entryButton != null) { entryButton.clicked += HandleEntry; }
        if (backButton != null) { backButton.clicked += Close; }
        if (checkButton != null) { checkButton.clicked += Check; }
        if (retryButton != null) { retryButton.clicked += Retry; }

        screen.style.display = DisplayStyle.None;
        entryButton.style.display = entryVisible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    void Unbind()
    {
        if (entryButton != null) { entryButton.clicked -= HandleEntry; }
        if (backButton != null) { backButton.clicked -= Close; }
        if (checkButton != null) { checkButton.clicked -= Check; }
        if (retryButton != null) { retryButton.clicked -= Retry; }
        document = null;
    }

    void HandleEntry()
    {
        OnEntryClicked?.Invoke();
    }
}
