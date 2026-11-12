using AssetsTools.NET;

namespace InvectorModKit.ChartEditor.Tests;

/// <summary>Typetree value fields built by hand, shaped the way AssetsTools.NET 3 reads them from a game file.</summary>
static class Fields
{
    static AssetTypeValueField Leaf(string name, string type, AssetTypeValue value) => new()
    {
        TemplateField = new AssetTypeTemplateField { Name = name, Type = type, ValueType = value.ValueType, HasValue = true, Children = new() },
        Value = value,
        Children = new(),
    };

    public static AssetTypeValueField Int(string name, int v) => Leaf(name, "int", new AssetTypeValue(v));
    public static AssetTypeValueField Long(string name, long v) => Leaf(name, "SInt64", new AssetTypeValue(v));
    public static AssetTypeValueField ULong(string name, ulong v) => Leaf(name, "UInt64", new AssetTypeValue(v));
    public static AssetTypeValueField UInt8(string name, byte v) => Leaf(name, "UInt8", new AssetTypeValue(v));
    public static AssetTypeValueField Float(string name, float v) => Leaf(name, "float", new AssetTypeValue(v));
    public static AssetTypeValueField Double(string name, double v) => Leaf(name, "double", new AssetTypeValue(v));
    public static AssetTypeValueField Bool(string name, bool v) => Leaf(name, "bool", new AssetTypeValue(v));
    public static AssetTypeValueField Str(string name, string v) => Leaf(name, "string", new AssetTypeValue(v));

    public static AssetTypeValueField Obj(string name, params AssetTypeValueField[] children) => Typed(name, name, children);

    static AssetTypeValueField Typed(string name, string type, AssetTypeValueField[] children) => new()
    {
        TemplateField = new AssetTypeTemplateField { Name = name, Type = type, Children = children.Select(c => c.TemplateField).ToList() },
        Children = children.ToList(),
    };

    /// <summary>A vector: the field holds one "Array" child, whose children are the items.</summary>
    public static AssetTypeValueField Array(string name, params AssetTypeValueField[] items)
    {
        var data = items.Length > 0 ? items[0].TemplateField : new AssetTypeTemplateField { Name = "data", Type = "int", ValueType = AssetValueType.Int32, Children = new() };
        var array = new AssetTypeValueField
        {
            TemplateField = new AssetTypeTemplateField
            {
                Name = "Array", Type = "Array", IsArray = true, ValueType = AssetValueType.Array,
                Children = new() { new AssetTypeTemplateField { Name = "size", Type = "int", ValueType = AssetValueType.Int32, Children = new() }, data },
            },
            Value = new AssetTypeValue(AssetValueType.Array, new AssetTypeArrayInfo(items.Length)),
            Children = items.ToList(),
        };
        return Typed(name, "vector", new[] { array });
    }

    /// <summary>A vector of UInt8, which AssetsTools.NET reads as one byte array value.</summary>
    public static AssetTypeValueField Bytes(string name, byte[] bytes)
    {
        var array = new AssetTypeValueField
        {
            TemplateField = new AssetTypeTemplateField
            {
                Name = "Array", Type = "Array", IsArray = true, ValueType = AssetValueType.ByteArray,
                Children = new() { new AssetTypeTemplateField { Name = "size", Type = "int", ValueType = AssetValueType.Int32, Children = new() },
                                   new AssetTypeTemplateField { Name = "data", Type = "UInt8", ValueType = AssetValueType.UInt8, Children = new() } },
            },
            Value = new AssetTypeValue(bytes, false),
            Children = new(),
        };
        return Typed(name, "vector", new[] { array });
    }

    public static AssetTypeValueField PPtr(string name, string type, int fileId, long pathId) =>
        Typed(name, $"PPtr<{type}>", new[] { Int("m_FileID", fileId), Long("m_PathID", pathId) });
}
