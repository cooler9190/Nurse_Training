/// <summary>
/// How far the patient's frustration moves during shared decision making.
/// The meter starts at <see cref="Start"/> and the examination fails at <see cref="Max"/>.
/// </summary>
public static class ShareFrustration
{
    public const int Start = 20;
    public const int Max = 100;
    public const int AskGoals = -15;
    public const int SkipProsCons = 20;
    public const int DecideAlone = 25;
    public const int UnwantedTreatment = 20;
}
