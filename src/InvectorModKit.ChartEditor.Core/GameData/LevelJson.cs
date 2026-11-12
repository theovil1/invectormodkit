using System.Linq;
using Newtonsoft.Json.Linq;

namespace InvectorModKit.ChartEditor
{
    /// <summary>
    /// A world's level file from its components' fields: the path (Bezier points, node rotations, arc lengths), its
    /// Freefly areas and circle, the road, the tunnel (Toblerone) and the hold walls (TobleroneWalls).
    /// </summary>
    public static class LevelJson
    {
        static readonly string[] Bookkeeping = { "m_GameObject", "m_Enabled", "m_Script", "m_Name", "feed" };

        public static JObject Build(JObject spline, JObject editor, JObject splineMesh, JToken roadMesh, JToken roadMaterial, JObject toblerone, JObject walls, JObject bounceWalls = null) => new JObject
        {
            ["start"] = spline["StartPosition"],
            ["length"] = spline["totalLength"],
            ["pos"] = new JArray(spline["pos"].Select(p => new JArray(p["x"], p["y"], p["z"]))),
            ["rot"] = new JArray(spline["rot"].Select(q => new JArray(q["x"], q["y"], q["z"], q["w"]))),
            ["acc"] = spline["accumulatedLengthsPrecision"],
            ["fly"] = editor["FlyAreas"],
            ["circle"] = new JObject { ["y"] = editor["CircleY"], ["radius"] = editor["CircleRadius"], ["bottom"] = editor["BottomLine"] },
            ["road"] = splineMesh == null ? JValue.CreateNull() : new JObject
            {
                ["mesh"] = roadMesh ?? splineMesh["Prefabs"]?.FirstOrDefault() ?? JValue.CreateNull(),
                ["material"] = roadMaterial ?? JValue.CreateNull(),
                ["scale"] = splineMesh["scale"],
                ["offsetY"] = splineMesh["splineOffsetY"],
            },
            ["tunnel"] = Settings(toblerone),
            ["holdWalls"] = Settings(walls),
            ["holdWallsBounce"] = Settings(bounceWalls), // the road's: one wall, its own size, width, height and material
        };

        /// <summary>A component's own settings, without Unity's bookkeeping fields and the link to the path.</summary>
        static JToken Settings(JObject component)
        {
            if (component == null) return JValue.CreateNull();
            var result = (JObject)component.DeepClone();
            foreach (string key in Bookkeeping) result.Remove(key);
            return result;
        }
    }
}
