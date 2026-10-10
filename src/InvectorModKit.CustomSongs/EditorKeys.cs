using System;
using System.Collections.Generic;
using HelloThere.Avicii;
using Newtonsoft.Json.Linq;
using Rewired;
using UnityEngine;

namespace InvectorModKit.CustomSongs
{
    /// <summary>
    /// The player's own keyboard keys for the chart editor: which key strums, which keys the four pickups and the two
    /// turns ask for, with the picture the game puts on its pickups for each (as the pickups do it: the gameplay
    /// keyboard map, the keyboard icon map). Rewritten whenever the bindings change.
    /// </summary>
    internal sealed partial class EditorSkin
    {
        private static readonly (string Name, int Action, Pole Pole)[] Actions =
        {
            ("strum", 12, Pole.Positive), ("left", 10, Pole.Positive), ("right", 9, Pole.Positive), ("down", 0, Pole.Positive),
            ("up", 11, Pole.Positive), ("turnLeft", 31, Pole.Negative), ("turnRight", 31, Pole.Positive),
        };

        /// <summary>True when the keys changed (the manifest must be written).</summary>
        private bool ExportKeys(Dictionary<Texture, Texture2D> readable)
        {
            KeyboardMap map;
            try { map = ApplicationSession.OnlineUserData?.gameplayKeyboardMap ?? RewiredInputHelper.GetSystemKeyboardMap(2); }
            catch (Exception) { return false; }
            if (map == null) return false;
            var keys = new JObject();
            var sprites = (JObject)_manifest["sprites"];
            foreach (var (name, action, pole) in Actions)
            {
                try
                {
                    var element = RewiredInputHelper.GetActionElementMap(action, pole, map);
                    if (element == null) continue;
                    var entry = new JObject { ["key"] = element.keyCode.ToString() };
                    // what the game writes on its pickups for that key, in the player's keyboard layout (Q on AZERTY…)
                    try
                    {
                        string label = RewiredInputHelper.GetKeyStringForDisplayOnPickups(element.keyCode);
                        if (!string.IsNullOrEmpty(label)) entry["label"] = label;
                    }
                    catch (Exception) { }
                    var icons = ControllerIconMapCollection.Instance;
                    var sprite = icons?.GetIcon(element.elementIdentifierId, element.axisRange == AxisRange.Negative ? Pole.Negative : Pole.Positive, ControlType.Keyboard, forPickup: true);
                    if (sprite != null && sprite != icons.ErrorSprite && Wanted(sprite))
                    {
                        if (sprites[sprite.name] == null) ExportSprite(sprite, readable, sprites);
                        entry["sprite"] = sprite.name;
                    }
                    keys[name] = entry;
                }
                catch (Exception e) { CustomSongsPlugin.Log.LogDebug($"Editor skin key {name}: {e.Message}"); }
            }
            if (keys.Count == 0 || JToken.DeepEquals(keys, _manifest["keys"])) return false;
            _manifest["keys"] = keys;
            return true;
        }
    }
}
