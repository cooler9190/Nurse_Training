using System;
using UnityEngine;

/// <summary>
/// Clickable room on the hospital map. Leave the patient empty for a room that is not in use yet.
/// <para>
/// Every room can call the nurse: <see cref="StartCall"/> makes the room blink on the map until
/// <see cref="StopCall"/>, so any part of the game can ring a room whenever it wants (the night shift does it at a
/// clock time). A call can bring its own patient, which replaces the room's patient until the call stops. With
/// <see cref="RequiresCall"/> on, the room can only be entered while it is calling.
/// </para>
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
[RequireComponent(typeof(HospitalLabel))]
public class PatientRoom : MonoBehaviour
{
    [SerializeField] SO_Patient patient;
    [SerializeField] SpriteRenderer hoverHighlight;
    [Tooltip("On: the room can only be entered while it is calling the nurse.")]
    [SerializeField] bool requiresCall;
    [Tooltip("How many times the room blinks each second while it is calling.")]
    [SerializeField] float blinksPerSecond = 1.5f;
    [SerializeField] Color callColor = new Color(1f, 0.25f, 0.2f, 1f);

    HospitalLabel label;
    SpriteRenderer callGlow;
    SO_Patient callPatient;
    bool calling;

    /// <summary>Raised when the room starts or stops calling.</summary>
    public event Action<PatientRoom> OnCallChanged;

    /// <summary>Who is in the room now: the patient of the current call, otherwise the room's own patient.</summary>
    public SO_Patient Patient => callPatient != null ? callPatient : patient;
    /// <summary>The patient set on the room itself, whatever call is going on.</summary>
    public SO_Patient AssignedPatient => patient;
    public bool HasPatient => Patient != null;
    public bool IsCalling => calling;
    public bool RequiresCall => requiresCall;
    public Color CallColor => callColor;

    /// <summary>0 to 1, rising and falling while the room is calling. 0 when it is not.</summary>
    public float CallPulse => calling ? 0.5f + (0.5f * Mathf.Sin(Time.unscaledTime * blinksPerSecond * Mathf.PI * 2f)) : 0f;

    public string RoomName
    {
        get
        {
            if (label == null)
            {
                label = GetComponent<HospitalLabel>();
            }

            return label != null ? label.Text : name;
        }
    }

    public void SetHovered(bool hovered)
    {
        if (hoverHighlight != null)
        {
            hoverHighlight.enabled = hovered;
        }
    }

    /// <summary>Makes the room blink. <paramref name="callingPatient"/> is who is waiting; leave it empty to use the room's own patient.</summary>
    public void StartCall(SO_Patient callingPatient = null)
    {
        callPatient = callingPatient;
        if (calling)
        {
            return;
        }

        calling = true;
        EnsureGlow();
        if (callGlow != null)
        {
            callGlow.enabled = true;
        }

        OnCallChanged?.Invoke(this);
    }

    /// <summary>Stops the blinking and goes back to the room's own patient.</summary>
    public void StopCall()
    {
        callPatient = null;
        if (!calling)
        {
            return;
        }

        calling = false;
        if (callGlow != null)
        {
            callGlow.enabled = false;
        }

        OnCallChanged?.Invoke(this);
    }

    void Update()
    {
        if (!calling || callGlow == null)
        {
            return;
        }

        Color color = callColor;
        color.a = 0.08f + (0.3f * CallPulse);
        callGlow.color = color;
    }

    // The glow is a copy of the hover highlight, so it covers the room the same way and needs no scene setup
    void EnsureGlow()
    {
        if (callGlow != null)
        {
            return;
        }

        if (hoverHighlight != null)
        {
            callGlow = Instantiate(hoverHighlight, hoverHighlight.transform.parent);
            callGlow.name = "CallGlow";
            callGlow.sortingOrder = hoverHighlight.sortingOrder - 1;
            return;
        }

        GameObject glow = new GameObject("CallGlow");
        glow.transform.SetParent(transform, false);
        BoxCollider2D box = GetComponent<BoxCollider2D>();
        glow.transform.localPosition = box.offset;
        glow.transform.localScale = new Vector3(box.size.x, box.size.y, 1f);
        callGlow = glow.AddComponent<SpriteRenderer>();
        Texture2D white = Texture2D.whiteTexture;
        callGlow.sprite = Sprite.Create(white, new Rect(0f, 0f, white.width, white.height), new Vector2(0.5f, 0.5f), white.width);
        callGlow.sortingOrder = 1499;
    }
}
