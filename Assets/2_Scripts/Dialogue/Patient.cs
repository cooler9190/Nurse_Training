using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Entry point for a patient scenario and the owner of the player's collected cues.
/// <para>
/// <see cref="Begin"/> resets the cue list, sets up the UI for the given <see cref="SO_Patient"/>
/// (portrait, hotspots, folder) and starts the interview on <see cref="PatientInterviewController"/>.
/// </para>
/// <para>
/// Cues come from two places: the controller (question answers and ABCDE findings, via
/// <see cref="PatientInterviewController.OnCueRecognized"/>) and portrait hotspot clicks from
/// <see cref="PatientInterviewUI"/>. Each cue id is only accepted once; new cues are stored,
/// counted on the Cues button and re-broadcast through <see cref="OnCueRecognized"/> so other
/// systems (scoring, tutorials, ...) can react without knowing about the interview internals.
/// </para>
/// <para>
/// Pressing the Cues button opens the <see cref="HypothesisScreenUI"/> with all collected cues and
/// the patient's diagnoses, where the player ranks the cues.
/// </para>
/// </summary>
public class Patient : MonoBehaviour
{
    /// <summary>Raised once for every cue the player discovers for the first time.</summary>
    public event Action<RecognizedCue> OnCueRecognized;

    [SerializeField] PatientInterviewController interviewController;
    [SerializeField] PatientInterviewUI interviewUI;
    [SerializeField] HypothesisScreenUI hypothesisScreen;
    [FormerlySerializedAs("startingInterview")]
    [SerializeField] SO_Patient profile;
    [SerializeField] bool startOnPlay = true;

    readonly List<RecognizedCue> recognizedCues = new List<RecognizedCue>();
    readonly HashSet<string> recognizedIds = new HashSet<string>();
    PatientProgress progress;

    public SO_Patient Profile => profile;
    /// <summary>Cues found so far, in the order they were discovered.</summary>
    public IReadOnlyList<RecognizedCue> RecognizedCues => recognizedCues;

    /// <summary>Falls back to components on the same GameObject if references weren't assigned.</summary>
    void Awake()
    {
        if (interviewController == null)
        {
            interviewController = GetComponent<PatientInterviewController>();
        }

        if (interviewUI == null)
        {
            interviewUI = GetComponent<PatientInterviewUI>();
        }
    }

    void OnEnable()
    {
        Subscribe(true);
    }

    void OnDisable()
    {
        Subscribe(false);
    }

    void Start()
    {
        if (startOnPlay && profile != null)
        {
            Begin(profile);
        }
    }

    /// <summary>
    /// Starts (or restarts) the scenario with the given patient: clears previously found cues,
    /// prepares the UI for this patient and opens the interview.
    /// </summary>
    public void Begin(SO_Patient patientProfile)
    {
        if (patientProfile == null)
        {
            Debug.LogError("Patient: profile is missing.", this);
            return;
        }

        profile = patientProfile;
        recognizedCues.Clear();
        recognizedIds.Clear();

        // Bring back what the player already found with this patient
        progress = PatientProgressStore.Get(profile);
        for (int i = 0; i < progress.cues.Count; i++)
        {
            RecognizedCue saved = progress.cues[i].ToCue();
            if (!string.IsNullOrEmpty(saved.Id) && recognizedIds.Add(saved.Id))
            {
                recognizedCues.Add(saved);
            }
        }

        if (interviewUI != null)
        {
            interviewUI.BeginPatient(profile);
            for (int i = 0; i < progress.foundHotspotIds.Count; i++)
            {
                interviewUI.MarkHotspotFound(progress.foundHotspotIds[i]);
            }

            interviewUI.SetCueCount(recognizedCues.Count);
        }

        if (hypothesisScreen != null)
        {
            hypothesisScreen.ResetRanking();
        }

        if (interviewController != null)
        {
            interviewController.StartInterview(profile);
        }
    }

    /// <summary>Alias for <see cref="Begin"/>.</summary>
    public void PlayInterview(SO_Patient interview)
    {
        Begin(interview);
    }

    /// <summary>Hooks up (or removes) the cue, hotspot and Cues button listeners on the controller and UI.</summary>
    void Subscribe(bool subscribe)
    {
        if (interviewController != null)
        {
            if (subscribe)
            {
                interviewController.OnCueRecognized += HandleQuestionCue;
            }
            else
            {
                interviewController.OnCueRecognized -= HandleQuestionCue;
            }
        }

        if (interviewUI != null)
        {
            if (subscribe)
            {
                interviewUI.OnHotspotClicked += HandleHotspotClicked;
                interviewUI.OnCuesClicked += OpenHypothesisScreen;
            }
            else
            {
                interviewUI.OnHotspotClicked -= HandleHotspotClicked;
                interviewUI.OnCuesClicked -= OpenHypothesisScreen;
            }
        }
    }

    void HandleQuestionCue(RecognizedCue cue)
    {
        TryRecognize(cue);
    }

    void OpenHypothesisScreen()
    {
        if (hypothesisScreen == null)
        {
            Debug.LogError("Patient: HypothesisScreenUI is not assigned.", this);
            return;
        }

        hypothesisScreen.Open(recognizedCues, profile != null ? profile.diagnoses : null,
            profile != null ? profile.solutionDiagnosisIds : null);
    }

    /// <summary>
    /// Player clicked a region on the portrait: mark it as found in the UI, award its cue and,
    /// if that cue is new, unlock the follow-up questions linked to the hotspot.
    /// </summary>
    void HandleHotspotClicked(string hotspotId)
    {
        VisualHotspot hotspot = profile != null ? profile.FindHotspot(hotspotId) : null;
        if (hotspot == null)
        {
            return;
        }

        if (interviewUI != null)
        {
            interviewUI.MarkHotspotFound(hotspotId);
        }

        if (progress != null && !progress.foundHotspotIds.Contains(hotspotId))
        {
            progress.foundHotspotIds.Add(hotspotId);
            PatientProgressStore.Save();
        }

        TryRecognize(hotspot.ToCue());

        // Unlock even if the cue was already earned from a question with the same cue id
        if (interviewController != null)
        {
            interviewController.UnlockQuestions(hotspot.unlocksQuestionIds);
        }
    }

    /// <summary>
    /// Records a cue if its id hasn't been seen yet, updates the cue count and raises
    /// <see cref="OnCueRecognized"/>. Returns false for duplicates or cues without an id.
    /// </summary>
    bool TryRecognize(RecognizedCue cue)
    {
        if (string.IsNullOrEmpty(cue.Id) || !recognizedIds.Add(cue.Id))
        {
            return false;
        }

        recognizedCues.Add(cue);
        if (progress != null)
        {
            progress.cues.Add(new SavedCue(cue));
            PatientProgressStore.Save();
        }

        Debug.Log($"Cue recognized: {cue.Id}");
        OnCueRecognized?.Invoke(cue);

        if (interviewUI != null)
        {
            interviewUI.SetCueCount(recognizedCues.Count);
        }

        return true;
    }
}
