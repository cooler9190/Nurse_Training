using System;
using System.Collections.Generic;

/// <summary>How a diagnosis or cue was placed compared with the correct order.</summary>
public enum PriorityVerdict
{
    /// <summary>In the right place.</summary>
    Correct,
    /// <summary>One place away from the right place.</summary>
    NearlyThere,
    /// <summary>Two or more places away.</summary>
    Wrong,
    /// <summary>Not ranked at all, or never found.</summary>
    Missing
}

/// <summary>One line of the diagnosis comparison.</summary>
public class DiagnosisPriorityRow
{
    public string DiagnosisId;
    public string Title;
    /// <summary>Where it belongs on the player's list (1 is most urgent).</summary>
    public int Expected;
    /// <summary>Where the player's list has it, distractors included; 0 when it does not appear.</summary>
    public int Yours;
    public PriorityVerdict Verdict;
    public string Reason;
}

/// <summary>One line of the cue comparison.</summary>
public class CuePriorityRow
{
    public string CueId;
    public string Title;
    /// <summary>Where it belongs among the cues the player ranked (1 is most urgent).</summary>
    public int Expected;
    /// <summary>Where the player put it among those cues; 0 when it was not ranked.</summary>
    public int Yours;
    /// <summary>False when the player never found the cue.</summary>
    public bool Found;
    public PriorityVerdict Verdict;
    public string Reason;
}

/// <summary>A diagnosis on the player's list that is not part of the solution.</summary>
public class DistractorRow
{
    public string Title;
    public bool RankedFirst;
    /// <summary>True when it takes a place that belongs to a solution diagnosis.</summary>
    public bool InSolutionSlot;
}

/// <summary>Everything the hand over screen shows.</summary>
public class HandoverReport
{
    public bool NothingRanked;
    public List<DiagnosisPriorityRow> Diagnoses = new List<DiagnosisPriorityRow>();
    public List<CuePriorityRow> Cues = new List<CuePriorityRow>();
    public List<DistractorRow> Distractors = new List<DistractorRow>();
    public int DiagnosesCorrect;
    /// <summary>True when the player's top diagnoses are exactly the solution, in order.</summary>
    public bool DiagnosisOrderCorrect;
    public int CuesCorrect;
    public int CuesRanked;
    public string Summary;
    public string Explanation;
}

/// <summary>
/// Compares the player's prioritisation with the correct one authored on the <see cref="SO_Patient"/>.
/// Contains no UI code.
/// <para>
/// Diagnoses are judged by their absolute place on the player's list: the first places must be exactly
/// <see cref="SO_Patient.solutionDiagnosisIds"/> in order, so a distractor ranked above a real diagnosis
/// pushes it down. Distractors may still appear, but only after the solution.
/// </para>
/// <para>
/// Cues are judged by their order relative to the other cues the player ranked, so a cue the player never
/// found does not make the rest look wrong; those are listed as missing instead.
/// </para>
/// A place one step away is "nearly there".
/// </summary>
public static class PriorityEvaluator
{
    /// <param name="collected">Every cue the player has found.</param>
    /// <param name="rankedIds">Cue ids in the player's ranking order, most urgent first.</param>
    public static HandoverReport Evaluate(SO_Patient patient, IReadOnlyList<RecognizedCue> collected, IReadOnlyList<string> rankedIds)
    {
        HandoverReport report = new HandoverReport { Explanation = patient.prioritiesSummary };

        Dictionary<string, RecognizedCue> collectedById = new Dictionary<string, RecognizedCue>(StringComparer.Ordinal);
        for (int i = 0; collected != null && i < collected.Count; i++)
        {
            if (!string.IsNullOrEmpty(collected[i].Id))
            {
                collectedById[collected[i].Id] = collected[i];
            }
        }

        // The cues the player ranked, in their order, that actually exist
        List<RecognizedCue> playerCues = new List<RecognizedCue>();
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; rankedIds != null && i < rankedIds.Count; i++)
        {
            string id = rankedIds[i];
            if (id != null && collectedById.TryGetValue(id, out RecognizedCue cue) && seen.Add(id))
            {
                playerCues.Add(cue);
            }
        }

        report.NothingRanked = playerCues.Count == 0;
        BuildDiagnosisRows(report, patient, playerCues);
        BuildCueRows(report, patient, collectedById, playerCues);
        report.Summary = BuildSummary(report);
        return report;
    }

    static void BuildDiagnosisRows(HandoverReport report, SO_Patient patient, List<RecognizedCue> rankedCues)
    {
        List<RankedDiagnosis> player = DiagnosisRanker.Rank(rankedCues, patient.diagnoses);
        IReadOnlyList<string> order = patient.solutionDiagnosisIds ?? (IReadOnlyList<string>)Array.Empty<string>();
        int solutionCount = order.Count;

        Dictionary<string, int> solutionIndexById = new Dictionary<string, int>(solutionCount, StringComparer.Ordinal);
        for (int i = 0; i < solutionCount; i++)
        {
            if (!string.IsNullOrEmpty(order[i]) && !solutionIndexById.ContainsKey(order[i]))
            {
                solutionIndexById.Add(order[i], i);
            }
        }

        int[] yours = new int[solutionCount];
        for (int i = 0; i < player.Count; i++)
        {
            Diagnosis diagnosis = player[i].Diagnosis;
            if (diagnosis.id != null && solutionIndexById.TryGetValue(diagnosis.id, out int solutionIndex))
            {
                if (yours[solutionIndex] == 0)
                {
                    yours[solutionIndex] = i + 1;
                }

                continue;
            }

            report.Distractors.Add(new DistractorRow
            {
                Title = string.IsNullOrEmpty(diagnosis.title) ? diagnosis.id : diagnosis.title,
                RankedFirst = i == 0,
                InSolutionSlot = i < solutionCount
            });
        }

        Dictionary<string, Diagnosis> diagnosisById = new Dictionary<string, Diagnosis>(StringComparer.Ordinal);
        for (int i = 0; patient.diagnoses != null && i < patient.diagnoses.Count; i++)
        {
            Diagnosis diagnosis = patient.diagnoses[i];
            if (diagnosis != null && !string.IsNullOrEmpty(diagnosis.id) && !diagnosisById.ContainsKey(diagnosis.id))
            {
                diagnosisById.Add(diagnosis.id, diagnosis);
            }
        }

        for (int i = 0; i < solutionCount; i++)
        {
            string id = order[i];
            Diagnosis diagnosis = id != null && diagnosisById.TryGetValue(id, out Diagnosis found) ? found : null;
            PriorityVerdict verdict = Judge(i + 1, yours[i]);
            if (verdict == PriorityVerdict.Correct)
            {
                report.DiagnosesCorrect++;
            }

            report.Diagnoses.Add(new DiagnosisPriorityRow
            {
                DiagnosisId = id,
                Title = diagnosis != null && !string.IsNullOrEmpty(diagnosis.title) ? diagnosis.title : id,
                Expected = i + 1,
                Yours = yours[i],
                Verdict = verdict,
                Reason = diagnosis?.priorityReason
            });
        }

        report.DiagnosisOrderCorrect = report.DiagnosesCorrect == solutionCount;
    }

    static void BuildCueRows(HandoverReport report, SO_Patient patient, Dictionary<string, RecognizedCue> collectedById, List<RecognizedCue> playerCues)
    {
        List<CuePriority> correctOrder = patient.correctCueOrder;
        if (correctOrder == null)
        {
            return;
        }

        HashSet<string> expectedIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < correctOrder.Count; i++)
        {
            if (correctOrder[i] != null && !string.IsNullOrEmpty(correctOrder[i].id))
            {
                expectedIds.Add(correctOrder[i].id);
            }
        }

        // The player's place for each expected cue, counted among the expected cues they ranked
        Dictionary<string, int> yoursById = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < playerCues.Count; i++)
        {
            if (expectedIds.Contains(playerCues[i].Id))
            {
                yoursById[playerCues[i].Id] = yoursById.Count + 1;
            }
        }

        HashSet<string> listed = new HashSet<string>(StringComparer.Ordinal);
        int expectedPlace = 0;
        for (int i = 0; i < correctOrder.Count; i++)
        {
            CuePriority entry = correctOrder[i];
            if (entry == null || string.IsNullOrEmpty(entry.id) || !listed.Add(entry.id))
            {
                continue;
            }

            string id = entry.id;
            bool found = collectedById.TryGetValue(id, out RecognizedCue cue);
            int yours = yoursById.TryGetValue(id, out int place) ? place : 0;
            int expected = yours > 0 ? ++expectedPlace : 0;
            PriorityVerdict verdict = Judge(expected, yours);
            if (verdict == PriorityVerdict.Correct)
            {
                report.CuesCorrect++;
            }

            report.Cues.Add(new CuePriorityRow
            {
                CueId = id,
                Title = found && !string.IsNullOrEmpty(cue.Title) ? cue.Title : patient.FindCueTitle(id) ?? id,
                Found = found,
                Expected = expected,
                Yours = yours,
                Verdict = verdict,
                Reason = entry.reason
            });
        }

        report.CuesRanked = expectedPlace;
    }

    static PriorityVerdict Judge(int expected, int yours)
    {
        if (expected <= 0 || yours <= 0)
        {
            return PriorityVerdict.Missing;
        }

        int distance = Math.Abs(expected - yours);
        return distance == 0 ? PriorityVerdict.Correct : distance == 1 ? PriorityVerdict.NearlyThere : PriorityVerdict.Wrong;
    }

    static string BuildSummary(HandoverReport report)
    {
        if (report.NothingRanked)
        {
            return "You have not ranked any cues yet. Open Cues, rank them by urgency and press Diagnose, then hand over.";
        }

        string diagnoses = $"{report.DiagnosesCorrect} of {report.Diagnoses.Count} diagnoses are in the right priority";
        string cues = $"{report.CuesCorrect} of {report.CuesRanked} ranked cues are in the right place";

        bool distractorFirst = false;
        bool distractorInSolution = false;
        foreach (DistractorRow distractor in report.Distractors)
        {
            distractorFirst |= distractor.RankedFirst;
            distractorInSolution |= distractor.InSolutionSlot;
        }

        if (distractorFirst)
        {
            return $"Your top priority is not supported by the cues. {diagnoses}; {cues}.";
        }

        if (distractorInSolution)
        {
            return $"A diagnosis the cues do not support is ranked above a real one. {diagnoses}; {cues}.";
        }

        if (report.DiagnosisOrderCorrect && report.CuesCorrect == report.CuesRanked)
        {
            return $"Your priorities match the expected order. {diagnoses}; {cues}.";
        }

        return $"{diagnoses}; {cues}.";
    }
}
