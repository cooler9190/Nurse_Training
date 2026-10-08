using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SO_Patient))]
public class SO_PatientEditor : Editor
{
    const float PreviewMaxHeight = 420f;
    const float MinNormalizedSize = 0.02f;
    const float ResizeHandleSize = 12f;

    enum DragMode
    {
        None,
        Move,
        Resize
    }

    DragMode dragMode = DragMode.None;
    int dragIndex = -1;
    int selectedIndex = -1;
    int undoGroup;
    Vector2 dragStartMouse;
    Rect dragStartRect;
    Rect dragRect;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Portrait hotspots", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Drag a region to move it. Drag the corner of the selected region to resize it. Positions are 0 to 1 from the top-left of the portrait.",
            MessageType.None);
        DrawPreview();
        serializedObject.ApplyModifiedProperties();
    }

    void DrawPreview()
    {
        SerializedProperty portraitProperty = serializedObject.FindProperty("patientPortrait");
        Sprite sprite = portraitProperty != null ? portraitProperty.objectReferenceValue as Sprite : null;
        float aspect = sprite != null && sprite.rect.height > 0.01f
            ? sprite.rect.width / sprite.rect.height
            : 0.75f;

        float width = Mathf.Max(120f, EditorGUIUtility.currentViewWidth - 36f);
        float height = width / aspect;
        if (height > PreviewMaxHeight)
        {
            height = PreviewMaxHeight;
            width = height * aspect;
        }

        Rect preview = GUILayoutUtility.GetRect(width, height, GUILayout.ExpandWidth(false));
        int controlId = GUIUtility.GetControlID("PatientHotspotPreview".GetHashCode(), FocusType.Passive, preview);
        SerializedProperty hotspots = serializedObject.FindProperty("visualHotspots");

        if (Event.current.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(preview, new Color(0.1f, 0.18f, 0.24f, 1f));
            if (sprite != null && sprite.texture != null)
            {
                Rect textureRect = sprite.textureRect;
                Rect texCoords = new Rect(
                    textureRect.x / sprite.texture.width,
                    textureRect.y / sprite.texture.height,
                    textureRect.width / sprite.texture.width,
                    textureRect.height / sprite.texture.height);
                GUI.DrawTextureWithTexCoords(preview, sprite.texture, texCoords, true);
            }
            else
            {
                GUI.Label(preview, "Assign a portrait sprite", CenteredLabel());
            }

            if (hotspots != null)
            {
                for (int i = 0; i < hotspots.arraySize; i++)
                {
                    Rect normalized = DisplayedRect(hotspots, i);
                    Rect guiRect = NormalizedToGui(preview, normalized);
                    bool selected = i == selectedIndex;
                    Color fill = selected
                        ? new Color(1f, 0.78f, 0.28f, 0.2f)
                        : new Color(0.55f, 0.82f, 0.9f, 0.16f);
                    Color outline = selected
                        ? new Color(1f, 0.82f, 0.28f, 1f)
                        : new Color(0.78f, 0.93f, 0.97f, 0.95f);
                    DrawRectOutline(guiRect, fill, outline);
                    if (selected)
                    {
                        EditorGUI.DrawRect(ResizeHandle(guiRect), outline);
                    }
                }

                for (int i = 0; i < hotspots.arraySize; i++)
                {
                    Rect guiRect = NormalizedToGui(preview, DisplayedRect(hotspots, i));
                    string id = hotspots.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue;
                    if (!string.IsNullOrEmpty(id))
                    {
                        GUI.Label(new Rect(guiRect.x + 4f, guiRect.y + 2f, guiRect.width - 8f, 18f), id, EditorStyles.whiteMiniLabel);
                    }
                }
            }
        }

        HandlePreviewInput(controlId, preview, hotspots);
    }

    void HandlePreviewInput(int controlId, Rect preview, SerializedProperty hotspots)
    {
        Event evt = Event.current;
        if (hotspots == null || preview.width < 1f || preview.height < 1f)
        {
            return;
        }

        if (evt.type == EventType.MouseDown && evt.button == 0)
        {
            bool overHandle = false;
            if (selectedIndex >= 0 && selectedIndex < hotspots.arraySize)
            {
                Rect selectedGui = NormalizedToGui(preview, DisplayedRect(hotspots, selectedIndex));
                overHandle = ResizeHandle(selectedGui).Contains(evt.mousePosition);
                if (overHandle)
                {
                    BeginDrag(controlId, DragMode.Resize, selectedIndex, hotspots, evt.mousePosition);
                    evt.Use();
                    return;
                }
            }

            if (!preview.Contains(evt.mousePosition))
            {
                return;
            }

            int hit = HitTest(preview, hotspots, evt.mousePosition);
            if (hit >= 0)
            {
                selectedIndex = hit;
                BeginDrag(controlId, DragMode.Move, hit, hotspots, evt.mousePosition);
            }
            else
            {
                selectedIndex = -1;
            }

            evt.Use();
            Repaint();
        }
        else if (evt.type == EventType.MouseDrag && GUIUtility.hotControl == controlId && dragMode != DragMode.None)
        {
            Vector2 delta = evt.mousePosition - dragStartMouse;
            Rect next = dragStartRect;
            if (dragMode == DragMode.Move)
            {
                next.x += delta.x / preview.width;
                next.y += delta.y / preview.height;
            }
            else
            {
                next.width += delta.x / preview.width;
                next.height += delta.y / preview.height;
            }

            dragRect = ClampRect(next);
            SetHotspotRect(hotspots, dragIndex, dragRect);
            serializedObject.ApplyModifiedProperties();
            evt.Use();
            Repaint();
        }
        else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == controlId)
        {
            if (dragMode != DragMode.None)
            {
                SetHotspotRect(hotspots, dragIndex, dragRect);
                serializedObject.ApplyModifiedProperties();
                Undo.CollapseUndoOperations(undoGroup);
            }

            GUIUtility.hotControl = 0;
            dragMode = DragMode.None;
            dragIndex = -1;
            evt.Use();
            Repaint();
        }
    }

    void BeginDrag(int controlId, DragMode mode, int index, SerializedProperty hotspots, Vector2 mousePosition)
    {
        GUIUtility.hotControl = controlId;
        undoGroup = Undo.GetCurrentGroup();
        dragMode = mode;
        dragIndex = index;
        selectedIndex = index;
        dragStartMouse = mousePosition;
        dragStartRect = GetHotspotRect(hotspots, index);
        dragRect = dragStartRect;
    }

    static int HitTest(Rect preview, SerializedProperty hotspots, Vector2 mousePosition)
    {
        for (int i = hotspots.arraySize - 1; i >= 0; i--)
        {
            if (NormalizedToGui(preview, GetHotspotRect(hotspots, i)).Contains(mousePosition))
            {
                return i;
            }
        }

        return -1;
    }

    Rect DisplayedRect(SerializedProperty hotspots, int index)
    {
        if (dragMode != DragMode.None && index == dragIndex)
        {
            return dragRect;
        }

        return GetHotspotRect(hotspots, index);
    }

    static Rect GetHotspotRect(SerializedProperty hotspots, int index)
    {
        if (index < 0 || index >= hotspots.arraySize)
        {
            return new Rect(0.35f, 0.08f, 0.3f, 0.16f);
        }

        return hotspots.GetArrayElementAtIndex(index).FindPropertyRelative("normalizedRect").rectValue;
    }

    static void SetHotspotRect(SerializedProperty hotspots, int index, Rect rect)
    {
        if (index < 0 || index >= hotspots.arraySize)
        {
            return;
        }

        hotspots.GetArrayElementAtIndex(index).FindPropertyRelative("normalizedRect").rectValue = rect;
    }

    static void DrawRectOutline(Rect rect, Color fill, Color outline)
    {
        EditorGUI.DrawRect(rect, fill);
        const float thickness = 2f;
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), outline);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), outline);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), outline);
        EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), outline);
    }

    static Rect NormalizedToGui(Rect preview, Rect normalized)
    {
        return new Rect(
            preview.x + (normalized.x * preview.width),
            preview.y + (normalized.y * preview.height),
            normalized.width * preview.width,
            normalized.height * preview.height);
    }

    static Rect ResizeHandle(Rect guiRect)
    {
        return new Rect(guiRect.xMax - (ResizeHandleSize * 0.5f), guiRect.yMax - (ResizeHandleSize * 0.5f), ResizeHandleSize, ResizeHandleSize);
    }

    static Rect ClampRect(Rect rect)
    {
        rect.width = Mathf.Clamp(rect.width, MinNormalizedSize, 1f);
        rect.height = Mathf.Clamp(rect.height, MinNormalizedSize, 1f);
        rect.x = Mathf.Clamp(rect.x, 0f, 1f - rect.width);
        rect.y = Mathf.Clamp(rect.y, 0f, 1f - rect.height);
        return rect;
    }

    static GUIStyle CenteredLabel()
    {
        GUIStyle style = new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true
        };
        style.normal.textColor = new Color(0.85f, 0.93f, 0.96f, 1f);
        return style;
    }
}
