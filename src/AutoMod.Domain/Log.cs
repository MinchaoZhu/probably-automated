using System;

namespace AutoMod.Domain;

/// <summary>mod 的日志出口：mod 入口把它接到游戏的 ModLog（F8 控制台）上；单元测试里不接（静默）。各层都通过它写日志，不直接引用插件入口。</summary>
public static class Log
{
    public static Action<string> InfoSink, WarnSink, ErrorSink;

    public static void Info(string m) => InfoSink?.Invoke(m);
    public static void Warn(string m) => WarnSink?.Invoke(m);
    public static void Error(string m) => ErrorSink?.Invoke(m);
}
