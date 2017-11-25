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

        private static long i;

        public Image<Gray, TDepth> Background { get; }

        public MotionFinder(Image<Gray, TDepth> background)
        {
            Background = background;
        }

        public Rectangle? FindBoundingBox(
            Image<Gray, TDepth> source,
            Gray threshold,
            Gray maxValue,
            int erode,
            int dilate)
        {
            var contours = GetContours(Background, source, threshold, maxValue, erode, dilate);

            if (contours.Size == 0)
            {
                // no contours, so we purge
                return null;
            }

            // create bounding box of all contours
            var bbox = MathUtil.GetMaxRectangle(contours);

            return bbox;
        }

        /// <summary>
        /// Finds the significant contours between the two images.
        /// </summary>
        private VectorOfVectorOfPoint GetContours(
            Image<Gray, TDepth> background,
            Image<Gray, TDepth> source,
            Gray threshold,
            Gray maxValue,
            int erode,
            int dilate)
        {
            // compute absolute diff between current frame and first frame
            var diff = background.AbsDiff(source);

            // binarize image
            var t = diff.ThresholdBinary(threshold, maxValue);

            // erode to get rid of small dots
            t = t.Erode(erode);

            // dilate the threshold image to fill in holes
            t = t.Dilate(dilate);

            // find contours
            var contours = new VectorOfVectorOfPoint();
            var hierarchy = new Mat();
            CvInvoke.FindContours(t, contours, hierarchy, RetrType.External, ChainApproxMethod.ChainApproxSimple);

            // TODO remove this debugging code once done
            if (contours.Size > 0)
            {
                for (var j = 0; j < contours.Size; j++)
                {
                    var bbox = CvInvoke.BoundingRectangle(contours[j]);
                    source.Draw(bbox, new Gray(255), 2);
                }
                source.Draw(MathUtil.GetMaxRectangle(contours), new Gray(200), 2);
                source.Save($@"C:\Thermobox\source{++i}.jpg");
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
            return GetContours(image1, image2, threshold, maxValue, 3, 15).Size > 0;
        }
    }
}
