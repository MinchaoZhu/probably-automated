using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using AutoMod.Domain.Workforce;
using AutoMod;
using AutoMod.UI.Intercom;
using AutoMod.UI.Kit;
using AutoMod.UI.Store;
using AutoMod.Domain;

using AutoMod.Adapter;

namespace AutoMod.UI;

/// <summary>界面根节点：mod 入口建的常驻物体上的组件，每帧驱动各功能（不打补丁，靠轮询游戏的公开状态）。</summary>
public class UiRoot : MonoBehaviour
{
    private void Start() => StartCoroutine(LocBridge.ApplyWhenReady());

    private void Update()
    {
        try
        {
            UiFixes.EnsureCjkFallback();
            GameWindows.Tick();
            UiFixes.Tick();
            ListDrag.Tick();
            Ui.UserScale = App.Settings.UiScale;
        }
        catch (Exception ex) { Log.Error($"[UI] {ex.Message}"); }
    }

    private void LateUpdate()
    {
        try { EscapeGuard.LateTick(); }
        catch (Exception ex) { Log.Error($"[UI] {ex.Message}"); }
    }
}
