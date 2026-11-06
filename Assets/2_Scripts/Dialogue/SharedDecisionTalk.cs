using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One treatment the player can discuss with the patient while sharing a diagnosis.
/// <see cref="patientWants"/> is true when choosing it does not frustrate the patient.
/// </summary>
[Serializable]
public class TreatmentOption
{
    public string title;
    [TextArea(1, 3)]
    public string pros;
    [TextArea(1, 3)]
    public string cons;
    [Tooltip("On when this is a treatment the patient is willing to accept.")]
    public bool patientWants;
}

/// <summary>
/// What the player can share for one diagnosis: the patient's goals and fears, and the treatments
/// to discuss. Nurse lines are fixed in the share screen. A diagnosis counts as shared only when
/// every treatment's pros and cons were discussed and the decision was made with the patient.
/// </summary>
[Serializable]
public class SharedDecisionTalk
{
    [TextArea(2, 5)]
    [Tooltip("Shown when the player explains the diagnosis and asks about the patient's goals.")]
    public string goalsAndFears;
    public List<TreatmentOption> treatments = new List<TreatmentOption>();

    /// <summary>True when there is at least one treatment with a title.</summary>
    public bool HasScript
    {
        get
        {
            if (treatments == null)
            {
                return false;
            }

            foreach (TreatmentOption treatment in treatments)
            {
                if (treatment != null && !string.IsNullOrWhiteSpace(treatment.title))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
