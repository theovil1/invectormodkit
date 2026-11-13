namespace InvectorModKit.Marketplace.Client
{
    /// <summary>
    /// A switch of the game held at one value while the marketplace is open (the menus' navigation). The game may set
    /// it meanwhile (song select turns navigation on after its intro, or when its list is rebuilt): the last value it
    /// set is the one given back on close, not the one seen on opening, which left song select with no keys.
    /// </summary>
    public sealed class HeldFlag
    {
        private bool _wanted, _forced;

        public bool Held { get; private set; }

        /// <summary>Starts holding at <paramref name="forced"/>; a second hold keeps the first value to give back.</summary>
        public void Hold(bool current, bool forced)
        {
            if (Held) return;
            _wanted = current;
            _forced = forced;
            Held = true;
        }

        /// <summary>Once per frame with the switch's value: the value to set it to (a change by the game is kept for later).</summary>
        public bool Watch(bool actual)
        {
            if (!Held) return actual;
            if (actual != _forced) _wanted = actual;
            return _forced;
        }

        /// <summary>The value to give back, once; null when not held.</summary>
        public bool? Release()
        {
            if (!Held) return null;
            Held = false;
            return _wanted;
        }
    }
}
