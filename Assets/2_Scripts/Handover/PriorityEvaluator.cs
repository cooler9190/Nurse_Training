using System.Collections.Generic;
using System.Linq;

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
    /// <summary>Where it belongs among the real diagnoses (1 is most urgent).</summary>
    public int Expected;
    /// <summary>Where the player put it among the real diagnoses; 0 when it does not appear on their list.</summary>
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

/// <summary>A diagnosis on the player's list that the cues do not support.</summary>
public class DistractorRow
{
    public string Title;
    public bool RankedFirst;
}

/// <summary>Everything the hand over screen shows.</summary>
public class HandoverReport
{
    public bool NothingRanked;
    public List<DiagnosisPriorityRow> Diagnoses = new List<DiagnosisPriorityRow>();
    public List<CuePriorityRow> Cues = new List<CuePriorityRow>();
    public List<DistractorRow> Distractors = new List<DistractorRow>();
    public int DiagnosesCorrect;
    public int CuesCorrect;
    public int CuesRanked;
    public string Summary;
    public string Explanation;
}

/// <summary>
/// Compares the player's prioritisation with the patient's correct one. Contains no UI code.
/// <para>
/// Diagnoses are judged by their place among the real diagnoses only, so a distractor sitting in between
/// does not push the others down. Cues are judged by their order relative to the other cues the player ranked,
/// so a cue the player never found does not make the rest look wrong; those are listed as missing instead.
/// A place one step away is "nearly there".
/// </para>
/// </summary>
public static class PriorityEvaluator
{
    public static HandoverReport Evaluate(SO_Priorities set, IReadOnlyList<RecognizedCue> collected, IReadOnlyList<string> rankedIds, IReadOnlyList<Diagnosis> diagnoses)
    {
        HandoverReport report = new HandoverReport { Explanation = set.prioritiesSummary };
        Dictionary<string, RecognizedCue> collectedById = new Dictionary<string, RecognizedCue>();
        foreach (RecognizedCue cue in collected)
        {
            collectedById[cue.Id] = cue;
        }

        // the cues the player ranked, in their order, that actually exist
        List<string> playerCues = rankedIds.Where(id => collectedById.ContainsKey(id)).Distinct().ToList();
        report.NothingRanked = playerCues.Count == 0;

        BuildDiagnosisRows(report, set, playerCues.Select(id => collectedById[id]).ToList(), diagnoses);
        BuildCueRows(report, set, collectedById, playerCues);
        report.Summary = BuildSummary(report, set);
        return report;
    }

    static void BuildDiagnosisRows(HandoverReport report, SO_Priorities set, List<RecognizedCue> rankedCues, IReadOnlyList<Diagnosis> diagnoses)
    {
        List<RankedDiagnosis> player = DiagnosisRanker.Rank(rankedCues, diagnoses);
        HashSet<string> expectedIds = new HashSet<string>(set.correctDiagnosisOrder);
        List<RankedDiagnosis> realOnPlayerList = player.Where(d => expectedIds.Contains(d.Diagnosis.id)).ToList();

        for (int i = 0; i < set.correctDiagnosisOrder.Count; i++)
        {
            string id = set.correctDiagnosisOrder[i];
            int index = realOnPlayerList.FindIndex(d => d.Diagnosis.id == id);
            Diagnosis diagnosis = diagnoses?.FirstOrDefault(d => d.id == id);
            report.Diagnoses.Add(new DiagnosisPriorityRow
            {
                DiagnosisId = id,
                Title = diagnosis != null && !string.IsNullOrEmpty(diagnosis.title) ? diagnosis.title : set.DiagnosisNote(id)?.title ?? id,
                Expected = i + 1,
                Yours = index + 1,
                Verdict = Judge(i + 1, index + 1),
                Reason = set.DiagnosisNote(id)?.reason
            });
        }

        report.DiagnosesCorrect = report.Diagnoses.Count(row => row.Verdict == PriorityVerdict.Correct);
        for (int i = 0; i < player.Count; i++)
        {
            if (!expectedIds.Contains(player[i].Diagnosis.id))
            {
                report.Distractors.Add(new DistractorRow { Title = player[i].Diagnosis.title ?? player[i].Diagnosis.id, RankedFirst = i == 0 });
            }
        }
    }

    static void BuildCueRows(HandoverReport report, SO_Priorities set, Dictionary<string, RecognizedCue> collectedById, List<string> playerCues)
    {
        // relative order among the expected cues the player actually ranked
        List<string> expectedRanked = set.correctCueOrder.Where(id => playerCues.Contains(id)).ToList();
        List<string> playerOrder = playerCues.Where(id => set.correctCueOrder.Contains(id)).ToList();
        report.CuesRanked = expectedRanked.Count;

        foreach (string id in set.correctCueOrder)
        {
            bool found = collectedById.ContainsKey(id);
            int expected = expectedRanked.IndexOf(id) + 1;
            int yours = playerOrder.IndexOf(id) + 1;
            string title = found && !string.IsNullOrEmpty(collectedById[id].Title) ? collectedById[id].Title : set.CueNote(id)?.title ?? id;
            report.Cues.Add(new CuePriorityRow
            {
                CueId = id,
                Title = title,
                Found = found,
                Expected = expected,
                Yours = yours,
                Verdict = expected == 0 ? PriorityVerdict.Missing : Judge(expected, yours),
                Reason = set.CueNote(id)?.reason
            });
        }

        report.CuesCorrect = report.Cues.Count(row => row.Verdict == PriorityVerdict.Correct);
    }

    static PriorityVerdict Judge(int expected, int yours)
    {
        if (yours <= 0)
        {
            return PriorityVerdict.Missing;
        }

        int distance = System.Math.Abs(expected - yours);
        return distance == 0 ? PriorityVerdict.Correct : distance == 1 ? PriorityVerdict.NearlyThere : PriorityVerdict.Wrong;
    }

    static string BuildSummary(HandoverReport report, SO_Priorities set)
    {
        if (report.NothingRanked)
        {
            return "You have not ranked any cues yet. Open Cues, rank them by urgency and press Diagnose, then hand over.";
        }

        string diagnoses = $"{report.DiagnosesCorrect} of {report.Diagnoses.Count} diagnoses are in the right priority";
        string cues = $"{report.CuesCorrect} of {report.CuesRanked} ranked cues are in the right place";
        bool firstIsDistractor = report.Distractors.Any(d => d.RankedFirst);
        if (firstIsDistractor)
        {
            return $"Your top priority is not supported by the cues. {diagnoses}; {cues}.";
        }

        if (report.DiagnosesCorrect == report.Diagnoses.Count && report.CuesCorrect == report.CuesRanked)
        {
            return $"Your priorities match the expected order. {diagnoses}; {cues}.";
        }

        return $"{diagnoses}; {cues}.";
    }
}
