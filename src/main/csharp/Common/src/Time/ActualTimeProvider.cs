using System;

namespace SebastianHaeni.ThermoBox.Common.Time
{
    internal class ActualTimeProvider : ITimeProvider
    {
        public DateTime Now => DateTime.Now;
    }
}
