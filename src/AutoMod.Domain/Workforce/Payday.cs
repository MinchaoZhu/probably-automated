using System.Collections.Generic;
using System.Linq;

namespace AutoMod.Domain.Workforce;

/// <summary>
/// 发薪日：每晚入睡时结算“当天”（这个晚班 + 第二天早班）谁上岗。勾选了上班的学徒上岗、付当天工资、AP 回满；
/// 现金不够时从名册后面往前取消，直到付得起，被取消的这一天罢工。
/// </summary>
public static class Payday
{
    /// <summary>
    /// 发工资的先后：学徒列表的顺序（也是执行的顺序），没指派流程的排在最后。资金不足时从最后一个开始罢工。
    /// </summary>
    public static List<Apprentice> Order(IEnumerable<Apprentice> apprentices, IList<string> scheduleIds) =>
        apprentices.OrderBy(a => a.Duties.Any(d => scheduleIds.Contains(d.ScheduleId)) ? 0 : 1).ToList(); // OrderBy 是稳定排序

    /// <summary>设置每个学徒的 OnDuty / Striking、AP 回满，返回要付的工资总额（不超过 cash）。</summary>
    public static int Settle(IList<Apprentice> apprentices, int cash)
    {
        foreach (var a in apprentices)
        {
            a.Striking = false;
            a.OnDuty = a.Paid;
            a.ApUsed = 0;
        }
        int total = apprentices.Where(a => a.OnDuty).Sum(a => a.Wage);
        foreach (var a in apprentices.Reverse())
        {
            if (total <= cash) break;
            if (!a.OnDuty) continue;
            a.OnDuty = false;
            a.Striking = true;
            total -= a.Wage;
        }
        return total;
    }
}
