using System.Linq;
using InvectorModKit.Marketplace.Client;
using UnityEngine;
using UnityEngine.UI;

namespace InvectorModKit.Marketplace.Ui
{
    /// <summary>
    /// Keeps a best-fit label from cutting a word in two: Unity's best fit picks the largest size at which the text
    /// fits, even when that means breaking a long word ("Niezainstalowan / e"). This lowers the largest size until
    /// every word fits whole on one line (down to the label's smallest size), whenever the text or the width changes.
    /// </summary>
    internal sealed class WordFit : MonoBehaviour
    {
        private Text _text;
        private int _largest;
        private string _fittedText;
        private float _fittedWidth = -1;

        public static void Add(Text text)
        {
            var fit = text.gameObject.GetComponent<WordFit>() ?? text.gameObject.AddComponent<WordFit>();
            fit._text = text;
            fit._largest = text.resizeTextMaxSize;
            fit._fittedText = null;
        }

        private void LateUpdate()
        {
            if (_text == null || !_text.resizeTextForBestFit) return;
            float width = _text.rectTransform.rect.width;
            if (_text.text == _fittedText && Mathf.Approximately(width, _fittedWidth)) return;
            _fittedText = _text.text;
            _fittedWidth = width;
            if (width <= 0) return;

            int size = _largest;
            var words = LabelText.UnbreakableWords(_text.text).ToList();
            if (words.Count > 0)
            {
                var settings = _text.GetGenerationSettings(Vector2.zero);
                settings.resizeTextForBestFit = false;
                settings.horizontalOverflow = HorizontalWrapMode.Overflow;
                var generator = _text.cachedTextGeneratorForLayout;
                foreach (var word in words)
                {
                    while (size > _text.resizeTextMinSize)
                    {
                        settings.fontSize = size;
                        if (generator.GetPreferredWidth(word, settings) / _text.pixelsPerUnit <= width) break;
                        size--;
                    }
                }
            }
            if (_text.resizeTextMaxSize != size) _text.resizeTextMaxSize = size;
        }
    }
}
