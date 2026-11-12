namespace InvectorModKit.Marketplace.Client
{
    /// <summary>
    /// A sheet's main button (My library, My publications): its label and the line under it as Loc keys with their
    /// arguments, whether it can be pressed, and how it looks.
    /// </summary>
    public sealed class SheetButton
    {
        public string LabelKey;
        public object[] LabelArgs = new object[0];
        public bool Enabled;
        public string SubKey;
        public object[] SubArgs = new object[0];
        /// <summary>0..100 while something runs (the bar fills), else null.</summary>
        public int? Percent;
        /// <summary>The violet of an action to take.</summary>
        public bool Accent;
        /// <summary>The yellow of something that went wrong.</summary>
        public bool Warning;
    }
}
