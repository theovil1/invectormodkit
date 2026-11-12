using System;
using InvectorModKit.Marketplace.Client;
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
        private bool _allSelected; // Ctrl+A

        public bool Visible => _root.activeSelf;

        public TextPrompt(MarketplaceScreen modal)
        {
            _root = GameUi.Clone(GameUi.DialogTemplate, modal.transform, "Prompt", stillImage: true);
            UiKit.Fill((RectTransform)_root.transform); // its dark backdrop over the whole screen
            var group = _root.GetComponent<CanvasGroup>();
            if (group) { group.alpha = 1; group.interactable = true; group.blocksRaycasts = true; }
            var backdrop = GameUi.Find<Image>(_root, "Background");
            if (backdrop) backdrop.color = new Color(0, 0, 0, 0.8f);
            var box = (RectTransform)_root.transform.Find("QuestionBox");
            GameUi.Place(box, 0, 0, 1040, 440);
            MarketplaceScreen.AddFill(box);

            _question = GameUi.Find<Text>(box.gameObject, "Text");
            _question.alignment = TextAnchor.MiddleCenter;
            _question.resizeTextForBestFit = true;
            _question.resizeTextMinSize = 20;
            _question.resizeTextMaxSize = 36;
            GameUi.Place(_question, 0, 140, 940, 80);

            // The field: one of the game's dialog buttons, not clickable, holding the text.
            var fieldButton = GameUi.Clone(GameUi.QuestionButtonTemplate.gameObject, box, "Field").GetComponent<ButtonWithEvents>();
            GameUi.Place(fieldButton, 0, 50, 900, 70);
            MarketplaceScreen.StyleButton(fieldButton, "", 28, () => { });
            fieldButton.interactable = false;
            var colors = fieldButton.colors;
            colors.disabledColor = new Color32(0x24, 0x20, 0x2A, 0xFF);
            fieldButton.colors = fieldButton.currentColorBlock = fieldButton.originalColors = colors;
            _field = fieldButton.GetComponentInChildren<Text>(true);
            _field.resizeTextForBestFit = true;
            _field.supportRichText = true; // Ctrl+A shows the selection in colour
            _field.color = Color.white;

            _hint = GameUi.Text(box, "Hint", UiKit.Regular, 24, TextAnchor.MiddleCenter, GameUi.Muted);
            GameUi.Place(_hint, 0, -20, 940, 50);

            _ok = GameUi.Find<ButtonWithEvents>(box.gameObject, "Answer Yes");
            var cancel = GameUi.Find<ButtonWithEvents>(box.gameObject, "Answer No");
            GameUi.Place(_ok, -165, -130, 300, 84);
            GameUi.Place(cancel, 165, -130, 300, 84);
            MarketplaceScreen.StyleButton(_ok, Loc.T("ok"), 34, Submit);
            MarketplaceScreen.StyleButton(cancel, Loc.T("cancel"), 34, Close);
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
            _allSelected = false;
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
            string typed = control ? "" : Input.inputString ?? "";
            if (control && Input.GetKeyDown(KeyCode.A)) typed += TypedText.SelectAll;
            if (control && Input.GetKeyDown(KeyCode.V)) typed += (GUIUtility.systemCopyBuffer ?? "").Trim();
            if (control && Input.GetKeyDown(KeyCode.Backspace)) typed += TypedText.SelectAll + "\b"; // clears the field
            bool wasSelected = _allSelected;
            bool changed = TypedText.Apply(ref _text, ref _allSelected, typed, 300);
            if (changed)
            {
                _hint.text = _defaultHint;
                _hint.color = GameUi.Muted;
            }
            if (changed || _allSelected != wasSelected) Refresh();
        }

        private void Refresh() => _field.text = TypedText.Show(_text, _allSelected);

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
