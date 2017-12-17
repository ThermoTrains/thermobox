using Emgu.CV;
using Emgu.CV.Structure;
using Flir.Atlas.Image;
using log4net;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Linq;
using SebastianHaeni.ThermoBox.Common.Motion;
using SebastianHaeni.ThermoBox.Common.Util;

namespace SebastianHaeni.ThermoBox.IRCompressor
{
    public static class IRSensorDataCompression
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public enum Mode
        {
            Train,
            Other
        }

        public static void Compress(string sourceFile, string outputVideoFile, Mode mode)
        {
            Log.Info($"Compressing {sourceFile} with H.264 to {outputVideoFile}. Using compression mode {mode}");

            using (var thermalImage = new ThermalImageFile(sourceFile))
            {
                // loop through every frame and calculate min and max values
                var (minValue, maxValue) = FindMinMaxValues(thermalImage);

                // Find bounding box of moving train

                var boundingBoxes = new[]
                {
                    (0, new Rectangle(0, 90, 640, 335))
                }.ToList();

                /*
                Log.Info($"{minValue}/{maxValue}");
                var signalImage = GetSignalImage(thermalImage, thermalImage.Width, thermalImage.Height);
                var outImage = ScaleDown(signalImage, minValue, 256f / (maxValue - minValue));
                CvInvoke.EqualizeHist(outImage, outImage);
                outImage.ROI = boundingBoxes[0].Item2;

                outImage.Save(@"C:\Thermobox\test.jpg");
                Environment.Exit(1);*/

                // Find min and max within the train bounds
                var (minTrain, maxTrain) = boundingBoxes.Count > 0
                    ? FindMinMaxTrainValues(boundingBoxes, thermalImage)
                    : (minValue, maxValue);

                // Write video from first bbox index to last with extracted min and max values
                var trainScale = 256f / (maxTrain - minTrain);
                var formatedScalePercent = Math.Max(0, (1 - trainScale) * 100).ToString("N");
                Log.Info($"Precision loss: {formatedScalePercent}%");

                boundingBoxes.Clear();

                WriteVideo(outputVideoFile, boundingBoxes, thermalImage, minTrain, trainScale);

                // Add compression parameters to file as metadata.
                AddCompressionParameters(outputVideoFile, minTrain, trainScale);
            }
        }

        private static void WriteVideo(
            string outputVideoFile,
            IReadOnlyCollection<(int index, Rectangle rect)> boundingBoxes,
            ThermalImageFile thermalImage,
            int minTrain,
            float trainScale)
        {
            var firstFrame = boundingBoxes
                .Select(v => v.index)
                .DefaultIfEmpty(0)
                .Min();

            var lastFrame = boundingBoxes
                .Select(v => v.index)
                .DefaultIfEmpty(thermalImage.ThermalSequencePlayer.Count() - 1)
                .Max();

            thermalImage.ThermalSequencePlayer.SelectedIndex = firstFrame;

            var fps = (int) thermalImage.ThermalSequencePlayer.FrameRate;
            var size = thermalImage.Size;

            using (var recorder = new Recorder(fps, thermalImage.Size, false).StartRecording(outputVideoFile))
            {
                while (thermalImage.ThermalSequencePlayer.SelectedIndex < lastFrame)
                {
                    var image = GetSignalImage(thermalImage, size.Width, size.Height);
                    thermalImage.ThermalSequencePlayer.Next();

                    var image8 = ScaleDown(image, minTrain, trainScale);

                    recorder.Write(image8.Mat);
                }

                Log.Info($"Created video with {recorder.FrameCounter} frames");
            }
        }

        private static (int minValue, int maxValue) FindMinMaxValues(ThermalImageFile thermalImage)
        {
            thermalImage.ThermalSequencePlayer.First();

            var minValue = int.MaxValue;
            var maxValue = int.MinValue;

            for (var i = 0; i < 1; i++)
            {
                if (thermalImage.MinSignalValue < minValue)
                {
                    minValue = thermalImage.MinSignalValue;
                }

                if (thermalImage.MaxSignalValue > maxValue)
                {
                    maxValue = thermalImage.MaxSignalValue;
                }

                thermalImage.ThermalSequencePlayer.Next();
            }

            return (minValue, maxValue);
        }

        private static List<(int index, Rectangle rect)> FindTrainBoundingBoxes(
            ThermalImageFile thermalImage,
            double maxValue,
            double minValue)
        {
            // The background is the last frame since we always record until the
            // train has completely left the frame.
            thermalImage.ThermalSequencePlayer.End();
            thermalImage.ThermalSequencePlayer.SelectedIndex -= 1;
            var size = thermalImage.Size;
            var background = GetSignalImage(thermalImage, size.Width, size.Height);
            var scale = 256f / (maxValue - minValue);
            var scaledDownBackground = ScaleDown(background, minValue, scale);
            var motionFinder = new MotionFinder<byte>(scaledDownBackground);
            var boundingBoxes = new List<(int index, Rectangle rect)>();

            // Move to start again
            thermalImage.ThermalSequencePlayer.First();

            for (var i = 0; i < thermalImage.ThermalSequencePlayer.Count(); i++)
            {
                thermalImage.ThermalSequencePlayer.Next();
                var image = GetSignalImage(thermalImage, size.Width, size.Height);

                var image8 = ScaleDown(image, minValue, scale);

                var bbox = motionFinder.FindBoundingBox(image8, new Gray(8.0), new Gray(byte.MaxValue), 10, 0);

                if (!bbox.HasValue)
                {
                    continue;
                }

                if (image8.Size.Width / (float) bbox.Value.Width < .5)
                {
                    // train does not cover enough of the horizontal span of the image
                    continue;
                }

                boundingBoxes.Add((index: i, rect: bbox.Value));
            }
            return boundingBoxes;
        }

        private static (int minTrain, int maxTrain) FindMinMaxTrainValues(
            IReadOnlyCollection<(int index, Rectangle rect)> boundingBoxes,
            ThermalImageFile thermalImage)
        {
            var medianBox = MathUtil.GetMedianRectangle(boundingBoxes.Select(v => v.rect));
            var minValues = new List<ushort>();
            var maxValues = new List<ushort>();
            var size = thermalImage.Size;

            foreach (var (index, rect) in boundingBoxes)
            {
                if (MathUtil.RectDiff(rect, medianBox) > .2)
                {
                    // the difference of the box to the median is too big to be a reliable source
                    continue;
                }

                thermalImage.ThermalSequencePlayer.SelectedIndex = index;
                var image = GetSignalImage(thermalImage, size.Width, size.Height);

                // Extract min and max within bounds
                var min = ushort.MaxValue;
                var max = ushort.MinValue;

                for (var x = rect.X; x < rect.X + rect.Width; x++)
                {
                    for (var y = rect.Y + 20; y < rect.Y + rect.Height; y++)
                    {
                        var val = image.Data[y, x, 0];

                        if (val < min)
                        {
                            min = val;
                        }
                        if (val > max)
                        {
                            max = val;
                        }
                    }
                }

                minValues.Add(min);
                maxValues.Add(max);
            }

            var minTrain = MathUtil.Median(minValues.Select(v => (int) v).ToArray());
            var maxTrain = MathUtil.Median(maxValues.Select(v => (int) v).ToArray());

            return (minTrain, maxTrain);
        }

        private static Image<Gray, byte> ScaleDown(Image<Gray, ushort> image, double minValue, double scale)
        {
            // Floor values to min value (img * 1 + img * 0 - minValue)
            var normalized = image.AddWeighted(image, 1, 0, -minValue);

            // Loosing precision, but there is no open video codec supporting 16 bit grayscale :(
            // Scaling values down to our established value span as a factor of 256
            return normalized.ConvertScale<byte>(scale, 0);
        }

        private static void AddCompressionParameters(string outputVideoFile, double minValue, double scale)
        {
            var tagFile = TagLib.File.Create(outputVideoFile);
            tagFile.Tag.Comment = $"{minValue}/{scale}";
            tagFile.Save();
        }

        private static Image<Gray, ushort> GetSignalImage(ImageBase thermalImage, int width, int height)
        {
            var pixels = thermalImage.ImageProcessing.GetPixels();

            // Lock thermal image pixel data
            pixels.LockPixelData();

            // Declare an array to hold the bytes of the signal.
            var signalValues = new byte[pixels.Stride * height];

            // Copy the signal values as bytes into the array.
            Marshal.Copy(pixels.PixelData, signalValues, 0, signalValues.Length);

            // Free thermal image lock
            pixels.UnlockPixelData();

            // Write the bytes into the new image.
            var image = new Image<Gray, ushort>(width, height);

            for (var column = 0; column < width; column++)
            {
                for (var row = 0; row < height; row++)
                {
                    var index = 2 * (row * width + column);
                    // Each part contains one byte
                    var part1 = signalValues[index];
                    var part2 = signalValues[index + 1];

                    // Merge two bytes into one short
                    var merged = BitConverter.ToUInt16(new[] {part1, part2}, 0);
                    image.Data[row, column, 0] = merged;
                }
            }

            return image;
        }
    }
}
