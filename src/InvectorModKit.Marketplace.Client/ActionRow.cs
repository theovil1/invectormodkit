using System;
using System.Collections.Generic;
using System.Linq;

namespace InvectorModKit.Marketplace.Client
{
    /// <summary>
    /// A row of buttons built again only when its labels change (building them again loses the player's focus), whose
    /// clicks run the actions given last: the sheet of another item with the same buttons acts on that item.
    /// </summary>
    public sealed class ActionRow
    {
        private string _key;
        private Action[] _acts = new Action[0];

        /// <summary>Takes the row's buttons; true when their labels (or <paramref name="extraKey"/>) changed and they must be built again.</summary>
        public bool Set(IEnumerable<string> labels, IEnumerable<Action> acts, string extraKey = "")
        {
            _acts = acts.ToArray();
            string key = string.Join("\n", labels) + "|" + extraKey;
            if (key == _key) return false;
            _key = key;
            return true;
        }

        /// <summary>The next <see cref="Set"/> builds the buttons again.</summary>
        public void Reset() => _key = null;

        /// <summary>Runs what button <paramref name="index"/> does now.</summary>
        public void Invoke(int index)
        {
            if (index >= 0 && index < _acts.Length) _acts[index]?.Invoke();
        }
    }
}
