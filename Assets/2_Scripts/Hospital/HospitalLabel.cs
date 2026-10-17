using UnityEngine;

// Screen-space name tag for a room, drawn by HospitalRoomPicker
public class HospitalLabel : MonoBehaviour
{
    [SerializeField] string text = "Room";
    [SerializeField] Vector2 offset;

    public string Text => text;
    public Vector3 WorldPosition => transform.TransformPoint(offset);
}
