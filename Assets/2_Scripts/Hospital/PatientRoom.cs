using UnityEngine;

// Clickable room. Leave the patient empty for a room that is not in use yet
[RequireComponent(typeof(BoxCollider2D))]
[RequireComponent(typeof(HospitalLabel))]
public class PatientRoom : MonoBehaviour
{
    [SerializeField] SO_Patient patient;
    [Tooltip("Cases for this room, easiest first. Clicking the room starts the first one that is not completed yet. Used instead of Patient when filled in.")]
    [SerializeField] SO_Patient[] cases;
    [SerializeField] SpriteRenderer hoverHighlight;

    HospitalLabel label;

    public bool HasPatient => NextCase() != null;

    // Moves on to the next case once one is completed; when all are done the room replays from the start
    public SO_Patient NextCase()
    {
        SO_Patient first = null;
        for (int i = 0; cases != null && i < cases.Length; i++)
        {
            if (cases[i] == null)
            {
                continue;
            }

            first = first != null ? first : cases[i];
            if (!PatientProgressStore.Get(cases[i]).completed)
            {
                return cases[i];
            }
        }

        return first != null ? first : patient;
    }

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
}
