using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Rules for shared decision making. The team talk plays once per patient when shared decision making
/// starts. After that each diagnosis card runs its own options talk and decision talk. The interview screen
/// shows the returned <see cref="SharePrompt"/>; this class does not build any UI.
/// <para>
/// In the decision talk the patient accepts the recommended treatment only while their frustration is at or
/// below <see cref="ShareFrustration.CalmMax"/>; otherwise they refuse and it is taken off the list. Any other
/// treatment is always accepted. Forcing applies the recommended treatment. Only an accepted recommended
/// treatment counts as shared; every other decision is a poor decision. At <see cref="ShareFrustration.Max"/>
/// the walkout lines play and the examination fails.
/// </para>
/// </summary>
public sealed class SharedDecisionSession
{
    enum Step
    {
        Idle,
        TeamTalk,
        Options,
        Decision,
        Walkout
    }

    readonly HashSet<string> sharedIds = new HashSet<string>(System.StringComparer.Ordinal);
    readonly HashSet<string> decidedIds = new HashSet<string>(System.StringComparer.Ordinal);
    readonly Dictionary<string, TreatmentOption> chosenTreatments = new Dictionary<string, TreatmentOption>(System.StringComparer.Ordinal);
    // The choices currently on screen, so a clicked index maps back to what was offered
    readonly List<ApproachTalk> approachChoices = new List<ApproachTalk>();
    readonly List<TreatmentOption> treatmentChoices = new List<TreatmentOption>();

    PatientSharedDecision script;
    Diagnosis active;
    string patientName;
    Step step = Step.Idle;
    int frustration = ShareFrustration.Start;
    bool teamTalkDone;
    bool closed;

    public int Frustration => frustration;
    public bool HasLeft { get; private set; }
    /// <summary>True from the first choice of a talk until it is finished or cancelled.</summary>
    public bool IsInProgress => step != Step.Idle;
    /// <summary>True while the team talk choices are waiting for an answer.</summary>
    public bool IsInTeamTalk => step == Step.TeamTalk;

    /// <summary>True when the patient agreed to the recommended treatment for this diagnosis.</summary>
    public bool IsShared(string diagnosisId)
    {
        return !string.IsNullOrEmpty(diagnosisId) && sharedIds.Contains(diagnosisId);
    }

    /// <summary>True after this diagnosis has been through the options and decision talk.</summary>
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

    /// <summary>Forgets every decision and the team talk. Used when a new patient starts.</summary>
    public void Reset()
    {
        sharedIds.Clear();
        decidedIds.Clear();
        chosenTreatments.Clear();
        ClearActive();
        frustration = ShareFrustration.Start;
        HasLeft = false;
        teamTalkDone = false;
        closed = false;
        script = null;
        patientName = null;
    }

    /// <summary>Drops the diagnosis currently being discussed. The team talk, results and frustration stay.</summary>
    public void CancelTalk()
    {
        if (HasLeft || step == Step.Walkout || step == Step.TeamTalk)
        {
            return;
        }

        ClearActive();
    }

    /// <summary>
    /// Starts the team talk. Returns null, and lets diagnosis cards be discussed straight away, when it was
    /// already played for this patient or none is written.
    /// </summary>
    public SharePrompt BeginTeamTalk(PatientSharedDecision patientScript, string name)
    {
        if (HasLeft || IsInProgress)
        {
            return null;
        }

        script = patientScript;
        patientName = name;
        if (teamTalkDone || script == null || !script.HasTeamTalk)
        {
            teamTalkDone = true;
            return null;
        }

        step = Step.TeamTalk;
        SharePrompt prompt = Prompt();
        AddLine(prompt, DialogueSpeaker.Narrator, $"Explain to {PatientName()} what is going on.");
        OfferApproaches(prompt, script.teamTalk);
        return prompt;
    }

    /// <summary>
    /// Starts the options talk for <paramref name="diagnosis"/>. A diagnosis with no script returns a single
    /// line and does not count as in progress. Returns null while another talk is open, before the team talk
    /// is finished, for a decided diagnosis, or once the patient has left.
    /// </summary>
    public SharePrompt Begin(Diagnosis diagnosis, string name)
    {
        if (HasLeft || IsInProgress || !teamTalkDone || (diagnosis != null && IsDecided(diagnosis.id)))
        {
            return null;
        }

        patientName = name;
        SharedDecisionTalk talk = diagnosis != null ? diagnosis.sharedDecision : null;
        if (talk == null || !talk.HasScript)
        {
            SharePrompt empty = Prompt();
            empty.Lines.Add(new DialogueLine(DialogueSpeaker.Narrator, "No shared decision is written for this diagnosis yet."));
            return empty;
        }

        active = diagnosis;
        step = Step.Options;
        SharePrompt prompt = Prompt();
        AddLine(prompt, DialogueSpeaker.Patient, talk.patientOpener);
        OfferApproaches(prompt, talk.optionsTalk);
        return prompt;
    }

    /// <summary>Applies the choice at <paramref name="index"/> and returns what to show next.</summary>
    public SharePrompt Choose(int index)
    {
        switch (step)
        {
            case Step.TeamTalk:
                return AnswerTeamTalk(index);
            case Step.Options:
                return AnswerOptions(index);
            case Step.Decision:
                return AnswerDecision(index);
            case Step.Walkout:
                return Leave();
            default:
                return null;
        }
    }

    /// <summary>
    /// The nurse's last line and the patient's goodbye, chosen by frustration. Returns an empty list after the
    /// first call, once the patient has left, or when no closing is written.
    /// </summary>
    public List<DialogueLine> Closing()
    {
        List<DialogueLine> lines = new List<DialogueLine>();
        if (closed || HasLeft || script == null)
        {
            return lines;
        }

        closed = true;
        AddLine(lines, DialogueSpeaker.Nurse, script.closingLine);
        AddLine(lines, DialogueSpeaker.Patient, IsCalm ? script.calmGoodbye : script.frustratedGoodbye);
        return lines;
    }

    SharePrompt AnswerTeamTalk(int index)
    {
        ApproachTalk approach = ApproachAt(index);
        if (approach == null)
        {
            return null;
        }

        SharePrompt prompt = Prompt();
        AddApproachLines(prompt, approach);
        teamTalkDone = true;
        ClearActive();
        if (AddFrustration(approach.frustrationChange))
        {
            return Walkout(prompt);
        }

        prompt.Frustration = frustration;
        return prompt;
    }

    SharePrompt AnswerOptions(int index)
    {
        ApproachTalk approach = ApproachAt(index);
        if (approach == null)
        {
            return null;
        }

        SharePrompt prompt = Prompt();
        AddApproachLines(prompt, approach);
        if (AddFrustration(approach.frustrationChange))
        {
            return Walkout(prompt);
        }

        prompt.Frustration = frustration;
        prompt.Lines.Add(new DialogueLine(DialogueSpeaker.Nurse, $"Ok {PatientName()}, let's decide on your treatment."));
        treatmentChoices.Clear();
        foreach (TreatmentOption treatment in active.sharedDecision.treatments)
        {
            if (treatment != null && !string.IsNullOrWhiteSpace(treatment.title))
            {
                treatmentChoices.Add(treatment);
            }
        }

        step = Step.Decision;
        OfferDecision(prompt);
        return prompt;
    }

    SharePrompt AnswerDecision(int index)
    {
        SharedDecisionTalk talk = active.sharedDecision;
        if (index == treatmentChoices.Count && talk.forcedDecision != null && talk.forcedDecision.HasText)
        {
            return Force(talk);
        }

        if (index < 0 || index >= treatmentChoices.Count)
        {
            return null;
        }

        TreatmentOption treatment = treatmentChoices[index];
        SharePrompt prompt = Prompt();
        AddLine(prompt, DialogueSpeaker.Nurse, ProposalText(treatment));
        bool calm = IsCalm;
        AddLine(prompt, DialogueSpeaker.Patient, calm ? treatment.calmReply : treatment.frustratedReply);
        if (treatment.recommended && !calm)
        {
            treatmentChoices.RemoveAt(index);
            OfferDecision(prompt);
            if (prompt.Choices.Count == 0)
            {
                return Finish(prompt, null, false);
            }

            return prompt;
        }

        return Finish(prompt, treatment, treatment.recommended);
    }

    SharePrompt Force(SharedDecisionTalk talk)
    {
        SharePrompt prompt = Prompt();
        AddApproachLines(prompt, talk.forcedDecision);
        if (AddFrustration(talk.forcedDecision.frustrationChange))
        {
            return Walkout(prompt);
        }

        prompt.Frustration = frustration;
        return Finish(prompt, talk.Recommended, false);
    }

    SharePrompt Finish(SharePrompt prompt, TreatmentOption chosen, bool shared)
    {
        string id = active != null ? active.id : null;
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

        ClearActive();
        prompt.Finished = true;
        prompt.DiagnosisId = id;
        prompt.Shared = shared;
        prompt.Chosen = chosen;
        return prompt;
    }

    /// <summary>Keeps the lines that pushed frustration over the limit, then adds the walkout lines and a Continue choice.</summary>
    SharePrompt Walkout(SharePrompt prompt)
    {
        ClearActive();
        step = Step.Walkout;
        prompt.Frustration = frustration;
        bool added = false;
        if (script != null && script.walkoutLines != null)
        {
            foreach (DialogueLine line in script.walkoutLines)
            {
                if (line != null && line.HasText)
                {
                    prompt.Lines.Add(line);
                    added = true;
                }
            }
        }

        if (!added)
        {
            prompt.Lines.Add(new DialogueLine(DialogueSpeaker.Patient, "I've had enough. I'm leaving."));
        }

        prompt.Choices.Add("Continue");
        return prompt;
    }

    SharePrompt Leave()
    {
        SharePrompt prompt = Prompt();
        prompt.Leave = true;
        return prompt;
    }

    void OfferApproaches(SharePrompt prompt, List<ApproachTalk> approaches)
    {
        approachChoices.Clear();
        if (approaches == null)
        {
            return;
        }

        foreach (ApproachTalk approach in approaches)
        {
            if (approach != null && approach.HasText)
            {
                approachChoices.Add(approach);
                prompt.Choices.Add(approach.nurseLine.Trim());
            }
        }
    }

    void OfferDecision(SharePrompt prompt)
    {
        foreach (TreatmentOption treatment in treatmentChoices)
        {
            prompt.Choices.Add(ProposalText(treatment));
        }

        ApproachTalk force = active != null ? active.sharedDecision.forcedDecision : null;
        if (force != null && force.HasText)
        {
            prompt.Choices.Add(force.nurseLine.Trim());
        }
    }

    ApproachTalk ApproachAt(int index)
    {
        return index >= 0 && index < approachChoices.Count ? approachChoices[index] : null;
    }

    static void AddApproachLines(SharePrompt prompt, ApproachTalk approach)
    {
        AddLine(prompt, DialogueSpeaker.Nurse, approach.nurseLine);
        if (approach.replies == null)
        {
            return;
        }

        foreach (DialogueLine line in approach.replies)
        {
            if (line != null && line.HasText)
            {
                prompt.Lines.Add(line);
            }
        }
    }

    static void AddLine(SharePrompt prompt, DialogueSpeaker speaker, string text)
    {
        AddLine(prompt.Lines, speaker, text);
    }

    static void AddLine(List<DialogueLine> lines, DialogueSpeaker speaker, string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            lines.Add(new DialogueLine(speaker, text.Trim()));
        }
    }

    static string ProposalText(TreatmentOption treatment)
    {
        return string.IsNullOrWhiteSpace(treatment.proposal) ? $"Shall we go with {treatment.title}?" : treatment.proposal.Trim();
    }

    bool IsCalm => frustration <= ShareFrustration.CalmMax;

    SharePrompt Prompt()
    {
        return new SharePrompt { Frustration = frustration };
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
        approachChoices.Clear();
        treatmentChoices.Clear();
    }

    string PatientName()
    {
        return string.IsNullOrWhiteSpace(patientName) ? "Patient" : patientName;
    }
}

/// <summary>What the interview screen should show after one shared-decision step.</summary>
public sealed class SharePrompt
{
    /// <summary>Played in order. A first nurse line is the choice the player just clicked.</summary>
    public List<DialogueLine> Lines = new List<DialogueLine>();
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
