using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Outcome of comparing the player's diagnoses with the patient's authored solution.
/// </summary>
public class DiagnosisCheckResult
{
    public DiagnosisCheckResult(IReadOnlyList<string> solutionIds, int[] playerPriorities, bool orderCorrect,
        IReadOnlyList<string> missingIds, IReadOnlyList<string> extraIds, IReadOnlyList<string> misplacedExtraIds)
    {
        SolutionIds = solutionIds;
        PlayerPriorities = playerPriorities;
        OrderCorrect = orderCorrect;
        MissingIds = missingIds;
        ExtraIds = extraIds;
        MisplacedExtraIds = misplacedExtraIds;
    }

    /// <summary>Solution diagnosis ids in the correct priority order.</summary>
    public IReadOnlyList<string> SolutionIds { get; }
    /// <summary>The player's priority for each solution diagnosis (same index as <see cref="SolutionIds"/>); 0 when not found.</summary>
    public IReadOnlyList<int> PlayerPriorities { get; }
    /// <summary>True when the player's top diagnoses are exactly the solution, in the same order.</summary>
    public bool OrderCorrect { get; }
    /// <summary>Solution diagnoses that are not among the player's diagnoses.</summary>
    public IReadOnlyList<string> MissingIds { get; }
    /// <summary>Player diagnoses that are not part of the solution (distractors), in the player's order.</summary>
    public IReadOnlyList<string> ExtraIds { get; }
    /// <summary>Distractors that took a priority slot belonging to a solution diagnosis.</summary>
    public IReadOnlyList<string> MisplacedExtraIds { get; }
    public bool AllFound => MissingIds.Count == 0;
    public bool IsCorrect => OrderCorrect;

    public string ToReport()
    {
        StringBuilder report = new StringBuilder();
        report.AppendLine($"Diagnosis check: {(IsCorrect ? "PASS" : "FAIL")} " +
            $"(found {SolutionIds.Count - MissingIds.Count}/{SolutionIds.Count}, order {(OrderCorrect ? "correct" : "wrong")})");

        for (int i = 0; i < SolutionIds.Count; i++)
        {
            int playerPriority = PlayerPriorities[i];
            if (playerPriority == 0)
            {
                report.AppendLine($"  {SolutionIds[i]}: expected #{i + 1}, NOT FOUND");
                continue;
            }

            string status = playerPriority == i + 1 ? "OK" : "WRONG POSITION";
            report.AppendLine($"  {SolutionIds[i]}: expected #{i + 1}, player #{playerPriority} {status}");
        }

        if (MisplacedExtraIds.Count > 0)
        {
            report.AppendLine($"  Distractors ranked within the solution: {string.Join(", ", MisplacedExtraIds)}");
        }

        if (ExtraIds.Count > MisplacedExtraIds.Count)
        {
            report.AppendLine($"  Distractors shown: {string.Join(", ", ExtraIds)}");
        }

        return report.ToString().TrimEnd();
    }
}

/// <summary>
/// Compares the player's prioritised diagnoses with the patient's authored solution. Contains no UI code.
/// <para>
/// The solution order is absolute: the player's first diagnoses must be exactly the solution
/// diagnoses in the authored order. Distractors may still be shown, but only after all of them.
/// </para>
/// </summary>
public static class DiagnosisSolutionChecker
{
    /// <param name="playerResults">The player's diagnoses from <see cref="DiagnosisRanker.Rank"/>, highest priority first.</param>
    /// <param name="solutionIds">Correct diagnosis ids, most urgent first.</param>
    public static DiagnosisCheckResult Check(IReadOnlyList<RankedDiagnosis> playerResults, IReadOnlyList<string> solutionIds)
    {
        IReadOnlyList<string> solution = solutionIds ?? Array.Empty<string>();
        int solutionCount = solution.Count;
        int playerCount = playerResults != null ? playerResults.Count : 0;

        bool orderCorrect = playerCount >= solutionCount;
        for (int i = 0; orderCorrect && i < solutionCount; i++)
        {
            orderCorrect = string.Equals(IdAt(playerResults, i), solution[i], StringComparison.Ordinal);
        }

        Dictionary<string, int> solutionIndexById = new Dictionary<string, int>(solutionCount, StringComparer.Ordinal);
        for (int i = 0; i < solutionCount; i++)
        {
            if (!string.IsNullOrEmpty(solution[i]) && !solutionIndexById.ContainsKey(solution[i]))
            {
                solutionIndexById.Add(solution[i], i);
            }
        }

        int[] playerPriorities = new int[solutionCount];
        List<string> extraIds = new List<string>();
        List<string> misplacedExtraIds = new List<string>();
        for (int i = 0; i < playerCount; i++)
        {
            string id = IdAt(playerResults, i);
            if (id == null)
            {
                continue;
            }

            if (solutionIndexById.TryGetValue(id, out int solutionIndex))
            {
                if (playerPriorities[solutionIndex] == 0)
                {
                    playerPriorities[solutionIndex] = i + 1;
                }
            }
            else
            {
                extraIds.Add(id);
                if (i < solutionCount)
                {
                    misplacedExtraIds.Add(id);
                }
            }
        }

        List<string> missingIds = new List<string>();
        for (int i = 0; i < solutionCount; i++)
        {
            if (playerPriorities[i] == 0)
            {
                missingIds.Add(solution[i]);
            }
        }

        return new DiagnosisCheckResult(solution, playerPriorities, orderCorrect, missingIds, extraIds, misplacedExtraIds);
    }

    static string IdAt(IReadOnlyList<RankedDiagnosis> results, int index)
    {
        return results[index].Diagnosis?.id;
    }
}
