public enum CueOrigin
{
    Question,
    Visual,
    Examination
}

public readonly struct RecognizedCue
{
    public RecognizedCue(string id, string title, string description, CueOrigin origin)
    {
        Id = id;
        Title = title;
        Description = description;
        Origin = origin;
    }

    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public CueOrigin Origin { get; }
}
