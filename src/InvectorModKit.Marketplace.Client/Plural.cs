namespace InvectorModKit.Marketplace.Client
{
    /// <summary>
    /// Which form a counted text takes. Only the languages that write "{0} things" need it: Polish, Russian, Japanese,
    /// Korean and Chinese texts say "things: {0}", the same for every count.
    /// </summary>
    public static class Plural
    {
        /// <summary>True when <paramref name="n"/> takes the singular in that language (French: 0 and 1; the others: 1).</summary>
        public static bool IsOne(int n, string language) => language == "fr" ? n == 0 || n == 1 : n == 1;
    }
}
