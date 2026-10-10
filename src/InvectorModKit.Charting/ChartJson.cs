using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InvectorModKit.Charting
{
    /// <summary>
    /// Reads and writes the human-editable custom chart format (*.chart.json). Example:
    /// <code>
    /// { "format": 1, "bpm": 128, "firstBeat": 0.412, "start": 0.0, "end": 181.5,
    ///   "generator": { "version": 3, "audioHash": "…" },          // remove to protect hand edits
    ///   "sections": [ { "t": 0, "mode": "Bounce" }, { "t": 30.4, "mode": "List" } ],
    ///   "notes": { "Casual": [ { "t": 6.1, "lane": 1, "color": 2 },
    ///                          { "t": 7.0, "strum": true, "len": 0.9 },
    ///                          { "t": 8.3, "lane": 0, "color": 0, "set": 1, "last": true } ], … } }
    /// </code>
    /// Times are seconds in the song's own audio. Omitted fields take their defaults
    /// (pickup, lane 1, color Down, no hold, no star set).
    /// </summary>
    public static class ChartJson
    {
        public static string Serialize(Chart chart)
        {
            var root = new JObject
            {
                ["format"] = chart.Format,
                ["bpm"] = Round(chart.Bpm, 4),
                ["firstBeat"] = Round(chart.FirstBeat, 4),
                ["start"] = Round(chart.StartTime, 3),
                ["end"] = Round(chart.EndTime, 3),
            };
            if (chart.Generator != null)
                root["generator"] = new JObject { ["version"] = chart.Generator.Version, ["audioHash"] = chart.Generator.AudioHash };

            var sections = new JArray();
            foreach (var s in chart.Sections)
                sections.Add(new JObject { ["t"] = Round(s.Start, 3), ["mode"] = s.Mode.ToString() });
            root["sections"] = sections;

            var notes = new JObject();
            foreach (Difficulty d in Enum.GetValues(typeof(Difficulty)))
            {
                var arr = new JArray();
                if (chart.Notes.TryGetValue(d, out var list))
                    foreach (var n in list)
                        arr.Add(WriteNote(n));
                notes[d.ToString()] = arr;
            }
            root["notes"] = notes;

            // One note per line keeps the file diffable and easy to hand-edit.
            using (var sw = new StringWriter(CultureInfo.InvariantCulture))
            {
                using (var w = new JsonTextWriter(sw) { Formatting = Formatting.Indented, Indentation = 1, IndentChar = '\t' })
                    root.WriteTo(w);
                return CompactNotes(sw.ToString());
            }
        }

        public static Chart Deserialize(string json)
        {
            var root = JObject.Parse(json);
            int format = (int?)root["format"] ?? 1;
            if (format > Chart.CurrentFormat)
                throw new FormatException($"Chart format {format} is newer than supported ({Chart.CurrentFormat}).");

            var chart = new Chart
            {
                Format = format,
                Bpm = Required<double>(root, "bpm"),
                FirstBeat = (double?)root["firstBeat"] ?? 0.0,
                StartTime = (double?)root["start"] ?? 0.0,
                EndTime = Required<double>(root, "end"),
            };
            if (chart.Bpm <= 0) throw new FormatException("bpm must be > 0.");

            if (root["generator"] is JObject gen)
                chart.Generator = new GeneratorStamp { Version = (int?)gen["version"] ?? 0, AudioHash = (string)gen["audioHash"] };

            if (root["sections"] is JArray sections)
                foreach (var s in sections)
                    chart.Sections.Add(new Section((double)s["t"], ParseMode((string)s["mode"])));
            if (chart.Sections.Count == 0)
                chart.Sections.Add(new Section(0, SectionMode.List));
            chart.Sections.Sort((a, b) => a.Start.CompareTo(b.Start));

            var notes = root["notes"] as JObject ?? throw new FormatException("Missing 'notes'.");
            foreach (Difficulty d in Enum.GetValues(typeof(Difficulty)))
            {
                var list = new List<Note>();
                if (notes[d.ToString()] is JArray arr)
                    foreach (var n in arr)
                        list.Add(ReadNote((JObject)n));
                list.Sort((a, b) => a.Time.CompareTo(b.Time));
                chart.Notes[d] = list;
            }
            return chart;
        }

        private static JObject WriteNote(Note n)
        {
            var o = new JObject { ["t"] = Round(n.Time, 4) };
            if (n.Kind == NoteKind.Strum)
            {
                o["strum"] = true;
                if (n.Length > 0) o["len"] = Round(n.Length, 3);
            }
            else
            {
                if (n.Lane != 1) o["lane"] = n.Lane;
                if (n.Color != NoteColor.Down) o["color"] = n.Color;
            }
            if (n.StarSet > 0) o["set"] = n.StarSet;
            if (n.LastInSet) o["last"] = true;
            return o;
        }

        private static Note ReadNote(JObject o)
        {
            bool strum = (bool?)o["strum"] ?? false;
            var n = new Note((double)o["t"], strum ? NoteKind.Strum : NoteKind.Pickup, (int?)o["lane"] ?? 1, (int?)o["color"] ?? NoteColor.Down)
            {
                Length = strum ? (float?)o["len"] ?? 0f : 0f,
                StarSet = (int?)o["set"] ?? 0,
                LastInSet = (bool?)o["last"] ?? false,
            };
            if (n.Lane < 0 || n.Lane > 2) throw new FormatException($"Note at {n.Time}s: lane must be 0..2.");
            if (n.Color < 0 || n.Color > 5) throw new FormatException($"Note at {n.Time}s: color must be 0..5.");
            return n;
        }

        private static SectionMode ParseMode(string s)
        {
            if (Enum.TryParse(s, true, out SectionMode m) && Enum.IsDefined(typeof(SectionMode), m)) return m;
            throw new FormatException($"Unknown section mode '{s}' (expected List, Bounce or Freefly).");
        }

        private static T Required<T>(JObject o, string key)
        {
            var token = o[key] ?? throw new FormatException($"Missing '{key}'.");
            return token.Value<T>();
        }

        private static double Round(double v, int digits) => Math.Round(v, digits);

        /// <summary>Puts each small note object on a single line.</summary>
        private static string CompactNotes(string indented)
        {
            var lines = indented.Replace("\r\n", "\n").Split('\n');
            var sb = new System.Text.StringBuilder(indented.Length);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.TrimStart('\t');
                // A note object starts with "{" on its own line and its first property is "t".
                if (trimmed == "{" && i + 1 < lines.Length && lines[i + 1].TrimStart('\t').StartsWith("\"t\":"))
                {
                    var one = new System.Text.StringBuilder(line.Substring(0, line.Length - trimmed.Length)).Append("{ ");
                    int j = i + 1;
                    for (; j < lines.Length; j++)
                    {
                        string t = lines[j].Trim('\t');
                        if (t.StartsWith("}")) { one.Append(" ").Append(t); break; }
                        one.Append(t.Replace("\": ", "\":")).Append(t.EndsWith(",") ? " " : "");
                    }
                    sb.Append(one).Append('\n');
                    i = j;
                    continue;
                }
                sb.Append(line).Append('\n');
            }
            return sb.ToString();
        }
    }
}
