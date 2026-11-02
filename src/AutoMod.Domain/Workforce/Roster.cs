using System.Collections.Generic;

namespace AutoMod.Domain.Workforce;

/// <summary>学徒名册：已雇学徒、名额。存档数据（ModState）继承它。</summary>
public class Roster
{
    public List<Apprentice> Apprentices { get; set; } = new();

    /// <summary>学徒名额（初始 1，通过中介扩编，最多 10）。</summary>
    public int Slots { get; set; } = 1;
    public const int MaxSlots = 10;
}
