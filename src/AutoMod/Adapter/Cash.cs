using System;
using AutoMod;
using AutoMod.Domain;

namespace AutoMod.Adapter;

/// <summary>店铺现金：中介招聘费、扩编费、培训费从这里扣。</summary>
public static class Cash
{
    public static int Current => GameApi.Store?.playerCash ?? 0;

    /// <summary>付得起就扣款并返回 true；付不起不扣、返回 false。</summary>
    public static bool TryPay(int amount, string what)
    {
        var store = GameApi.Store;
        if (store == null) return false;
        if (amount <= 0) return true;
        if (store.playerCash < amount) return false;
        int before = store.playerCash;
        store.ModCash(-amount, true);
        Log.Info($"[Cash] {what}：-{amount}（{before} → {store.playerCash}）");
        return true;
    }
}
