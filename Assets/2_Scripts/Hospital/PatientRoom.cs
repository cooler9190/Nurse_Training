using UnityEngine;

// Clickable room. Leave the patient empty for a room that is not in use yet
[RequireComponent(typeof(BoxCollider2D))]
[RequireComponent(typeof(HospitalLabel))]
public class PatientRoom : MonoBehaviour
{
    [SerializeField] SO_Patient patient;
    [SerializeField] SpriteRenderer hoverHighlight;

    HospitalLabel label;

    public SO_Patient Patient => patient;
    public bool HasPatient => patient != null;

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
