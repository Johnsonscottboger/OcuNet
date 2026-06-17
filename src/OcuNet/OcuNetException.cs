using System;

namespace OcuNet;

public abstract class OcuNetException : Exception
{
    protected OcuNetException(string message)
        : base(message)
    {
    }
}
