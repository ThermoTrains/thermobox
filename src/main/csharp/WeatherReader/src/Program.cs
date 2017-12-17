using SebastianHaeni.ThermoBox.Common.Component;

namespace SebastianHaeni.ThermoBox.WeatherReader
{
    internal static class Program
    {
        private static void Main()
        {
            ComponentLauncher.Launch(() => new WeatherReaderComponent());
        }
    }
}
