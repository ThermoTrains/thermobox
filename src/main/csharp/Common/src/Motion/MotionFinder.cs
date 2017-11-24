using System;
using System.Drawing;
using System.Reflection;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using log4net;
using SebastianHaeni.ThermoBox.Common.Util;

namespace SebastianHaeni.ThermoBox.Common.Motion
{
    public class MotionFinder<TDepth>
        where TDepth : new()
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private const double MinHeightFactor = .15;
        private readonly int _minHeight;
        private static long i;

        public Image<Gray, TDepth> Background { get; }

        public MotionFinder(Image<Gray, TDepth> background)
        {
            Background = background;
            _minHeight = Convert.ToInt32(background.Height * MinHeightFactor);
        }

        public Rectangle? FindBoundingBox(
            Image<Gray, TDepth> source,
            Gray threshold,
            Gray maxValue)
        {
            var contours = GetContours(Background, source, threshold, maxValue);

            if (contours.Size == 0)
            {
                // no contours, so we purge
                return null;
            }

            // create bounding box of all contours
            var bbox = MathUtil.GetMaxRectangle(contours);

            if (bbox.Height < _minHeight)
            {
                // TODO remove debug log once done
                Log.Info(
                    $"Found a change, but it's not covering enough height. Required: {_minHeight}px. Actual: {bbox.Height}px");
                Log.Info($"x:{bbox.X}, y:{bbox.Y}, height:{bbox.Height}, width:{bbox.Width}");
                return null;
            }

            return bbox;
        }

        /// <summary>
        /// Finds the significant contours between the two images.
        /// </summary>
        private VectorOfVectorOfPoint GetContours(
            Image<Gray, TDepth> background,
            Image<Gray, TDepth> source,
            Gray threshold,
            Gray maxValue)
        {
            // compute absolute diff between current frame and first frame
            var diff = background.AbsDiff(source);

            // binarize image
            var t = diff.ThresholdBinary(threshold, maxValue);

            // erode to get rid of small dots
            t = t.Erode(8);

            // dilate the threshold image to fill in holes
            t = t.Dilate(15);

            // find contours
            var contours = new VectorOfVectorOfPoint();
            var hierarchy = new Mat();
            CvInvoke.FindContours(t, contours, hierarchy, RetrType.External, ChainApproxMethod.ChainApproxSimple);

            // TODO remove this debugging code once done
            if (contours.Size > 0)
            {
                i++;
                if (i % 20 == 0)
                {
                    for (var j = 0; j < contours.Size; j++)
                    {
                        var bbox = CvInvoke.BoundingRectangle(contours[j]);
                        source.Draw(bbox, new Gray(255), 2);
                    }
                    source.Draw(MathUtil.GetMaxRectangle(contours), new Gray(200), 2);
                    source.Save($@"C:\Thermobox\source{(i / 20)}.jpg");
                }
            }

            return contours;
        }

        /// <summary>
        /// Tells if the two images have a significant difference.
        /// </summary>
        /// <param name="image1"></param>
        /// <param name="image2"></param>
        /// <param name="threshold"></param>
        /// <param name="maxValue"></param>
        /// <returns></returns>
        public bool HasDifference(Image<Gray, TDepth> image1, Image<Gray, TDepth> image2, Gray threshold, Gray maxValue)
        {
            return GetContours(image1, image2, threshold, maxValue).Size > 0;
        }
    }
}
