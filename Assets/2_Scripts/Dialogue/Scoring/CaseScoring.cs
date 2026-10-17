using System;
using System.Collections.Generic;
using UnityEngine;

public enum CaseDifficulty
{
    Easy,
    Medium,
    Hard,
    Expert
}

// Each category maps to a step of the nursing material the cases are built on
public enum ScoreCategory
{
    InformationGathering,
    Abcde,
    ProblemRecognition,
    ClinicalReasoning,
    Interventions,
    Evaluation,
    Communication,
    Sbar
}

public enum AssessmentKind
{
    SingleChoice,
    MultipleChoice
}

[Serializable]
public class CaseScoringRules
{
    [Tooltip("Life-threatening case: history questions before A, B and C are assessed cost points.")]
    public bool urgent;
    [Tooltip("Bonus for examining the ABCDE letters in order. 0 turns it off.")]
    public int abcdeOrderBonus = 5;
    public int earlyHistoryPenalty = 2;
    [Tooltip("Penalty for putting a distractor diagnosis first on the hypothesis screen.")]
    public int misdiagnosisPenalty = 4;
    [Tooltip("Share of the maximum score needed for one, two and three stars.")]
    public int oneStarPercent = 35;
    public int twoStarPercent = 55;
    public int threeStarPercent = 75;
}

[Serializable]
public class AssessmentOption
{
    public string text;
    [Tooltip("Points when selected. Negative for harmful or wrong choices.")]
    public int points;
    [TextArea(1, 4)]
    public string feedback;
}

// One hand-over question after the interview: diagnosis, interventions, evaluation, SBAR...
[Serializable]
public class AssessmentStep
{
    public string id;
    public ScoreCategory category = ScoreCategory.ClinicalReasoning;
    public AssessmentKind kind;
    [Tooltip("Shown as a small heading, e.g. SBAR or ProActive Nursing.")]
    public string framework;
    [TextArea(2, 10)]
    public string prompt;
    public List<AssessmentOption> options = new List<AssessmentOption>();
    [TextArea(2, 6)]
    public string rationale;

    public int MaxPoints
    {
        get
        {
            int best = 0;
            int sum = 0;
            for (int i = 0; options != null && i < options.Count; i++)
            {
                int points = options[i].points;
                if (points > 0)
                {
                    sum += points;
                    best = Mathf.Max(best, points);
                }
            }

            return kind == AssessmentKind.SingleChoice ? best : sum;
        }
    }

    // Wrong picks can cancel right ones, but a step never scores below zero
    public int Score(IReadOnlyList<int> selected)
    {
        if (selected == null || options == null)
        {
            return 0;
        }

        int total = 0;
        HashSet<int> seen = new HashSet<int>();
        for (int i = 0; i < selected.Count; i++)
        {
            int index = selected[i];
            if (index < 0 || index >= options.Count || !seen.Add(index))
            {
                continue;
            }

            total += options[index].points;
            if (kind == AssessmentKind.SingleChoice)
            {
                break;
            }
        }

        return Mathf.Clamp(total, 0, MaxPoints);
    }
}

public static class ScoreCategoryNames
{
    public static string Display(ScoreCategory category)
    {
        switch (category)
        {
            case ScoreCategory.InformationGathering: return "Information gathering";
            case ScoreCategory.Abcde: return "ABCDE assessment";
            case ScoreCategory.ProblemRecognition: return "Problem recognition";
            case ScoreCategory.ClinicalReasoning: return "Clinical reasoning";
            case ScoreCategory.Interventions: return "Interventions";
            case ScoreCategory.Evaluation: return "Evaluation";
            case ScoreCategory.Communication: return "Culture and communication";
            case ScoreCategory.Sbar: return "SBAR hand-over";
            default: return category.ToString();
        }
    }
}

public class ScoreLine
{
    public ScoreCategory Category;
    public int Earned;
    public int Max;
}

public class CaseResult
{
    public int Earned;
    public int Max;
    public int Percent;
    public int Stars;
    public readonly List<ScoreLine> Lines = new List<ScoreLine>();
    public readonly List<string> Penalties = new List<string>();
}

public static class CaseScorer
{
    const int CategoryCount = 8;

    // Each key scores once, so repeating an action or re-entering a room never double-counts
    public static bool Award(PatientProgress progress, string key, ScoreCategory category, int points, string label)
    {
        if (progress == null || points == 0 || string.IsNullOrEmpty(key))
        {
            return false;
        }

        for (int i = 0; i < progress.scoreEvents.Count; i++)
        {
            if (progress.scoreEvents[i].key == key)
            {
                return false;
            }
        }

        progress.scoreEvents.Add(new ScoreEvent { key = key, category = category, points = points, label = label });
        return true;
    }

    // Live total shown during play: never negative
    public static int RunningTotal(SO_Patient patient, PatientProgress progress)
    {
        return Mathf.Max(0, Evaluate(patient, progress).Earned);
    }

    public static int MaxFor(SO_Patient patient, ScoreCategory category)
    {
        int max = 0;
        switch (category)
        {
            case ScoreCategory.InformationGathering:
                for (int i = 0; patient.questions != null && i < patient.questions.Count; i++)
                {
                    max += Mathf.Max(0, patient.questions[i].points);
                }

                for (int i = 0; patient.visualHotspots != null && i < patient.visualHotspots.Count; i++)
                {
                    max += Mathf.Max(0, patient.visualHotspots[i].points);
                }

                break;
            case ScoreCategory.ClinicalReasoning:
                for (int i = 0; patient.diagnoses != null && i < patient.diagnoses.Count; i++)
                {
                    if (patient.diagnoses[i].correctPriority > 0)
                    {
                        max += Mathf.Max(0, patient.diagnoses[i].points);
                    }
                }

                break;
            case ScoreCategory.Abcde:
                for (int i = 0; patient.abcdeFindings != null && i < patient.abcdeFindings.Count; i++)
                {
                    max += Mathf.Max(0, patient.abcdeFindings[i].points);
                }

                if (patient.scoringRules != null)
                {
                    max += Mathf.Max(0, patient.scoringRules.abcdeOrderBonus);
                }

                break;
        }

        for (int i = 0; patient.assessmentSteps != null && i < patient.assessmentSteps.Count; i++)
        {
            if (patient.assessmentSteps[i].category == category)
            {
                max += patient.assessmentSteps[i].MaxPoints;
            }
        }

        return max;
    }

    // Scores the player's hypothesis ranking against the authored priorities. A diagnosis is judged by its
    // place among the real diagnoses only, so a distractor ranked in between does not push the others down.
    // Only the latest ranking counts, so re-diagnosing after finding more cues replaces the earlier result.
    public static void ScoreDiagnoses(SO_Patient patient, PatientProgress progress, IReadOnlyList<RankedDiagnosis> ranked)
    {
        if (patient.diagnoses == null || progress == null)
        {
            return;
        }

        progress.scoreEvents.RemoveAll(e => e.key.StartsWith("dx:"));
        CaseScoringRules rules = patient.scoringRules ?? new CaseScoringRules();
        int realPosition = 0;
        for (int r = 0; ranked != null && r < ranked.Count; r++)
        {
            Diagnosis diagnosis = FindDiagnosis(patient, ranked[r].Diagnosis.id);
            if (diagnosis == null)
            {
                continue;
            }

            string title = string.IsNullOrEmpty(diagnosis.title) ? diagnosis.id : diagnosis.title;
            if (diagnosis.correctPriority > 0)
            {
                realPosition++;
                int distance = Mathf.Abs(realPosition - diagnosis.correctPriority);
                int points = distance == 0 ? diagnosis.points : distance == 1 ? diagnosis.points / 2 : 0;
                Award(progress, "dx:" + diagnosis.id, ScoreCategory.ClinicalReasoning, points, $"{title} ranked {realPosition} (expected {diagnosis.correctPriority})");
            }
            else if (r == 0)
            {
                Award(progress, "dx:" + diagnosis.id, ScoreCategory.ClinicalReasoning, -rules.misdiagnosisPenalty, $"{title} ranked as the top priority, but the cues do not support it");
            }
        }
    }

    static Diagnosis FindDiagnosis(SO_Patient patient, string id)
    {
        for (int i = 0; i < patient.diagnoses.Count; i++)
        {
            if (patient.diagnoses[i].id == id)
            {
                return patient.diagnoses[i];
            }
        }

        return null;
    }

    public static bool ExaminedInOrder(PatientProgress progress, int minimumLetters)
    {
        if (progress.examOrder.Count < minimumLetters)
        {
            return false;
        }

        for (int i = 1; i < progress.examOrder.Count; i++)
        {
            if ((int)progress.examOrder[i] <= (int)progress.examOrder[i - 1])
            {
                return false;
            }
        }

        return true;
    }

    public static CaseResult Evaluate(SO_Patient patient, PatientProgress progress)
    {
        CaseResult result = new CaseResult();
        int[] earned = new int[CategoryCount];
        CaseScoringRules rules = patient.scoringRules ?? new CaseScoringRules();

        for (int i = 0; i < progress.scoreEvents.Count; i++)
        {
            ScoreEvent scoreEvent = progress.scoreEvents[i];
            earned[(int)scoreEvent.category] += scoreEvent.points;
            if (scoreEvent.points < 0)
            {
                result.Penalties.Add($"{scoreEvent.points}  {scoreEvent.label}");
            }
        }

        if (rules.abcdeOrderBonus > 0 && ExaminedInOrder(progress, 3))
        {
            earned[(int)ScoreCategory.Abcde] += rules.abcdeOrderBonus;
        }

        for (int i = 0; i < progress.stepAnswers.Count; i++)
        {
            StepAnswer answer = progress.stepAnswers[i];
            AssessmentStep step = FindStep(patient, answer.stepId);
            if (step != null)
            {
                earned[(int)step.category] += answer.points;
            }
        }

        for (int c = 0; c < CategoryCount; c++)
        {
            ScoreCategory category = (ScoreCategory)c;
            int max = MaxFor(patient, category);
            if (max <= 0)
            {
                continue;
            }

            int value = Mathf.Clamp(earned[c], 0, max);
            result.Lines.Add(new ScoreLine { Category = category, Earned = value, Max = max });
            result.Earned += value;
            result.Max += max;
        }

        result.Percent = result.Max > 0 ? Mathf.RoundToInt(100f * result.Earned / result.Max) : 0;
        result.Stars = result.Percent >= rules.threeStarPercent ? 3 : result.Percent >= rules.twoStarPercent ? 2 : result.Percent >= rules.oneStarPercent ? 1 : 0;
        return result;
    }

    public static AssessmentStep FindStep(SO_Patient patient, string id)
    {
        for (int i = 0; patient.assessmentSteps != null && i < patient.assessmentSteps.Count; i++)
        {
            if (patient.assessmentSteps[i].id == id)
            {
                return patient.assessmentSteps[i];
            }
        }

        return null;
    }
}
