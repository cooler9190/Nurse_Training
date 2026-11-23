using FMODUnity;
using UnityEngine;

/// <summary>
/// A character other than the patient and the player who can speak in a conversation, such as the
/// head nurse. Referenced by <see cref="DialogueLine.otherSpeaker"/>. Leave the dialogue event empty
/// for a character whose voice has not been made yet: their lines are still typed, just silent.
/// </summary>
[CreateAssetMenu(fileName = "SO_Speaker", menuName = "Scriptable Objects/Speaker")]
public class SO_Speaker : ScriptableObject
{
    public string displayName = "Speaker";
    /// <summary>FMOD event played while this speaker's line is being typed out.</summary>
    public EventReference dialogueEvent;
    [Tooltip("Pitch multiplier for this speaker's voice. 1 is unchanged, below 1 is deeper, above 1 is higher.")]
    [Range(0.5f, 2f)]
    public float dialoguePitch = 1f;
    [Tooltip("Seconds between each typed character. Lower is faster.")]
    [Range(0.005f, 0.1f)]
    public float textSpeed = 0.02f;
    [Tooltip("Play a voice blip every N typed characters.")]
    [Min(1)]
    public int charactersPerVoiceBlip = 3;
}
