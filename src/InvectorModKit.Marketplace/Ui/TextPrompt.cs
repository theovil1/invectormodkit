using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace InvectorModKit.Marketplace.Ui
{
    /// <summary>
    /// One line of text to type or paste (Ctrl+V), in the game's question dialog: a field drawn like the
    /// game's buttons, a hint that turns into an error when the text is not accepted, OK / cancel.
    /// </summary>
    internal sealed class TextPrompt
    {
        private readonly GameObject _root;
        private readonly Text _question, _field, _hint;
        private readonly ButtonWithEvents _ok;
        private Func<string, string> _validate;
        private Action<string> _onOk;
        private string _text = "", _defaultHint = "";

        public bool Visible => _root.activeSelf;

        public TextPrompt(MarketplaceModal modal)
        {
            _root = GameUi.Clone(GameUi.DialogTemplate, modal.transform, "Prompt", stillImage: true);
            var group = _root.GetComponent<CanvasGroup>();
            if (group) { group.alpha = 1; group.interactable = true; group.blocksRaycasts = true; }
            var backdrop = GameUi.Find<Image>(_root, "Background");
            if (backdrop) backdrop.color = new Color(0, 0, 0, 0.8f);
            var box = (RectTransform)_root.transform.Find("QuestionBox");
            GameUi.Place(box, 0, 0, 1040, 440);
            MarketplaceModal.AddFill(box);

            _question = GameUi.Find<Text>(box.gameObject, "Text");
            _question.alignment = TextAnchor.MiddleCenter;
            _question.resizeTextForBestFit = true;
            _question.resizeTextMinSize = 20;
            _question.resizeTextMaxSize = 36;
            GameUi.Place(_question, 0, 140, 940, 80);

            // The field: one of the game's dialog buttons, not clickable, holding the text.
            var fieldButton = modal.MakeButton(box, "", 0, 50, 900, 70, () => { }, 28);
            fieldButton.interactable = false;
            var colors = fieldButton.colors;
            colors.disabledColor = new Color32(0x24, 0x20, 0x2A, 0xFF);
            fieldButton.colors = colors;
            _field = fieldButton.GetComponentInChildren<Text>(true);
            _field.resizeTextForBestFit = true;
            _field.color = Color.white;

            _hint = GameUi.Text(box, "Hint", modal.Regular, 24, TextAnchor.MiddleCenter, GameUi.Muted);
            GameUi.Place(_hint, 0, -20, 940, 50);

            _ok = GameUi.Find<ButtonWithEvents>(box.gameObject, "Answer Yes");
            var cancel = GameUi.Find<ButtonWithEvents>(box.gameObject, "Answer No");
            GameUi.Place(_ok, -165, -130, 300, 84);
            GameUi.Place(cancel, 165, -130, 300, 84);
            MarketplaceModal.StyleButton(_ok, "VALIDER", 34, Submit);
            MarketplaceModal.StyleButton(cancel, "ANNULER", 34, Close);
            _ok.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnRight = cancel };
            cancel.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = _ok };
            _root.SetActive(false);
        }

        /// <param name="validate">Returns why the text is refused, or null when it is fine.</param>
        public void Ask(string question, string hint, string initial, Func<string, string> validate, Action<string> onOk)
        {
            _question.text = question;
            _defaultHint = hint;
            _hint.text = hint;
            _hint.color = GameUi.Muted;
            _text = initial ?? "";
            _validate = validate;
            _onOk = onOk;
            Refresh();
            _root.SetActive(true);
            _root.transform.SetAsLastSibling();
            GameUi.Select(_ok.gameObject);
        }

        /// <summary>Called every frame while visible: typing, paste, Enter, Escape.</summary>
        public void Tick()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) { Submit(); return; }
            bool control = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool changed = false;
            if (control && Input.GetKeyDown(KeyCode.V))
            {
                _text += (GUIUtility.systemCopyBuffer ?? "").Trim();
                changed = true;
            }
            else if (!control)
            {
                foreach (char c in Input.inputString)
                {
                    if (c == '\b') { if (_text.Length > 0) { _text = _text.Substring(0, _text.Length - 1); changed = true; } }
                    else if (!char.IsControl(c) && _text.Length < 300) { _text += c; changed = true; }
                }
            }
            if (control && Input.GetKeyDown(KeyCode.Backspace)) { _text = ""; changed = true; }
            if (changed)
            {
                _hint.text = _defaultHint;
                _hint.color = GameUi.Muted;
                Refresh();
            }
        }

        private void Refresh() => _field.text = _text + "▌";

        private void Submit()
        {
            string error = _validate?.Invoke(_text);
            if (error != null)
            {
                _hint.text = error;
                _hint.color = new Color32(0xFF, 0x8A, 0x8A, 0xFF);
                return;
            }
            var onOk = _onOk;
            string text = _text;
            Close();
            onOk?.Invoke(text);
        }

        private void Close()
        {
            _root.SetActive(false);
            _onOk = null;
        }
    }
}
