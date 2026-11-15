using System;
using System.Linq;
using AutoMod.Domain.Reports;
using AutoMod.Domain.Rules;

namespace AutoMod.Domain.Simulation;

/// <summary>
/// 超科技涡轮增压器，照游戏 MachineTurboBoosterAdv / ToolDirectory 的规则：每晚从电池槽取“充能速度”点电（默认 5），
/// 存进当前电量（可以存好几次）；电量满 15 即就绪。用于机器（标准机器或标记了可增压的）时立即完成机器当前的周期，
/// 电量 −15。复制店铺时游戏只回答得了那一刻能不能用，所以能不能用按模拟里的电量判断，不问游戏。
/// </summary>
public partial class SimWorld
{
    /// <summary>增压一次要的电量（游戏的 POWER_TARGET）。</summary>
    public const int BoostCost = 15;

    public const string BoostChargeTag = "tag:CURRENT_CHARGE_TAG";
    public const string BoostReadyTag = "has:TURBO_READY_TAG";
    private const string BoostRateTag = "tag:HYPERTECH_CHARGE_RATE_TAG";

    public static bool IsHypertechBooster(SimItem it) => it != null && (it.Kind == "turbo_booster_hypertech" || it.N("has:TURBO_BOOST_HYPERTECH") == 1);

    private static bool Boostable(SimItem m) =>
        m != null && (m.N("has:STANDARD_MACHINE_TAG") == 1 || m.N("has:TURBO_BOOST_ELIGIBLE_TAG") == 1 || IsDnaEditor(m));

    private static void SetCharge(SimItem b, double charge)
    {
        b.Num[BoostChargeTag] = charge;
        b.Num[BoostReadyTag] = charge >= BoostCost ? 1 : 0;
    }

    /// <summary>增压器作用于机器：电量够、机器能增压才行；反过来（机器作用于增压器）不行。不涉及增压器时为 null。</summary>
    private static bool? BoostAllowed(SimItem tool, SimItem target)
    {
        if (IsHypertechBooster(target) && !IsHypertechBooster(tool)) return false;
        if (!IsHypertechBooster(tool)) return null;
        return tool.N(BoostChargeTag) >= BoostCost && Boostable(target);
    }

    /// <summary>立即完成机器当前的周期：和夜里推进一个周期一样（没电时机器不动，电量照样用掉，和游戏一致）。</summary>
    private StepResult Boost(SimItem tool, SimItem target)
    {
        if (!IsHypertechBooster(tool)) return null;
        if (BoostAllowed(tool, target) != true) return StepResult.Fail(L.T("sim.fail.boost"), FailCode.NotAllowed);
        CycleOnce(target);
        SetCharge(tool, Math.Max(0, tool.N(BoostChargeTag) - BoostCost));
        return StepResult.Done(L.T("sim.step.boosted", target.Name));
    }

    private void CycleOnce(SimItem m)
    {
        if (IsDnaEditor(m)) { DnaNight(m); return; }
        var seed = m.Children.FirstOrDefault(c => Has(c, "seed") && c.InSlot == SlotNames.Prefix + "seed");
        if (seed != null) { SeedNight(seed); return; }
        if (m.N("working") == 1 && m.N("progressMax") > 0) MachineNight(m);
    }

    /// <summary>夜里充能：电池槽里的电池电量够“充能速度”就取这么多，存进增压器。</summary>
    private static void BoosterNight(SimItem b)
    {
        double rate = b.N(BoostRateTag);
        if (rate <= 0) rate = 5;
        var battery = b.Children.FirstOrDefault(c => c.InBatterySlot);
        if (battery != null && battery.N("energy") >= rate)
        {
            battery.Num["energy"] = battery.N("energy") - rate;
            SetCharge(b, b.N(BoostChargeTag) + rate);
        }
        else SetCharge(b, b.N(BoostChargeTag));
    }
}
