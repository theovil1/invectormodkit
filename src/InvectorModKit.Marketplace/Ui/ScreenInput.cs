using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace InvectorModKit.Marketplace.Ui
{
    /// <summary>
    /// The marketplace screen's input. While it is open (<see cref="Active"/>) the menus underneath take none: their
    /// state updates, the ship selector and the console menu layout are held by Harmony prefixes (EntryPoints), and the
    /// game's input module sends no navigation and no mouse. The screen reads the keyboard keys itself, and the game's
    /// UI actions only while a controller is the last device used, so a menu key (R, S, Q, E) never acts here.
    /// </summary>
    internal sealed class ScreenInput
    {
        // The game's UI actions (RewiredConsts.Action) used for controllers.
        public const int ActionOk = 24, ActionCancel = 25, ActionNext = 59, ActionPrevious = 60, ActionListen = 63, ActionSearch = 58;

        /// <summary>The screen is open: the menus underneath must not react to anything.</summary>
        public static bool Active { get; set; }

        /// <summary>A mouse click is being delivered (rows tell a click, which selects, from Enter, which acts).</summary>
        public static bool MouseClicking { get; private set; }

        /// <summary>The search field has the keys: only text, Backspace, Enter and Esc count.</summary>
        public bool Typing { get; set; }

        public Vector2Int Move { get; private set; }
        public bool Ok { get; private set; }
        public bool Back { get; private set; }
        public bool Listen { get; private set; }
        public bool Search { get; private set; }
        public bool NextTab { get; private set; }
        public bool PreviousTab { get; private set; }
        /// <summary>What was typed this frame (Backspace as '\b'), while <see cref="Typing"/>.</summary>
        public string Typed { get; private set; } = "";

        private ControlType _lastDevice = ControlType.Keyboard;

        /// <summary>Once per frame, before the screen acts.</summary>
        public void Read()
        {
            Move = Vector2Int.zero;
            Ok = Back = Listen = Search = NextTab = PreviousTab = false;
            Typed = "";
            bool enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
            bool escape = Input.GetKeyDown(KeyCode.Escape);
            if (Typing)
            {
                Ok = enter;
                Back = escape;
                Typed = Input.inputString ?? "";
                bool control = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                if (control && Input.GetKeyDown(KeyCode.A)) Typed += Client.TypedText.SelectAll;
                if (control && Input.GetKeyDown(KeyCode.V))
                    Typed += (GUIUtility.systemCopyBuffer ?? "").Replace("\r", "").Replace("\n", "").Trim();
                return;
            }
            bool pad = UsingController();
            try { Move = new Vector2Int(RewiredInputHelper.GetHorizontalDirectionUIAll(), RewiredInputHelper.GetVerticalDirectionUIAll()); }
            catch (Exception) { }
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            Ok = enter || (pad && Action(ActionOk));
            Back = escape || (pad && Action(ActionCancel));
            Listen = Input.GetKeyDown(KeyCode.Space) || (pad && Action(ActionListen));
            Search = Input.GetKeyDown(KeyCode.F) || (pad && Action(ActionSearch));
            NextTab = (Input.GetKeyDown(KeyCode.Tab) && !shift) || (pad && Action(ActionNext));
            PreviousTab = (Input.GetKeyDown(KeyCode.Tab) && shift) || (pad && Action(ActionPrevious));
        }

        private bool UsingController()
        {
            try { _lastDevice = ControlTypeHelper.GetLastControllerDontChangePreviousIfMouse(_lastDevice); }
            catch (Exception) { _lastDevice = ControlType.Keyboard; }
            return _lastDevice != ControlType.Keyboard && _lastDevice != ControlType.None;
        }

        private static bool Action(int id)
        {
            try { return RewiredInputHelper.GetButtonDownAll(id); }
            catch (Exception) { return false; }
        }

        // ----- the game's input module, held while the screen is open

        private Rewired.Integration.UnityUI.RewiredStandaloneInputModule _module;
        private bool _moduleMouse, _taken;
        private readonly Client.HeldFlag _navigation = new Client.HeldFlag();
        private Vector3 _lastMouse;
        private readonly List<RaycastResult> _hits = new List<RaycastResult>();

        public void Take()
        {
            _taken = true;
            Active = true;
            _lastMouse = Input.mousePosition;
            if (EventSystem.current == null) return;
            _navigation.Hold(EventSystem.current.sendNavigationEvents, forced: false);
            EventSystem.current.sendNavigationEvents = false;
            _module = EventSystem.current.currentInputModule as Rewired.Integration.UnityUI.RewiredStandaloneInputModule;
            // The game's module only feeds the mouse to the Rewired player of the current menu, which may not own it:
            // the screen reads the mouse itself (Mouse) and the module sends none.
            if (_module == null) return;
            _moduleMouse = _module.allowMouseInput;
            _module.allowMouseInput = false;
        }

        /// <summary>
        /// Once per frame: song select turns navigation back on behind the screen (after its intro, or when its list is
        /// rebuilt): off again, and on once the screen closes.
        /// </summary>
        public void HoldNavigation()
        {
            if (_taken && EventSystem.current) EventSystem.current.sendNavigationEvents = _navigation.Watch(EventSystem.current.sendNavigationEvents);
        }

        /// <summary>Once: a screen being destroyed must not give back what the next one (opened the same frame) took.</summary>
        public void GiveBack()
        {
            if (!_taken) return;
            _taken = false;
            Active = false;
            if (_module) _module.allowMouseInput = _moduleMouse;
            var navigation = _navigation.Release();
            if (EventSystem.current && navigation.HasValue) EventSystem.current.sendNavigationEvents = navigation.Value;
            _module = null;
        }

        /// <summary>
        /// The mouse, over <paramref name="root"/> only: hovering a button selects it, a click presses it, the wheel
        /// scrolls what is under it. Clicks come from Rewired, which keeps a press and release made within one frame
        /// (Unity's own Input lost clicks made without moving the mouse).
        /// </summary>
        public void Mouse(Transform root)
        {
            var position = Input.mousePosition;
            bool moved = (position - _lastMouse).sqrMagnitude > 1f;
            _lastMouse = position;
            bool click;
            try { click = Rewired.ReInput.isReady ? Rewired.ReInput.controllers.Mouse.GetButtonDown(0) : Input.GetMouseButtonDown(0); }
            catch (Exception) { click = Input.GetMouseButtonDown(0); }
            float wheel = Input.mouseScrollDelta.y;
            if ((!moved && !click && wheel == 0) || EventSystem.current == null) return;
            if (!click && !Cursor.visible) return;

            var data = new PointerEventData(EventSystem.current) { position = position, button = PointerEventData.InputButton.Left, scrollDelta = Input.mouseScrollDelta };
            _hits.Clear();
            EventSystem.current.RaycastAll(data, _hits);
            var hit = _hits.Count > 0 && _hits[0].gameObject.transform.IsChildOf(root) ? _hits[0].gameObject : null;
            if (hit == null) return;
            var selectable = hit.GetComponentInParent<UnityEngine.UI.Selectable>();
            bool usable = selectable && selectable.IsInteractable() && selectable.transform.IsChildOf(root);
            if (moved && usable && GameUi.Selected != selectable.gameObject) GameUi.Select(selectable.gameObject);
            if (click && usable)
            {
                // A whole click (down, up, click, exit): a lone click event left buttons stuck in a pressed look.
                GameUi.Select(selectable.gameObject);
                var target = selectable.gameObject;
                MouseClicking = true;
                try
                {
                    ExecuteEvents.Execute(target, data, ExecuteEvents.pointerDownHandler);
                    ExecuteEvents.Execute(target, data, ExecuteEvents.pointerUpHandler);
                    ExecuteEvents.Execute(target, data, ExecuteEvents.pointerClickHandler);
                    ExecuteEvents.Execute(target, data, ExecuteEvents.pointerExitHandler);
                }
                finally { MouseClicking = false; }
            }
            if (wheel != 0) ExecuteEvents.ExecuteHierarchy(hit, data, ExecuteEvents.scrollHandler);
        }
    }
}
