using System;
using System.Drawing;
using Emgu.CV;

namespace SebastianHaeni.ThermoBox.Common.Util
{
    public class Recorder : IDisposable
    {
        private static readonly int Compression = VideoWriter.Fourcc('H', '2', '6', '4');
        private readonly int _fps;
        private readonly Size _size;
        private readonly bool _isColor;

        private VideoWriter _videoWriter;
        private bool _paused;

        public int FrameCounter { get; private set; }

        public Recorder(int fps, Size size, bool isColor)
        {
            _fps = fps;
            _size = size;
            _isColor = isColor;
        }

        public Recorder StartRecording(string filepath)
        {
            return StartRecording(filepath, _fps);
        }

        public Recorder StartRecording(string filepath, int fps)
        {
            if (_videoWriter != null)
            {
                StopRecording();
            }

            FrameCounter = 0;
            _videoWriter = new VideoWriter(filepath, Compression, fps, _size, _isColor);

            return this;
        }

        public Recorder Write(Mat image)
        {
            if (_videoWriter == null || _paused)
            {
                return this;
            }

            FrameCounter++;
            _videoWriter.Write(image);

            return this;
        }

        public void StopRecording()
        {
            _videoWriter?.Dispose();
            _videoWriter = null;
        }

        public void Dispose()
        {
            _videoWriter?.Dispose();
        }

        public void Pause()
        {
            _paused = true;
        }

        public void Resume()
        {
            _paused = false;
        }
    }
}
