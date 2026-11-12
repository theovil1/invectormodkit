using System.Collections.Generic;
using System.Linq;

namespace InvectorModKit.Marketplace.Client
{
    /// <summary>How a button label may wrap: between words, never inside one.</summary>
    public static class LabelText
    {
        /// <summary>
        /// The words of <paramref name="label"/> that must each fit whole on one line: split at spaces and line breaks.
        /// Japanese and Chinese have no spaces and break between any two characters, so their words are left out.
        /// </summary>
        public static IEnumerable<string> UnbreakableWords(string label)
        {
            if (string.IsNullOrEmpty(label)) return Enumerable.Empty<string>();
            return label.Split(new[] { ' ', '\n', '\r', '\t', ' ' }, System.StringSplitOptions.RemoveEmptyEntries)
                        .Where(word => !word.Any(BreaksAnywhere))
                        .ToList();
        }

        /// <summary>
        /// A line break after each Japanese or Chinese sentence (。？！) that more text follows. The game's text wraps only
        /// at spaces: without these, a long Japanese question is one "word" and best fit shrinks it to its smallest size.
        /// </summary>
        public static string SentencePerLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var sb = new System.Text.StringBuilder(text.Length + 8);
            for (int i = 0; i < text.Length; i++)
            {
                sb.Append(text[i]);
                bool end = text[i] == '。' || text[i] == '？' || text[i] == '！';
                if (end && i + 1 < text.Length && text[i + 1] != '\n') sb.Append('\n');
            }
            return sb.ToString();
        }

        private static bool BreaksAnywhere(char c) =>
            (c >= '぀' && c <= 'ヿ')    // hiragana, katakana
            || (c >= '㐀' && c <= '鿿') // CJK ideographs
            || (c >= '豈' && c <= '﫿') // CJK compatibility ideographs
            || (c >= '＀' && c <= '￯') // full-width forms
            || (c >= '　' && c <= '〿'); // CJK punctuation
    }
}
