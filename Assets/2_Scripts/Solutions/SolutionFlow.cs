using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Connects the hypothesis screen to the Generate Solutions step without changing either of them.
/// <para>
/// Once the player has pressed Diagnose it shows a Generate solutions button next to it. Pressing that
/// reads the player's current cue ranking from <see cref="HypothesisScreenUI"/>, turns it into the
/// prioritised diagnoses with <see cref="DiagnosisRanker"/>, picks the <see cref="SO_Solutions"/> set that
/// belongs to the patient being played and opens <see cref="SolutionScreenUI"/>. The button is hidden when
/// the hypothesis screen closes, or when the patient has no solutions set.
/// </para>
/// Missing references are looked up in the scene.
/// </summary>
public class SolutionFlow : MonoBehaviour
{
    [SerializeField] Patient patient;
    [SerializeField] HypothesisScreenUI hypothesisScreen;
    [SerializeField] SolutionScreenUI solutionScreen;
    [Tooltip("One set per patient that has solutions to generate. The set whose patient is being played is used.")]
    [SerializeField] SO_Solutions[] solutionSets;

    void Awake()
    {
        if (patient == null)
        {
            patient = FindAnyObjectByType<Patient>(FindObjectsInactive.Include);
        }

        if (hypothesisScreen == null)
        {
            hypothesisScreen = FindAnyObjectByType<HypothesisScreenUI>(FindObjectsInactive.Include);
        }

        if (solutionScreen == null)
        {
            solutionScreen = FindAnyObjectByType<SolutionScreenUI>(FindObjectsInactive.Include);
        }
    }

    void OnEnable()
    {
        if (hypothesisScreen != null)
        {
            hypothesisScreen.OnDiagnosed += HandleDiagnosed;
            hypothesisScreen.OnClosed += HandleHypothesisClosed;
        }

        if (solutionScreen != null)
        {
            solutionScreen.OnEntryClicked += HandleEntryClicked;
        }
    }

    void OnDisable()
    {
        if (hypothesisScreen != null)
        {
            hypothesisScreen.OnDiagnosed -= HandleDiagnosed;
            hypothesisScreen.OnClosed -= HandleHypothesisClosed;
        }

        if (solutionScreen != null)
        {
            solutionScreen.OnEntryClicked -= HandleEntryClicked;
        }
    }

    void HandleDiagnosed(IReadOnlyList<RankedDiagnosis> ranked)
    {
        solutionScreen.SetEntryVisible(ranked.Count > 0 && FindSet() != null);
    }

    void HandleHypothesisClosed()
    {
        solutionScreen.SetEntryVisible(false);
    }

    void HandleEntryClicked()
    {
        SO_Solutions set = FindSet();
        List<RankedDiagnosis> ranked = CurrentDiagnoses();
        if (set == null || ranked.Count == 0)
        {
            return;
        }

        solutionScreen.Open(set, ranked);
    }

    /// <summary>The diagnoses for the cues as the player has them ranked right now.</summary>
    List<RankedDiagnosis> CurrentDiagnoses()
    {
        List<RecognizedCue> rankedCues = new List<RecognizedCue>();
        foreach (string id in hypothesisScreen.RankedCueIds)
        {
            foreach (RecognizedCue cue in patient.RecognizedCues)
            {
                if (cue.Id == id)
                {
                    rankedCues.Add(cue);
                    break;
                }
            }
        }

        return DiagnosisRanker.Rank(rankedCues, patient.Profile != null ? patient.Profile.diagnoses : null);
    }

    SO_Solutions FindSet()
    {
        if (patient == null || patient.Profile == null)
        {
            return null;
        }

        for (int i = 0; solutionSets != null && i < solutionSets.Length; i++)
        {
            if (solutionSets[i] != null && solutionSets[i].patient == patient.Profile)
            {
                return solutionSets[i];
            }
        }

        return null;
    }
}
