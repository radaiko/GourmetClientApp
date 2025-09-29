using System;

namespace GourmetClientApp.Update;

public class GourmetUpdateException : Exception
{
    public GourmetUpdateException(string message)
        : base(message)
    {
    }

    public GourmetUpdateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}