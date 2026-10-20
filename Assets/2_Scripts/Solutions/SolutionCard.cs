using UnityEngine.UIElements;

/// <summary>
/// UI Toolkit element that displays one <see cref="SolutionOption"/> as a card: its title and a short
/// description. In the hand it is shown in full; placed on a diagnosis it is compact (title only).
/// Styling lives in <c>SolutionScreen.uss</c>; this class only builds the labels and classes.
/// </summary>
public class SolutionCard : VisualElement
{
    readonly Label description;

    public SolutionOption Option { get; }

    public SolutionCard(SolutionOption option)
    {
        Option = option;
        AddToClassList("solution-card");

        Label title = new Label(string.IsNullOrEmpty(option.title) ? option.id : option.title);
        title.AddToClassList("solution-card-title");
        Add(title);

        description = new Label(option.description);
        description.AddToClassList("solution-card-description");
        description.style.display = string.IsNullOrWhiteSpace(option.description) ? DisplayStyle.None : DisplayStyle.Flex;
        Add(description);

        // The card must be the only pick target, otherwise moving over a child label would
        // count as leaving the card and break hover and dragging.
        foreach (VisualElement child in Children())
        {
            child.pickingMode = PickingMode.Ignore;
        }
    }

    /// <summary>Compact cards (placed on a diagnosis) show only the title.</summary>
    public void SetCompact(bool compact)
    {
        EnableInClassList("solution-card--placed", compact);
        description.style.display = compact || string.IsNullOrWhiteSpace(Option.description) ? DisplayStyle.None : DisplayStyle.Flex;
    }
}
