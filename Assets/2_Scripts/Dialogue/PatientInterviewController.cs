using System;
using System.Collections;
using System.Collections.Generic;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

/// <summary>
/// Runs the conversation with a patient: which questions are available, playing out each
/// question/answer exchange, and the ABCDE examination steps.
/// <para>
/// How it works: the UI (<see cref="PatientInterviewUI"/>) only reports clicks; this controller
/// decides what happens. When the player picks a question, the player's line is shown briefly,
/// then the patient's reply is typed out letter by letter while an FMOD voice event plays.
/// Clicking the dialogue panel skips ahead. After the reply, the question's cue (if any) is
/// raised through <see cref="OnCueRecognized"/> and its follow-up questions are unlocked.
/// Only one exchange can run at a time; input is disabled while <c>isBusy</c> is true.
/// </para>
/// <para>
/// Each question and each ABCDE step can only be used once per interview.
/// </para>
/// </summary>
public class PatientInterviewController : MonoBehaviour
{
    public event Action OnInterviewStarted;
    /// <summary>Raised when the interview closes (Done button or <see cref="EndInterview"/>).</summary>
    public event Action OnInterviewEnded;
    /// <summary>Raised when a question answer or ABCDE finding awards a cue. <see cref="Patient"/> listens to this.</summary>
    public event Action<RecognizedCue> OnCueRecognized;
    /// <summary>Raised when Retry wipes the attempt; <see cref="Patient"/> restarts the case from scratch.</summary>
    public event Action OnCaseRestarted;

    [SerializeField] PatientInterviewUI interviewUI;
    [Tooltip("Hand-over steps and results. Found in the scene automatically if left empty.")]
    [SerializeField] AssessmentScreenUI assessmentScreen;
    [SerializeField] string playerDisplayName = "You";
    [Tooltip("Seconds between each typed character of the patient's reply.")]
    [SerializeField] float textSpeed = 0.02f;
    [Tooltip("Seconds the player's question stays on screen before the patient starts answering.")]
    [SerializeField] float playerLineHold = 0.35f;

    SO_Patient interview;
    readonly HashSet<string> askedIds = new HashSet<string>();
    // Unlocked question ids in the order they are shown; follow-ups are inserted under the question that unlocked them.
    readonly List<string> unlockedIds = new List<string>();
    // Indexed by (int)AbcdeLetter.
    readonly bool[] examined = new bool[5];
    bool isPlaying;
    // True while an exchange or examination coroutine is running.
    bool isBusy;
    bool skipRequested;
    bool warnedMissingVoice;
    EventInstance patientVoice;
    PatientProgress progress;
    string pendingQuestionId;
    int pendingExamination = -1;
    // True from pressing Hand over until the results are closed.
    bool assessing;
    AssessmentStep currentStep;

    public bool IsPlaying => isPlaying;

    void Awake()
    {
        if (interviewUI == null)
        {
            interviewUI = GetComponent<PatientInterviewUI>();
        }

        if (assessmentScreen == null)
        {
            assessmentScreen = FindAnyObjectByType<AssessmentScreenUI>(FindObjectsInactive.Include);
        }
    }

    void OnEnable()
    {
        SubscribeUi(true);
    }

    void OnDisable()
    {
        SubscribeUi(false);
        StopPatientVoice();
    }

    /// <summary>
    /// Opens the interview UI for the given patient and resets all progress (asked questions,
    /// unlocked questions, examined ABCDE steps). Only questions marked <c>startsUnlocked</c>
    /// are available at first. Ends any interview that is already running.
    /// </summary>
    public void StartInterview(SO_Patient interviewAsset)
    {
        if (interviewAsset == null)
        {
            Debug.LogError("PatientInterviewController: interview asset is missing.", this);
            return;
        }

        if (interviewUI == null)
        {
            Debug.LogError("PatientInterviewController: PatientInterviewUI is not assigned.", this);
            return;
        }

        if (isPlaying)
        {
            EndInterview();
        }

        interview = interviewAsset;
        askedIds.Clear();
        unlockedIds.Clear();
        Array.Clear(examined, 0, examined.Length);
        warnedMissingVoice = false;
        isPlaying = true;
        isBusy = false;
        pendingQuestionId = null;
        pendingExamination = -1;

        // Resume where the player left off with this patient
        progress = PatientProgressStore.Get(interview);
        askedIds.UnionWith(progress.askedQuestionIds);
        // Keep the saved order so follow-ups stay under the question that unlocked them
        for (int i = 0; i < progress.unlockedQuestionIds.Count; i++)
        {
            if (!unlockedIds.Contains(progress.unlockedQuestionIds[i]))
            {
                unlockedIds.Add(progress.unlockedQuestionIds[i]);
            }
        }
        for (int i = 0; i < progress.examinedLetters.Count; i++)
        {
            examined[(int)progress.examinedLetters[i]] = true;
        }

        if (interview.questions != null)
        {
            for (int i = 0; i < interview.questions.Count; i++)
            {
                InterviewQuestion question = interview.questions[i];
                if (question != null && question.startsUnlocked)
                {
                    TryUnlock(question.id, unlockedIds.Count);
                }
            }
        }

        // A previous interview may have ended mid-line, leaving the questions disabled
        interviewUI.SetQuestionsEnabled(true);
        interviewUI.SetVisible(true);
        interviewUI.SetSpeakerName(interview.patientDisplayName);
        interviewUI.SetDialogueText(progress.HasProgress
            ? $"Welcome back. Continue your conversation with {interview.patientDisplayName}."
            : !string.IsNullOrWhiteSpace(interview.introText)
                ? interview.introText
                : $"Select a question to ask {interview.patientDisplayName}.");
        assessing = false;
        currentStep = null;
        RefreshScore();
        RefreshQuestions();
        for (int i = 0; i < examined.Length; i++)
        {
            if (examined[i])
            {
                interviewUI.SetAbcdeExamined((AbcdeLetter)i);
            }
        }

        OnInterviewStarted?.Invoke();
    }

    /// <summary>Stops any running exchange and voice, hides the UI and raises <see cref="OnInterviewEnded"/>.</summary>
    public void EndInterview()
    {
        if (!isPlaying)
        {
            return;
        }

        // An exchange cut short doesn't count, otherwise its cue would be lost for good
        if (pendingQuestionId != null)
        {
            askedIds.Remove(pendingQuestionId);
            pendingQuestionId = null;
        }

        if (pendingExamination >= 0)
        {
            examined[pendingExamination] = false;
            pendingExamination = -1;
        }

        SaveProgress();

        StopAllCoroutines();
        StopPatientVoice();
        isPlaying = false;
        isBusy = false;
        assessing = false;
        currentStep = null;
        if (assessmentScreen != null)
        {
            assessmentScreen.Hide();
        }

        interview = null;
        askedIds.Clear();
        unlockedIds.Clear();

        if (interviewUI != null)
        {
            interviewUI.SetVisible(false);
        }

        OnInterviewEnded?.Invoke();
    }

    void SubscribeUi(bool subscribe)
    {
        if (assessmentScreen != null)
        {
            if (subscribe)
            {
                assessmentScreen.OnStepSubmitted += HandleStepSubmitted;
                assessmentScreen.OnNextClicked += HandleNextStep;
                assessmentScreen.OnResultsClosed += HandleResultsClosed;
                assessmentScreen.OnRetryClicked += RestartCase;
            }
            else
            {
                assessmentScreen.OnStepSubmitted -= HandleStepSubmitted;
                assessmentScreen.OnNextClicked -= HandleNextStep;
                assessmentScreen.OnResultsClosed -= HandleResultsClosed;
                assessmentScreen.OnRetryClicked -= RestartCase;
            }
        }

        if (interviewUI == null)
        {
            return;
        }

        if (subscribe)
        {
            interviewUI.OnQuestionClicked += HandleQuestionClicked;
            interviewUI.OnAbcdeClicked += HandleAbcdeClicked;
            interviewUI.OnDoneClicked += EndInterview;
            interviewUI.OnFinishClicked += HandleFinishClicked;
            interviewUI.OnSkipRequested += HandleSkipRequested;
        }
        else
        {
            interviewUI.OnQuestionClicked -= HandleQuestionClicked;
            interviewUI.OnAbcdeClicked -= HandleAbcdeClicked;
            interviewUI.OnDoneClicked -= EndInterview;
            interviewUI.OnFinishClicked -= HandleFinishClicked;
            interviewUI.OnSkipRequested -= HandleSkipRequested;
        }
    }

    /// <summary>Dialogue panel clicked: fast-forward the current line (only while something is playing).</summary>
    void HandleSkipRequested()
    {
        if (isBusy)
        {
            skipRequested = true;
        }
    }

    /// <summary>Starts the exchange for a question if it is unlocked, not yet asked, and nothing else is playing.</summary>
    void HandleQuestionClicked(string questionId)
    {
        if (!isPlaying || isBusy || askedIds.Contains(questionId))
        {
            return;
        }

        InterviewQuestion question = interview != null ? interview.FindQuestion(questionId) : null;
        if (question == null || !unlockedIds.Contains(questionId))
        {
            return;
        }

        askedIds.Add(questionId);
        pendingQuestionId = questionId;
        StartCoroutine(PlayExchange(question));
    }

    /// <summary>
    /// Plays one question/answer: shows the player's question for <c>playerLineHold</c> seconds,
    /// types out the patient's reply with voice, then awards the cue and unlocks follow-ups.
    /// </summary>
    IEnumerator PlayExchange(InterviewQuestion question)
    {
        isBusy = true;
        skipRequested = false;
        interviewUI.SetQuestionsEnabled(false);
        RefreshQuestions();

        interviewUI.SetSpeakerName(playerDisplayName);
        interviewUI.SetDialogueText(question.questionText);

        float hold = playerLineHold;
        while (hold > 0f && !skipRequested)
        {
            hold -= Time.unscaledDeltaTime;
            yield return null;
        }

        skipRequested = false;
        interviewUI.SetSpeakerName(interview.patientDisplayName);
        yield return TypePatientLine(question.patientResponse);

        AwardCueIfNeeded(question);
        UnlockFollowUps(question);
        AwardQuestionScore(question);
        pendingQuestionId = null;
        SaveProgress();
        isBusy = false;
        interviewUI.SetQuestionsEnabled(true);
        RefreshQuestions();
    }

    /// <summary>Starts an ABCDE examination step if it hasn't been done yet and nothing else is playing.</summary>
    void HandleAbcdeClicked(AbcdeLetter letter)
    {
        if (!isPlaying || isBusy || examined[(int)letter])
        {
            return;
        }

        examined[(int)letter] = true;
        pendingExamination = (int)letter;
        StartCoroutine(PlayExamination(letter));
    }

    /// <summary>
    /// Types out the finding for one ABCDE step (without patient voice, since it's an observation,
    /// not speech), awards its cue if configured, and marks the button as examined.
    /// </summary>
    IEnumerator PlayExamination(AbcdeLetter letter)
    {
        isBusy = true;
        skipRequested = false;
        interviewUI.SetQuestionsEnabled(false);

        AbcdeFinding finding = interview.FindAbcdeFinding(letter);
        string text = finding != null && !string.IsNullOrWhiteSpace(finding.findingText)
            ? finding.findingText
            : "No abnormal findings.";

        string letterName = letter.ToString();
        interviewUI.SetSpeakerName($"{letterName[0]} - {letterName}");
        yield return TypePatientLine(text, false);

        if (finding != null && finding.awardsCue)
        {
            OnCueRecognized?.Invoke(finding.ToCue());
        }

        interviewUI.SetAbcdeExamined(letter);
        AwardExaminationScore(letter, finding);
        pendingExamination = -1;
        SaveProgress();
        isBusy = false;
        interviewUI.SetQuestionsEnabled(true);
    }

    /// <summary>
    /// Typewriter effect: reveals <paramref name="line"/> one character every <c>textSpeed</c> seconds.
    /// Uses unscaled/realtime timing so it still works when the game is paused (timeScale 0).
    /// A skip request shows the full line immediately.
    /// </summary>
    IEnumerator TypePatientLine(string line, bool playVoice = true)
    {
        string text = line ?? string.Empty;
        interviewUI.SetDialogueText(string.Empty);
        if (playVoice)
        {
            StartPatientVoice();
        }

        for (int i = 0; i < text.Length; i++)
        {
            if (skipRequested)
            {
                interviewUI.SetDialogueText(text);
                break;
            }

            interviewUI.SetDialogueText(text.Substring(0, i + 1));
            yield return new WaitForSecondsRealtime(textSpeed);
        }

        interviewUI.SetDialogueText(text);
        StopPatientVoice();
        skipRequested = false;
    }

    /// <summary>
    /// Makes extra questions available from outside the interview flow (e.g. when <see cref="Patient"/>
    /// recognizes a visual hotspot cue). They are added to the bottom of the list, which is only
    /// refreshed if something new was unlocked.
    /// </summary>
    public void UnlockQuestions(IList<string> questionIds)
    {
        if (!isPlaying || questionIds == null)
        {
            return;
        }

        bool changed = false;
        for (int i = 0; i < questionIds.Count; i++)
        {
            if (TryUnlock(questionIds[i], unlockedIds.Count))
            {
                changed = true;
            }
        }

        if (changed)
        {
            SaveProgress();
            RefreshQuestions();
        }
    }

    /// <summary>Points for a completed question, plus a small penalty for background questions that delay A-B-C in an urgent case.</summary>
    void AwardQuestionScore(InterviewQuestion question)
    {
        CaseScorer.Award(progress, "q:" + question.id, ScoreCategory.InformationGathering, question.points, question.questionText);

        CaseScoringRules rules = interview.scoringRules;
        bool airwayBreathingCirculationDone = examined[(int)AbcdeLetter.Airway] && examined[(int)AbcdeLetter.Breathing] && examined[(int)AbcdeLetter.Circulation];
        if (rules != null && rules.urgent && question.historyQuestion && !airwayBreathingCirculationDone)
        {
            CaseScorer.Award(progress, "early:" + question.id, ScoreCategory.Abcde, -rules.earlyHistoryPenalty,
                $"Asked a background question before assessing A, B and C: {question.questionText}");
        }
    }

    /// <summary>Points for examining one ABCDE letter; the order they were done in is scored when the case is handed over.</summary>
    void AwardExaminationScore(AbcdeLetter letter, AbcdeFinding finding)
    {
        if (finding != null)
        {
            CaseScorer.Award(progress, "abcde:" + letter, ScoreCategory.Abcde, finding.points, $"{letter} examined");
        }

        if (!progress.examOrder.Contains(letter))
        {
            progress.examOrder.Add(letter);
        }
    }

    /// <summary>Updates the score shown on the interview screen. Also called by <see cref="Patient"/> after it scores hotspots and diagnoses.</summary>
    public void RefreshScore()
    {
        if (interviewUI != null && interview != null && progress != null)
        {
            interviewUI.SetScore(CaseScorer.RunningTotal(interview, progress));
        }
    }

    /// <summary>Hand over: walks through the steps not yet answered, then shows the results.</summary>
    void HandleFinishClicked()
    {
        if (!isPlaying || isBusy || assessing || interview == null)
        {
            return;
        }

        if (assessmentScreen == null)
        {
            Debug.LogWarning("PatientInterviewController: no AssessmentScreenUI in the scene, so the case cannot be handed over.", this);
            return;
        }

        assessing = true;
        ShowNextStep();
    }

    void ShowNextStep()
    {
        List<AssessmentStep> steps = interview.assessmentSteps;
        currentStep = null;
        int number = 0;
        for (int i = 0; steps != null && i < steps.Count; i++)
        {
            if (!HasAnswer(steps[i].id))
            {
                currentStep = steps[i];
                number = i + 1;
                break;
            }
        }

        if (currentStep == null)
        {
            ShowResults();
            return;
        }

        assessmentScreen.ShowStep(currentStep, number, steps.Count);
    }

    bool HasAnswer(string stepId)
    {
        for (int i = 0; i < progress.stepAnswers.Count; i++)
        {
            if (progress.stepAnswers[i].stepId == stepId)
            {
                return true;
            }
        }

        return false;
    }

    void HandleStepSubmitted(IReadOnlyList<int> selected)
    {
        if (!assessing || currentStep == null || HasAnswer(currentStep.id))
        {
            return;
        }

        int points = currentStep.Score(selected);
        progress.stepAnswers.Add(new StepAnswer { stepId = currentStep.id, selected = new List<int>(selected), points = points });
        SaveProgress();
        RefreshScore();

        bool isLast = true;
        for (int i = 0; i < interview.assessmentSteps.Count; i++)
        {
            isLast &= HasAnswer(interview.assessmentSteps[i].id);
        }

        assessmentScreen.ShowStepFeedback(currentStep, selected, points, isLast);
    }

    void HandleNextStep()
    {
        if (assessing)
        {
            ShowNextStep();
        }
    }

    void ShowResults()
    {
        CaseResult result = CaseScorer.Evaluate(interview, progress);
        progress.completed = true;
        if (result.Percent >= progress.bestPercent)
        {
            progress.bestPercent = result.Percent;
            progress.bestStars = result.Stars;
        }

        PatientProgressStore.Save();
        assessmentScreen.ShowResults(interview, result, progress.bestStars);
    }

    void HandleResultsClosed()
    {
        EndInterview();
    }

    /// <summary>Retry: wipes this attempt (the best score is kept) and has <see cref="Patient"/> start the case again.</summary>
    public void RestartCase()
    {
        if (!isPlaying || interview == null)
        {
            return;
        }

        SO_Patient patient = interview;
        StopAllCoroutines();
        StopPatientVoice();
        // Stop quietly: raising OnInterviewEnded here would send the player back to the map
        isPlaying = false;
        isBusy = false;
        assessing = false;
        currentStep = null;
        pendingQuestionId = null;
        pendingExamination = -1;
        interview = null;
        askedIds.Clear();
        unlockedIds.Clear();
        if (assessmentScreen != null)
        {
            assessmentScreen.Hide();
        }

        PatientProgressStore.ResetCase(patient);
        OnCaseRestarted?.Invoke();
    }

    void SaveProgress()
    {
        if (progress == null)
        {
            return;
        }

        progress.askedQuestionIds.Clear();
        progress.askedQuestionIds.AddRange(askedIds);
        progress.unlockedQuestionIds.Clear();
        progress.unlockedQuestionIds.AddRange(unlockedIds);
        progress.examinedLetters.Clear();
        for (int i = 0; i < examined.Length; i++)
        {
            if (examined[i])
            {
                progress.examinedLetters.Add((AbcdeLetter)i);
            }
        }

        PatientProgressStore.Save();
    }

    void AwardCueIfNeeded(InterviewQuestion question)
    {
        if (question == null || !question.awardsCue)
        {
            return;
        }

        OnCueRecognized?.Invoke(question.ToCue());
    }

    /// <summary>Unlocks the question's follow-ups directly below it, in the order they are listed.</summary>
    void UnlockFollowUps(InterviewQuestion question)
    {
        if (question.unlocksQuestionIds == null)
        {
            return;
        }

        int insertAt = unlockedIds.IndexOf(question.id) + 1;
        for (int i = 0; i < question.unlocksQuestionIds.Count; i++)
        {
            if (TryUnlock(question.unlocksQuestionIds[i], insertAt))
            {
                insertAt++;
            }
        }
    }

    /// <summary>
    /// Adds a question to the visible list at <paramref name="index"/>. Returns false if the id is empty,
    /// already unlocked, or doesn't match a question in the asset.
    /// </summary>
    bool TryUnlock(string id, int index)
    {
        if (string.IsNullOrEmpty(id) || unlockedIds.Contains(id) || interview.FindQuestion(id) == null)
        {
            return false;
        }

        unlockedIds.Insert(index, id);
        return true;
    }

    /// <summary>
    /// Rebuilds the question buttons from the unlocked questions, in their unlocked display order.
    /// Already asked questions stay visible but are shown as asked (disabled).
    /// </summary>
    void RefreshQuestions()
    {
        if (interviewUI == null || interview == null)
        {
            return;
        }

        List<InterviewQuestionView> views = new List<InterviewQuestionView>();
        for (int i = 0; i < unlockedIds.Count; i++)
        {
            InterviewQuestion question = interview.FindQuestion(unlockedIds[i]);
            views.Add(new InterviewQuestionView(question.id, question.questionText, askedIds.Contains(question.id)));
        }

        interviewUI.RebuildQuestions(views);
    }

    /// <summary>
    /// Creates and starts a new FMOD instance of the patient's dialogue event. A missing or invalid
    /// event is reported only once per interview so the console isn't flooded.
    /// </summary>
    void StartPatientVoice()
    {
        StopPatientVoice();
        if (interview == null || interview.patientDialogueEvent.IsNull)
        {
            if (!warnedMissingVoice)
            {
                warnedMissingVoice = true;
                string assetName = interview != null ? interview.name : "null";
                Debug.LogWarning($"PatientInterviewController: no patientDialogueEvent assigned on '{assetName}'.", this);
            }

            return;
        }

        patientVoice = RuntimeManager.CreateInstance(interview.patientDialogueEvent);
        if (!patientVoice.isValid())
        {
            if (!warnedMissingVoice)
            {
                warnedMissingVoice = true;
                Debug.LogError("PatientInterviewController: FMOD patientDialogueEvent is invalid. Check the assigned event.", this);
            }

            return;
        }

        patientVoice.start();
    }

    /// <summary>Fades out and releases the current voice instance, if any.</summary>
    void StopPatientVoice()
    {
        if (!patientVoice.isValid())
        {
            return;
        }

        patientVoice.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        patientVoice.release();
        patientVoice.clearHandle();
    }
}
