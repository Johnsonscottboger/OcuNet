using System;

namespace OcuNet.SourceGenerator;

internal interface IDateTimeProvider
{
    public DateTime Now { get; }
}
