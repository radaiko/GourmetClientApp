using System;

namespace GourmetClientApp.Network;

public class GourmetHtmlNodeException : Exception
{
    public GourmetHtmlNodeException(string message)
        : base(message)
    {
    }
}