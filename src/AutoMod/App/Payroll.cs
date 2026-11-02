using System.Collections.Generic;
using System.Linq;
using AutoMod.Adapter;
using AutoMod.Domain.Workforce;

namespace AutoMod.App;

/// <summary>
/// 入睡时发薪（领域规则在 Payday）：勾选了上班的学徒上岗、付当天（这个晚班 + 第二天早班）的工资，付不起的罢工。
/// 玩家在对讲机学徒页勾选；工资用 ModHelper.AddCredit 扣，结果写进夜间日志（第二天早报里看）。
/// </summary>
public static class Payroll
{
    public static void Settle()
    {
        var store = GameApi.Store;
        if (store == null) return;
        var apps = Automation.State.Apprentices;
        int total = Payday.Settle(PayOrder(), store.playerCash);
        if (total > 0)
        {
            ModHelper.AddCredit(-total, false);
            store.AddNightLog(Domain.L.T("payroll.log_wages", total, Domain.L.Join("list", apps.Where(a => a.OnDuty).Select(a => a.Name))));
        }
        var striking = apps.Where(a => a.Striking).Select(a => a.Name).ToList();
        Automation.State.AddWages(StoreStation.GetDayCounter(), total, striking.Count);
        if (striking.Count > 0) store.AddNightLog(Domain.L.T("payroll.log_strike", Domain.L.Join("list", striking)));
    }

    /// <summary>发工资的先后：学徒列表的顺序，没指派流程的排在最后。资金不足时从最后一个开始罢工。</summary>
    public static List<Apprentice> PayOrder() =>
        Payday.Order(Automation.State.Apprentices, Automation.State.Rules.Schedules.Select(s => s.Id).ToList());

    /// <summary>今晚要付的工资（勾选了的学徒，对讲机里显示）。</summary>
    public static int Tonight() => Automation.State.Apprentices.Where(a => a.Paid).Sum(a => a.Wage);
}
