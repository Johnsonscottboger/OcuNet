using System;

namespace OcuNet.SourceGenerator;

public class UtcDateTimeProvider : IDateTimeProvider
{
    public DateTime Now
    {
        get => DateTime.UtcNow;
    }
}
