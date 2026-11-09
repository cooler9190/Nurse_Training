using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One night shift: the clock time it starts and the bell calls that come in, in the order they ring. Each call
/// sends the player to one patient. Create one via <c>Assets &gt; Create &gt; Scriptable Objects &gt; Night Shift</c>.
/// <para>
/// Kept separate from <see cref="SO_Patient"/> so the same patient can be used in other levels and the patient
/// data stays untouched. <see cref="NightShiftFlow"/> plays the shift.
/// </para>
/// </summary>
[CreateAssetMenu(fileName = "SO_NightShift", menuName = "Scriptable Objects/Night Shift")]
public class SO_NightShift : ScriptableObject
{
    public string shiftName = "Night shift";
    [Tooltip("Clock time when the shift starts, e.g. 23:00.")]
    public string startTime = "23:00";
    [Tooltip("Clock time when the shift ends, e.g. 06:00. It is the next day when it is not later than the start.")]
    public string endTime = "06:00";
    [Tooltip("How many real minutes the whole shift lasts. The clock runs from the start to the end time over this long.")]
    public float lengthMinutes = 20f;
    [Tooltip("The bell calls in the order they ring.")]
    public List<NightCall> calls = new List<NightCall>();

#if UNITY_EDITOR
    void OnValidate()
    {
        HashSet<SO_Patient> seen = new HashSet<SO_Patient>();
        for (int i = 0; calls != null && i < calls.Count; i++)
        {
            if (calls[i] == null || calls[i].patient == null)
            {
                Debug.LogWarning($"{name}: call {i + 1} has no patient.", this);
            }
            else if (!seen.Add(calls[i].patient))
            {
                Debug.LogWarning($"{name}: {calls[i].patient.name} is called more than once, so only the first call can be completed.", this);
            }
        }
    }
#endif
}

/// <summary>One bell call: when it rings, which bed, what the nurse is told, and who is waiting.</summary>
[Serializable]
public class NightCall
{
    [Tooltip("Clock time the call starts ringing, e.g. 02:10. The player can attend it from then on.")]
    public string time;
    public string bed;
    [TextArea(1, 3)]
    [Tooltip("What the nurse hears when the bell rings. Keep it to what a person on the ward could notice, not the diagnosis.")]
    public string hint;
    public SO_Patient patient;
}
