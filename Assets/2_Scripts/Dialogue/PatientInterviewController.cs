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
/// decides what happens. When the player picks a question, the player's line is typed and held briefly,
/// then the patient's reply and any follow-up lines are played through <see cref="PlayLines"/>: each line is
/// typed out letter by letter, voiced by its speaker, and waits for a click before the next one.
/// Clicking the dialogue panel while a line is typing shows it in full. After the reply, the question's cue
/// (if any) is raised through <see cref="OnCueRecognized"/> and its follow-up questions are unlocked.
/// Only one exchange can run at a time; input is disabled while <c>isBusy</c> is true.
/// </para>
/// <para>
/// Each question and each ABCDE step can only be used once per interview.
/// </para>
/// </summary>
public class PatientInterviewController : MonoBehaviour
{
    public event Action OnInterviewStarted;
    /// <summary>Raised when the interview closes, for example when handover finishes the case.</summary>
    public event Action OnInterviewEnded;
    /// <summary>Raised when a question answer or ABCDE finding awards a cue. <see cref="Patient"/> listens to this.</summary>
    public event Action<RecognizedCue> OnCueRecognized;

    [SerializeField] PatientInterviewUI interviewUI;
    [SerializeField] string playerDisplayName = "You";
    [Tooltip("Scales every patient's text speed. 1 uses the patient's own speed, 2 types twice as slowly, 0.5 twice as fast.")]
    [SerializeField, Range(0.25f, 4f)] float textSpeedMultiplier = 1f;
    [Tooltip("Seconds between each typed character of the player's lines. Lower is faster.")]
    [SerializeField, Range(0.005f, 0.1f)] float playerTextSpeed = 0.015f;
    [Tooltip("Seconds the player's chosen line stays on screen before the next line starts.")]
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
    PatientProgress progress;
    string pendingQuestionId;
    int pendingExamination = -1;
    Coroutine spokenLine;

    public bool IsPlaying => isPlaying;
    /// <summary>True while a question or ABCDE line is still being shown.</summary>
    public bool IsBusy => isBusy;

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
        interviewUI.SetContinueHintVisible(false);
        interviewUI.SetVisible(true);
        bool showOpening = !progress.HasProgress && !string.IsNullOrWhiteSpace(interview.openingText);
        interviewUI.SetSpeakerName(showOpening ? string.Empty : interview.patientDisplayName);
        if (progress.HasProgress)
        {
            interviewUI.SetDialogueText($"Welcome back. Continue your conversation with {interview.patientDisplayName}.");
        }
        else
        {
            interviewUI.SetDialogueText(showOpening
                ? interview.openingText.Trim()
                : $"Select a question to ask {interview.patientDisplayName}.");
        }
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
        spokenLine = null;
        isPlaying = false;
        isBusy = false;
        interview = null;
        askedIds.Clear();
        unlockedIds.Clear();

        if (interviewUI != null)
        {
            interviewUI.SetContinueHintVisible(false);
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
            interviewUI.OnSkipRequested += HandleSkipRequested;
        }
        else
        {
            interviewUI.OnQuestionClicked -= HandleQuestionClicked;
            interviewUI.OnAbcdeClicked -= HandleAbcdeClicked;
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
    /// Plays one question/answer: the player's question, the patient's reply and any follow-up lines,
    /// then awards the cue and unlocks follow-up questions.
    /// </summary>
    IEnumerator PlayExchange(InterviewQuestion question)
    {
        isBusy = true;
        skipRequested = false;
        interviewUI.SetQuestionsEnabled(false);
        RefreshQuestions();

        List<DialogueLine> lines = new List<DialogueLine>
        {
            new DialogueLine(DialogueSpeaker.Nurse, question.questionText),
            new DialogueLine(DialogueSpeaker.Patient, question.patientResponse)
        };
        if (question.followUpLines != null)
        {
            lines.AddRange(question.followUpLines);
        }

        yield return PlayLinesRoutine(lines);

        AwardCueIfNeeded(question);
        UnlockFollowUps(question);
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
        yield return TypeLine(text, Voice.Silent(interview.textSpeed));

        if (finding != null && finding.awardsCue)
        {
            OnCueRecognized?.Invoke(finding.ToCue());
        }

        interviewUI.SetAbcdeExamined(letter);
        pendingExamination = -1;
        SaveProgress();
        isBusy = false;
        interviewUI.SetQuestionsEnabled(true);
    }

    /// <summary>
    /// Plays <paramref name="lines"/> in order (see <see cref="PlayLinesRoutine"/>), then calls
    /// <paramref name="onComplete"/>. Input is blocked until the last line has been typed.
    /// </summary>
    public void PlayLines(IReadOnlyList<DialogueLine> lines, Action onComplete)
    {
        if (spokenLine != null)
        {
            StopCoroutine(spokenLine);
            spokenLine = null;
            isBusy = false;
            interviewUI?.SetContinueHintVisible(false);
        }

        spokenLine = StartCoroutine(PlayLinesThenComplete(lines, onComplete));
    }

    IEnumerator PlayLinesThenComplete(IReadOnlyList<DialogueLine> lines, Action onComplete)
    {
        if (!isPlaying || interview == null || interviewUI == null)
        {
            spokenLine = null;
            onComplete?.Invoke();
            yield break;
        }

        isBusy = true;
        skipRequested = false;
        interviewUI.SetQuestionsEnabled(false);
        yield return PlayLinesRoutine(lines);
        isBusy = false;
        interviewUI.SetQuestionsEnabled(true);
        spokenLine = null;
        onComplete?.Invoke();
    }

    /// <summary>
    /// Types each line with its speaker's name and voice. A nurse line at the start is the choice the
    /// player just clicked, so it moves on by itself after <c>playerLineHold</c>; every other line waits
    /// for a click on the dialogue box before the next one. The last line stays on screen.
    /// </summary>
    IEnumerator PlayLinesRoutine(IReadOnlyList<DialogueLine> lines)
    {
        if (lines == null)
        {
            yield break;
        }

        bool waitBeforeNext = false;
        bool first = true;
        for (int i = 0; i < lines.Count; i++)
        {
            DialogueLine line = lines[i];
            if (line == null || !line.HasText)
            {
                continue;
            }

            if (waitBeforeNext)
            {
                yield return WaitForClick();
            }

            interviewUI.SetSpeakerName(SpeakerName(line));
            yield return TypeLine(line.text.Trim(), VoiceFor(line));

            bool chosenLine = first && line.speaker == DialogueSpeaker.Nurse;
            if (chosenLine)
            {
                yield return Hold(playerLineHold);
            }

            waitBeforeNext = !chosenLine;
            first = false;
        }
    }

    IEnumerator WaitForClick()
    {
        skipRequested = false;
        interviewUI.SetContinueHintVisible(true);
        while (!skipRequested)
        {
            yield return null;
        }

        skipRequested = false;
        interviewUI.SetContinueHintVisible(false);
    }

    IEnumerator Hold(float seconds)
    {
        while (seconds > 0f && !skipRequested)
        {
            seconds -= Time.unscaledDeltaTime;
            yield return null;
        }

        skipRequested = false;
    }

    string SpeakerName(DialogueLine line)
    {
        switch (line.speaker)
        {
            case DialogueSpeaker.Nurse:
                return playerDisplayName;
            case DialogueSpeaker.Patient:
                return interview.patientDisplayName;
            case DialogueSpeaker.Other:
                return line.otherSpeaker != null ? line.otherSpeaker.displayName : string.Empty;
            default:
                return string.Empty;
        }
    }

    Voice VoiceFor(DialogueLine line)
    {
        switch (line.speaker)
        {
            case DialogueSpeaker.Nurse:
                return Voice.Silent(playerTextSpeed);
            case DialogueSpeaker.Patient:
                return new Voice(interview.patientDialogueEvent, interview.dialoguePitch, interview.textSpeed, interview.charactersPerVoiceBlip, true);
            case DialogueSpeaker.Other when line.otherSpeaker != null:
                SO_Speaker speaker = line.otherSpeaker;
                return new Voice(speaker.dialogueEvent, speaker.dialoguePitch, speaker.textSpeed, speaker.charactersPerVoiceBlip, false);
            default:
                return Voice.Silent(interview.textSpeed);
        }
    }

    /// <summary>
    /// Typewriter effect: reveals <paramref name="line"/> one character every <see cref="Voice.TextSpeed"/>
    /// seconds, scaled by <c>textSpeedMultiplier</c>.
    /// Uses unscaled/realtime timing so it still works when the game is paused (timeScale 0).
    /// A skip request shows the full line immediately. A voiced line plays a blip every
    /// <see cref="Voice.BlipInterval"/> characters, so the sound follows the text speed.
    /// </summary>
    IEnumerator TypeLine(string line, Voice voice)
    {
        string text = line ?? string.Empty;
        interviewUI.SetDialogueText(string.Empty);
        float characterDelay = voice.TextSpeed * textSpeedMultiplier;
        int blipInterval = Mathf.Max(1, voice.BlipInterval);

        for (int i = 0; i < text.Length; i++)
        {
            if (skipRequested)
            {
                break;
            }

            interviewUI.SetDialogueText(text.Substring(0, i + 1));
            yield return new WaitForSecondsRealtime(characterDelay);

            if (voice.Voiced && i % blipInterval == 0)
            {
                PlayVoiceBlip(voice);
            }
        }

        interviewUI.SetDialogueText(text);
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
    /// Rebuilds the question buttons from the unlocked questions that have not been asked yet,
    /// in their unlocked display order.
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
            if (askedIds.Contains(unlockedIds[i]))
            {
                continue;
            }

            InterviewQuestion question = interview.FindQuestion(unlockedIds[i]);
            if (question == null)
            {
                continue;
            }

            views.Add(new InterviewQuestionView(question.id, question.questionText, false));
        }

        interviewUI.RebuildQuestions(views);
    }

    /// <summary>
    /// Plays one short, fire-and-forget instance of the speaker's dialogue event. FMOD frees it once
    /// it finishes. A missing patient voice or an invalid event is reported only once per interview so the
    /// console isn't flooded. Other speakers without an event stay silent.
    /// </summary>
    void PlayVoiceBlip(Voice voice)
    {
        if (voice.Event.IsNull)
        {
            if (voice.WarnIfMissing && !warnedMissingVoice)
            {
                warnedMissingVoice = true;
                string assetName = interview != null ? interview.name : "null";
                Debug.LogWarning($"PatientInterviewController: no patientDialogueEvent assigned on '{assetName}'.", this);
            }

            return;
        }

        EventInstance blip = RuntimeManager.CreateInstance(voice.Event);
        if (!blip.isValid())
        {
            if (!warnedMissingVoice)
            {
                warnedMissingVoice = true;
                Debug.LogError("PatientInterviewController: FMOD dialogue event is invalid. Check the assigned event.", this);
            }

            return;
        }

        blip.setPitch(voice.Pitch);
        blip.start();
        blip.release();
    }

    /// <summary>How one line is typed and voiced, taken from the patient, an <see cref="SO_Speaker"/>, or nobody.</summary>
    readonly struct Voice
    {
        public readonly EventReference Event;
        public readonly float Pitch;
        public readonly float TextSpeed;
        public readonly int BlipInterval;
        public readonly bool Voiced;
        public readonly bool WarnIfMissing;

        public Voice(EventReference dialogueEvent, float pitch, float textSpeed, int blipInterval, bool warnIfMissing)
        {
            Event = dialogueEvent;
            Pitch = pitch;
            TextSpeed = textSpeed;
            BlipInterval = blipInterval;
            Voiced = true;
            WarnIfMissing = warnIfMissing;
        }

        Voice(float textSpeed)
        {
            Event = default;
            Pitch = 1f;
            TextSpeed = textSpeed;
            BlipInterval = 1;
            Voiced = false;
            WarnIfMissing = false;
        }

        public static Voice Silent(float textSpeed)
        {
            return new Voice(textSpeed);
        }
    }
}
