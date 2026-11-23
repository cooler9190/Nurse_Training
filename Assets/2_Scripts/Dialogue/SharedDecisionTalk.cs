using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>How the nurse talks to the patient in one choice of shared decision making.</summary>
public enum CommunicationStyle
{
    Collaborative,
    Transactional,
    Authoritarian
}

/// <summary>
/// One choice the player can make in the team talk, the options talk or when forcing a treatment:
/// the nurse's line, the lines that follow, and how much the choice moves the patient's frustration.
/// </summary>
[Serializable]
public class ApproachTalk
{
    public CommunicationStyle style;
    [Tooltip("Added to the patient's frustration when this is chosen. Negative values calm them down.")]
    public int frustrationChange;
    [TextArea(2, 6)]
    [Tooltip("Shown on the choice button, then spoken by the nurse.")]
    public string nurseLine;
    [Tooltip("What follows the nurse's line, e.g. the patient's reply and any back and forth.")]
    public List<DialogueLine> replies = new List<DialogueLine>();

    public bool HasText => !string.IsNullOrWhiteSpace(nurseLine);
}

/// <summary>
/// One treatment the nurse can propose in the decision talk. The patient accepts the recommended treatment
/// only while calm; any other treatment is always accepted but counts as a poor decision
/// (see <c>SharedDecisionSession</c>).
/// </summary>
[Serializable]
public class TreatmentOption
{
    [Tooltip("Short name written on the diagnosis card once chosen.")]
    public string title;
    [Tooltip("On for the treatment the patient should end up agreeing to.")]
    public bool recommended;
    [TextArea(1, 3)]
    [Tooltip("Summary shown on the diagnosis card.")]
    public string pros;
    [TextArea(1, 3)]
    [Tooltip("Summary shown on the diagnosis card.")]
    public string cons;
    [TextArea(1, 3)]
    [Tooltip("The nurse proposes this treatment. Shown on the choice button.")]
    public string proposal;
    [TextArea(1, 3)]
    [Tooltip("The patient's answer while their frustration is low.")]
    public string calmReply;
    [TextArea(1, 3)]
    [Tooltip("The patient's answer while their frustration is high. For the recommended treatment this is a refusal.")]
    public string frustratedReply;
}

/// <summary>
/// The options and decision talk for one diagnosis, played when its card is dropped on the patient.
/// The decision counts as shared only when the patient agrees to the recommended treatment.
/// </summary>
[Serializable]
public class SharedDecisionTalk
{
    [TextArea(1, 3)]
    [Tooltip("The patient's question that opens the options talk.")]
    public string patientOpener;
    [Tooltip("The ways the nurse can explain the options, one per communication style.")]
    public List<ApproachTalk> optionsTalk = new List<ApproachTalk>();
    public List<TreatmentOption> treatments = new List<TreatmentOption>();
    [Tooltip("The nurse imposes the recommended treatment. Offered as the last decision choice.")]
    public ApproachTalk forcedDecision = new ApproachTalk { style = CommunicationStyle.Authoritarian, frustrationChange = 50 };

    /// <summary>True when there is an options talk and a recommended treatment to decide on.</summary>
    public bool HasScript => FirstWithText(optionsTalk) != null && Recommended != null;

    /// <summary>The recommended treatment, or null when none is marked.</summary>
    public TreatmentOption Recommended
    {
        get
        {
            if (treatments == null)
            {
                return null;
            }

            foreach (TreatmentOption treatment in treatments)
            {
                if (treatment != null && treatment.recommended && !string.IsNullOrWhiteSpace(treatment.title))
                {
                    return treatment;
                }
            }

            return null;
        }
    }

    static ApproachTalk FirstWithText(List<ApproachTalk> approaches)
    {
        if (approaches == null)
        {
            return null;
        }

        foreach (ApproachTalk approach in approaches)
        {
            if (approach != null && approach.HasText)
            {
                return approach;
            }
        }

        return null;
    }
}

/// <summary>
/// Shared decision making lines that belong to the whole case rather than one diagnosis: the team talk
/// played once when shared decision making starts, the goodbye once every diagnosis is decided, and what
/// happens when the patient's frustration reaches the limit.
/// </summary>
[Serializable]
public class PatientSharedDecision
{
    [Tooltip("The nurse explains what is going on. Played once, when Shared decision making is pressed.")]
    public List<ApproachTalk> teamTalk = new List<ApproachTalk>();
    [TextArea(1, 3)]
    [Tooltip("The nurse's last line once every diagnosis has been decided.")]
    public string closingLine;
    [TextArea(1, 3)]
    [Tooltip("The patient's goodbye while their frustration is low.")]
    public string calmGoodbye;
    [TextArea(1, 3)]
    [Tooltip("The patient's goodbye while their frustration is high.")]
    public string frustratedGoodbye;
    [Tooltip("Played when frustration reaches the limit. Empty: the patient says they are leaving.")]
    public List<DialogueLine> walkoutLines = new List<DialogueLine>();

    public bool HasTeamTalk
    {
        get
        {
            if (teamTalk == null)
            {
                return false;
            }

            foreach (ApproachTalk approach in teamTalk)
            {
                if (approach != null && approach.HasText)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
