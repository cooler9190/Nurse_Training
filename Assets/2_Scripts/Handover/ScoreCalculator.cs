using System;
using System.Collections.Generic;

/// <summary>How well a step went, from the share of its points the player earned.</summary>
public enum ScoreLevel
{
    Beginning,
    Developing,
    Accomplished,
    Exemplary
}

/// <summary>The score for one step of clinical judgment.</summary>
public class StepScore
{
    public string Name;
    public int Points;
    public int MaxPoints;
    public ScoreLevel Level;
    /// <summary>A short plain-language account of how the points were earned.</summary>
    public string Detail;

    public float Fraction => MaxPoints > 0 ? (float)Points / MaxPoints : 0f;
}

/// <summary>The score for a whole hand over: one score per step and an overall percentage.</summary>
public class ScoreBreakdown
{
    public List<StepScore> Steps = new List<StepScore>();
    public int Percent;
    public ScoreLevel Level;
}

/// <summary>
/// Turns a <see cref="HandoverReport"/> into a score. Contains no UI code and reads no assets, so the answer key
/// stays where it is (<see cref="SO_Patient"/>) and the only inputs are the verdicts the evaluator already made.
/// <para>
/// Each step earns 2 points for a right place and 1 for one place off. The step that judges the diagnosis order
/// also loses 1 point for every diagnosis the cues do not support that takes a place belonging to a real one;
/// a step never goes below 0. A step with nothing to judge is left out, so a cue the player never found is
/// only counted once, in the first step. The overall score is the average of the steps.
/// </para>
/// </summary>
public static class ScoreCalculator
{
    const int RightPlacePoints = 2;
    const int OneOffPoints = 1;

    public static ScoreBreakdown Calculate(HandoverReport report)
    {
        ScoreBreakdown score = new ScoreBreakdown();
        AddStep(score, RecogniseCues(report));
        AddStep(score, AnalyseCues(report));
        AddStep(score, PrioritiseDiagnoses(report));

        float total = 0f;
        foreach (StepScore step in score.Steps)
        {
            total += step.Fraction;
        }

        float overall = score.Steps.Count > 0 ? total / score.Steps.Count : 0f;
        score.Percent = (int)Math.Round(overall * 100f, MidpointRounding.AwayFromZero);
        score.Level = LevelFor(overall);
        return score;
    }

    static StepScore RecogniseCues(HandoverReport report)
    {
        int found = 0;
        foreach (CuePriorityRow row in report.Cues)
        {
            if (row.Found)
            {
                found++;
            }
        }

        return new StepScore
        {
            Name = "Recognise cues",
            Points = found,
            MaxPoints = report.Cues.Count,
            Detail = $"Found {found} of {report.Cues.Count} key cues."
        };
    }

    static StepScore AnalyseCues(HandoverReport report)
    {
        int found = 0;
        int points = 0;
        foreach (CuePriorityRow row in report.Cues)
        {
            if (!row.Found)
            {
                continue;
            }

            found++;
            points += PointsFor(row.Verdict);
        }

        return new StepScore
        {
            Name = "Analyse cues",
            Points = points,
            MaxPoints = found * RightPlacePoints,
            Detail = $"{report.CuesCorrect} of {found} cues you found are ranked in the right place."
        };
    }

    static StepScore PrioritiseDiagnoses(HandoverReport report)
    {
        int points = 0;
        foreach (DiagnosisPriorityRow row in report.Diagnoses)
        {
            points += PointsFor(row.Verdict);
        }

        int misplaced = 0;
        foreach (DistractorRow distractor in report.Distractors)
        {
            if (distractor.InSolutionSlot)
            {
                misplaced++;
            }
        }

        string detail = $"{report.DiagnosesCorrect} of {report.Diagnoses.Count} diagnoses are in the right priority.";
        if (misplaced > 0)
        {
            detail += misplaced == 1 ? " An unsupported diagnosis ranked above a real one cost a point." : $" {misplaced} unsupported diagnoses ranked above real ones cost a point each.";
        }

        return new StepScore
        {
            Name = "Prioritise diagnosis",
            Points = Math.Max(0, points - misplaced),
            MaxPoints = report.Diagnoses.Count * RightPlacePoints,
            Detail = detail
        };
    }

    static void AddStep(ScoreBreakdown score, StepScore step)
    {
        if (step.MaxPoints <= 0)
        {
            return;
        }

        step.Level = LevelFor(step.Fraction);
        score.Steps.Add(step);
    }

    static int PointsFor(PriorityVerdict verdict)
    {
        switch (verdict)
        {
            case PriorityVerdict.Correct: return RightPlacePoints;
            case PriorityVerdict.NearlyThere: return OneOffPoints;
            default: return 0;
        }
    }

    static ScoreLevel LevelFor(float fraction)
    {
        if (fraction >= 0.9f) { return ScoreLevel.Exemplary; }
        if (fraction >= 0.75f) { return ScoreLevel.Accomplished; }
        if (fraction >= 0.5f) { return ScoreLevel.Developing; }
        return ScoreLevel.Beginning;
    }
}
