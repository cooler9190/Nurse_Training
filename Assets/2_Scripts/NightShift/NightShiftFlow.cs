using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plays a night shift without changing the interview. While the player is on the ward map it shows the bell
/// calls from <see cref="SO_NightShift"/> and the clock. Attending a call hides the map and starts that call's
/// patient with <see cref="Patient.Begin"/>, the same way clicking a room does. A call is done when the player
/// hands the patient over (<see cref="HandoverFlow.OnHandedOver"/>), which records the score. When every call is
/// done and the player is back on the map, the end-of-shift summary shows.
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
    bool summaryPending;

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

        progress = new NightShiftProgress(shift.startTime, times);
    }

    void StartCall(int index)
    {
        if (progress == null || patient == null || index < 0 || index >= shift.calls.Count)
        {
            return;
        }

        NightCall call = shift.calls[index];
        if (call == null || call.patient == null || (controller != null && controller.IsPlaying))
        {
            return;
        }

        current = index;
        progress.Attend(index);
        if (hospitalRoot != null) { hospitalRoot.SetActive(false); }
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
        if (screen != null) { screen.Hide(); }
    }

    void HandleInterviewEnded()
    {
        current = -1;
        if (hospitalRoot != null) { hospitalRoot.SetActive(true); }
        if (summaryPending)
        {
            summaryPending = false;
            ShowSummary();
        }
        else
        {
            ShowCalls();
        }
    }

    void Restart()
    {
        if (progress == null)
        {
            return;
        }

        progress.Reset();
        summaryPending = false;
        ShowCalls();
    }

    void ShowCalls()
    {
        if (progress == null || screen == null)
        {
            return;
        }

        screen.ShowCalls(shift.shiftName, progress.Clock, BuildCards());
    }

    void ShowSummary()
    {
        if (progress == null || screen == null)
        {
            return;
        }

        screen.ShowSummary(shift.shiftName, progress.AveragePercent, progress.AverageLevel, BuildCards());
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
                Done = progress.IsDone(i),
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
