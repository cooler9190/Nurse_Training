using UnityEngine.UIElements;

/// <summary>
/// Compact diagnosis card for the share-decisions hand: priority, title, and whether it has been
/// shared with the patient. Styling lives in <c>HypothesisScreen.uss</c>.
/// </summary>
public class DiagnosisCard : VisualElement
{
    readonly Label badge;
    readonly VisualElement body;
    Label treatmentTitle;
    Label treatmentPros;
    Label treatmentCons;

    public RankedDiagnosis Result { get; }
    public bool Shared { get; private set; }
    public TreatmentOption ChosenTreatment { get; private set; }

    public DiagnosisCard(RankedDiagnosis result, bool shared)
    {
        Result = result;
        Diagnosis diagnosis = result.Diagnosis;
        AddToClassList("diagnosis-card");
        AddToClassList("diagnosis-card--in-hand");

        VisualElement cardBody = new VisualElement();
        cardBody.AddToClassList("diagnosis-card-body");
        Add(cardBody);
        body = cardBody;

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

    /// <summary>Writes the decided treatment onto this card. A later decision replaces it.</summary>
    public void SetChosenTreatment(TreatmentOption treatment)
    {
        ChosenTreatment = treatment;
        bool show = treatment != null && !string.IsNullOrWhiteSpace(treatment.title);
        if (show && treatmentTitle == null)
        {
            treatmentTitle = AddDetail("diagnosis-card-treatment");
            treatmentPros = AddDetail("diagnosis-card-treatment-detail");
            treatmentCons = AddDetail("diagnosis-card-treatment-detail");
        }

        if (treatmentTitle == null)
        {
            return;
        }

        treatmentTitle.text = show ? treatment.title : string.Empty;
        treatmentPros.text = show && !string.IsNullOrWhiteSpace(treatment.pros) ? $"Pros: {treatment.pros}" : string.Empty;
        treatmentCons.text = show && !string.IsNullOrWhiteSpace(treatment.cons) ? $"Cons: {treatment.cons}" : string.Empty;
        DisplayStyle detail = show ? DisplayStyle.Flex : DisplayStyle.None;
        treatmentTitle.style.display = detail;
        treatmentPros.style.display = string.IsNullOrEmpty(treatmentPros.text) ? DisplayStyle.None : DisplayStyle.Flex;
        treatmentCons.style.display = string.IsNullOrEmpty(treatmentCons.text) ? DisplayStyle.None : DisplayStyle.Flex;
    }

    Label AddDetail(string className)
    {
        Label label = new Label();
        label.AddToClassList(className);
        label.pickingMode = PickingMode.Ignore;
        body.Add(label);
        return label;
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
