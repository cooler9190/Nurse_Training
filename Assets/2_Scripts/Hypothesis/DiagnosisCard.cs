using UnityEngine.UIElements;

/// <summary>
/// UI Toolkit element that displays one <see cref="RankedDiagnosis"/>: priority badge, title,
/// description, treatment and the ranked cues it is based on.
/// Styling lives in <c>HypothesisScreen.uss</c>; this class only builds the labels and classes.
/// </summary>
public class DiagnosisCard : VisualElement
{
    public DiagnosisCard(RankedDiagnosis result)
    {
        Diagnosis diagnosis = result.Diagnosis;
        AddToClassList("diagnosis-card");

        VisualElement header = new VisualElement();
        header.AddToClassList("diagnosis-card-header");
        Add(header);

        Label priority = new Label($"Priority {result.Priority}");
        priority.AddToClassList("diagnosis-card-priority");
        header.Add(priority);

        Label title = new Label(string.IsNullOrEmpty(diagnosis.title) ? diagnosis.id : diagnosis.title);
        title.AddToClassList("diagnosis-card-title");
        header.Add(title);

        AddSection("Diagnosis", diagnosis.description);
        AddSection("Treatment", diagnosis.treatment);

        if (result.SupportingCueTitles != null && result.SupportingCueTitles.Count > 0)
        {
            Label basedOn = new Label($"Based on: {string.Join(", ", result.SupportingCueTitles)}");
            basedOn.AddToClassList("diagnosis-card-based-on");
            Add(basedOn);
        }
    }

    void AddSection(string heading, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        Label headingLabel = new Label(heading);
        headingLabel.AddToClassList("diagnosis-card-heading");
        Add(headingLabel);

        Label body = new Label(text);
        body.AddToClassList("diagnosis-card-text");
        Add(body);
    }
}
