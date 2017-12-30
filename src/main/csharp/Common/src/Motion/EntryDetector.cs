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

        public DetectorState CurrentState { get; set; } = DetectorState.Nothing;

        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Once this threshold is reached, the background will be reinitailized.
        /// </summary>
        public const int NoBoundingBoxBackgroundThreshold = 30;

        /// <summary>
        /// The minimum seconds that have to pass after a train exited until one can
        /// trigger a new recording.
        /// </summary>
        public const int MinTimeAfterExit = 3;

        /// <summary>
        /// The minimum seconds that has to pass after the train entered. Otherwise an abort will
        /// be published.
        /// </summary>
        public const int MinTimeAfterEntry = 60;

        /// <summary>
        /// Maximum seconds a recording can be. After it has passed, the recording will be stopped.
        /// It may happen that a train stands still at the entrance for quite a while.
        /// </summary>
        public const int MaxRecordingDuration = 60 * 45;

        /// <summary>
        /// Timeout in seconds background reset to accomodate camera adjusting lens exposure time.
        /// </summary>
        public const int AutoExposureTimeout = 2;

        /// <summary>
        /// Threshold that has to be met in succession.
        /// </summary>
        public const int ExitThreshold = 8;

        /// <summary>
        /// Timeout in minutes after the background will be force reset.
        /// </summary>
        private const int ForceBackgroundResetTimeout = 5;

        /// <summary>
        /// Time in seconds for how long we keep the camera rolling after we detect an exit.
        /// </summary>
        private const int ExitScheduleTime = 20;

        private int _foundNothingCount;
        private DateTime? _noBoundingBox;
        private DateTime _entryDateTime = DateTime.MinValue;
        private DateTime? _exitDateTime;
        private DateTime? _resetBackground;
        private DateTime _lastBackgroundReset = DateTime.MaxValue;
        private DateTime? _scheduledExit;
        private DateTime _scheduledExposureCorrection = DateTime.MaxValue;

        private Image<Gray, byte>[] _images;

        private static long _backgroundIndex;
        private int _exitLikelihood;
        private static int _i;

        private readonly ITimeProvider _timeProvider = new ActualTimeProvider();
        private readonly Action _correctExposure;
        private double _backgroundMean;

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

        private void OnTrainExit(bool abort)
        {
            CurrentState = DetectorState.Exit;
            _exitDateTime = _timeProvider.Now;
            _scheduledExit = null;

            if (abort)
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
            if (_correctExposure != null && _scheduledExposureCorrection > _timeProvider.Now)
            {
                // Correct the exposure
                _correctExposure?.Invoke();
                _scheduledExposureCorrection = _timeProvider.Now.Subtract(TimeSpan.FromSeconds(90));
            }

            if (_scheduledExit.HasValue)
            {
                if (_timeProvider.Now >= _scheduledExit)
                {
                    ChangeState(DetectorState.Exit);
                }

                return;
            }

            if (CurrentState == DetectorState.Entry)
            {
                if (_entryDateTime.AddSeconds(MaxRecordingDuration) > _timeProvider.Now)
                {
                    // We are in enter mode for quite long now, we should abort.
                    Log.Warn($"Recording for longer than {MaxRecordingDuration}s.");
                    OnTrainExit(false);
                    return;
                }
            }

            _images = images;

            if (_backgroundMean <= 0 ||
                _resetBackground.HasValue && _resetBackground.Value < _timeProvider.Now ||
                CurrentState != DetectorState.Entry &&
                _timeProvider.Now.Subtract(_lastBackgroundReset).TotalMinutes > ForceBackgroundResetTimeout)
            {
                ResetBackground(_images.Last());
            }

            EvaluateState(_images);
        }

        private void EvaluateState(IReadOnlyCollection<Image<Gray, byte>> images)
        {
            var backgroundMean = _backgroundMean * images.Count;
            var imageMean = images
                .Select(img => CvInvoke.Mean(img).V0)
                .Sum();

            ///// DEBUG
            if (backgroundMean - imageMean > backgroundMean * .05 && backgroundMean - imageMean <= backgroundMean * .15)
            {
                Log.Info($"DEBUG: BG Mean: {backgroundMean}, image mean: {imageMean}");
            }
            ///// END DEBUG

            if (backgroundMean - imageMean > backgroundMean * .15)
            {
                ///// DEBUG
                _i++;
                if (_i % 20 == 0)
                {
                    _images.Last().Save($@"C:\Thermobox\trigger-{_i}.jpg");
                }
                Log.Info($"TRIGGER: BG Mean: {backgroundMean}, image mean: {imageMean}");
                ///// END DEBUG

                _noBoundingBox = null;
                HandleEntry();

                return;
            }

            if (CurrentState == DetectorState.Entry)
            {
                ///// DEBUG
                Log.Info($"EXIT: BG Mean: {backgroundMean}, image mean: {imageMean}");
                ///// END DEBUG

                // activate background reset timer
                _noBoundingBox = _timeProvider.Now.Subtract(TimeSpan.FromSeconds(NoBoundingBoxBackgroundThreshold - 5));
                HandleExit();

                return;
            }

            if (_noBoundingBox == null)
            {
                Log.Info("No difference => setting background reset timer");
                _noBoundingBox = _timeProvider.Now;
            }

            if (_timeProvider.Now.Subtract(_noBoundingBox.Value).TotalSeconds > NoBoundingBoxBackgroundThreshold)
            {
                UpdateMotionFinder();
            }

            // Nothing
            ChangeState(DetectorState.Nothing);
        }

        private void HandleEntry()
        {
            ChangeState(DetectorState.Entry);
        }

        private void HandleExit()
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

            _exitLikelihood = 0;

            if (_timeProvider.Now.Subtract(TimeSpan.FromSeconds(MinTimeAfterEntry)) < _entryDateTime)
            {
                OnTrainExit(true);
                return;
            }

            Log.Info($"Exit confirmed. Stopping recording in {ExitScheduleTime} seconds.");
            _scheduledExit = _timeProvider.Now.AddSeconds(ExitScheduleTime);
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
                    OnTrainExit(false);
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
            _backgroundMean = CvInvoke.Mean(blurredBackground).V0;

            _lastBackgroundReset = _timeProvider.Now;
            _noBoundingBox = null;
            _resetBackground = null;
            _foundNothingCount = 0;
        }
    }
}
