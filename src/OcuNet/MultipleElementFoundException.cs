namespace OcuNet;

public sealed class MultipleElementFoundException : OcuNetException
{
    public MultipleElementFoundException(SearchResult result)
        : base(Messages.MultipleElementFoundException_Message.FormatInvariant(result.Element, result.Locations.ToCenterString()))
    {
        this.Result = result;
    }

    public SearchResult Result { get; }
}
