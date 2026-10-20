using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The correct priorities for one patient, which the hand over compares with what the player ranked: the order
/// of the cues and of the diagnoses, with the reason each one sits where it does. Kept as its own asset so the
/// patient and diagnosis data stay untouched. Create one via
/// <c>Assets &gt; Create &gt; Scriptable Objects &gt; Priorities</c>.
/// <para>
/// There is one asset per patient: <see cref="HandoverFlow"/> uses the one whose <see cref="patient"/> is being
/// played, and a patient without one has no Hand over button.
/// </para>
/// </summary>
[CreateAssetMenu(fileName = "SO_Priorities", menuName = "Scriptable Objects/Priorities")]
public class SO_Priorities : ScriptableObject
{
    /// <summary>The patient these priorities belong to.</summary>
    public SO_Patient patient;
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

#if UNITY_EDITOR
    void OnValidate()
    {
        if (patient == null)
        {
            Debug.LogWarning($"{name}: no patient assigned, so these priorities will never be used.", this);
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
