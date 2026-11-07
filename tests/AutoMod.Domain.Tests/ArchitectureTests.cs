using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// 架构测试：扫描源码里的 using 与完全限定引用，检查层与功能之间的依赖方向。
/// </summary>
internal static class ArchitectureTests
{
    /// <summary>插件里的层（按文件夹）→ 允许引用的 mod 命名空间前缀。</summary>
    private static readonly (string folder, string ns, string[] allowed)[] Layers =
    {
        ("Adapter/", "AutoMod.Adapter", new[] { "AutoMod.Domain" }),
        ("App/", "AutoMod.App", new[] { "AutoMod.Domain", "AutoMod.Adapter" }),
        ("UI/Kit/", "AutoMod.UI.Kit", new[] { "AutoMod.Domain", "AutoMod.Adapter" }),
        ("UI/Intercom/", "AutoMod.UI.Intercom", Feature),
        ("UI/Schedules/", "AutoMod.UI.Schedules", Feature),
        ("UI/Simulator/", "AutoMod.UI.Simulator", Feature),
        ("UI/Store/", "AutoMod.UI.Store", Feature),
        ("UI/Guide/", "AutoMod.UI.Guide", Feature),
    };

    private static string[] Feature => new[] { "AutoMod.Domain", "AutoMod.Adapter", "AutoMod.App", "AutoMod.UI.Kit", "AutoMod.UI.Intercom", "AutoMod.UI.Schedules", "AutoMod.UI.Simulator", "AutoMod.UI.Store", "AutoMod.UI.Guide" };

    /// <summary>UI 各功能对外的入口类：其他功能只能用这些类型。</summary>
    private static readonly Dictionary<string, string[]> Entries = new()
    {
        ["AutoMod.UI.Intercom"] = new[] { "MainWindow", "AgencyWindow" },
        ["AutoMod.UI.Schedules"] = new[] { "SchedulePage", "ScheduleEditor", "ObjectDialog" },
        ["AutoMod.UI.Simulator"] = new[] { "SimulatorWindow" },
        ["AutoMod.UI.Store"] = new[] { "DeviceNameDialog" },
        ["AutoMod.UI.Guide"] = new[] { "Handbook", "HandbookPages" },
    };

    private static readonly Regex Token = new(@"\bAutoMod(?:\.\w+)+");

    public static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        string root = FindRoot();
        string plugin = Path.Combine(root, "src", "AutoMod");
        string domain = Path.Combine(root, "src", "AutoMod.Domain");

        test("架构：领域不引用游戏和插件", () =>
        {
            var bad = new List<string>();
            foreach (var f in Sources(domain))
            {
                var s = Code(File.ReadAllText(f));
                foreach (Match m in Regex.Matches(s, @"\b(UnityEngine|Il2Cpp\w*|BepInEx|HarmonyLib|GameItem|PlayerStore|AutoMod\.(Adapter|App|UI))\b"))
                    bad.Add($"{Rel(root, f)}：{m.Value}");
            }
            check(bad.Count == 0, "领域里出现了游戏或插件引用：" + string.Join("；", bad.Distinct().Take(10)));
        });

        test("架构：升级步骤只改 JSON，不引用当前模型", () =>
        {
            var bad = new List<string>();
            foreach (var f in Sources(Path.Combine(domain, "Persistence", "Migrations")))
                foreach (Match m in Regex.Matches(Code(File.ReadAllText(f)), @"\bAutoMod\.Domain\.(Rules|Workforce|Reports|Simulation)\b|\b(ModState|RulesData|SelectorDef|ActionDef|Schedule|Apprentice)\b"))
                    bad.Add($"{Rel(root, f)}：{m.Value}");
            check(bad.Count == 0, "升级步骤引用了当前模型：" + string.Join("；", bad.Distinct().Take(10)));
        });

        test("架构：插件各层的依赖方向", () =>
        {
            var bad = new List<string>();
            var featureTypes = FeatureTypes(plugin);
            var known = Namespaces(root);
            foreach (var f in Sources(plugin))
            {
                string rel = Rel(plugin, f);
                var layer = Layers.FirstOrDefault(l => rel.StartsWith(l.folder));
                if (layer.folder == null) continue; // ModEntry.cs、UI/UiRoot.cs：组装层，不限制
                var s = Code(File.ReadAllText(f));
                foreach (Match m in Token.Matches(s))
                {
                    // 引用的命名空间 = 已知命名空间里最长的前缀（AutoMod.UI.Kit.Ui.Guard → AutoMod.UI.Kit）
                    string ns = known.Where(k => m.Value == k || m.Value.StartsWith(k + ".")).OrderByDescending(k => k.Length).FirstOrDefault();
                    if (ns == null || ns == "AutoMod" || ns.StartsWith(layer.ns)) continue;
                    if (!layer.allowed.Any(a => ns == a || ns.StartsWith(a + "."))) bad.Add($"{rel} → {ns}");
                }
                if (Regex.IsMatch(s, @"(?<![\w.])ModEntry\b")) bad.Add($"{rel} → ModEntry（mod 的 id 和版本在 App.ModInfo）");
                // UI 功能之间只能用对方的入口类
                if (Entries.ContainsKey(layer.ns))
                    foreach (var (ns, types) in featureTypes)
                    {
                        if (ns == layer.ns) continue;
                        foreach (var t in types.Except(Entries[ns]))
                            if (Regex.IsMatch(s, $@"(?<![\w.]){t}\b")) bad.Add($"{rel} → {ns}.{t}（不是入口类）");
                    }
            }
            check(bad.Count == 0, "违反依赖规则：\n    " + string.Join("\n    ", bad.Distinct()));
        });

        // 发布版要在游戏的 mod 扫描器里不需要任何能力（manifest 里 <Capabilities />），也不打补丁。
        // 规则对照游戏的 ModScanner：反射只算按名字查成员 / 类型、动态加载；提到反射的类型名（MemberInfo）不算
        test("架构：发布代码不打补丁、不碰文件 / 网络 / 进程 / 反射", () =>
        {
            var bad = new List<string>();
            var forbidden = new Regex(@"\b(HarmonyLib|HarmonyPatch|System\.IO\.(File|Directory|FileStream|StreamWriter|StreamReader)|(?<![\w.])(File|Directory)\.\w+\(|System\.Net|System\.Diagnostics\.Process|Application\.OpenURL|\.(GetMethod|GetField|GetProperty|InvokeMember|GetTypes)\(|Type\.GetType\(|Assembly\.Load|Reflection\.Emit|Activator\.|DllImport|unsafe|GetManifestResourceStream|\bdynamic\b)");
            foreach (var dir in new[] { plugin, domain })
                foreach (var f in Sources(dir))
                {
                    string rel = Rel(root, f);
                    foreach (Match m in forbidden.Matches(Code(File.ReadAllText(f)))) bad.Add($"{rel}：{m.Value}");
                }
            check(bad.Count == 0, "发布代码里出现了需要能力或补丁的用法：" + string.Join("；", bad.Distinct().Take(15)));
        });

        // mod 加载阶段读当前语言，后面的 mod 在加载阶段取本地化表时，游戏的本地化初始化会失败。
        test("架构：mod 加载阶段不读游戏语言", () =>
        {
            string entry = Code(File.ReadAllText(Path.Combine(plugin, "ModEntry.cs")));
            string bridge = Code(File.ReadAllText(Path.Combine(plugin, "Adapter", "LocBridge.cs")));
            string init = Regex.Match(bridge, @"void Init\(\)\s*\{.*?\n    \}", RegexOptions.Singleline).Value;
            var reads = new Regex(@"\b(SelectedLocale|GetCurrentLocale|GetCurrentLocaleCode|CurrentCode|Apply)\b");
            check(init.Length > 0, "没找到 LocBridge.Init");
            check(!reads.IsMatch(entry) && !reads.IsMatch(init), "ModEntry 或 LocBridge.Init 读了当前语言");
        });
    }

    /// <summary>UI 各功能里定义的类型。</summary>
    private static Dictionary<string, List<string>> FeatureTypes(string plugin)
    {
        var map = new Dictionary<string, List<string>>();
        foreach (var f in Sources(plugin))
        {
            var layer = Layers.FirstOrDefault(l => Rel(plugin, f).StartsWith(l.folder));
            if (layer.folder == null || !Entries.ContainsKey(layer.ns)) continue;
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"^(?:public |internal )?(?:static |sealed |partial )*(?:class|struct|enum) (\w+)", RegexOptions.Multiline))
            {
                if (!map.TryGetValue(layer.ns, out var list)) map[layer.ns] = list = new List<string>();
                list.Add(m.Groups[1].Value);
            }
        }
        return map;
    }

    /// <summary>源码里声明过的全部命名空间。</summary>
    private static List<string> Namespaces(string root) =>
        Sources(Path.Combine(root, "src")).SelectMany(f => Regex.Matches(File.ReadAllText(f), @"^namespace ([\w.]+);", RegexOptions.Multiline).Select(m => m.Groups[1].Value))
            .Distinct().ToList();

    /// <summary>去掉注释、字符串和命名空间声明，只看代码。</summary>
    private static string Code(string s)
    {
        s = Regex.Replace(s, @"^namespace [\w.]+;", "", RegexOptions.Multiline);
        s = Regex.Replace(s, @"//[^\n]*", "");
        s = Regex.Replace(s, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(s, @"\$?@?""(?:[^""\\\n]|\\.)*""", "\"\"");
    }

    private static IEnumerable<string> Sources(string dir) =>
        Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static string Rel(string root, string f) => Path.GetRelativePath(root, f).Replace('\\', '/');

    private static string FindRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "ProbablyStolenMod.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("找不到仓库根目录");
    }
}
