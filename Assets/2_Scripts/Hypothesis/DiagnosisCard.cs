using UnityEngine.UIElements;

/// <summary>
/// Compact diagnosis card for the share-decisions hand: priority, title, and whether it has been
/// shared with the patient. Styling lives in <c>HypothesisScreen.uss</c>.
/// </summary>
public class DiagnosisCard : VisualElement
{
    readonly Label badge;

    public RankedDiagnosis Result { get; }
    public bool Shared { get; private set; }

    public DiagnosisCard(RankedDiagnosis result, bool shared)
    {
        Result = result;
        Diagnosis diagnosis = result.Diagnosis;
        AddToClassList("diagnosis-card");
        AddToClassList("diagnosis-card--in-hand");

        VisualElement body = new VisualElement();
        body.AddToClassList("diagnosis-card-body");
        Add(body);

        Label priority = new Label($"Priority {result.Priority}");
        priority.AddToClassList("diagnosis-card-priority");
        body.Add(priority);

        string title = diagnosis != null && !string.IsNullOrEmpty(diagnosis.title) ? diagnosis.title : diagnosis?.id;
        Label titleLabel = new Label(string.IsNullOrEmpty(title) ? "Diagnosis" : title);
        titleLabel.AddToClassList("diagnosis-card-title");
        body.Add(titleLabel);

        badge = new Label();
        badge.AddToClassList("diagnosis-card-badge");
        body.Add(badge);
        SetShared(shared);

        VisualElement select = new VisualElement();
        select.AddToClassList("card-select");
        select.pickingMode = PickingMode.Ignore;
        Add(select);

        // The card must be the only pick target, otherwise moving over a child label
        // would count as leaving the card and break hover and dragging.
        foreach (VisualElement child in Children())
        {
            SetPickingIgnored(child);
        }
    }

    /// <summary>Shows Shared or Not shared.</summary>
    public void SetShared(bool shared)
    {
        Shared = shared;
        badge.text = shared ? "Shared" : "Not shared";
        EnableInClassList("diagnosis-card--shared", shared);
    }

    static void SetPickingIgnored(VisualElement element)
    {
        element.pickingMode = PickingMode.Ignore;
        foreach (VisualElement child in element.Children())
        {
            SetPickingIgnored(child);
        }
    }
}
