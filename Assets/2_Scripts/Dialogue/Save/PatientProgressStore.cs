using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Keeps PatientProgress for the session so re-entering a room resumes where the player left off.
// Nothing is written to disk unless PersistToDisk is switched on; then it is saved as JSON in
// Application.persistentDataPath/patient_progress.json after every change and loaded at start.
// Patients are keyed by their SO_Patient asset name, so renaming an asset starts it from scratch
public static class PatientProgressStore
{
    [Serializable]
    class SaveFile
    {
        public int version = 1;
        public List<PatientProgress> patients = new List<PatientProgress>();
    }

    static readonly Dictionary<string, PatientProgress> progressById = new Dictionary<string, PatientProgress>();
    static bool loaded;

    /// <summary>Off by default: progress lives in memory only and is gone when the game closes. Set before the first patient starts.</summary>
    public static bool PersistToDisk { get; set; }

    public static string SavePath => Path.Combine(Application.persistentDataPath, "patient_progress.json");

    // Static state survives play sessions when domain reload is disabled
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStaticState()
    {
        progressById.Clear();
        loaded = false;
    }

    public static PatientProgress Get(SO_Patient patient)
    {
        EnsureLoaded();
        string id = patient.name;
        if (!progressById.TryGetValue(id, out PatientProgress progress))
        {
            progress = new PatientProgress { patientId = id };
            progressById[id] = progress;
        }

        return progress;
    }

    /// <summary>Called after every change. Does nothing unless <see cref="PersistToDisk"/> is on.</summary>
    public static void Save()
    {
        if (PersistToDisk)
        {
            SaveNow();
        }
    }

    /// <summary>Writes the progress to disk right away, whatever <see cref="PersistToDisk"/> says (e.g. a future Save button).</summary>
    public static void SaveNow()
    {
        EnsureLoaded();
        SaveFile file = new SaveFile();
        foreach (PatientProgress progress in progressById.Values)
        {
            file.patients.Add(progress);
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SavePath));
            // Write beside the real file first so a crash mid-write can't corrupt existing progress
            string tempPath = SavePath + ".tmp";
            File.WriteAllText(tempPath, JsonUtility.ToJson(file, true));
            if (File.Exists(SavePath))
            {
                File.Delete(SavePath);
            }

            File.Move(tempPath, SavePath);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"PatientProgressStore: could not save progress to '{SavePath}': {e.Message}");
        }
    }

    // Retry: wipe this attempt, keep the best score
    public static void ResetCase(SO_Patient patient)
    {
        Get(patient).ResetAttempt();
        Save();
    }

    public static void ResetAll()
    {
        progressById.Clear();
        loaded = true;
        try
        {
            if (File.Exists(SavePath))
            {
                File.Delete(SavePath);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"PatientProgressStore: could not delete '{SavePath}': {e.Message}");
        }
    }

    static void EnsureLoaded()
    {
        if (loaded)
        {
            return;
        }

        loaded = true;
        progressById.Clear();
        if (!PersistToDisk || !File.Exists(SavePath))
        {
            return;
        }

        try
        {
            SaveFile file = JsonUtility.FromJson<SaveFile>(File.ReadAllText(SavePath));
            if (file == null || file.patients == null)
            {
                return;
            }

            for (int i = 0; i < file.patients.Count; i++)
            {
                PatientProgress progress = file.patients[i];
                if (progress != null && !string.IsNullOrEmpty(progress.patientId))
                {
                    progressById[progress.patientId] = progress;
                }
            }
        }
        catch (Exception e)
        {
            // Keep the unreadable file around for inspection, then start fresh
            Debug.LogWarning($"PatientProgressStore: '{SavePath}' could not be read ({e.Message}). Starting with empty progress.");
            try
            {
                File.Copy(SavePath, SavePath + ".bad", true);
            }
            catch (Exception)
            {
                // Best effort only
            }
        }
    }
}
