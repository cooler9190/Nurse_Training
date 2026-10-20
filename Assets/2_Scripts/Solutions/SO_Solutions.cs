using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The solutions a player can choose from for one patient: nursing actions linked to the patient's
/// diagnoses (matched by <see cref="Diagnosis.id"/>). Kept as its own asset so the patient and
/// diagnosis data stay untouched. Create one via <c>Assets &gt; Create &gt; Scriptable Objects &gt; Solutions</c>.
/// <para>
/// Every option is shown to the player as a card, whether it is a right action or a distractor. Right
/// options are the ones that belong to a diagnosis and are marked <see cref="SolutionOption.correct"/>.
/// </para>
/// <para>
/// It also holds the patient's correct priorities (cue order and diagnosis order, with the reasons), which the
/// hand over compares with what the player ranked. Together this is the answer key for one patient.
/// </para>
/// </summary>
[CreateAssetMenu(fileName = "SO_Solutions", menuName = "Scriptable Objects/Solutions")]
public class SO_Solutions : ScriptableObject
{
    /// <summary>The patient these solutions belong to. <see cref="SolutionFlow"/> uses the set whose patient is the one being played.</summary>
    public SO_Patient patient;
    /// <summary>Options per diagnosis.</summary>
    public List<DiagnosisSolutions> diagnoses = new List<DiagnosisSolutions>();

    [Header("Priorities (checked at hand over)")]
    [Tooltip("Cue ids, most urgent first. Only the relative order matters, so cues the player never found are skipped.")]
    public List<string> correctCueOrder = new List<string>();
    [Tooltip("Diagnosis ids, most urgent first. Diagnoses left out here are distractors.")]
    public List<string> correctDiagnosisOrder = new List<string>();
    [Tooltip("Why each cue sits where it does. Shown at hand over.")]
    public List<PriorityNote> cueNotes = new List<PriorityNote>();
    [Tooltip("Why each diagnosis sits where it does. Shown at hand over.")]
    public List<PriorityNote> diagnosisNotes = new List<PriorityNote>();
    [TextArea(2, 6)]
    [Tooltip("A short explanation of the whole ordering.")]
    public string prioritiesSummary;

    /// <summary>Returns the options for the given diagnosis id, or null if there are none.</summary>
    public DiagnosisSolutions Find(string diagnosisId)
    {
        if (string.IsNullOrEmpty(diagnosisId) || diagnoses == null)
        {
            return null;
        }

        for (int i = 0; i < diagnoses.Count; i++)
        {
            if (diagnoses[i] != null && diagnoses[i].diagnosisId == diagnosisId)
            {
                return diagnoses[i];
            }
        }

        return null;
    }

    /// <summary>Returns the note for a cue id, or null if there is none.</summary>
    public PriorityNote CueNote(string cueId)
    {
        return FindNote(cueNotes, cueId);
    }

    /// <summary>Returns the note for a diagnosis id, or null if there is none.</summary>
    public PriorityNote DiagnosisNote(string diagnosisId)
    {
        return FindNote(diagnosisNotes, diagnosisId);
    }

    static PriorityNote FindNote(List<PriorityNote> notes, string id)
    {
        for (int i = 0; notes != null && i < notes.Count; i++)
        {
            if (notes[i] != null && notes[i].id == id)
            {
                return notes[i];
            }
        }

        return null;
    }

    /// <summary>Returns the id of the diagnosis that owns the option, or null if the option is unknown.</summary>
    public string OwnerOf(string optionId)
    {
        for (int i = 0; diagnoses != null && i < diagnoses.Count; i++)
        {
            DiagnosisSolutions entry = diagnoses[i];
            for (int j = 0; entry != null && entry.options != null && j < entry.options.Count; j++)
            {
                if (entry.options[j].id == optionId)
                {
                    return entry.diagnosisId;
                }
            }
        }

        return null;
    }

#if UNITY_EDITOR
    /// <summary>Option ids must be unique across the whole set, because the screen tracks cards by id.</summary>
    void OnValidate()
    {
        HashSet<string> seen = new HashSet<string>();
        for (int i = 0; diagnoses != null && i < diagnoses.Count; i++)
        {
            DiagnosisSolutions entry = diagnoses[i];
            for (int j = 0; entry != null && entry.options != null && j < entry.options.Count; j++)
            {
                string id = entry.options[j].id;
                if (string.IsNullOrWhiteSpace(id))
                {
                    Debug.LogWarning($"{name}: option {j} of '{entry.diagnosisId}' is missing an id.", this);
                }
                else if (!seen.Add(id))
                {
                    Debug.LogWarning($"{name}: duplicate option id '{id}'.", this);
                }
            }
        }
    }
#endif
}

/// <summary>The reason a cue or diagnosis belongs at its place in the correct order.</summary>
[Serializable]
public class PriorityNote
{
    /// <summary>The cue id or diagnosis id this note is for.</summary>
    public string id;
    /// <summary>Shown when the player never found this cue, so the title cannot come from their collected cues.</summary>
    public string title;
    [TextArea(1, 4)]
    public string reason;
}

/// <summary>The options offered for one diagnosis.</summary>
[Serializable]
public class DiagnosisSolutions
{
    /// <summary>Matches <see cref="Diagnosis.id"/> on the patient.</summary>
    public string diagnosisId;
    public List<SolutionOption> options = new List<SolutionOption>();
}

/// <summary>One solution card: a nursing action that is either right for its diagnosis or a distractor.</summary>
[Serializable]
public class SolutionOption
{
    /// <summary>Unique key across the whole <see cref="SO_Solutions"/> asset.</summary>
    public string id;
    public string title;
    [TextArea(1, 3)]
    public string description;
    /// <summary>True for a right action for its diagnosis; false for a distractor.</summary>
    public bool correct;
    [TextArea(1, 3)]
    [Tooltip("Why it is right or wrong. Shown when the solutions are checked.")]
    public string feedback;
}
