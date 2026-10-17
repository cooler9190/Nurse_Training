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

    // Scoring: what has been earned so far, and the answers given on the hand-over steps
    public List<AbcdeLetter> examOrder = new List<AbcdeLetter>();
    public List<ScoreEvent> scoreEvents = new List<ScoreEvent>();
    public List<StepAnswer> stepAnswers = new List<StepAnswer>();

    // Best result so far survives a retry
    public bool completed;
    public int bestPercent;
    public int bestStars;

    public bool HasProgress =>
        askedQuestionIds.Count > 0 || examinedLetters.Count > 0 || cues.Count > 0 || foundHotspotIds.Count > 0 || stepAnswers.Count > 0;

    // Starts the case over but keeps the best result
    public void ResetAttempt()
    {
        askedQuestionIds.Clear();
        unlockedQuestionIds.Clear();
        examinedLetters.Clear();
        cues.Clear();
        foundHotspotIds.Clear();
        examOrder.Clear();
        scoreEvents.Clear();
        stepAnswers.Clear();
        completed = false;
    }
}

[Serializable]
public class ScoreEvent
{
    public string key;
    public ScoreCategory category;
    public int points;
    public string label;
}

[Serializable]
public class StepAnswer
{
    public string stepId;
    public List<int> selected = new List<int>();
    public int points;
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
