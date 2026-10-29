using UnityEngine.UIElements;

/// <summary>
/// UI Toolkit element that displays one <see cref="RecognizedCue"/> as a card: title, an origin
/// badge (how the cue was obtained), the description, the source detail and, once ranked, its rank.
/// The card art and hover outline live in <c>HypothesisScreen.uss</c>. Vertical art is the hand;
/// horizontal art is the ranking row. This class only builds the labels and classes.
/// </summary>
public class CueCard : VisualElement
{
    readonly VisualElement body;
    readonly Label rankLabel;

    public RecognizedCue Cue { get; }

    public CueCard(RecognizedCue cue)
    {
        Cue = cue;
        AddToClassList("cue-card");
        AddToClassList($"cue-card--{cue.Origin.ToString().ToLowerInvariant()}");
        SetOrientation(true);

        VisualElement header = new VisualElement();
        header.AddToClassList("cue-card-header");
        Add(header);

        body = new VisualElement();
        body.AddToClassList("cue-card-body");
        Add(body);

        rankLabel = new Label();
        rankLabel.AddToClassList("cue-card-rank");
        header.Add(rankLabel);

        Label origin = new Label(OriginLabel(cue.Origin));
        origin.AddToClassList("cue-card-origin");
        origin.AddToClassList($"cue-card-origin--{cue.Origin.ToString().ToLowerInvariant()}");
        header.Add(origin);

        AddLabel(string.IsNullOrEmpty(cue.Title) ? cue.Id : cue.Title, "cue-card-title");
        AddLabel(cue.Description, "cue-card-description");
        AddLabel(cue.Source, "cue-card-source");
        AddSelector();

        // The card must be the only pick target, otherwise moving over a child label
        // would count as leaving the card and break hover and dragging.
        foreach (VisualElement child in Children())
        {
            SetPickingIgnored(child);
        }

        SetRank(0);
    }

    /// <summary>Vertical art for the hand, horizontal art for a ranking row.</summary>
    public void SetOrientation(bool vertical)
    {
        EnableInClassList("cue-card--vertical", vertical);
        EnableInClassList("cue-card--horizontal", !vertical);
    }

    /// <summary>Shows the rank badge with the given number; 0 or less hides it.</summary>
    public void SetRank(int rank)
    {
        rankLabel.text = rank > 0 ? rank.ToString() : string.Empty;
        rankLabel.style.display = rank > 0 ? DisplayStyle.Flex : DisplayStyle.None;
    }

    void AddLabel(string text, string className)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        Label label = new Label(text);
        label.AddToClassList(className);
        body.Add(label);
    }

    void AddSelector()
    {
        VisualElement select = new VisualElement();
        select.AddToClassList("card-select");
        select.pickingMode = PickingMode.Ignore;
        Add(select);
    }

    static void SetPickingIgnored(VisualElement element)
    {
        element.pickingMode = PickingMode.Ignore;
        foreach (VisualElement child in element.Children())
        {
            SetPickingIgnored(child);
        }
    }

    static string OriginLabel(CueOrigin origin)
    {
        switch (origin)
        {
            case CueOrigin.Question: return "Interview";
            case CueOrigin.Visual: return "Observation";
            case CueOrigin.Examination: return "Examination";
            default: return origin.ToString();
        }
    }
}
