using UnityEngine;

// Hospital map controller: hover/click a patient room to open its examination, return to the map when it ends
public class HospitalRoomPicker : MonoBehaviour
{
    [SerializeField] Camera viewCamera;
    [SerializeField] GameObject hospitalRoot;
    [SerializeField] Patient patient;
    [SerializeField] PatientInterviewController interviewController;
    [SerializeField] Vector2 worldCenter;
    [SerializeField] Vector2 worldSize = new Vector2(36f, 28f);
    [SerializeField] float noticeSeconds = 2.5f;

    HospitalLabel[] labels = new HospitalLabel[0];
    PatientRoom hovered;
    string notice;
    float noticeUntil;
    int lastScreenWidth;
    int lastScreenHeight;
    GUIStyle labelStyle;
    GUIStyle hintStyle;

    void Awake()
    {
        if (viewCamera == null)
        {
            viewCamera = Camera.main;
        }

        if (hospitalRoot != null)
        {
            labels = hospitalRoot.GetComponentsInChildren<HospitalLabel>(true);
        }
    }

    void OnEnable()
    {
        if (interviewController != null)
        {
            interviewController.OnInterviewEnded += HandleInterviewEnded;
        }
    }

    void OnDisable()
    {
        if (interviewController != null)
        {
            interviewController.OnInterviewEnded -= HandleInterviewEnded;
        }
    }

    void Update()
    {
        if (viewCamera == null)
        {
            return;
        }

        if (Screen.width != lastScreenWidth || Screen.height != lastScreenHeight)
        {
            FitCamera();
        }

        if (interviewController != null && interviewController.IsPlaying)
        {
            SetHovered(null);
            return;
        }

        if (!TryReadPointer(out Vector2 screenPosition, out bool pressed))
        {
            return;
        }

        Vector3 world = viewCamera.ScreenToWorldPoint(screenPosition);
        Collider2D hit = Physics2D.OverlapPoint(world);
        PatientRoom room = hit != null ? hit.GetComponentInParent<PatientRoom>() : null;
        SetHovered(room);

        if (pressed && room != null)
        {
            Select(room);
        }
    }

    void Select(PatientRoom room)
    {
        GameAudio.PlayClick();
        if (!room.HasPatient)
        {
            ShowNotice($"{room.RoomName}: no patient assigned yet.");
            return;
        }

        if (patient == null)
        {
            Debug.LogError("HospitalRoomPicker: Patient is not assigned.", this);
            return;
        }

        SetHovered(null);
        hospitalRoot.SetActive(false);
        patient.Begin(room.Patient);

        // Begin can fail (missing asset/UI), so don't leave the player on an empty screen
        if (interviewController != null && !interviewController.IsPlaying)
        {
            hospitalRoot.SetActive(true);
        }
    }

    void HandleInterviewEnded()
    {
        if (hospitalRoot != null)
        {
            hospitalRoot.SetActive(true);
        }
    }

    void SetHovered(PatientRoom room)
    {
        if (hovered == room)
        {
            return;
        }

        if (hovered != null)
        {
            hovered.SetHovered(false);
        }

        hovered = room;
        if (hovered != null)
        {
            hovered.SetHovered(true);
        }
    }

    void ShowNotice(string message)
    {
        notice = message;
        noticeUntil = Time.unscaledTime + noticeSeconds;
    }

    // Fit the whole hospital in view whatever the window shape
    void FitCamera()
    {
        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;
        float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
        viewCamera.orthographic = true;
        viewCamera.orthographicSize = Mathf.Max(worldSize.y * 0.5f, worldSize.x * 0.5f / aspect);
        viewCamera.transform.position = new Vector3(worldCenter.x, worldCenter.y, -10f);
    }

    static bool TryReadPointer(out Vector2 screenPosition, out bool pressedThisFrame)
    {
#if ENABLE_INPUT_SYSTEM
        UnityEngine.InputSystem.Pointer pointer = UnityEngine.InputSystem.Pointer.current;
        if (pointer == null)
        {
            screenPosition = default;
            pressedThisFrame = false;
            return false;
        }

        screenPosition = pointer.position.ReadValue();
        pressedThisFrame = pointer.press.wasPressedThisFrame;
        return true;
#else
        screenPosition = Input.mousePosition;
        pressedThisFrame = Input.GetMouseButtonDown(0);
        return true;
#endif
    }

    void OnGUI()
    {
        if (viewCamera == null || hospitalRoot == null || !hospitalRoot.activeInHierarchy)
        {
            return;
        }

        EnsureStyles();
        for (int i = 0; i < labels.Length; i++)
        {
            HospitalLabel label = labels[i];
            if (label == null)
            {
                continue;
            }

            Vector3 screen = viewCamera.WorldToScreenPoint(label.WorldPosition);
            Vector2 size = labelStyle.CalcSize(new GUIContent(label.Text));
            Rect rect = new Rect(screen.x - (size.x * 0.5f) - 8f, Screen.height - screen.y - (size.y * 0.5f) - 4f, size.x + 16f, size.y + 8f);
            bool isHovered = hovered != null && label.gameObject == hovered.gameObject;
            GUI.color = isHovered ? new Color(1f, 0.9f, 0.45f, 1f) : Color.white;
            GUI.Box(rect, GUIContent.none);
            GUI.color = Color.white;
            GUI.Label(rect, label.Text, labelStyle);
        }

        float barHeight = hintStyle.fontSize * 2.2f;
        GUI.Box(new Rect(0f, Screen.height - barHeight, Screen.width, barHeight), GUIContent.none);
        GUI.Label(new Rect(0f, Screen.height - barHeight, Screen.width, barHeight), "Click a patient room to begin the examination", hintStyle);

        if (!string.IsNullOrEmpty(notice) && Time.unscaledTime < noticeUntil)
        {
            Rect noticeRect = new Rect(0f, 12f, Screen.width, barHeight);
            GUI.Box(new Rect(Screen.width * 0.25f, noticeRect.y, Screen.width * 0.5f, barHeight), GUIContent.none);
            GUI.Label(noticeRect, notice, hintStyle);
        }
    }

    void EnsureStyles()
    {
        int fontSize = Mathf.Max(12, Screen.height / 40);
        if (labelStyle != null && labelStyle.fontSize == fontSize)
        {
            return;
        }

        labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = false
        };
        labelStyle.normal.textColor = Color.white;

        hintStyle = new GUIStyle(labelStyle) { fontSize = Mathf.RoundToInt(fontSize * 0.9f) };
    }
}
