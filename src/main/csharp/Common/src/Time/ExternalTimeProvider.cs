using System;

namespace SebastianHaeni.ThermoBox.Common.Time
{
    public class ExternalTimeProvider : ITimeProvider
    {
        public DateTime CurrentTime { get; set; } = DateTime.Now;

        public DateTime Now => CurrentTime;
    }
}
