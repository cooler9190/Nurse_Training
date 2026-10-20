using System.Collections.Generic;
using System.Linq;

/// <summary>How one card the player placed on a diagnosis was judged.</summary>
public class PlacedSolution
{
    public PlacedSolution(SolutionOption option, bool right, string note)
    {
        Option = option;
        Right = right;
        Note = note;
    }

    public SolutionOption Option { get; }
    public bool Right { get; }
    /// <summary>Why it was right or wrong.</summary>
    public string Note { get; }
}

/// <summary>The outcome for one diagnosis: what was placed on it and which right solutions were missed.</summary>
public class DiagnosisReport
{
    public DiagnosisReport(RankedDiagnosis diagnosis)
    {
        Diagnosis = diagnosis;
    }

    public RankedDiagnosis Diagnosis { get; }
    public List<PlacedSolution> Placed { get; } = new List<PlacedSolution>();
    /// <summary>Right solutions for this diagnosis that were not placed on it.</summary>
    public List<SolutionOption> Missed { get; } = new List<SolutionOption>();
    public bool Complete => Missed.Count == 0 && Placed.All(p => p.Right);
}

/// <summary>
/// Decides which solutions a player is offered and judges where they placed them. Contains no UI code.
/// <para>
/// A placed card is right only when it is a right action (<see cref="SolutionOption.correct"/>) and it was
/// placed on the diagnosis it belongs to. A right action put on the wrong diagnosis, and any distractor,
/// is wrong. Right solutions that were not placed on their diagnosis are reported as missed.
/// </para>
/// </summary>
public static class SolutionEvaluator
{
    /// <summary>
    /// The cards to offer: every option of every diagnosis the player arrived at, right and wrong mixed in a
    /// fixed pseudo-random order so the distractors are not grouped and the order is the same every time.
    /// </summary>
    public static List<SolutionOption> HandFor(SO_Solutions set, IReadOnlyList<RankedDiagnosis> ranked)
    {
        List<SolutionOption> hand = new List<SolutionOption>();
        if (set == null || ranked == null)
        {
            return hand;
        }

        foreach (RankedDiagnosis diagnosis in ranked)
        {
            DiagnosisSolutions entry = set.Find(diagnosis.Diagnosis.id);
            if (entry?.options != null)
            {
                hand.AddRange(entry.options.Where(o => o != null && !string.IsNullOrEmpty(o.id)));
            }
        }

        return hand.OrderBy(option => StableHash(option.id)).ToList();
    }

    /// <param name="assignments">Option id to the id of the diagnosis it was placed on.</param>
    public static List<DiagnosisReport> Evaluate(SO_Solutions set, IReadOnlyList<RankedDiagnosis> ranked, IReadOnlyDictionary<string, string> assignments)
    {
        List<DiagnosisReport> reports = new List<DiagnosisReport>();
        if (set == null || ranked == null)
        {
            return reports;
        }

        List<SolutionOption> hand = HandFor(set, ranked);
        foreach (RankedDiagnosis diagnosis in ranked)
        {
            DiagnosisReport report = new DiagnosisReport(diagnosis);
            DiagnosisSolutions entry = set.Find(diagnosis.Diagnosis.id);
            if (entry?.options == null)
            {
                reports.Add(report);
                continue;
            }

            foreach (SolutionOption option in hand)
            {
                if (!assignments.TryGetValue(option.id, out string placedOn) || placedOn != diagnosis.Diagnosis.id)
                {
                    continue;
                }

                string owner = set.OwnerOf(option.id);
                if (option.correct && owner == diagnosis.Diagnosis.id)
                {
                    report.Placed.Add(new PlacedSolution(option, true, option.feedback));
                }
                else if (option.correct)
                {
                    report.Placed.Add(new PlacedSolution(option, false, $"A good action, but it belongs to {TitleOf(ranked, owner)}."));
                }
                else
                {
                    report.Placed.Add(new PlacedSolution(option, false, string.IsNullOrWhiteSpace(option.feedback) ? "This is not a suitable action." : option.feedback));
                }
            }

            foreach (SolutionOption option in entry.options)
            {
                bool placedHere = assignments.TryGetValue(option.id, out string placedOn) && placedOn == diagnosis.Diagnosis.id;
                if (option.correct && !placedHere)
                {
                    report.Missed.Add(option);
                }
            }

            reports.Add(report);
        }

        return reports;
    }

    static string TitleOf(IReadOnlyList<RankedDiagnosis> ranked, string diagnosisId)
    {
        foreach (RankedDiagnosis diagnosis in ranked)
        {
            if (diagnosis.Diagnosis.id == diagnosisId)
            {
                return string.IsNullOrEmpty(diagnosis.Diagnosis.title) ? diagnosisId : diagnosis.Diagnosis.title;
            }
        }

        return diagnosisId ?? "another diagnosis";
    }

    // string.GetHashCode is not stable between runs, so the hand order uses its own hash
    static uint StableHash(string text)
    {
        uint hash = 2166136261;
        foreach (char c in text)
        {
            hash = (hash ^ c) * 16777619;
        }

        return hash;
    }
}
