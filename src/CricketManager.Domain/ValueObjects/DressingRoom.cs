namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// Post-Phase-5 rectification pass, Wave 5 (point 10): a player's standing in the dressing room -
/// how much weight his opinion carries when the room reads the coach, and how much the juniors
/// look to him. Not persisted; DressingRoomService.RoleOf computes it on demand from reputation,
/// experience, leadership and age, the same "derive it, never trust a bare default" discipline
/// this codebase uses for SquadStatus and traits.
/// </summary>
public enum DressingRoomRole
{
    /// <summary>New to this level - learning the environment, not yet shaping it.</summary>
    Junior,
    /// <summary>An established member of the group, but not one whose word moves the room.</summary>
    SquadPlayer,
    /// <summary>A senior voice - respected, listened to, part of how the room feels about things.</summary>
    Established,
    /// <summary>A leader of men whether or not he wears the armband - the room takes its temperature from him.</summary>
    SeniorPro
}

/// <summary>
/// Post-Phase-5 rectification pass, Wave 5: a mentoring pairing within one team - a senior pro
/// working with a junior. The Training brief's Section 7 (mentoring groups) was deferred pending
/// exactly this: the squad-culture foundation it needs to sit on. Dynamic - MentoringService
/// reviews and re-forms these each quarter as juniors graduate and seniors leave.
/// </summary>
public sealed class MentoringGroup
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TeamId { get; init; }
    public Guid MentorId { get; init; }
    public Guid MenteeId { get; init; }
    public DateOnly FormedDate { get; init; }

    /// <summary>
    /// 0-100: how well this pairing actually works - personality compatibility blended with the
    /// mentor's own quality as a mentor. A low-strength pairing barely helps the mentee and can
    /// even grate; a high-strength one genuinely accelerates his development and steadies him.
    /// </summary>
    public double Strength { get; set; }
}
