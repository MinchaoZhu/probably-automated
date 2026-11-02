using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace AutoMod.Domain;

/// <summary>
/// 领域数据的 JSON 编解码（存档、深拷贝）。用游戏自带的 Newtonsoft.Json（mod 不能自带 JSON 库）：
/// 只序列化公有属性（不含字段）、属性名驼峰但字典的键原样、不写 null、读时大小写不敏感、
/// 集合整体替换（不往初始值里追加）。
/// </summary>
public static class JsonCodec
{
    public static readonly JsonSerializerSettings Settings = new()
    {
        ContractResolver = new PropertiesOnlyResolver
        {
            NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false, OverrideSpecifiedNames = false },
        },
        NullValueHandling = NullValueHandling.Ignore,
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        MissingMemberHandling = MissingMemberHandling.Ignore,
        Formatting = Formatting.None,
    };

    public static string Serialize<T>(T value) => JsonConvert.SerializeObject(value, Settings);

    public static T Deserialize<T>(string json) => JsonConvert.DeserializeObject<T>(json, Settings);

    /// <summary>深拷贝：序列化后再读回。</summary>
    public static T Clone<T>(T value) => Deserialize<T>(Serialize(value));

    /// <summary>只要公有属性：System.Text.Json 的默认行为，Newtonsoft 默认还会带上公有字段。</summary>
    private sealed class PropertiesOnlyResolver : DefaultContractResolver
    {
        protected override List<System.Reflection.MemberInfo> GetSerializableMembers(System.Type objectType) =>
            base.GetSerializableMembers(objectType).Where(m => m.MemberType == System.Reflection.MemberTypes.Property).ToList();
    }
}
