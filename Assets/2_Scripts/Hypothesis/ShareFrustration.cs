/// <summary>
/// Limits of the patient's frustration during shared decision making. How far each choice moves it is
/// authored per choice on the patient asset (<see cref="ApproachTalk.frustrationChange"/>).
/// The meter starts at <see cref="Start"/> and the examination fails at <see cref="Max"/>.
/// </summary>
public static class ShareFrustration
{
    public const int Start = 20;
    public const int Max = 100;
    /// <summary>At or below this the patient is calm enough to accept the recommended treatment.</summary>
    public const int CalmMax = 45;
}
