using UnityEngine.UIElements;

/// <summary>
/// One treatment in the share-decisions hand. Hover uses the same select outline and scale as a cue card.
/// </summary>
public class TreatmentCard : VisualElement
{
    readonly Label badge;

    public TreatmentOption Treatment { get; }
    public int Index { get; }

    public TreatmentCard(TreatmentOption treatment, int index)
    {
        Treatment = treatment;
        Index = index;
        AddToClassList("treatment-card");
        AddToClassList("treatment-card--in-hand");

        VisualElement body = new VisualElement();
        body.AddToClassList("treatment-card-body");
        Add(body);

        Label title = new Label(string.IsNullOrWhiteSpace(treatment.title) ? "Treatment" : treatment.title);
        title.AddToClassList("treatment-card-title");
        body.Add(title);

        badge = new Label();
        badge.AddToClassList("diagnosis-card-badge");
        badge.style.display = DisplayStyle.None;
        body.Add(badge);

        VisualElement select = new VisualElement();
        select.AddToClassList("card-select");
        select.pickingMode = PickingMode.Ignore;
        Add(select);

        foreach (VisualElement child in Children())
        {
            SetPickingIgnored(child);
        }
    }

    /// <summary>Marks the card after the player has addressed it. Discussed treatments count toward sharing.</summary>
    public void SetAddressed(bool discussedProsCons)
    {
        badge.text = discussedProsCons ? "Discussed" : "Skipped";
        badge.style.display = DisplayStyle.Flex;
        EnableInClassList("treatment-card--discussed", discussedProsCons);
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
