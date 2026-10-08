using System;
using System.Collections.Generic;
using FMODUnity;
using UnityEngine;

[CreateAssetMenu(fileName = "SO_Patient", menuName = "Scriptable Objects/Patient")]
public class SO_Patient : ScriptableObject
{
    public string patientDisplayName = "Patient";
    public Sprite patientPortrait;
    public EventReference patientDialogueEvent;
    public PatientChart chart = new PatientChart();
    public List<InterviewQuestion> questions = new List<InterviewQuestion>();
    public List<VisualHotspot> visualHotspots = new List<VisualHotspot>();

    public InterviewQuestion FindQuestion(string id)
    {
        if (string.IsNullOrEmpty(id) || questions == null)
        {
            return null;
        }

        for (int i = 0; i < questions.Count; i++)
        {
            InterviewQuestion question = questions[i];
            if (question != null && question.id == id)
            {
                return question;
            }
        }

        return null;
    }

    public VisualHotspot FindHotspot(string id)
    {
        if (string.IsNullOrEmpty(id) || visualHotspots == null)
        {
            return null;
        }

        for (int i = 0; i < visualHotspots.Count; i++)
        {
            VisualHotspot hotspot = visualHotspots[i];
            if (hotspot != null && hotspot.id == id)
            {
                return hotspot;
            }
        }

        return null;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        WarnDuplicateIds(questions, "question", question => question != null ? question.id : null);
        WarnDuplicateIds(visualHotspots, "hotspot", hotspot => hotspot != null ? hotspot.id : null);
    }

    void WarnDuplicateIds<T>(List<T> items, string label, Func<T, string> idSelector)
    {
        if (items == null)
        {
            return;
        }

        HashSet<string> seenIds = new HashSet<string>();
        for (int i = 0; i < items.Count; i++)
        {
            string id = idSelector(items[i]);
            if (string.IsNullOrWhiteSpace(id))
            {
                Debug.LogWarning($"{name}: {label} at index {i} is missing an id.", this);
                continue;
            }

            if (!seenIds.Add(id))
            {
                Debug.LogWarning($"{name}: duplicate {label} id '{id}'.", this);
            }
        }
    }
#endif
}

public enum PatientGender
{
    Female,
    Male,
    Other
}

[Serializable]
public class PatientChart
{
    public int age;
    public PatientGender gender;
    public float weightKg;
    public float heightCm;
    public bool hasPacemaker;
    public List<string> allergies = new List<string>();
    [Tooltip("Other specifics shown in the patient file, e.g. \"Type 2 diabetes\" or \"Hard of hearing\".")]
    [TextArea(1, 3)]
    public List<string> notes = new List<string>();
}

[Serializable]
public class InterviewQuestion
{
    public string id;
    [TextArea(1, 3)]
    public string questionText;
    [TextArea(2, 6)]
    public string patientResponse;
    public bool startsUnlocked = true;
    public List<string> unlocksQuestionIds = new List<string>();
    public bool awardsCue;
    public string cueId;
    public string cueTitle;
    [TextArea(1, 3)]
    public string cueDescription;

    public RecognizedCue ToCue()
    {
        string resolvedId = string.IsNullOrWhiteSpace(cueId) ? id : cueId.Trim();
        string resolvedTitle = string.IsNullOrWhiteSpace(cueTitle) ? resolvedId : cueTitle.Trim();
        return new RecognizedCue(resolvedId, resolvedTitle, cueDescription ?? string.Empty, CueOrigin.Question);
    }
}

[Serializable]
public class VisualHotspot
{
    public string id;
    [Tooltip("Normalized rectangle on the portrait. Origin is the top-left. Each value is from 0 to 1.")]
    public Rect normalizedRect = new Rect(0.35f, 0.08f, 0.3f, 0.16f);
    public string cueId;
    public string cueTitle;
    [TextArea(1, 3)]
    public string cueDescription;
    public List<string> unlocksQuestionIds = new List<string>();

    public RecognizedCue ToCue()
    {
        string resolvedId = string.IsNullOrWhiteSpace(cueId) ? id : cueId.Trim();
        string resolvedTitle = string.IsNullOrWhiteSpace(cueTitle) ? resolvedId : cueTitle.Trim();
        return new RecognizedCue(resolvedId, resolvedTitle, cueDescription ?? string.Empty, CueOrigin.Visual);
    }
}
