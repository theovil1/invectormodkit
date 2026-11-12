namespace InvectorModKit.Marketplace.Client
{
    /// <summary>
    /// The marketplace's one-line text fields, typed key by key: Backspace ('\b') erases the last letter; Ctrl+A
    /// (<see cref="SelectAll"/>) selects the whole text, which the next letter replaces and Backspace clears.
    /// </summary>
    public static class TypedText
    {
        /// <summary>Ctrl+A among the typed characters (the character Windows types for it).</summary>
        public const char SelectAll = '\u0001';

        /// <summary>Applies this frame's keys; true when the text changed (a selection alone changes only how it shows).</summary>
        public static bool Apply(ref string text, ref bool selected, string typed, int max)
        {
            text = text ?? "";
            bool changed = false;
            foreach (char c in typed ?? "")
            {
                if (c == SelectAll) { selected = text.Length > 0; continue; }
                if (c == '\b')
                {
                    if (selected) { text = ""; selected = false; changed = true; }
                    else if (text.Length > 0) { text = text.Substring(0, text.Length - 1); changed = true; }
                }
                else if (!char.IsControl(c))
                {
                    if (selected) { text = ""; selected = false; changed = true; }
                    if (text.Length < max) { text += c; changed = true; }
                }
            }
            return changed;
        }

        /// <summary>The field while typing: the text and the caret, the text in the accent colour while it is selected.</summary>
        public static string Show(string text, bool selected) => (selected ? "<color=#E9B8FF>" + text + "</color>" : text) + "▌";
    }
}
