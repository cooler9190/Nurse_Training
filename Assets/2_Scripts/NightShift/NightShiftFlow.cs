using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plays a night shift without changing the interview. The shift clock runs in real time from the start time to
/// the end time of <see cref="SO_NightShift"/> (see <see cref="NightShiftProgress"/>). When the clock reaches a
/// call's time its room starts blinking, and the player attends it by clicking the room (see
/// <see cref="HospitalRoomPicker"/>). Calls are not announced beforehand.
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
    [Tooltip("The hospital map, brought back when an interview ends.")]
    [SerializeField] GameObject hospitalRoot;

    NightShiftProgress progress;
    PatientRoom[] rooms = new PatientRoom[0];
    PatientRoom[] callRooms = new PatientRoom[0];
    bool[] announced = new bool[0];
    bool onMap;
    bool summaryPending;
    bool timeUpHandled;

    void Awake()
    {
        if (patient == null) { patient = FindAnyObjectByType<Patient>(FindObjectsInactive.Include); }
        if (controller == null) { controller = FindAnyObjectByType<PatientInterviewController>(FindObjectsInactive.Include); }
        if (handover == null) { handover = FindAnyObjectByType<HandoverFlow>(FindObjectsInactive.Include); }
        if (screen == null) { screen = FindAnyObjectByType<NightShiftScreenUI>(FindObjectsInactive.Include); }
        rooms = FindObjectsByType<PatientRoom>(FindObjectsInactive.Include);
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
            screen.OnRestartClicked += Restart;
            screen.OnSummaryClosed += ShowMap;
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
            screen.OnRestartClicked -= Restart;
            screen.OnSummaryClosed -= ShowMap;
        }
    }

    void Start()
    {
        if (controller == null || !controller.IsPlaying)
        {
            ShowMap();
        }
        else if (screen != null && progress != null)
        {
            screen.ShowInterviewClock();
        }
    }

    void Update()
    {
        if (progress == null || screen == null)
        {
            return;
        }

        // With nobody waiting the clock runs faster, so the player never sits through dead time before the next call
        float delta = Time.unscaledDeltaTime;
        if (shift != null && shift.idleSpeedUp > 1f && IsIdle())
        {
            delta *= shift.idleSpeedUp;
        }

        progress.Tick(delta);
        screen.SetClock(progress.Clock, progress.Elapsed01);
        AnnounceCalls();

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

        announced = new bool[shift.calls.Count];
        callRooms = new PatientRoom[shift.calls.Count];
        for (int i = 0; i < shift.calls.Count; i++)
        {
            callRooms[i] = FindRoom(shift.calls[i]);
        }
    }

    // The call's own room if it names one, otherwise the room that holds the patient
    PatientRoom FindRoom(NightCall call)
    {
        if (call == null)
        {
            return null;
        }

        for (int i = 0; i < rooms.Length; i++)
        {
            if (rooms[i] == null)
            {
                continue;
            }

            bool named = !string.IsNullOrWhiteSpace(call.room) && rooms[i].RoomName.IndexOf(call.room.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
            bool holdsPatient = string.IsNullOrWhiteSpace(call.room) && call.patient != null && rooms[i].AssignedPatient == call.patient;
            if (named || holdsPatient)
            {
                return rooms[i];
            }
        }

        if (!string.IsNullOrWhiteSpace(call.room))
        {
            Debug.LogWarning($"NightShiftFlow: no room matches \"{call.room}\" for the call at {call.time}.", this);
        }

        return null;
    }

    // True when the player is on the map, no call is waiting for them and there is still a call to come
    bool IsIdle()
    {
        if (!onMap || progress.IsOver)
        {
            return false;
        }

        bool upcoming = false;
        for (int i = 0; i < progress.CallCount; i++)
        {
            if (progress.IsDone(i))
            {
                continue;
            }

            if (progress.IsRinging(i))
            {
                return false;
            }

            upcoming = true;
        }

        return upcoming;
    }

    // Starts the room blinking for every call that has just begun to ring
    void AnnounceCalls()
    {
        for (int i = 0; i < announced.Length; i++)
        {
            if (announced[i] || !progress.IsRinging(i) || progress.IsDone(i))
            {
                continue;
            }

            announced[i] = true;
            if (callRooms[i] != null)
            {
                callRooms[i].StartCall(shift.calls[i].patient);
            }
        }
    }

    void StopRoomCall(int index)
    {
        if (index >= 0 && index < callRooms.Length && callRooms[index] != null)
        {
            callRooms[index].StopCall();
        }
    }

    void StopAllRoomCalls()
    {
        for (int i = 0; i < callRooms.Length; i++)
        {
            StopRoomCall(i);
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
        StopRoomCall(index);
        summaryPending = progress.IsComplete;
    }

    void HandleInterviewStarted()
    {
        onMap = false;

        // The nurse is in the room now, so it stops blinking
        StopRoomCall(IndexOf(patient != null ? patient.Profile : null));
        if (screen != null)
        {
            screen.ShowInterviewClock();
        }
    }

    void HandleInterviewEnded()
    {
        if (hospitalRoot != null) { hospitalRoot.SetActive(true); }
        if (summaryPending || (progress != null && progress.IsOver))
        {
            summaryPending = false;
            StopAllRoomCalls();
            ShowSummary();
        }
        else
        {
            // A call the player left without handing over is still waiting, so its room blinks again
            for (int i = 0; progress != null && i < announced.Length; i++)
            {
                if (announced[i] && progress.IsRinging(i) && !progress.IsDone(i))
                {
                    announced[i] = false;
                }
            }

            ShowMap();
        }
    }

    // The shift is over: leave the patient the player is with, then show how the night went
    void HandleTimeUp()
    {
        timeUpHandled = true;
        StopAllRoomCalls();
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
        StopAllRoomCalls();
        announced = new bool[announced.Length];
        summaryPending = false;
        timeUpHandled = false;
        ShowMap();
    }

    void ShowMap()
    {
        if (progress == null || screen == null)
        {
            return;
        }

        onMap = true;
        screen.SetClock(progress.Clock, progress.Elapsed01);
        screen.ShowMapClock();
    }

    void ShowSummary()
    {
        if (progress == null || screen == null)
        {
            return;
        }

        onMap = false;
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
                Done = progress.IsDone(i),
                Percent = progress.Percent(i),
                Level = progress.Level(i)
            });
        }

        return cards;
    }

    int IndexOf(SO_Patient profile)
    {
        if (shift == null || profile == null)
        {
            return -1;
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
