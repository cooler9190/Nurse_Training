using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Rules for sharing one diagnosis at a time: goals, each treatment's pros and cons, then the
/// decision. The interview screen shows the returned <see cref="SharePrompt"/>; this class does
/// not build any UI. Frustration and the Shared flag match the previous share screen.
/// </summary>
public sealed class SharedDecisionSession
{
    enum Step
    {
        Idle,
        Goals,
        Treatments,
        Decision,
        Pick,
        Walkout
    }

    readonly HashSet<string> sharedIds = new HashSet<string>(System.StringComparer.Ordinal);
    readonly HashSet<string> decidedIds = new HashSet<string>(System.StringComparer.Ordinal);
    readonly HashSet<string> askedGoalIds = new HashSet<string>(System.StringComparer.Ordinal);
    readonly Dictionary<string, TreatmentOption> chosenTreatments = new Dictionary<string, TreatmentOption>(System.StringComparer.Ordinal);
    readonly List<TreatmentOption> sessionTreatments = new List<TreatmentOption>();
    readonly HashSet<int> discussed = new HashSet<int>();

    Diagnosis active;
    string patientName;
    Step step = Step.Idle;
    int currentTreatment;
    int frustration = ShareFrustration.Start;
    bool decidingWithPatient;

    public int Frustration => frustration;
    public bool HasLeft { get; private set; }
    /// <summary>True from the first choice of a diagnosis until that decision is finished or cancelled.</summary>
    public bool IsInProgress => step != Step.Idle;

    /// <summary>True when every treatment was discussed and the decision was made with the patient.</summary>
    public bool IsShared(string diagnosisId)
    {
        return !string.IsNullOrEmpty(diagnosisId) && sharedIds.Contains(diagnosisId);
    }

    /// <summary>True after this diagnosis has been through goals, options and the decision.</summary>
    public bool IsDecided(string diagnosisId)
    {
        return !string.IsNullOrEmpty(diagnosisId) && decidedIds.Contains(diagnosisId);
    }

    /// <summary>The treatment chosen for this diagnosis, or null if none has been chosen yet.</summary>
    public TreatmentOption ChosenTreatment(string diagnosisId)
    {
        if (string.IsNullOrEmpty(diagnosisId))
        {
            return null;
        }

        chosenTreatments.TryGetValue(diagnosisId, out TreatmentOption chosen);
        return chosen;
    }

    /// <summary>Forgets every decision. Used when a new patient starts.</summary>
    public void Reset()
    {
        sharedIds.Clear();
        decidedIds.Clear();
        askedGoalIds.Clear();
        chosenTreatments.Clear();
        ClearActive();
        frustration = ShareFrustration.Start;
        HasLeft = false;
        patientName = null;
    }

    /// <summary>Drops the diagnosis currently being shared. Shared results and frustration stay.</summary>
    public void CancelTalk()
    {
        if (HasLeft || step == Step.Walkout)
        {
            return;
        }

        ClearActive();
    }

    /// <summary>
    /// Starts sharing <paramref name="diagnosis"/>. A diagnosis with no script returns a single line
    /// and does not count as in progress. Returns null when a talk is already open or the patient has left.
    /// </summary>
    public SharePrompt Begin(Diagnosis diagnosis, string name)
    {
        if (HasLeft || IsInProgress)
        {
            return null;
        }

        patientName = name;
        if (diagnosis != null && IsDecided(diagnosis.id))
        {
            return null;
        }

        if (diagnosis == null || diagnosis.sharedDecision == null || !diagnosis.sharedDecision.HasScript)
        {
            return Line("You", "No shared decision is written for this diagnosis yet.");
        }

        active = diagnosis;
        sessionTreatments.Clear();
        discussed.Clear();
        currentTreatment = 0;
        foreach (TreatmentOption treatment in diagnosis.sharedDecision.treatments)
        {
            if (treatment != null && !string.IsNullOrWhiteSpace(treatment.title))
            {
                sessionTreatments.Add(treatment);
            }
        }

        if (sessionTreatments.Count == 0)
        {
            active = null;
            return Line("You", "No shared decision is written for this diagnosis yet.");
        }

        bool alreadyAsked = !string.IsNullOrEmpty(diagnosis.id) && askedGoalIds.Contains(diagnosis.id);
        if (alreadyAsked)
        {
            step = Step.Treatments;
            return TreatmentPrompt("You", "Discuss the pros and cons of each treatment, or skip them.", null);
        }

        step = Step.Goals;
        SharePrompt prompt = Line("You", "Explain the diagnosis, then ask what matters to them.");
        prompt.Choices.Add("Ask what matters to them.");
        return prompt;
    }

    /// <summary>Applies the choice at <paramref name="index"/> and returns what to show next.</summary>
    public SharePrompt Choose(int index)
    {
        switch (step)
        {
            case Step.Goals:
                return AskGoals();
            case Step.Treatments:
                return AnswerTreatment(index);
            case Step.Decision:
                return AnswerDecision(index);
            case Step.Pick:
                return PickTreatment(index);
            case Step.Walkout:
                return Leave();
            default:
                return null;
        }
    }

    SharePrompt AskGoals()
    {
        string id = active != null ? active.id : null;
        if (!string.IsNullOrEmpty(id) && askedGoalIds.Add(id))
        {
            AddFrustration(ShareFrustration.AskGoals);
        }

        if (HasLeft)
        {
            return WalkoutPrompt();
        }

        string goals = active != null && active.sharedDecision != null ? active.sharedDecision.goalsAndFears : null;
        string text = string.IsNullOrWhiteSpace(goals)
            ? "They are not sure what else matters to them right now."
            : goals;
        currentTreatment = 0;
        step = Step.Treatments;
        return TreatmentPrompt(PatientName(), null, text);
    }

    SharePrompt AnswerTreatment(int index)
    {
        if (index != 0 && index != 1)
        {
            return null;
        }

        TreatmentOption treatment = sessionTreatments[currentTreatment];
        if (index == 0)
        {
            discussed.Add(currentTreatment);
            return Advance(PatientName(), ProsConsPreface(treatment), "I understand the benefits and the drawbacks.");
        }

        if (AddFrustration(ShareFrustration.SkipProsCons))
        {
            return WalkoutPrompt();
        }

        return Advance(PatientName(), null, "You have not told me what this treatment would mean for me.");
    }

    SharePrompt AnswerDecision(int index)
    {
        if (index != 0 && index != 1)
        {
            return null;
        }

        decidingWithPatient = index == 0;
        step = Step.Pick;
        SharePrompt prompt = Line("You", decidingWithPatient
            ? "Ask which treatment fits what matters to them."
            : "Tell them which treatment they will have.");
        for (int i = 0; i < sessionTreatments.Count; i++)
        {
            prompt.Choices.Add(sessionTreatments[i].title);
        }

        return prompt;
    }

    SharePrompt PickTreatment(int index)
    {
        if (index < 0 || index >= sessionTreatments.Count)
        {
            return null;
        }

        TreatmentOption chosen = sessionTreatments[index];
        if (!decidingWithPatient && AddFrustration(ShareFrustration.DecideAlone))
        {
            return WalkoutPrompt();
        }

        if (chosen != null && !chosen.patientWants && AddFrustration(ShareFrustration.UnwantedTreatment))
        {
            return WalkoutPrompt();
        }

        string id = active != null ? active.id : null;
        bool shared = decidingWithPatient && discussed.Count >= sessionTreatments.Count;
        if (!string.IsNullOrEmpty(id))
        {
            chosenTreatments[id] = chosen;
            decidedIds.Add(id);
            if (shared)
            {
                sharedIds.Add(id);
            }
            else
            {
                sharedIds.Remove(id);
            }
        }

        string title = chosen != null && !string.IsNullOrWhiteSpace(chosen.title) ? chosen.title : "the treatment";
        bool withPatient = decidingWithPatient;
        ClearActive();
        SharePrompt prompt = withPatient
            ? Line(PatientName(), null)
            : Line("You", $"You tell them they will have {title}.");
        if (withPatient)
        {
            prompt.PatientLine = $"Let's go with {title}.";
        }

        prompt.Finished = true;
        prompt.DiagnosisId = id;
        prompt.Shared = shared;
        prompt.Chosen = chosen;
        return prompt;
    }

    SharePrompt Advance(string speaker, string dialogue, string patientLine)
    {
        if (currentTreatment + 1 < sessionTreatments.Count)
        {
            currentTreatment++;
            step = Step.Treatments;
            return TreatmentPrompt(speaker, dialogue, patientLine);
        }

        step = Step.Decision;
        SharePrompt prompt = Line(speaker, dialogue);
        prompt.PatientLine = patientLine;
        prompt.Choices.Add("Decide with the patient.");
        prompt.Choices.Add("Decide yourself.");
        return prompt;
    }

    SharePrompt TreatmentPrompt(string speaker, string dialogue, string patientLine)
    {
        TreatmentOption treatment = sessionTreatments[currentTreatment];
        string title = treatment != null ? treatment.title : "this treatment";
        SharePrompt prompt = Line(speaker, dialogue);
        prompt.PatientLine = patientLine;
        prompt.Choices.Add($"Discuss the pros and cons of {title}.");
        prompt.Choices.Add($"Skip the pros and cons of {title}.");
        return prompt;
    }

    SharePrompt WalkoutPrompt()
    {
        step = Step.Walkout;
        SharePrompt prompt = Line(PatientName(), null);
        prompt.PatientLine = "I've had enough. I'm leaving.";
        prompt.Choices.Add("Continue");
        return prompt;
    }

    SharePrompt Leave()
    {
        SharePrompt prompt = Line(PatientName(), null);
        prompt.Leave = true;
        return prompt;
    }

    SharePrompt Line(string speaker, string dialogue)
    {
        return new SharePrompt
        {
            Speaker = speaker,
            Dialogue = dialogue,
            Frustration = frustration
        };
    }

    /// <summary>Moves frustration. Returns true when the patient leaves.</summary>
    bool AddFrustration(int delta)
    {
        if (HasLeft)
        {
            return true;
        }

        frustration = Mathf.Clamp(frustration + delta, 0, ShareFrustration.Max);
        if (frustration < ShareFrustration.Max)
        {
            return false;
        }

        HasLeft = true;
        return true;
    }

    void ClearActive()
    {
        active = null;
        step = Step.Idle;
        currentTreatment = 0;
        decidingWithPatient = false;
        sessionTreatments.Clear();
        discussed.Clear();
    }

    string PatientName()
    {
        return string.IsNullOrWhiteSpace(patientName) ? "Patient" : patientName;
    }

    static string ProsConsPreface(TreatmentOption treatment)
    {
        string pros = treatment != null ? treatment.pros : null;
        string cons = treatment != null ? treatment.cons : null;
        string text = string.Empty;
        if (!string.IsNullOrWhiteSpace(pros))
        {
            text += $"Pros: {pros.Trim()}";
        }

        if (!string.IsNullOrWhiteSpace(cons))
        {
            if (text.Length > 0)
            {
                text += "\n";
            }

            text += $"Cons: {cons.Trim()}";
        }

        return text.Length == 0 ? null : text;
    }
}

/// <summary>What the interview screen should show after one shared-decision step.</summary>
public sealed class SharePrompt
{
    public string Speaker;
    /// <summary>Shown at once. Nurse lines, or pros and cons kept on screen while the patient speaks.</summary>
    public string Dialogue;
    /// <summary>Typed with the patient's text speed and voice. Null when nobody is speaking.</summary>
    public string PatientLine;
    public List<string> Choices = new List<string>();
    public int Frustration;
    /// <summary>A treatment was chosen. The interview screen writes it onto the diagnosis card.</summary>
    public bool Finished;
    /// <summary>The player acknowledged the walkout. The examination fails.</summary>
    public bool Leave;
    public string DiagnosisId;
    public bool Shared;
    public TreatmentOption Chosen;
}
