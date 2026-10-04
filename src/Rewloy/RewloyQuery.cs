namespace Rewloy
{
    /// <summary>The base of every generated query-parameter object.</summary>
    public abstract class RewloyQuery
    {
        internal RewloyQuery()
        {
        }

        internal abstract void WriteTo(QueryWriter writer);
    }
}
