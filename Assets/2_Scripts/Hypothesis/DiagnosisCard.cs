using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Compact diagnosis card: priority, title, and whether it has been shared. After a decision,
/// the chosen treatment is written on the card and every label scales by the same factor so the
/// text stays inside the card. Styling lives in <c>HypothesisScreen.uss</c>.
/// </summary>
public class DiagnosisCard : VisualElement
{
    const float TextWidth = 129f;
    const float TextHeight = 186f;
    const float MinTextScale = 0.72f;
    const float PrioritySize = 13f;
    const float TitleSize = 17f;
    const float BadgeSize = 12f;
    const float TreatmentSize = 12f;
    const float DetailSize = 11f;

    readonly Label badge;
    readonly VisualElement body;
    readonly List<Label> fitLabels = new List<Label>();
    readonly List<float> fitSizes = new List<float>();
    Label treatmentTitle;
    Label treatmentPros;
    Label treatmentCons;

    public RankedDiagnosis Result { get; }
    public bool Shared { get; private set; }
    public bool Decided { get; private set; }
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

        AddFittedLabel(body, "diagnosis-card-priority", PrioritySize, $"Priority {result.Priority}");

        string title = diagnosis != null && !string.IsNullOrEmpty(diagnosis.title) ? diagnosis.title : diagnosis?.id;
        AddFittedLabel(body, "diagnosis-card-title", TitleSize, string.IsNullOrEmpty(title) ? "Diagnosis" : title);

        badge = AddFittedLabel(body, "diagnosis-card-badge", BadgeSize);
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

    /// <summary>A finished diagnosis cannot be dragged into another talk.</summary>
    public void SetDecided(bool decided)
    {
        Decided = decided;
        EnableInClassList("diagnosis-card--decided", decided);
    }

    /// <summary>Writes the decided treatment onto this card and scales the text so it fits. A later decision replaces it.</summary>
    public void SetChosenTreatment(TreatmentOption treatment)
    {
        ChosenTreatment = treatment;
        bool show = treatment != null && !string.IsNullOrWhiteSpace(treatment.title);
        if (show && treatmentTitle == null)
        {
            treatmentTitle = AddFittedLabel(body, "diagnosis-card-treatment", TreatmentSize);
            treatmentPros = AddFittedLabel(body, "diagnosis-card-treatment-detail", DetailSize);
            treatmentCons = AddFittedLabel(body, "diagnosis-card-treatment-detail", DetailSize);
        }

        if (treatmentTitle == null)
        {
            RequestFit();
            return;
        }

        treatmentTitle.text = show ? treatment.title : string.Empty;
        treatmentPros.text = show && !string.IsNullOrWhiteSpace(treatment.pros) ? $"Pros: {treatment.pros}" : string.Empty;
        treatmentCons.text = show && !string.IsNullOrWhiteSpace(treatment.cons) ? $"Cons: {treatment.cons}" : string.Empty;
        treatmentTitle.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        treatmentPros.style.display = string.IsNullOrEmpty(treatmentPros.text) ? DisplayStyle.None : DisplayStyle.Flex;
        treatmentCons.style.display = string.IsNullOrEmpty(treatmentCons.text) ? DisplayStyle.None : DisplayStyle.Flex;
        RequestFit();
    }

    /// <summary>Fits once the card is on a panel, so text measurement has a font to use.</summary>
    void RequestFit()
    {
        if (panel != null)
        {
            schedule.Execute(FitText);
            return;
        }

        RegisterCallback<AttachToPanelEvent>(FitWhenAttached);
    }

    void FitWhenAttached(AttachToPanelEvent evt)
    {
        UnregisterCallback<AttachToPanelEvent>(FitWhenAttached);
        schedule.Execute(FitText);
    }

    Label AddFittedLabel(VisualElement parent, string className, float fontSize, string text = null)
    {
        Label label = new Label(text ?? string.Empty);
        label.AddToClassList(className);
        label.style.fontSize = fontSize;
        label.style.whiteSpace = WhiteSpace.Normal;
        label.pickingMode = PickingMode.Ignore;
        parent.Add(label);
        fitLabels.Add(label);
        fitSizes.Add(fontSize);
        return label;
    }

    /// <summary>
    /// One scale for every visible label, from how tall the text is compared with the card.
    /// Labels that cannot be measured yet keep their normal size.
    /// </summary>
    void FitText()
    {
        float needed = 0f;
        int visible = 0;
        for (int i = 0; i < fitLabels.Count; i++)
        {
            Label label = fitLabels[i];
            label.style.fontSize = fitSizes[i];
            if (!IsShown(label))
            {
                continue;
            }

            Vector2 size = label.MeasureTextSize(label.text, TextWidth, MeasureMode.AtMost, 0f, MeasureMode.Undefined);
            if (size.y <= 0f)
            {
                return;
            }

            needed += size.y;
            visible++;
        }

        if (visible > 1)
        {
            needed += (visible - 1) * 4f;
        }

        float factor = needed <= 1f ? 1f : Mathf.Clamp(TextHeight / needed, MinTextScale, 1f);
        for (int i = 0; i < fitLabels.Count; i++)
        {
            fitLabels[i].style.fontSize = fitSizes[i] * (IsShown(fitLabels[i]) ? factor : 1f);
        }
    }

    static bool IsShown(Label label)
    {
        return label.style.display != DisplayStyle.None && !string.IsNullOrEmpty(label.text);
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
