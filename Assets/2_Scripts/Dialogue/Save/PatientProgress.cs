using System;
using System.Collections.Generic;

// Everything the player has done with one patient, so an interview can be resumed instead of repeated
[Serializable]
public class PatientProgress
{
    public string patientId;
    public List<string> askedQuestionIds = new List<string>();
    public List<string> unlockedQuestionIds = new List<string>();
    public List<AbcdeLetter> examinedLetters = new List<AbcdeLetter>();
    public List<SavedCue> cues = new List<SavedCue>();
    public List<string> foundHotspotIds = new List<string>();

    public bool HasProgress =>
        askedQuestionIds.Count > 0 || examinedLetters.Count > 0 || cues.Count > 0 || foundHotspotIds.Count > 0;
}

[Serializable]
public class SavedCue
{
    public string id;
    public string title;
    public string description;
    public CueOrigin origin;
    public string source;

    public SavedCue()
    {
    }

    public SavedCue(RecognizedCue cue)
    {
        id = cue.Id;
        title = cue.Title;
        description = cue.Description;
        origin = cue.Origin;
        source = cue.Source;
    }

    public RecognizedCue ToCue()
    {
        return new RecognizedCue(id, title, description, origin, source);
    }
}
