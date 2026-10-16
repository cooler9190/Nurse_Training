using System.Collections.Generic;
using System.Linq;

/// <summary>
/// A diagnosis the player arrived at, with the priority derived from their cue ranking.
/// </summary>
public readonly struct RankedDiagnosis
{
    public RankedDiagnosis(Diagnosis diagnosis, int priority, IReadOnlyList<string> supportingCueTitles)
    {
        Diagnosis = diagnosis;
        Priority = priority;
        SupportingCueTitles = supportingCueTitles;
    }

    public Diagnosis Diagnosis { get; }
    /// <summary>1 is the most urgent.</summary>
    public int Priority { get; }
    /// <summary>Titles of the ranked cues that support this diagnosis, in ranking order.</summary>
    public IReadOnlyList<string> SupportingCueTitles { get; }
}

/// <summary>
/// Turns the player's cue ranking into a prioritised list of diagnoses. Contains no UI code.
/// <para>
/// A diagnosis is included when at least one of its supporting cues was ranked. Diagnoses are
/// ordered by their best (highest) ranked supporting cue, then by how many of their supporting
/// cues were ranked, then by the order they were authored in the patient asset.
/// </para>
/// </summary>
public static class DiagnosisRanker
{
    /// <param name="rankedCues">Cues in the player's order; index 0 is the most severe/urgent.</param>
    /// <param name="diagnoses">All diagnoses authored for the patient.</param>
    public static List<RankedDiagnosis> Rank(IReadOnlyList<RecognizedCue> rankedCues, IReadOnlyList<Diagnosis> diagnoses)
    {
        List<RankedDiagnosis> results = new List<RankedDiagnosis>();
        if (rankedCues == null || diagnoses == null)
        {
            return results;
        }

        Dictionary<string, int> rankById = new Dictionary<string, int>();
        for (int i = 0; i < rankedCues.Count; i++)
        {
            rankById[rankedCues[i].Id] = i;
        }

        var candidates = new List<(Diagnosis diagnosis, List<int> ranks)>();
        foreach (Diagnosis diagnosis in diagnoses)
        {
            if (diagnosis == null || diagnosis.supportingCueIds == null)
            {
                continue;
            }

            List<int> ranks = diagnosis.supportingCueIds
                .Where(id => !string.IsNullOrEmpty(id) && rankById.ContainsKey(id))
                .Select(id => rankById[id])
                .Distinct()
                .OrderBy(rank => rank)
                .ToList();

            if (ranks.Count > 0)
            {
                candidates.Add((diagnosis, ranks));
            }
        }

        // OrderBy is stable, so ties keep the authoring order.
        var ordered = candidates
            .OrderBy(candidate => candidate.ranks[0])
            .ThenByDescending(candidate => candidate.ranks.Count);

        foreach (var candidate in ordered)
        {
            List<string> titles = candidate.ranks.Select(rank => rankedCues[rank].Title).ToList();
            results.Add(new RankedDiagnosis(candidate.diagnosis, results.Count + 1, titles));
        }

        return results;
    }
}
