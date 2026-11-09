using System;
using System.Collections.Generic;

/// <summary>
/// Tracks the night shift: the shift clock, which calls are ringing, which are done and what each hand over scored.
/// Contains no UI code and does not touch Unity objects.
/// <para>
/// The clock runs from the start time to the end time over a fixed real-time length, so a 23:00 to 06:00 shift
/// played over 20 minutes advances 21 clock minutes every real minute. Times after midnight count as the next day.
/// A call rings once the clock reaches its time. A call is done once the player hands the patient over; handing the
/// same patient over again replaces the earlier score. When the shift ends, calls that were not done count as 0%
/// in the shift average.
/// </para>
/// </summary>
public class NightShiftProgress
{
    const int MinutesPerDay = 1440;

    readonly int startMinutes;
    readonly int endMinutes;
    readonly float lengthSeconds;
    readonly int[] callMinutes;
    readonly int[] percents;
    readonly ScoreLevel[] levels;
    readonly bool[] done;
    float elapsedSeconds;

    /// <param name="startTime">Clock time the shift starts, as HH:mm.</param>
    /// <param name="endTime">Clock time the shift ends, as HH:mm. It is the next day if it is not later than the start.</param>
    /// <param name="lengthMinutes">How many real minutes the shift lasts.</param>
    /// <param name="callTimes">When each call rings, as HH:mm. A time that cannot be read rings from the start.</param>
    public NightShiftProgress(string startTime, string endTime, float lengthMinutes, IReadOnlyList<string> callTimes)
    {
        startMinutes = ParseMinutes(startTime, 0);
        int end = ParseMinutes(endTime, startMinutes);
        endMinutes = end > startMinutes ? end : end + MinutesPerDay;
        lengthSeconds = Math.Max(1f, lengthMinutes * 60f);

        int count = callTimes != null ? callTimes.Count : 0;
        callMinutes = new int[count];
        for (int i = 0; i < count; i++)
        {
            int minutes = ParseMinutes(callTimes[i], -1);
            callMinutes[i] = minutes < 0 ? startMinutes : (minutes < startMinutes ? minutes + MinutesPerDay : minutes);
        }

        percents = new int[count];
        levels = new ScoreLevel[count];
        done = new bool[count];
    }

    public int CallCount => callMinutes.Length;
    public int DoneCount { get; private set; }
    public bool IsComplete => CallCount > 0 && DoneCount == CallCount;
    /// <summary>True once the clock has reached the end of the shift.</summary>
    public bool IsOver => elapsedSeconds >= lengthSeconds;
    /// <summary>How much of the shift has passed, from 0 to 1.</summary>
    public float Elapsed01 => Math.Min(1f, elapsedSeconds / lengthSeconds);
    /// <summary>The time on the ward clock, as HH:mm.</summary>
    public string Clock => Format(startMinutes + (int)Math.Floor(Elapsed01 * (endMinutes - startMinutes)));
    /// <summary>The clock time in minutes since the shift started, used to see which calls are ringing.</summary>
    int ClockMinutes => startMinutes + (int)Math.Floor(Elapsed01 * (endMinutes - startMinutes));

    /// <summary>Moves the clock on by real time. Does nothing once the shift is over.</summary>
    public void Tick(float seconds)
    {
        if (seconds > 0f && !IsOver)
        {
            elapsedSeconds = Math.Min(lengthSeconds, elapsedSeconds + seconds);
        }
    }

    /// <summary>True once the clock has reached the time of the call.</summary>
    public bool IsRinging(int index) => IsValid(index) && callMinutes[index] <= ClockMinutes;

    /// <summary>How many calls are ringing now.</summary>
    public int RingingCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < CallCount; i++)
            {
                if (IsRinging(i)) { count++; }
            }

            return count;
        }
    }

    /// <summary>A call that was never done and can no longer be, because the shift is over.</summary>
    public bool IsMissed(int index) => IsValid(index) && IsOver && !done[index];

    /// <summary>
    /// Records the score of a hand over for a call. The first hand over is final, so a call cannot be retried for a
    /// better score. Ignores a call that does not exist.
    /// </summary>
    public void Complete(int index, int percent, ScoreLevel level)
    {
        if (!IsValid(index) || done[index])
        {
            return;
        }

        DoneCount++;
        done[index] = true;
        percents[index] = percent;
        levels[index] = level;
    }

    public bool IsDone(int index) => IsValid(index) && done[index];
    public int Percent(int index) => IsValid(index) ? percents[index] : 0;
    public ScoreLevel Level(int index) => IsValid(index) ? levels[index] : ScoreLevel.Beginning;

    /// <summary>The mean score over every call, rounded. A call that was not done counts as 0%.</summary>
    public int AveragePercent
    {
        get
        {
            if (CallCount == 0)
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

            return (int)Math.Round((double)total / CallCount, MidpointRounding.AwayFromZero);
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
        elapsedSeconds = 0f;
    }

    /// <summary>Reads HH:mm into minutes since midnight, or returns the fallback when it cannot be read.</summary>
    public static int ParseMinutes(string time, int fallback)
    {
        if (string.IsNullOrWhiteSpace(time))
        {
            return fallback;
        }

        string[] parts = time.Trim().Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out int hours) || !int.TryParse(parts[1], out int minutes)
            || hours < 0 || hours > 23 || minutes < 0 || minutes > 59)
        {
            return fallback;
        }

        return hours * 60 + minutes;
    }

    static string Format(int minutes)
    {
        int wrapped = ((minutes % MinutesPerDay) + MinutesPerDay) % MinutesPerDay;
        return $"{wrapped / 60:00}:{wrapped % 60:00}";
    }

    bool IsValid(int index) => index >= 0 && index < CallCount;
}
