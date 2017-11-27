using System;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using SebastianHaeni.ThermoBox.Common.Util;
using SebastianHaeni.ThermoBox.SeqConverter.Color.Custom;

namespace SebastianHaeni.ThermoBox.SeqConverter.Color
{
    internal static class ColorConverter
    {
        public static void ConvertToColor(string input, string output, ColorMap palette)
        {
            Console.WriteLine($"Using palette {palette}");

            var mappingFunction = GetMappingFunction(palette);

            using (var capture = new VideoCapture(input))
            {
                Mat mat;
                Recorder recorder = null;
                var fps = Convert.ToInt32(capture.GetCaptureProperty(CapProp.Fps));

                while ((mat = capture.QueryFrame()) != null)
                {
                    var frame = mat.ToImage<Gray, byte>();

                    if (recorder == null)
                    {
                        recorder = new Recorder(fps, frame.Size, true);
                        recorder.StartRecording(output);
                    }

                    var colorMapped = mappingFunction.Invoke(frame);
                    recorder.Write(colorMapped.Mat);
                }

                recorder?.StopRecording();
            }
        }

        private static Func<Image<Gray, byte>, Image<Bgr, byte>> GetMappingFunction(ColorMap palette)
        {
            switch (palette)
            {
                case ColorMap.Hot:
                    return gray => Map(gray, ColorMapType.Hot);
                case ColorMap.Iron:
                    return IronPalette.Map;
                default:
                    return IronPalette.Map;
            }
        }

        private static Image<Bgr, byte> Map(IImage image, ColorMapType palette)
        {
            var output = new Image<Bgr, byte>(image.Size);
            CvInvoke.ApplyColorMap(image, output, palette);

            return output;
        }
    }
}
