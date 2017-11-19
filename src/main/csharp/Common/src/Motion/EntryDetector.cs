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
        /// Once this threshold is reached, the recording will be resumed.
        /// After we find bounding boxes again, we resume.
        /// </summary>
        public const int NoMotionPauseThreshold = 5;

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
        /// Assuming a train has 6 cars, each about 25 meters long and it travels at 1 km/h that
        /// would result in 10 minutes or 600 seconds.
        /// </summary>
        public const int MaxRecordingDuration = 600;

        public MotionFinder<byte> MotionFinder { get; private set; }

        private int _recordingSeconds;
        private int _foundNothingCount;
        private DateTime? _noBoundingBox;
        private DateTime? _lastTick;
        private DateTime? _noMotionCount;
        private DateTime _entryDateTime = DateTime.MinValue;
        private DateTime? _exitDateTime;
        private Image<Gray, byte>[] _images;

        private bool _paused;
        private readonly ITimeProvider _timeProvider = new ActualTimeProvider();

        public EntryDetector()
        {
            // constructor with no background image, background will be initialized lazily
        }

        public EntryDetector(Image<Gray, byte> background)
        {
            UpdateMotionFinder(background);
        }

        public EntryDetector(Image<Gray, byte> background, ITimeProvider timeProvider) : this(background)
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
                UpdateMotionFinder(_images.First());
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

            if (MotionFinder == null)
            {
                UpdateMotionFinder(_images.First());
            }

            var threshold = new Gray(20.0);
            var maxValue = new Gray(byte.MaxValue);

            var boundingBoxes = _images
                .Select(image => MotionFinder.FindBoundingBox(image, threshold, maxValue))
                .Where(box => box.HasValue)
                .Select(box => box.Value)
                .ToArray();

            Evaluate(boundingBoxes);

            // After some time we need to use a new background.
            // We do this if either no bounding box was found n times or if nothing was the result n times.

            if (boundingBoxes.Any())
            {
                _noBoundingBox = null;

                if (CurrentState != DetectorState.Entry)
                {
                    return;
                }

                if (!MotionFinder.HasDifference(_images.First(), _images.Last(), threshold, maxValue))
                {
                    if (_noMotionCount == null)
                    {
                        // The train (or whatever) is covering the whole image and it's not moving
                        _noMotionCount = _timeProvider.Now;
                    }
                }
                else if (_paused)
                {
                    // We were paused => resume since we have found some moving things again.
                    Resume?.Invoke(this, new EventArgs());
                    _paused = false;
                }

                if (_paused ||
                    _noMotionCount == null ||
                    _timeProvider.Now.Subtract(TimeSpan.FromSeconds(NoMotionPauseThreshold)) <= _noMotionCount)
                {
                    return;
                }

                // Not found moving things for a while => pause until we find movement again.
                _noMotionCount = null;
                Pause?.Invoke(this, new EventArgs());
                _paused = true;

                return;
            }

            if (!_noBoundingBox.HasValue)
            {
                _noBoundingBox = _timeProvider.Now;
            }

            if (_timeProvider.Now.Subtract(_noBoundingBox.Value).TotalSeconds > NoBoundingBoxBackgroundThreshold)
            {
                UpdateMotionFinder(_images.First());
            }
        }

        private void Evaluate(IReadOnlyCollection<Rectangle> boundingBoxes)
        {
            // Not found anything useful.
            if (!boundingBoxes.Any())
            {
                return;
            }

            var first = boundingBoxes.First();

            var threshold = MotionFinder.Background.Size.Width / 100;
            var leftBound = first.X < threshold;
            var rightBound = first.X + first.Width > MotionFinder.Background.Width - threshold;

            if (!leftBound && !rightBound)
            {
                return;
            }

            var lastWidth = first.Width;

            var indicator = 0;

            // Count if the box is thinning or widening.
            foreach (var box in boundingBoxes)
            {
                if (box.Width > lastWidth)
                {
                    indicator++;
                }
                else if (box.Width < lastWidth)
                {
                    indicator--;
                }

                lastWidth = box.Width;
            }

            // The count of images that indicates consistent width change.
            // The first image represents the start, so we cannot count that.
            var referenceCount = boundingBoxes.Count - 1;

            // Entry
            if (indicator == referenceCount)
            {
                ChangeState(DetectorState.Entry);
                return;
            }

            // Exit
            if (indicator == -referenceCount)
            {
                ChangeState(DetectorState.Exit);
                return;
            }

            // Nothing
            ChangeState(DetectorState.Nothing);
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

        private void UpdateMotionFinder(Image<Gray, byte> background)
        {
            if (CurrentState == DetectorState.Entry)
            {
                // do not update background as long as something has entered and not exited yet
                return;
            }

            Log.Debug("(Re)initializing background");
            MotionFinder = new MotionFinder<byte>(background);
            _noBoundingBox = null;
            _foundNothingCount = 0;
        }
    }
}
