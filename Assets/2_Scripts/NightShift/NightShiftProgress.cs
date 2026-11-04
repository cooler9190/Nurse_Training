using System;
using System.Collections.Generic;

/// <summary>
/// Tracks how far the player is through a night shift: which calls are done and what each hand over scored, plus
/// the shift clock. Contains no UI code and does not touch Unity objects.
/// <para>
/// A call counts as done once the player hands the patient over. Handing the same patient over again replaces the
/// earlier score, so a retry is not stuck with the first attempt. The clock shows the shift start until a call is
/// attended, then the time of that call.
/// </para>
/// </summary>
public class NightShiftProgress
{
    readonly string startTime;
    readonly List<string> callTimes;
    readonly int[] percents;
    readonly ScoreLevel[] levels;
    readonly bool[] done;

    public NightShiftProgress(string startTime, IReadOnlyList<string> callTimes)
    {
        this.startTime = startTime ?? string.Empty;
        this.callTimes = new List<string>(callTimes ?? Array.Empty<string>());
        percents = new int[this.callTimes.Count];
        levels = new ScoreLevel[this.callTimes.Count];
        done = new bool[this.callTimes.Count];
        Clock = this.startTime;
    }

    public int CallCount => callTimes.Count;
    /// <summary>The time on the ward clock.</summary>
    public string Clock { get; private set; }
    public int DoneCount { get; private set; }
    public bool IsComplete => CallCount > 0 && DoneCount == CallCount;

    /// <summary>Moves the clock to the time of the call the player is going to.</summary>
    public void Attend(int index)
    {
        if (IsValid(index))
        {
            Clock = callTimes[index];
        }
    }

    /// <summary>Records the score of a hand over for a call. Ignores a call that does not exist.</summary>
    public void Complete(int index, int percent, ScoreLevel level)
    {
        if (!IsValid(index))
        {
            return;
        }

        if (!done[index])
        {
            DoneCount++;
        }

        done[index] = true;
        percents[index] = percent;
        levels[index] = level;
    }

    public bool IsDone(int index) => IsValid(index) && done[index];
    public int Percent(int index) => IsValid(index) ? percents[index] : 0;
    public ScoreLevel Level(int index) => IsValid(index) ? levels[index] : ScoreLevel.Beginning;

    /// <summary>The mean score of the calls done so far, rounded; 0 when none are done.</summary>
    public int AveragePercent
    {
        get
        {
            if (DoneCount == 0)
            {
                return 0;
            }

            int total = 0;
            for (int i = 0; i < CallCount; i++)
            {
                if (done[i])
                {
                    total += percents[i];
                }
            }

            return (int)Math.Round((double)total / DoneCount, MidpointRounding.AwayFromZero);
        }
    }

    /// <summary>The shift's level, from the average using the same thresholds as a single hand over.</summary>
    public ScoreLevel AverageLevel => ScoreCalculator.LevelFor(AveragePercent / 100f);

    /// <summary>Clears every call and puts the clock back to the start.</summary>
    public void Reset()
    {
        Array.Clear(percents, 0, percents.Length);
        Array.Clear(levels, 0, levels.Length);
        Array.Clear(done, 0, done.Length);
        DoneCount = 0;
        Clock = startTime;
    }

    bool IsValid(int index) => index >= 0 && index < CallCount;
}
