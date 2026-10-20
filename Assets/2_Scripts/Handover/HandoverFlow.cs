using UnityEngine;

/// <summary>
/// Connects the interview to the hand over without changing it. The Hand over button is shown while an
/// interview is running (not while the hypothesis screen or the hand over itself covers it). Pressing it reads
/// the player's current cue ranking from <see cref="HypothesisScreenUI"/>, builds the comparison with
/// <see cref="PriorityEvaluator"/> against the patient's <see cref="SO_Priorities"/> and shows it. Finish case
/// ends the interview, which sends the player back to the map. Patients without a priorities asset get no button.
/// Missing references are looked up in the scene.
/// </summary>
public class HandoverFlow : MonoBehaviour
{
    [SerializeField] Patient patient;
    [SerializeField] PatientInterviewController controller;
    [SerializeField] PatientInterviewUI interviewUI;
    [SerializeField] HypothesisScreenUI hypothesisScreen;
    [SerializeField] HandoverScreenUI handoverScreen;
    [Tooltip("One asset per patient. The one whose patient is being played is used.")]
    [SerializeField] SO_Priorities[] prioritySets;

    bool interviewActive;
    bool hypothesisOpen;
    bool handoverOpen;

    void Awake()
    {
        if (patient == null) { patient = FindAnyObjectByType<Patient>(FindObjectsInactive.Include); }
        if (controller == null) { controller = FindAnyObjectByType<PatientInterviewController>(FindObjectsInactive.Include); }
        if (interviewUI == null) { interviewUI = FindAnyObjectByType<PatientInterviewUI>(FindObjectsInactive.Include); }
        if (hypothesisScreen == null) { hypothesisScreen = FindAnyObjectByType<HypothesisScreenUI>(FindObjectsInactive.Include); }
        if (handoverScreen == null) { handoverScreen = FindAnyObjectByType<HandoverScreenUI>(FindObjectsInactive.Include); }
    }

    void OnEnable()
    {
        if (controller != null)
        {
            controller.OnInterviewStarted += HandleInterviewStarted;
            controller.OnInterviewEnded += HandleInterviewEnded;
        }

        if (interviewUI != null) { interviewUI.OnCuesClicked += HandleHypothesisOpened; }
        if (hypothesisScreen != null) { hypothesisScreen.OnClosed += HandleHypothesisClosed; }
        if (handoverScreen != null)
        {
            handoverScreen.OnHandoverClicked += HandleHandover;
            handoverScreen.OnClosed += HandleHandoverClosed;
            handoverScreen.OnFinishClicked += HandleFinish;
        }
    }

    void OnDisable()
    {
        if (controller != null)
        {
            controller.OnInterviewStarted -= HandleInterviewStarted;
            controller.OnInterviewEnded -= HandleInterviewEnded;
        }

        if (interviewUI != null) { interviewUI.OnCuesClicked -= HandleHypothesisOpened; }
        if (hypothesisScreen != null) { hypothesisScreen.OnClosed -= HandleHypothesisClosed; }
        if (handoverScreen != null)
        {
            handoverScreen.OnHandoverClicked -= HandleHandover;
            handoverScreen.OnClosed -= HandleHandoverClosed;
            handoverScreen.OnFinishClicked -= HandleFinish;
        }
    }

    void HandleInterviewStarted()
    {
        interviewActive = true;
        hypothesisOpen = false;
        handoverOpen = false;
        UpdateButton();
    }

    void HandleInterviewEnded()
    {
        interviewActive = false;
        handoverOpen = false;
        handoverScreen.Hide();
        UpdateButton();
    }

    void HandleHypothesisOpened()
    {
        hypothesisOpen = true;
        UpdateButton();
    }

    void HandleHypothesisClosed()
    {
        hypothesisOpen = false;
        UpdateButton();
    }

    void HandleHandover()
    {
        SO_Priorities set = FindSet();
        if (set == null)
        {
            return;
        }

        HandoverReport report = PriorityEvaluator.Evaluate(set, patient.RecognizedCues, hypothesisScreen.RankedCueIds, patient.Profile.diagnoses);
        handoverOpen = true;
        handoverScreen.Show(report);
    }

    void HandleHandoverClosed()
    {
        handoverOpen = false;
        UpdateButton();
    }

    void HandleFinish()
    {
        handoverOpen = false;
        handoverScreen.Hide();
        controller.EndInterview();
    }

    void UpdateButton()
    {
        handoverScreen.SetButtonVisible(interviewActive && !hypothesisOpen && !handoverOpen && FindSet() != null);
    }

    SO_Priorities FindSet()
    {
        if (patient == null || patient.Profile == null)
        {
            return null;
        }

        for (int i = 0; prioritySets != null && i < prioritySets.Length; i++)
        {
            if (prioritySets[i] != null && prioritySets[i].patient == patient.Profile)
            {
                return prioritySets[i];
            }
        }

        return null;
    }
}
