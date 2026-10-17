using UnityEditor;
using UnityEngine;

public static class PatientProgressMenu
{
    [MenuItem("Nurse Training/Clear Saved Patient Progress")]
    static void ClearSavedProgress()
    {
        if (!EditorUtility.DisplayDialog("Clear saved progress", "Delete all saved patient progress?\n\n" + PatientProgressStore.SavePath, "Delete", "Cancel"))
        {
            return;
        }

        PatientProgressStore.ResetAll();
        Debug.Log("Saved patient progress cleared.");
    }

    [MenuItem("Nurse Training/Show Saved Patient Progress")]
    static void RevealSavedProgress()
    {
        EditorUtility.RevealInFinder(PatientProgressStore.SavePath);
    }
}
