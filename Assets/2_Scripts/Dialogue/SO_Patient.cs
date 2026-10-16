using System;
using System.Collections.Generic;
using FMODUnity;
using UnityEngine;

/// <summary>
/// Data asset describing one training patient: who they are, what they look like, what they say,
/// and which clinical cues can be discovered. Create one via
/// <c>Assets &gt; Create &gt; Scriptable Objects &gt; Patient</c> and hand it to <see cref="Patient"/>.
/// <para>
/// Cues can be found in three ways: asking <see cref="questions"/>, clicking
/// <see cref="visualHotspots"/> on the portrait, and running ABCDE examinations
/// (<see cref="abcdeFindings"/>). Hotspot regions can be edited visually with <c>SO_PatientEditor</c>.
/// </para>
/// </summary>
[CreateAssetMenu(fileName = "SO_Patient", menuName = "Scriptable Objects/Patient")]
public class SO_Patient : ScriptableObject
{
    /// <summary>Name shown as the speaker and in the patient folder.</summary>
    public string patientDisplayName = "Patient";
    /// <summary>Portrait image. Hotspot rectangles are placed relative to this sprite.</summary>
    public Sprite patientPortrait;
    /// <summary>FMOD event played while the patient's reply is being typed out (e.g. mumbling voice).</summary>
    public EventReference patientDialogueEvent;
    /// <summary>Background information shown in the patient folder (age, allergies, notes...).</summary>
    public PatientChart chart = new PatientChart();
    /// <summary>All questions the player can ask, including follow-ups that start locked.</summary>
    public List<InterviewQuestion> questions = new List<InterviewQuestion>();
    /// <summary>Clickable regions on the portrait that reveal visual cues.</summary>
    public List<VisualHotspot> visualHotspots = new List<VisualHotspot>();
    /// <summary>Result text for each ABCDE examination step. Steps with no entry report "No abnormal findings."</summary>
    public List<AbcdeFinding> abcdeFindings = new List<AbcdeFinding>();
    /// <summary>Diagnoses the player can arrive at on the hypothesis screen, linked to the cues that support them.</summary>
    public List<Diagnosis> diagnoses = new List<Diagnosis>();

    /// <summary>Returns the finding for the given ABCDE step, or null if none is defined.</summary>
    public AbcdeFinding FindAbcdeFinding(AbcdeLetter letter)
    {
        if (abcdeFindings == null)
        {
            return null;
        }

        for (int i = 0; i < abcdeFindings.Count; i++)
        {
            AbcdeFinding finding = abcdeFindings[i];
            if (finding != null && finding.letter == letter)
            {
                return finding;
            }
        }

        return null;
    }

    /// <summary>Returns the question with the given id, or null if it doesn't exist.</summary>
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

    /// <summary>Returns the hotspot with the given id, or null if it doesn't exist.</summary>
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
    /// <summary>
    /// Editor-only sanity check run whenever the asset is changed in the Inspector.
    /// Questions, hotspots and diagnoses are looked up by id at runtime, so missing or duplicate ids
    /// would silently break unlocking and cue tracking; this logs a warning instead.
    /// </summary>
    void OnValidate()
    {
        WarnDuplicateIds(questions, "question", question => question != null ? question.id : null);
        WarnDuplicateIds(visualHotspots, "hotspot", hotspot => hotspot != null ? hotspot.id : null);
        WarnDuplicateIds(diagnoses, "diagnosis", diagnosis => diagnosis != null ? diagnosis.id : null);
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

/// <summary>
/// Static patient record shown in the slide-out patient folder. Pacemaker and allergies
/// are highlighted in the UI when present.
/// </summary>
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

/// <summary>
/// One question the player can ask and the patient's reply. Questions can be chained:
/// a question with <see cref="startsUnlocked"/> off only shows up once another question
/// (or a visual hotspot) lists its id in <c>unlocksQuestionIds</c>.
/// </summary>
[Serializable]
public class InterviewQuestion
{
    /// <summary>Unique key used for unlocking and for tracking which questions were asked.</summary>
    public string id;
    [TextArea(1, 3)]
    public string questionText;
    [TextArea(2, 6)]
    public string patientResponse;
    /// <summary>If true the question is available from the start; otherwise it must be unlocked.</summary>
    public bool startsUnlocked = true;
    /// <summary>Ids of follow-up questions that become available after this one is answered.</summary>
    public List<string> unlocksQuestionIds = new List<string>();
    /// <summary>If true, hearing the answer gives the player a cue (see the cue fields below).</summary>
    public bool awardsCue;
    /// <summary>Optional cue id. Defaults to the question id; reuse an id to share a cue with another source.</summary>
    public string cueId;
    /// <summary>Optional cue card title. Defaults to the cue id.</summary>
    public string cueTitle;
    [TextArea(1, 3)]
    public string cueDescription;

    /// <summary>Builds the cue awarded by this question, filling in defaults for empty fields.</summary>
    public RecognizedCue ToCue()
    {
        string resolvedId = string.IsNullOrWhiteSpace(cueId) ? id : cueId.Trim();
        string resolvedTitle = string.IsNullOrWhiteSpace(cueTitle) ? resolvedId : cueTitle.Trim();
        return new RecognizedCue(resolvedId, resolvedTitle, cueDescription ?? string.Empty, CueOrigin.Question,
            $"Asked: \"{questionText}\"");
    }
}

/// <summary>
/// The five steps of the ABCDE assessment. The numeric values (0-4) are used as array
/// indices and match the <c>abcde-0</c> ... <c>abcde-4</c> button names in the UI.
/// </summary>
public enum AbcdeLetter
{
    Airway,
    Breathing,
    Circulation,
    Disability,
    Exposure
}

/// <summary>
/// What the player observes when performing one ABCDE examination step on this patient.
/// </summary>
[Serializable]
public class AbcdeFinding
{
    public AbcdeLetter letter;
    [TextArea(2, 6)]
    public string findingText;
    /// <summary>If true, performing this step gives the player a cue.</summary>
    public bool awardsCue;
    /// <summary>Optional cue id. Defaults to "abcde-&lt;letter&gt;", e.g. "abcde-airway".</summary>
    public string cueId;
    /// <summary>Optional cue card title. Defaults to the letter name.</summary>
    public string cueTitle;
    [TextArea(1, 3)]
    public string cueDescription;

    /// <summary>Builds the cue awarded by this finding, filling in defaults for empty fields.</summary>
    public RecognizedCue ToCue()
    {
        string resolvedId = string.IsNullOrWhiteSpace(cueId) ? $"abcde-{letter.ToString().ToLowerInvariant()}" : cueId.Trim();
        string resolvedTitle = string.IsNullOrWhiteSpace(cueTitle) ? letter.ToString() : cueTitle.Trim();
        return new RecognizedCue(resolvedId, resolvedTitle, cueDescription ?? string.Empty, CueOrigin.Examination,
            $"ABCDE examination: {letter}");
    }
}

/// <summary>
/// A clickable region on the patient portrait (e.g. a rash or a swollen ankle). Clicking it
/// always awards a cue, and the first time it does so it can also unlock follow-up questions.
/// </summary>
[Serializable]
public class VisualHotspot
{
    /// <summary>Unique key; also used to name the hotspot element in the UI.</summary>
    public string id;
    [Tooltip("Normalized rectangle on the portrait. Origin is the top-left. Each value is from 0 to 1.")]
    public Rect normalizedRect = new Rect(0.35f, 0.08f, 0.3f, 0.16f);
    /// <summary>Optional cue id. Defaults to the hotspot id.</summary>
    public string cueId;
    /// <summary>Optional cue card title. Defaults to the cue id.</summary>
    public string cueTitle;
    [TextArea(1, 3)]
    public string cueDescription;
    /// <summary>Ids of questions that become available once this hotspot's cue is recognized.</summary>
    public List<string> unlocksQuestionIds = new List<string>();

    /// <summary>Builds the cue awarded by this hotspot, filling in defaults for empty fields.</summary>
    public RecognizedCue ToCue()
    {
        string resolvedId = string.IsNullOrWhiteSpace(cueId) ? id : cueId.Trim();
        string resolvedTitle = string.IsNullOrWhiteSpace(cueTitle) ? resolvedId : cueTitle.Trim();
        return new RecognizedCue(resolvedId, resolvedTitle, cueDescription ?? string.Empty, CueOrigin.Visual,
            "Observed on the patient");
    }
}

/// <summary>
/// A possible diagnosis for this patient. It shows up on the hypothesis screen when at least one
/// of its <see cref="supportingCueIds"/> has been ranked by the player; its priority follows from
/// how high those cues were ranked (see <c>DiagnosisRanker</c>).
/// </summary>
[Serializable]
public class Diagnosis
{
    public string id;
    public string title;
    [TextArea(2, 5)]
    public string description;
    [TextArea(2, 5)]
    public string treatment;
    /// <summary>Cue ids that point towards this diagnosis.</summary>
    public List<string> supportingCueIds = new List<string>();
}
