using System;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using SebastianHaeni.ThermoBox.Common.Util;

namespace SebastianHaeni.ThermoBox.SeqConverter
{
    internal static class ColorConverter
    {
        public static void Convert(string input, string output, ColorMapType palette)
        {
            Console.WriteLine($"Using palette {palette}");
            using (var capture = new VideoCapture(input))
            {
                Mat mat;
                Recorder recorder = null;

                while ((mat = capture.QueryFrame()) != null)
                {
                    var frame = mat.ToImage<Gray, byte>();

                    if (recorder == null)
                    {
                        recorder = new Recorder(25, frame.Size, true);
                        recorder.StartRecording(output);
                    }

                    CvInvoke.ApplyColorMap(frame, frame, palette);
                    recorder.Write(frame);
                }

                recorder?.StopRecording();
            }
        }
    }
}
