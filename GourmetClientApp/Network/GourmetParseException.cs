using System;

namespace GourmetClientApp.Network;

public class GourmetParseException : Exception
{
    public GourmetParseException(string message, string uriInfo, string responseContent)
        : base(message)
    {
        UriInfo = uriInfo;
        ResponseContent = responseContent;
    }

    public GourmetParseException(string message, string uriInfo, string responseContent, Exception innerException)
        : base(message, innerException)
    {
        UriInfo = uriInfo;
        ResponseContent = responseContent;
    }

    public string UriInfo { get; }

    public string ResponseContent { get; }
}