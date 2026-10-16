/// <summary>
/// Where a cue was discovered during the interview.
/// </summary>
public enum CueOrigin
{
    /// <summary>The patient revealed it in response to an interview question.</summary>
    Question,
    /// <summary>The player spotted it by clicking a hotspot on the patient portrait.</summary>
    Visual,
    /// <summary>It came out of an ABCDE examination step.</summary>
    Examination
}

/// <summary>
/// A clinical cue (a relevant observation about the patient) that the player has found.
/// Cues are created from <see cref="InterviewQuestion"/>, <see cref="VisualHotspot"/> or
/// <see cref="AbcdeFinding"/> data via their <c>ToCue()</c> methods, collected by <see cref="Patient"/>
/// and shown as cards on the hypothesis screen. <see cref="Id"/> is what makes a cue unique, so the same
/// cue found through two different routes is only counted once.
/// </summary>
public readonly struct RecognizedCue
{
    public RecognizedCue(string id, string title, string description, CueOrigin origin, string source)
    {
        Id = id;
        Title = title;
        Description = description;
        Origin = origin;
        Source = source;
    }

    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public CueOrigin Origin { get; }
    /// <summary>Human-readable detail of how the cue was obtained, e.g. the question that was asked.</summary>
    public string Source { get; }
}
