using System;
using System.Collections;
using System.Collections.Generic;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

public class PatientInterviewController : MonoBehaviour
{
    public event Action OnInterviewStarted;
    public event Action OnInterviewEnded;
    public event Action<RecognizedCue> OnCueRecognized;

    [SerializeField] PatientInterviewUI interviewUI;
    [SerializeField] string playerDisplayName = "You";
    [SerializeField] float textSpeed = 0.02f;
    [SerializeField] float playerLineHold = 0.35f;

    SO_Patient interview;
    readonly HashSet<string> askedIds = new HashSet<string>();
    readonly HashSet<string> unlockedIds = new HashSet<string>();
    readonly bool[] examined = new bool[5];
    bool isPlaying;
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
                if (question != null && question.startsUnlocked && !string.IsNullOrEmpty(question.id))
                {
                    unlockedIds.Add(question.id);
                }
            }
        }

        interviewUI.SetVisible(true);
        interviewUI.SetSpeakerName(interview.patientDisplayName);
        interviewUI.SetDialogueText($"Select a question to ask {interview.patientDisplayName}.");
        RefreshQuestions();
        OnInterviewStarted?.Invoke();
    }

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

    void HandleSkipRequested()
    {
        if (isBusy)
        {
            skipRequested = true;
        }
    }

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

    void HandleAbcdeClicked(AbcdeLetter letter)
    {
        if (!isPlaying || isBusy || examined[(int)letter])
        {
            return;
        }

        examined[(int)letter] = true;
        StartCoroutine(PlayExamination(letter));
    }

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

    public void UnlockQuestions(IList<string> questionIds)
    {
        if (!isPlaying || questionIds == null)
        {
            return;
        }

        bool changed = false;
        for (int i = 0; i < questionIds.Count; i++)
        {
            string id = questionIds[i];
            if (!string.IsNullOrEmpty(id) && unlockedIds.Add(id))
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

    void UnlockFollowUps(InterviewQuestion question)
    {
        if (question.unlocksQuestionIds == null)
        {
            return;
        }

        for (int i = 0; i < question.unlocksQuestionIds.Count; i++)
        {
            string id = question.unlocksQuestionIds[i];
            if (!string.IsNullOrEmpty(id))
            {
                unlockedIds.Add(id);
            }
        }
    }

    void RefreshQuestions()
    {
        if (interviewUI == null || interview == null)
        {
            return;
        }

        List<InterviewQuestionView> views = new List<InterviewQuestionView>();
        if (interview.questions != null)
        {
            for (int i = 0; i < interview.questions.Count; i++)
            {
                InterviewQuestion question = interview.questions[i];
                if (question == null || string.IsNullOrEmpty(question.id) || !unlockedIds.Contains(question.id))
                {
                    continue;
                }

                views.Add(new InterviewQuestionView(question.id, question.questionText, askedIds.Contains(question.id)));
            }
        }

        interviewUI.RebuildQuestions(views);
    }

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
