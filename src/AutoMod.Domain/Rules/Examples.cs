using System.Collections.Generic;
using System.Linq;

namespace AutoMod.Domain.Rules;

/// <summary>
/// 自动换电示例：不用设置，全店找。两个任务：
/// ① 交换 低电量电池（电量从低到高）与 满电电池（电量从高到低）：按顺序一一互换位置，
///    满电电池多半在充能器里，换下来的电池正好进充能器接着充。
/// ② 将 未充满的闲置电池（不在机器、充能器里）放入 有空位的充能器。
/// </summary>
public static class Examples
{
    public static string BatteryName => L.T("example.battery.name");

    public static Schedule BatteryRotation(RulesData d)
    {
        string schName = BatteryName;
        var s = new Schedule { Id = Defs.NewId("sch"), Created = Defs.Now(), Name = Defs.UniqueName(d.Schedules.Select(x => x.Name), schName) };
        d.Schedules.Add(s);

        string Name(bool selector, string name)
        {
            string n = name;
            for (int i = 2; Scope.NameConflict(d, selector, n, s.Id) != null; i++) n = L.T("transfer.dup_suffix", name, i);
            return n;
        }
        ObjectDef Param(string nameKey, double v, string hintKey)
        {
            string name = L.T(nameKey);
            string hint = L.T(hintKey);
            var o = new ObjectDef { Id = Defs.NewId("o"), Name = Name(false, name), Owner = s.Id, Value = v, Unit = "%", Hint = hint };
            d.Objects.Add(o);
            return o;
        }
        SelectorDef Sel(string nameKey, string from, string kind, CondGroup where = null, SortSpec sort = null, string slot = null)
        {
            string name = L.T(nameKey);
            var x = new SelectorDef { Id = Defs.NewId("s"), Name = Name(true, name), Owner = s.Id, From = { from }, Kind = kind, Where = where ?? new CondGroup(), Sort = sort, Slot = slot };
            d.Selectors.Add(x);
            return x;
        }
        Cond Pct(string op, ObjectDef k) => Cond.Cmp("energyPct", op, Expr.Ref(TermKind.Const, k.Id));
        Cond Is(string prop, int v) => Cond.Cmp(prop, "=", Expr.Of(v));

        var kFull = Param("example.battery.param.full", 95, "example.battery.param.full.hint");
        var kLow = Param("example.battery.param.low", 30, "example.battery.param.low.hint");

        var machines = Sel("example.battery.sel.machines", Places.Shop, "machine");
        var low = Sel("example.battery.sel.low_battery", machines.Id, "battery", new CondGroup { Items = { Pct("<", kLow) } }, new SortSpec { Prop = "energyPct", Desc = false }, slot: "battery");
        var full = Sel("example.battery.sel.full_battery", Places.Shop, "battery", new CondGroup { Items = { Is("inBatterySlot", 0), Pct(">=", kFull) } }, new SortSpec { Prop = "energyPct", Desc = true });
        var waiting = Sel("example.battery.sel.idle_battery", Places.Shop, "battery", new CondGroup { Items = { Is("inStorage", 1), Pct("<", kFull) } }, new SortSpec { Prop = "energyPct", Desc = false });
        var free = Sel("example.battery.sel.free_recharger", Places.Shop, "recharger", new CondGroup { Items = { Cond.Of("hasRoom") } });

        s.Rules.Add(new WorkRule
        {
            Id = Defs.NewId("r"), Name = L.T("example.battery.rule.swap"),
            Pre = { new PreCond { RefKind = PreRef.Selector, Ref = low.Id, Test = PreTest.NotEmpty } },
            Actions = { new ActionDef { Verb = ActVerb.Swap, List = low.Id, To = full.Id } },
        });
        s.Rules.Add(new WorkRule
        {
            Id = Defs.NewId("r"), Name = L.T("example.battery.rule.charge"),
            Actions = { new ActionDef { Verb = ActVerb.Move, List = waiting.Id, To = free.Id } },
        });
        Scope.Normalize(d);
        return s;
    }
}
