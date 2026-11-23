using System;
using UnityEngine;

/// <summary>
/// Who says a <see cref="DialogueLine"/>, which decides the name shown and the voice used.
/// <para>
/// Nurse lines are the player's own: typed, not voiced. Patient lines use the <see cref="SO_Patient"/>
/// voice. Other lines use the voice of the assigned <see cref="SO_Speaker"/> (e.g. the head nurse).
/// Narrator lines describe what happens: typed, not voiced and without a speaker name.
/// </para>
/// </summary>
public enum DialogueSpeaker
{
    Nurse,
    Patient,
    Narrator,
    Other
}

/// <summary>One line of a scripted conversation, played by <see cref="PatientInterviewController.PlayLines"/>.</summary>
[Serializable]
public class DialogueLine
{
    public DialogueSpeaker speaker;
    [Tooltip("Only used when the speaker is Other, e.g. the head nurse.")]
    public SO_Speaker otherSpeaker;
    [TextArea(1, 4)]
    public string text;

    public DialogueLine()
    {
    }

    public DialogueLine(DialogueSpeaker speaker, string text, SO_Speaker otherSpeaker = null)
    {
        this.speaker = speaker;
        this.text = text;
        this.otherSpeaker = otherSpeaker;
    }

    /// <summary>True when there is something to show.</summary>
    public bool HasText => !string.IsNullOrWhiteSpace(text);
}
