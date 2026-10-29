using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One line the nurse can say, and what the patient says back.</summary>
[Serializable]
public class TalkChoice
{
    [TextArea(1, 3)]
    public string playerLine;
    [TextArea(1, 4)]
    public string patientResponse;
}

/// <summary>One stage of a shared decision: a short opening, then the nurse's choices.</summary>
[Serializable]
public class TalkStage
{
    [TextArea(1, 4)]
    public string introduction;
    public List<TalkChoice> choices = new List<TalkChoice>();
}

/// <summary>A treatment option shown during option talk, with its pros and cons.</summary>
[Serializable]
public class TreatmentOption
{
    public string title;
    [TextArea(1, 3)]
    public string pros;
    [TextArea(1, 3)]
    public string cons;
}

/// <summary>
/// The Three-talk conversation for one diagnosis: team talk, option talk, then decision talk.
/// Choice quality is not scored. Finishing the conversation is what marks the diagnosis as shared.
/// </summary>
[Serializable]
public class SharedDecisionTalk
{
    public List<TreatmentOption> options = new List<TreatmentOption>();
    public TalkStage teamTalk = new TalkStage();
    public TalkStage optionTalk = new TalkStage();
    public TalkStage decisionTalk = new TalkStage();

    /// <summary>True when every stage has at least one choice the player can pick.</summary>
    public bool HasScript => HasChoices(teamTalk) && HasChoices(optionTalk) && HasChoices(decisionTalk);

    /// <summary>Stage 0 is team talk, 1 is option talk, 2 is decision talk.</summary>
    public TalkStage StageAt(int index)
    {
        switch (index)
        {
            case 0: return teamTalk;
            case 1: return optionTalk;
            default: return decisionTalk;
        }
    }

    static bool HasChoices(TalkStage stage)
    {
        return stage != null && stage.choices != null && stage.choices.Count > 0;
    }
}
