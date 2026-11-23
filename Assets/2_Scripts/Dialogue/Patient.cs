using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Entry point for a patient scenario and the owner of the player's collected cues and shared decisions.
/// <para>
/// <see cref="Begin"/> resets the cue list, sets up the UI for the given <see cref="SO_Patient"/>
/// (portrait, hotspots, folder) and starts the interview on <see cref="PatientInterviewController"/>.
/// </para>
/// <para>
/// Cues come from two places: the controller (question answers and ABCDE findings, via
/// <see cref="PatientInterviewController.OnCueRecognized"/>) and portrait hotspot clicks from
/// <see cref="PatientInterviewUI"/>. Each cue id is only accepted once; new cues are stored,
/// added to the patient file notes and re-broadcast through <see cref="OnCueRecognized"/>.
/// </para>
/// <para>
/// Prioritise hypotheses opens <see cref="HypothesisScreenUI"/>. After Diagnose, the diagnosis
/// cards are shown in the patient file. Shared decision making moves that hand onto the interview and
/// starts the team talk; after it, each diagnosis card dropped on the patient plays its options and
/// decision talk through <see cref="SharedDecisionSession"/>. Once every card with a talk is decided,
/// the closing lines play.
/// </para>
/// </summary>
public class Patient : MonoBehaviour
{
    /// <summary>Raised once for every cue the player discovers for the first time.</summary>
    public event Action<RecognizedCue> OnCueRecognized;
    /// <summary>The patient's frustration reached the limit during shared decision making.</summary>
    public event Action OnExaminationFailed;

    [SerializeField] PatientInterviewController interviewController;
    [SerializeField] PatientInterviewUI interviewUI;
    [SerializeField] HypothesisScreenUI hypothesisScreen;
    [FormerlySerializedAs("startingInterview")]
    [SerializeField] SO_Patient profile;
    [SerializeField] bool startOnPlay = true;

    readonly List<RecognizedCue> recognizedCues = new List<RecognizedCue>();
    readonly HashSet<string> recognizedIds = new HashSet<string>();
    readonly SharedDecisionSession session = new SharedDecisionSession();
    readonly List<Diagnosis> diagnosedHand = new List<Diagnosis>();
    PatientProgress progress;

    public SO_Patient Profile => profile;
    /// <summary>Cues found so far, in the order they were discovered.</summary>
    public IReadOnlyList<RecognizedCue> RecognizedCues => recognizedCues;

    /// <summary>True when the patient agreed to the recommended treatment for this diagnosis.</summary>
    public bool IsDiagnosisShared(string diagnosisId)
    {
        return session.IsShared(diagnosisId);
    }

    /// <summary>True once this diagnosis has been through its decision talk, however it ended.</summary>
    public bool IsDiagnosisDecided(string diagnosisId)
    {
        return session.IsDecided(diagnosisId);
    }

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
        session.Reset();
        diagnosedHand.Clear();

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

            interviewUI.SetCueNotes(recognizedCues);
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

    /// <summary>Hooks up (or removes) the cue, hotspot, file and diagnosis listeners.</summary>
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
                interviewUI.OnSharedDecisionClicked += BeginSharedDecision;
                interviewUI.OnDiagnosisDropped += HandleDiagnosisDropped;
                interviewUI.OnShareChoiceClicked += HandleShareChoice;
            }
            else
            {
                interviewUI.OnHotspotClicked -= HandleHotspotClicked;
                interviewUI.OnCuesClicked -= OpenHypothesisScreen;
                interviewUI.OnSharedDecisionClicked -= BeginSharedDecision;
                interviewUI.OnDiagnosisDropped -= HandleDiagnosisDropped;
                interviewUI.OnShareChoiceClicked -= HandleShareChoice;
            }
        }

        if (hypothesisScreen != null)
        {
            if (subscribe)
            {
                hypothesisScreen.OnDiagnosed += HandleDiagnosed;
            }
            else
            {
                hypothesisScreen.OnDiagnosed -= HandleDiagnosed;
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

        hypothesisScreen.Open(recognizedCues, profile);
    }

    void HandleDiagnosed(IReadOnlyList<RankedDiagnosis> results)
    {
        // An unanswered team talk stays open, otherwise the cards could never be discussed
        if (!session.HasLeft && !session.IsInTeamTalk)
        {
            session.CancelTalk();
            if (interviewUI != null)
            {
                interviewUI.HideShareChoices();
            }
        }

        diagnosedHand.Clear();
        if (results != null)
        {
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].Diagnosis != null)
                {
                    diagnosedHand.Add(results[i].Diagnosis);
                }
            }
        }

        if (interviewUI != null)
        {
            interviewUI.ShowDiagnosisHand(results, session.IsShared, session.ChosenTreatment, session.IsDecided);
        }
    }

    void BeginSharedDecision()
    {
        if (interviewUI == null || InterviewIsBusy || !interviewUI.BeginSharedDecision())
        {
            return;
        }

        interviewUI.SetFrustration(session.Frustration, true);
        ApplyPrompt(session.BeginTeamTalk(profile != null ? profile.sharedDecision : null, PatientName));
    }

    void HandleDiagnosisDropped(Diagnosis diagnosis)
    {
        if (diagnosis == null || session.IsInProgress || session.HasLeft || session.IsDecided(diagnosis.id) || InterviewIsBusy)
        {
            return;
        }

        ApplyPrompt(session.Begin(diagnosis, PatientName));
    }

    void HandleShareChoice(int index)
    {
        if (InterviewIsBusy)
        {
            return;
        }

        ApplyPrompt(session.Choose(index));
    }

    /// <summary>Plays the prompt's lines, adding the closing lines after the last decision, then shows its choices.</summary>
    void ApplyPrompt(SharePrompt prompt)
    {
        if (prompt == null || interviewUI == null)
        {
            return;
        }

        if (prompt.Finished && AllTalksDecided())
        {
            prompt.Lines.AddRange(session.Closing());
        }

        interviewUI.SetFrustration(prompt.Frustration, true);
        interviewUI.ClearShareChoices();
        if (prompt.Lines.Count == 0)
        {
            FinishPrompt(prompt);
            return;
        }

        interviewUI.SetShareLinePlaying(true);
        if (interviewController != null)
        {
            interviewController.PlayLines(prompt.Lines, () => FinishPrompt(prompt));
            return;
        }

        DialogueLine last = prompt.Lines[prompt.Lines.Count - 1];
        interviewUI.SetDialogueText(last != null ? last.text : string.Empty);
        FinishPrompt(prompt);
    }

    /// <summary>True when every diagnosis on the hand that has a talk has been decided.</summary>
    bool AllTalksDecided()
    {
        bool any = false;
        for (int i = 0; i < diagnosedHand.Count; i++)
        {
            Diagnosis diagnosis = diagnosedHand[i];
            if (diagnosis.sharedDecision == null || !diagnosis.sharedDecision.HasScript)
            {
                continue;
            }

            any = true;
            if (!session.IsDecided(diagnosis.id))
            {
                return false;
            }
        }

        return any;
    }

    string PatientName => profile != null ? profile.patientDisplayName : null;

    void FinishPrompt(SharePrompt prompt)
    {
        if (interviewUI == null || prompt == null)
        {
            return;
        }

        interviewUI.SetShareLinePlaying(false);
        interviewUI.SetContinueHintVisible(false);
        interviewUI.ShowSharePrompt(null, null, prompt.Choices);
        if (prompt.Finished)
        {
            interviewUI.UpdateDiagnosisCard(prompt.DiagnosisId, prompt.Shared, prompt.Chosen);
        }

        if (prompt.Leave)
        {
            OnExaminationFailed?.Invoke();
        }
    }

    bool InterviewIsBusy => interviewController != null && interviewController.IsBusy;

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
    /// Records a cue if its id hasn't been seen yet, adds it to the patient file and raises
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
            interviewUI.AddCueNote(cue);
        }

        return true;
    }
}
