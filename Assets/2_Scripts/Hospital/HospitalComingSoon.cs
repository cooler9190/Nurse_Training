using UnityEngine;

/// <summary>
/// Marks a room that is not in use yet. Put it on a child of the room together with a SpriteRenderer that shades
/// the room; this draws a dark stripe with the text across the middle of that shade, the same way
/// <see cref="HospitalRoomPicker"/> draws the room names. Rooms without a <see cref="PatientRoom"/> are not
/// clickable, so this only changes how they look.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class HospitalComingSoon : MonoBehaviour
{
    [SerializeField] string text = "Coming soon";
    [SerializeField] Camera viewCamera;

    SpriteRenderer shade;
    GUIStyle style;
    int styleHeight;

    void Awake()
    {
        shade = GetComponent<SpriteRenderer>();
        if (viewCamera == null)
        {
            viewCamera = Camera.main;
        }
    }

    // The room is a child of the hospital map, so this only runs while the map is showing
    void OnGUI()
    {
        if (viewCamera == null || shade == null)
        {
            return;
        }

        EnsureStyle();
        Bounds bounds = shade.bounds;
        Vector3 min = viewCamera.WorldToScreenPoint(bounds.min);
        Vector3 max = viewCamera.WorldToScreenPoint(bounds.max);
        float width = Mathf.Abs(max.x - min.x);
        float centerX = (min.x + max.x) * 0.5f;
        float centerY = Screen.height - ((min.y + max.y) * 0.5f);
        float height = style.fontSize * 2.4f;
        Rect stripe = new Rect(centerX - (width * 0.5f), centerY - (height * 0.5f), width, height);

        GUI.color = new Color(0f, 0f, 0f, 0.88f);
        GUI.DrawTexture(stripe, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(stripe, text, style);
    }

    void EnsureStyle()
    {
        int fontSize = Mathf.Max(14, Screen.height / 32);
        if (style != null && styleHeight == fontSize)
        {
            return;
        }

        styleHeight = fontSize;
        style = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = false
        };
        style.normal.textColor = new Color(0.85f, 0.9f, 0.93f, 1f);
    }
}
