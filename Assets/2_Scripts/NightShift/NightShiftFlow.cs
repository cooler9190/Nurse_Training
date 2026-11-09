using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plays a night shift without changing the interview. The shift clock runs in real time from the start time to
/// the end time of <see cref="SO_NightShift"/> (see <see cref="NightShiftProgress"/>). While the player is on the
/// ward map it shows the bell calls; a call can be attended once the clock reaches its time. Attending a call hides
/// the map and starts that call's patient with <see cref="Patient.Begin"/>, the same way clicking a room does.
/// A call is done when the player hands the patient over (<see cref="HandoverFlow.OnHandedOver"/>), which records
/// the score. The summary shows when every call is done, or when the clock reaches the end of the shift: an
/// interview still running is ended and calls that were not done count as 0%.
/// Missing references are looked up in the scene.
/// </summary>
public class NightShiftFlow : MonoBehaviour
{
    [SerializeField] SO_NightShift shift;
    [SerializeField] Patient patient;
    [SerializeField] PatientInterviewController controller;
    [SerializeField] HandoverFlow handover;
    [SerializeField] NightShiftScreenUI screen;
    [Tooltip("The hospital map, hidden while a patient is being examined. The room picker brings it back.")]
    [SerializeField] GameObject hospitalRoot;

    NightShiftProgress progress;
    int current = -1;
    int lastRinging;
    bool callsShowing;
    bool summaryPending;
    bool timeUpHandled;

    void Awake()
    {
        if (patient == null) { patient = FindAnyObjectByType<Patient>(FindObjectsInactive.Include); }
        if (controller == null) { controller = FindAnyObjectByType<PatientInterviewController>(FindObjectsInactive.Include); }
        if (handover == null) { handover = FindAnyObjectByType<HandoverFlow>(FindObjectsInactive.Include); }
        if (screen == null) { screen = FindAnyObjectByType<NightShiftScreenUI>(FindObjectsInactive.Include); }
        BuildProgress();
    }

    void OnEnable()
    {
        if (controller != null)
        {
            controller.OnInterviewStarted += HandleInterviewStarted;
            controller.OnInterviewEnded += HandleInterviewEnded;
        }

        if (handover != null) { handover.OnHandedOver += HandleHandedOver; }
        if (screen != null)
        {
            screen.OnCallClicked += StartCall;
            screen.OnRestartClicked += Restart;
            screen.OnSummaryClosed += ShowCalls;
        }
    }

    void OnDisable()
    {
        if (controller != null)
        {
            controller.OnInterviewStarted -= HandleInterviewStarted;
            controller.OnInterviewEnded -= HandleInterviewEnded;
        }

        if (handover != null) { handover.OnHandedOver -= HandleHandedOver; }
        if (screen != null)
        {
            screen.OnCallClicked -= StartCall;
            screen.OnRestartClicked -= Restart;
            screen.OnSummaryClosed -= ShowCalls;
        }
    }

    void Start()
    {
        if (controller == null || !controller.IsPlaying)
        {
            ShowCalls();
        }
        else if (screen != null && progress != null)
        {
            screen.SetClockBarVisible(true);
        }
    }

    void Update()
    {
        if (progress == null || screen == null)
        {
            return;
        }

        progress.Tick(Time.unscaledDeltaTime);
        screen.SetClock(progress.Clock, progress.Elapsed01);

        // A call that has just started ringing needs the list to be rebuilt
        int ringing = progress.RingingCount;
        if (ringing != lastRinging)
        {
            lastRinging = ringing;
            if (callsShowing) { ShowCalls(); }
        }

        if (progress.IsOver && !timeUpHandled)
        {
            HandleTimeUp();
        }
    }

    void BuildProgress()
    {
        if (shift == null)
        {
            return;
        }

        List<string> times = new List<string>();
        foreach (NightCall call in shift.calls)
        {
            times.Add(call != null ? call.time : string.Empty);
        }

        progress = new NightShiftProgress(shift.startTime, shift.endTime, shift.lengthMinutes, times);
    }

    void StartCall(int index)
    {
        if (progress == null || patient == null || progress.IsOver || index < 0 || index >= shift.calls.Count || !progress.IsRinging(index) || progress.IsDone(index))
        {
            return;
        }

        NightCall call = shift.calls[index];
        if (call == null || call.patient == null || (controller != null && controller.IsPlaying))
        {
            return;
        }

        current = index;
        if (hospitalRoot != null) { hospitalRoot.SetActive(false); }
        callsShowing = false;
        screen.Hide();
        patient.Begin(call.patient);

        // Begin can fail (missing asset or UI), so don't leave the player on an empty screen
        if (controller != null && !controller.IsPlaying)
        {
            if (hospitalRoot != null) { hospitalRoot.SetActive(true); }
            current = -1;
            ShowCalls();
        }
    }

    void HandleHandedOver(SO_Patient profile, ScoreBreakdown score)
    {
        int index = IndexOf(profile);
        if (progress == null || index < 0)
        {
            return;
        }

        progress.Complete(index, score.Percent, score.Level);
        summaryPending = progress.IsComplete;
    }

    // Covers an interview started from a room on the map, not only from a call
    void HandleInterviewStarted()
    {
        callsShowing = false;
        if (screen != null)
        {
            screen.Hide();
            screen.SetClockBarVisible(true);
        }
    }

    void HandleInterviewEnded()
    {
        current = -1;
        if (hospitalRoot != null) { hospitalRoot.SetActive(true); }
        if (screen != null) { screen.SetClockBarVisible(false); }
        if (summaryPending || (progress != null && progress.IsOver))
        {
            summaryPending = false;
            ShowSummary();
        }
        else
        {
            ShowCalls();
        }
    }

    // The shift is over: leave the patient the player is with, then show how the night went
    void HandleTimeUp()
    {
        timeUpHandled = true;
        if (controller != null && controller.IsPlaying)
        {
            controller.EndInterview();
        }
        else
        {
            ShowSummary();
        }
    }

    void Restart()
    {
        if (progress == null)
        {
            return;
        }

        progress.Reset();
        lastRinging = 0;
        summaryPending = false;
        timeUpHandled = false;
        ShowCalls();
    }

    void ShowCalls()
    {
        if (progress == null || screen == null)
        {
            return;
        }

        callsShowing = true;
        screen.SetClockBarVisible(false);
        screen.ShowCalls(shift.shiftName, progress.Clock, BuildCards());
    }

    void ShowSummary()
    {
        if (progress == null || screen == null)
        {
            return;
        }

        callsShowing = false;
        screen.SetClockBarVisible(false);
        screen.ShowSummary(shift.shiftName, progress.AveragePercent, progress.AverageLevel, BuildCards(), progress.IsOver);
    }

    List<NightCallCard> BuildCards()
    {
        List<NightCallCard> cards = new List<NightCallCard>();
        for (int i = 0; i < shift.calls.Count; i++)
        {
            NightCall call = shift.calls[i];
            cards.Add(new NightCallCard
            {
                Time = call != null ? call.time : string.Empty,
                Bed = call != null ? call.bed : string.Empty,
                Hint = call != null && !string.IsNullOrWhiteSpace(call.hint) ? call.hint : "The call bell is ringing.",
                PatientName = call != null && call.patient != null ? call.patient.patientDisplayName : string.Empty,
                Ringing = progress.IsRinging(i),
                Done = progress.IsDone(i),
                Missed = progress.IsMissed(i),
                Percent = progress.Percent(i),
                Level = progress.Level(i)
            });
        }

        return cards;
    }

    // Prefers the call being attended, in case the same patient appears twice
    int IndexOf(SO_Patient profile)
    {
        if (shift == null || profile == null)
        {
            return -1;
        }

        if (current >= 0 && current < shift.calls.Count && shift.calls[current] != null && shift.calls[current].patient == profile)
        {
            return current;
        }

        for (int i = 0; i < shift.calls.Count; i++)
        {
            if (shift.calls[i] != null && shift.calls[i].patient == profile)
            {
                return i;
            }
        }

        return -1;
    }
}
