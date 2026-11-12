using System;
using System.Linq;
using AssetsTools.NET;
using Newtonsoft.Json.Linq;

namespace InvectorModKit.ChartEditor
{
    /// <summary>
    /// A game object's fields (as AssetsTools.NET reads them from the type tree) as JSON, with Unity's field names and
    /// nesting. The result must parse in a browser: non-finite floats become null and integers beyond 2^53 strings.
    /// </summary>
    public static class TypeTreeJson
    {
        /// <summary>Byte arrays longer than this are mesh or texture data, written elsewhere: only their length is kept.</summary>
        public const int MaxBytes = 4096;

        const long MaxSafeInteger = 9007199254740991; // 2^53 - 1, the largest integer a JavaScript number holds exactly

        /// <param name="pptr">Called for every object reference with a non-zero path id (file id, path id); its result is used as is.</param>
        public static JToken Convert(AssetTypeValueField field, Func<int, long, JToken> pptr)
        {
            if (field.TypeName != null && field.TypeName.StartsWith("PPtr<", StringComparison.Ordinal))
            {
                long pathId = field["m_PathID"].AsLong;
                return pathId == 0 ? JValue.CreateNull() : pptr(field["m_FileID"].AsInt, pathId);
            }

            var value = field.Value;
            if (value != null)
            {
                switch (value.ValueType)
                {
                    case AssetValueType.Bool: return new JValue(value.AsBool);
                    case AssetValueType.Int8: return new JValue(value.AsSByte);
                    case AssetValueType.UInt8: return new JValue(value.AsByte);
                    case AssetValueType.Int16: return new JValue(value.AsShort);
                    case AssetValueType.UInt16: return new JValue(value.AsUShort);
                    case AssetValueType.Int32: return new JValue(value.AsInt);
                    case AssetValueType.UInt32: return new JValue(value.AsUInt);
                    case AssetValueType.Int64:
                    {
                        long v = value.AsLong;
                        return v > MaxSafeInteger || v < -MaxSafeInteger ? new JValue(v.ToString()) : new JValue(v);
                    }
                    case AssetValueType.UInt64:
                    {
                        ulong v = value.AsULong;
                        return v > MaxSafeInteger ? new JValue(v.ToString()) : new JValue(v);
                    }
                    case AssetValueType.Float:
                    {
                        float v = value.AsFloat;
                        return float.IsNaN(v) || float.IsInfinity(v) ? JValue.CreateNull() : new JValue(v);
                    }
                    case AssetValueType.Double:
                    {
                        double v = value.AsDouble;
                        return double.IsNaN(v) || double.IsInfinity(v) ? JValue.CreateNull() : new JValue(v);
                    }
                    case AssetValueType.String: return new JValue(value.AsString);
                    case AssetValueType.ByteArray:
                    {
                        byte[] bytes = value.AsByteArray ?? new byte[0];
                        return bytes.Length > MaxBytes ? (JToken)new JObject { ["omitted"] = bytes.Length } : new JArray(bytes.Select(b => (int)b));
                    }
                    case AssetValueType.Array: return new JArray(field.Children.Select(c => Convert(c, pptr)));
                    case AssetValueType.ManagedReferencesRegistry: return JValue.CreateNull();
                }
            }

            // A vector or map: its only child is the array itself.
            if (field.Children.Count == 1 && field.Children[0].TemplateField.IsArray) return Convert(field.Children[0], pptr);

            var obj = new JObject();
            foreach (var child in field.Children) obj[child.FieldName] = Convert(child, pptr);
            return obj;
        }
    }
}
