using System.Drawing;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using SebastianHaeni.ThermoBox.Common.Util;

namespace SebastianHaeni.ThermoBox.Common.Motion
{
    public class MotionFinder<TDepth>
        where TDepth : new()
    {
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

            return contours;
        }
    }
}
