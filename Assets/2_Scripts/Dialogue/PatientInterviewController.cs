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

    [SerializeField] PatientInterviewUI interviewUI;
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

    public bool IsPlaying => isPlaying;

    void Awake()
    {
        if (interviewUI == null)
        {
            interviewUI = GetComponent<PatientInterviewUI>();
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
        interviewUI.SetDialogueText($"Select a question to ask {interview.patientDisplayName}.");
        RefreshQuestions();
        OnInterviewStarted?.Invoke();
    }

    /// <summary>Stops any running exchange and voice, hides the UI and raises <see cref="OnInterviewEnded"/>.</summary>
    public void EndInterview()
    {
        if (!isPlaying)
        {
            return;
        }

        StopAllCoroutines();
        StopPatientVoice();
        isPlaying = false;
        isBusy = false;
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
        if (interviewUI == null)
        {
            return;
        }

        if (subscribe)
        {
            interviewUI.OnQuestionClicked += HandleQuestionClicked;
            interviewUI.OnAbcdeClicked += HandleAbcdeClicked;
            interviewUI.OnDoneClicked += EndInterview;
            interviewUI.OnSkipRequested += HandleSkipRequested;
        }
        else
        {
            interviewUI.OnQuestionClicked -= HandleQuestionClicked;
            interviewUI.OnAbcdeClicked -= HandleAbcdeClicked;
            interviewUI.OnDoneClicked -= EndInterview;
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
            RefreshQuestions();
        }
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
