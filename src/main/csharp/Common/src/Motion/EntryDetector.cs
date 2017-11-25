using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using Emgu.CV;
using Emgu.CV.Structure;
using log4net;
using SebastianHaeni.ThermoBox.Common.Time;

namespace SebastianHaeni.ThermoBox.Common.Motion
{
    public class EntryDetector
    {
        public event EventHandler Enter;
        public event EventHandler Exit;
        public event EventHandler Abort;
        public event EventHandler Pause;
        public event EventHandler Resume;

        public DetectorState CurrentState { get; set; } = DetectorState.Nothing;

        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Once this threshold is reached, the background will be reinitailized.
        /// </summary>
        public const int NoBoundingBoxBackgroundThreshold = 30;

        /// <summary>
        /// Once this threshold is reached, the recording will be paused.
        /// After we find bounding boxes again, we resume.
        /// </summary>
        public const int NoMotionPauseThreshold = 10;

        /// <summary>
        /// Minimum time motion has to be picked up again when we are paused.
        /// </summary>
        public const int ResumingThreshold = 1;

        /// <summary>
        /// The minimum time that has to pass after a train exited the image until one can
        /// entering can trigger a new recording.
        /// </summary>
        public const int MinTimeAfterExit = 3;

        /// <summary>
        /// The minimum time that has to pass after the train entered. Otherwise an abort will
        /// be published.
        /// </summary>
        public const int MinTimeAfterEntry = 5;

        /// <summary>
        /// Maximum time a recording can be. After it has passed, the recording will be stopped.
        /// Assuming the longest trains are 300 meters long and travel at 0.9 km/h that would result in 15 minutes.
        /// </summary>
        public const int MaxRecordingDuration = 60 * 15;

        /// <summary>
        /// Timeout background reset to accomodate camera adjusting lens exposure time.
        /// </summary>
        public const int AutoExposureTimeout = 2;

        /// <summary>
        /// Threshold that has to be met in succession.
        /// </summary>
        public const int ExitThreshold = 5;

        public MotionFinder<byte> MotionFinder { get; private set; }

        private int _recordingSeconds;
        private int _foundNothingCount;
        private DateTime? _noBoundingBox;
        private DateTime? _lastTick;
        private DateTime? _noMotionTimestamp;
        private DateTime? _lastMotionTimestamp;
        private DateTime _entryDateTime = DateTime.MinValue;
        private DateTime? _exitDateTime;
        private DateTime? _resetBackground;
        private DateTime _lastBackgroundReset = DateTime.MaxValue;

        private Image<Gray, byte>[] _images;

        private bool _paused;
        private static long _backgroundIndex;
        private int _exitLikelihood;

        private readonly ITimeProvider _timeProvider = new ActualTimeProvider();
        private readonly Action _correctExposure;

        // TODO remove this debugging field when done
        private static long _entryCount;

        public EntryDetector()
        {
            // constructor with no background image, background will be initialized lazily
        }

        public EntryDetector(Action correctExposure) : this()
        {
            _correctExposure = correctExposure;
        }

        public EntryDetector(IImage background)
        {
            ResetBackground(background);
        }

        public EntryDetector(IImage background, ITimeProvider timeProvider) : this(background)
        {
            _timeProvider = timeProvider;
        }

        private void OnTrainEnter()
        {
            if (_timeProvider.Now.Subtract(TimeSpan.FromSeconds(MinTimeAfterExit)) < _exitDateTime)
            {
                // It has not been long enough since the last exit.
                return;
            }

            CurrentState = DetectorState.Entry;
            _entryDateTime = _timeProvider.Now;
            _foundNothingCount = 0;

            if (_exitDateTime == null)
            {
                // We also set exit time if it's null
                _exitDateTime = _timeProvider.Now;
            }

            Enter?.Invoke(this, new EventArgs());
        }

        private void OnTrainExit()
        {
            CurrentState = DetectorState.Exit;
            _exitDateTime = _timeProvider.Now;
            _recordingSeconds = 0;

            if (_timeProvider.Now.Subtract(TimeSpan.FromSeconds(MinTimeAfterEntry)) < _entryDateTime)
            {
                // It has not been long enough since the entry. So this probably was a misfire.
                Log.Warn($"Entry followed by exit was shorter than {MinTimeAfterEntry}s. Aborting.");
                Abort?.Invoke(this, new EventArgs());
            }
            else
            {
                // Train properly exited.
                Exit?.Invoke(this, new EventArgs());
            }
        }

        private void OnNothing()
        {
            CurrentState = DetectorState.Nothing;
            _foundNothingCount++;

            if (_foundNothingCount > NoBoundingBoxBackgroundThreshold)
            {
                UpdateMotionFinder();
            }
        }

        public void Tick(Image<Gray, byte>[] images)
        {
            if (CurrentState == DetectorState.Entry && !_paused && _lastTick.HasValue)
            {
                _recordingSeconds += (int) _timeProvider.Now.Subtract(_lastTick.Value).TotalSeconds;

                if (_recordingSeconds > MaxRecordingDuration)
                {
                    // We are in enter mode for quite long now, we should abort.
                    Log.Warn($"Recording for longer than {MaxRecordingDuration}s.");
                    OnTrainExit();
                    return;
                }
            }

            _lastTick = _timeProvider.Now;
            _images = images;

            if (MotionFinder == null || _resetBackground.HasValue && _resetBackground.Value < _timeProvider.Now)
            {
                ResetBackground(_images.First());
            }

            var threshold = new Gray(10.0);
            var maxValue = new Gray(byte.MaxValue);

            var boundingBoxes = _images
                .Select(image => MotionFinder.FindBoundingBox(image, threshold, maxValue))
                .Where(box => box.HasValue)
                .Select(box => box.Value)
                .ToArray();

            Evaluate(boundingBoxes, _images.Last());

            // After some time we need to use a new background.
            // We do this if either no bounding box was found n times or if nothing was the result n times.

            if (boundingBoxes.Any())
            {
                if (_timeProvider.Now.Subtract(_lastBackgroundReset).TotalMinutes < 5)
                {
                    _noBoundingBox = null;
                }

                if (CurrentState != DetectorState.Entry)
                {
                    return;
                }

                if (!MotionFinder.HasDifference(_images.First(), _images.Last(), threshold, maxValue))
                {
                    _lastMotionTimestamp = null;

                    if (_noMotionTimestamp == null)
                    {
                        // The train (or whatever) is covering the whole image and it's not moving
                        Log.Info("No motion, timestamp set");
                        _noMotionTimestamp = _timeProvider.Now;
                    }
                }
                else
                {
                    _noMotionTimestamp = null;

                    if (_lastMotionTimestamp == null)
                    {
                        _lastMotionTimestamp = _timeProvider.Now;
                    }

                    if (_paused &&
                        _timeProvider.Now.Subtract(TimeSpan.FromSeconds(ResumingThreshold)) > _lastMotionTimestamp)
                    {
                        // We were paused => resume since we have found some moving things again.
                        Resume?.Invoke(this, new EventArgs());
                        _paused = false;
                    }
                }

                if (_paused ||
                    _noMotionTimestamp == null ||
                    _timeProvider.Now.Subtract(TimeSpan.FromSeconds(NoMotionPauseThreshold)) <= _noMotionTimestamp)
                {
                    return;
                }

                // Not found moving things for a while => pause until we find movement again.
                _noMotionTimestamp = null;
                Pause?.Invoke(this, new EventArgs());
                _paused = true;

                return;
            }

            if (!_noBoundingBox.HasValue)
            {
                Log.Info("No Bounding box, timestamp set");
                _noBoundingBox = _timeProvider.Now;
            }

            if (_timeProvider.Now.Subtract(_noBoundingBox.Value).TotalSeconds > NoBoundingBoxBackgroundThreshold)
            {
                UpdateMotionFinder();
            }
        }

        private void Evaluate(IReadOnlyCollection<Rectangle> boundingBoxes, Image<Gray, byte> image)
        {
            // Not found anything useful.
            if (!boundingBoxes.Any())
            {
                HandleExit(boundingBoxes, image);

                return;
            }

            var first = boundingBoxes.First();

            var threshold = MotionFinder.Background.Size.Width / 100;
            var rightBound = first.X + first.Width > MotionFinder.Background.Width - threshold;

            if (!rightBound)
            {
                return;
            }

            // Entry
            if (boundingBoxes.All(bbox => bbox.Width == image.Width))
            {
                HandleEntry(boundingBoxes, image);
                return;
            }

            // Nothing
            ChangeState(DetectorState.Nothing);
        }

        private void HandleEntry(IEnumerable<Rectangle> boundingBoxes, Image<Gray, byte> image)
        {
            foreach (var bbox in boundingBoxes)
            {
                image.Draw(bbox, new Gray(255), 2);
            }
            image.Save($@"C:\Thermobox\entry-{++_entryCount}.jpg");

            ChangeState(DetectorState.Entry);
        }

        private void HandleExit(IEnumerable<Rectangle> boundingBoxes, Image<Gray, byte> image)
        {
            if (CurrentState != DetectorState.Entry)
            {
                return;
            }

            _exitLikelihood++;

            // Exit
            if (_exitLikelihood <= ExitThreshold)
            {
                return;
            }

            foreach (var bbox in boundingBoxes)
            {
                image.Draw(bbox, new Gray(255), 2);
            }
            image.Save($@"C:\Thermobox\exit-{++_entryCount}.jpg");

            _exitLikelihood = 0;
            ChangeState(DetectorState.Exit);
        }

        private void ChangeState(DetectorState state)
        {
            var newState = CurrentState.GetStates().Contains(state) ? state : CurrentState;

            if (newState == CurrentState)
            {
                return;
            }

            switch (newState)
            {
                case DetectorState.Entry:
                    OnTrainEnter();
                    return;
                case DetectorState.Exit:
                    OnTrainExit();
                    return;
                case DetectorState.Nothing:
                    OnNothing();
                    return;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void UpdateMotionFinder()
        {
            if (CurrentState == DetectorState.Entry)
            {
                // do not update background as long as something has entered and not exited yet
                return;
            }

            // Correct the exposure
            _correctExposure?.Invoke();

            // Schedule the background reset after the exposure has been corrected
            _resetBackground = _timeProvider.Now.AddSeconds(AutoExposureTimeout);

            // reset
            _noBoundingBox = null;
            _foundNothingCount = 0;
        }

        private void ResetBackground(IImage background)
        {
            Log.Info("(Re)initializing background");

            var blurredBackground = new Image<Gray, byte>(background.Size);
            CvInvoke.Blur(background, blurredBackground, new Size(10, 10), new Point(-1, -1));

            blurredBackground.Save($@"C:\Thermobox\background{++_backgroundIndex}.jpg");
            MotionFinder = new MotionFinder<byte>(blurredBackground);

            _lastBackgroundReset = _timeProvider.Now;
            _noBoundingBox = null;
            _resetBackground = null;
            _foundNothingCount = 0;
        }
    }
}
