using System;
using System.Configuration;
using System.IO;
using System.Net;
using SebastianHaeni.ThermoBox.Common.Component;

namespace SebastianHaeni.ThermoBox.WeatherReader
{
    internal class WeatherReaderComponent : ThermoBoxComponent
    {
        private static readonly string CaptureFolder = ConfigurationManager.AppSettings["CAPTURE_FOLDER"];
        private static readonly string RecordingLocation = ConfigurationManager.AppSettings["RECORDING_LOCATION"];

        public WeatherReaderComponent()
        {
            var openWeatherMapApiKey = Environment.GetEnvironmentVariable("OPEN_WEATHER_MAP_API_KEY");

            string recordingFilename = null;

            Subscription(Commands.CaptureStart, (channel, filename) => recordingFilename = filename);
            Subscription(Commands.CaptureAbort, (channel, filename) => recordingFilename = null);

            Subscription(Commands.CaptureStop, (channel, filename) =>
            {
                if (recordingFilename == null)
                {
                    return;
                }

                using (var w = new WebClient())
                {
                    var url = "http://api.openweathermap.org/data/2.5/weather" +
                              $"?q={RecordingLocation}" +
                              $"&appid={openWeatherMapApiKey}";

                    var jsonData = w.DownloadString(url);

                    // ensuring the recordings directory exists
                    var recordingDirectory = new DirectoryInfo(CaptureFolder);
                    if (!recordingDirectory.Exists)
                    {
                        recordingDirectory.Create();
                    }

                    var absolutePath = $@"{CaptureFolder}\{recordingFilename}-weather.json";
                    File.WriteAllText(absolutePath, jsonData);
                    Publish(Commands.Upload, absolutePath);
                }

                recordingFilename = null;
            });
        }
    }
}
