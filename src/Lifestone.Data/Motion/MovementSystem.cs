namespace Lifestone.Data.Motion;

/// <summary>
/// Retail <c>MovementSystem</c> (acclient 11.4186 PORTAL/gameplay_misc/MovementSystem.c; function names and addresses
/// cited, nothing copied): the run rate, the jump height and the jump's stamina cost from the skills and the load.
/// Addresses are 11.4186 / 11.6096. Doc 13 of acclient-AI-RE found the functions byte-identical in 11.6096; what
/// changed is the skill fed in (Enlightenment is added to Run and Jump), which is the qualities' business.
/// </summary>
/// <remarks>
/// The player's <see cref="IMotionWeenie"/> answers <c>InqRunRate</c> with <see cref="GetRunRate"/> of its burden and
/// Run skill; until the qualities arrive it cannot tell, and the interpreter runs at <see cref="MotionInterpreter.MyRunRate"/>.
/// </remarks>
public static class MovementSystem
{
    /// <summary>The run rate at the Run skill that retail treats specially (18 / 4).</summary>
    public const double SpecialRunRate = 18.0 / 4.0;

    /// <summary>
    /// Retail <c>GetRunRate</c> (0x006B0950 / 0x006B1820): <c>(LoadMod(load) × (skill / (skill + 200) × 11) + 4) / scaling
    /// / 4</c>, and 4.5 at a Run skill of exactly 800.
    /// </summary>
    /// <remarks>
    /// The 11.4186 decompile compares the skill with 800 for equality; ACE (MovementSystem.cs) returns 4.5 from 800 up.
    /// With the decompile's reading a skill of 801 runs at 3.2 again. TODO(avatars): confirm the comparison from the
    /// 11.6096 bytes (a D4-style read by the lead). No M2 character comes near 800.
    /// </remarks>
    /// <param name="load">The burden ratio (<see cref="EncumbranceSystem.Load"/>).</param>
    /// <param name="runSkill">The Run skill.</param>
    /// <param name="scaling">The object scale.</param>
    public static double GetRunRate(float load, int runSkill, float scaling)
    {
        var loadMod = EncumbranceSystem.LoadMod(load);
        double skill = runSkill;
        return skill == 800.0
            ? SpecialRunRate
            : ((loadMod * (skill / (skill + 200.0) * 11.0)) + 4.0) / scaling / 4.0;
    }

    /// <summary>
    /// Retail <c>GetJumpHeight</c> (0x006B09B0 / 0x006B1880): <c>LoadMod(load) × (skill / (skill + 1300) × 22.2 + 0.05) ×
    /// power / scaling</c>, with the power clamped to [0, 1] and the result at least 0.35.
    /// </summary>
    public static double GetJumpHeight(float load, int jumpSkill, float power, float scaling)
    {
        var p = power > 1f ? 1f : power < 0f ? 0f : power;
        var skill = (double)(float)jumpSkill;
        var height = EncumbranceSystem.LoadMod(load) * ((skill / (skill + 1300.0) * 22.2f) + 0.05f) * p / scaling;
        return height < 0.35f ? 0.35f : height;
    }

    /// <summary>
    /// Retail <c>JumpStaminaCost</c> (0x006B0A40 / 0x006B1910): for a player killer <c>(power + 1) × 100</c> truncated,
    /// else <c>ceil((load + 0.5) × power × 8 + 2)</c>.
    /// </summary>
    public static long JumpStaminaCost(float power, float load, bool playerKiller) =>
        playerKiller
            ? (long)((power + 1.0) * 100.0)
            : (long)Math.Ceiling(((load + 0.5) * power * 8.0) + 2.0);
}

/// <summary>
/// Retail <c>EncumbranceSystem</c> (acclient 11.4186 PORTAL/gameplay_misc/EncumbranceSystem.c; names and addresses cited,
/// nothing copied): burden capacity and the load ratio that slows running and jumping. Addresses are 11.4186 / 11.6096.
/// </summary>
public static class EncumbranceSystem
{
    /// <summary>
    /// Retail <c>EncumbranceCapacity</c> (0x004FCC00 / 0x004FD7A0): 150 × strength, plus strength × 30 per augmentation
    /// (at most 150); 0 for a strength of 0 or less.
    /// </summary>
    public static int EncumbranceCapacity(int strength, int augmentations)
    {
        if (strength <= 0)
        {
            return 0;
        }

        var bonus = 30 * augmentations;
        if (bonus < 0)
        {
            return 150 * strength;
        }

        return (150 * strength) + (strength * Math.Min(bonus, 150));
    }

    /// <summary>
    /// Retail <c>Load</c> (0x004FCC40 / 0x004FD7E0): burden / capacity; 3 without capacity; 0 for a negative burden.
    /// </summary>
    public static double Load(int capacity, int burden)
    {
        if (capacity <= 0)
        {
            return 3.0;
        }

        return burden >= 0 ? (double)burden / capacity : 0.0;
    }

    /// <summary>
    /// Retail <c>LoadMod</c> (0x004FCC70 / 0x004FD810): 1 below a load of 1, falling linearly to 0 at a load of 2.
    /// </summary>
    public static double LoadMod(float load) => load < 1f ? 1.0 : load < 2f ? 2.0 - load : 0.0;
}
