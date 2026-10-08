using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class Patient : MonoBehaviour
{
    public event Action<RecognizedCue> OnCueRecognized;

    [SerializeField] PatientInterviewController interviewController;
    [SerializeField] PatientInterviewUI interviewUI;
    [FormerlySerializedAs("startingInterview")]
    [SerializeField] SO_Patient profile;
    [SerializeField] bool startOnPlay = true;

    readonly List<RecognizedCue> recognizedCues = new List<RecognizedCue>();
    readonly HashSet<string> recognizedIds = new HashSet<string>();

    public SO_Patient Profile => profile;
    public IReadOnlyList<RecognizedCue> RecognizedCues => recognizedCues;

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

        if (interviewUI != null)
        {
            interviewUI.BeginPatient(profile);
            interviewUI.RebuildCues(recognizedCues);
        }

        if (interviewController != null)
        {
            interviewController.StartInterview(profile);
        }
    }

    public void PlayInterview(SO_Patient interview)
    {
        Begin(interview);
    }

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
            }
            else
            {
                interviewUI.OnHotspotClicked -= HandleHotspotClicked;
            }
        }
    }

    void HandleQuestionCue(RecognizedCue cue)
    {
        TryRecognize(cue);
    }

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

        if (!TryRecognize(hotspot.ToCue()))
        {
            return;
        }

        if (interviewController != null)
        {
            interviewController.UnlockQuestions(hotspot.unlocksQuestionIds);
        }
    }

    bool TryRecognize(RecognizedCue cue)
    {
        if (string.IsNullOrEmpty(cue.Id) || !recognizedIds.Add(cue.Id))
        {
            return false;
        }

        recognizedCues.Add(cue);
        Debug.Log($"Cue recognized: {cue.Id}");
        OnCueRecognized?.Invoke(cue);

        if (interviewUI != null)
        {
            interviewUI.RebuildCues(recognizedCues);
        }

        return true;
    }
}
