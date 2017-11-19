using System;

namespace SebastianHaeni.ThermoBox.Common.Time
{
    public interface ITimeProvider
    {
        DateTime Now { get; }
    }
}
