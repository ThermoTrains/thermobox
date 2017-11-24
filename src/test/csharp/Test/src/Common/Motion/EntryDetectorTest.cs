using System;
using System.Linq;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SebastianHaeni.ThermoBox.Common.Motion;
using SebastianHaeni.ThermoBox.Common.Time;

namespace Test.Common.Motion
{
    [TestClass]
    public class EntryDetectorTest
    {
        private static readonly DateTime Year2000 = DateTime.Parse("2000-01-01 00:00:00");

        private static Image<Gray, byte> Background =>
            new Image<Rgb, byte>(@"Resources\train-background.jpg").Convert<Gray, byte>();

        private static Image<Gray, byte> FlippedBackground
        {
            get
            {
                var background = new Image<Rgb, byte>(@"Resources\train-background.jpg").Convert<Gray, byte>();
                CvInvoke.Flip(background, background, FlipType.Horizontal);
                return background;
            }
        }

        [TestMethod]
        public void DetectRightEntryTest()
        {
            var detector = new EntryDetector(Background);

            var images = Enumerable.Range(2, 3)
                .Select(i => new Image<Rgb, byte>($@"Resources\train-{i}.jpg"))
                .Select(train => train.Convert<Gray, byte>())
                .ToArray();

            detector.Tick(images);

            Assert.AreEqual(DetectorState.Entry, detector.CurrentState);
        }

        [TestMethod]
        public void DetectLeftEntryTest()
        {
            var detector = new EntryDetector(FlippedBackground);

            var images = Enumerable.Range(2, 3)
                .Select(i => new Image<Rgb, byte>($@"Resources\train-{i}.jpg"))
                .Select(image => image.Convert<Gray, byte>())
                .Select(image =>
                {
                    // flipping the image and reverse the order will produce a train entering from the other side
                    CvInvoke.Flip(image, image, FlipType.Horizontal);
                    return image;
                })
                .ToArray();

            detector.Tick(images);
            Assert.AreEqual(DetectorState.Entry, detector.CurrentState);
        }

        [TestMethod]
        public void DetectRightExitTest()
        {
            var detector = new EntryDetector(Background)
            {
                CurrentState = DetectorState.Entry
            };

            var images = Enumerable.Range(1, 4)
                .Select(i => new Image<Rgb, byte>($@"Resources\train-{i}.jpg"))
                .Select(image => image.Convert<Gray, byte>())
                .Reverse() // just by reversing, the train exits :)
                .ToArray();

            detector.Tick(images);

            Assert.AreEqual(DetectorState.Exit, detector.CurrentState);
        }

        [TestMethod]
        public void DetectLeftExitTest()
        {
            var detector = new EntryDetector(FlippedBackground) {CurrentState = DetectorState.Entry};

            var images = Enumerable.Range(1, 4)
                .Select(i => new Image<Rgb, byte>($@"Resources\train-{i}.jpg"))
                .Select(image => image.Convert<Gray, byte>())
                .Select(image =>
                {
                    // flipping the image and reverse the order will produce a train entering from the other side
                    CvInvoke.Flip(image, image, FlipType.Horizontal);
                    return image;
                })
                .Reverse()
                .ToArray();

            detector.Tick(images);

            Assert.AreEqual(DetectorState.Exit, detector.CurrentState);
        }

        [TestMethod]
        public void DetectNothingTest()
        {
            var detector = new EntryDetector(Background);

            // confusing the algorithm should just say "nothing"
            var image1 = new Image<Rgb, byte>(@"Resources\train-1.jpg");
            var image2 = new Image<Rgb, byte>(@"Resources\train-2.jpg");
            var image3 = new Image<Rgb, byte>(@"Resources\train-3.jpg");
            var image4 = new Image<Rgb, byte>(@"Resources\train-2.jpg");

            var images = new[] {image1, image2, image3, image4}
                .Select(image => image.Convert<Gray, byte>())
                .ToArray();

            detector.CurrentState = DetectorState.Exit;

            detector.Tick(images);

            Assert.AreEqual(DetectorState.Nothing, detector.CurrentState);
        }

        /// <summary>
        /// This test provokes an abort because the exit follows next after the entry.
        /// </summary>
        [TestMethod]
        public void TestAbort()
        {
            var timeProvider = new ExternalTimeProvider();
            var detector = new EntryDetector(Background, timeProvider);
            var enterRaised = false;
            var abortRaised = false;

            detector.Enter += (sender, args) => enterRaised = true;
            detector.Abort += (sender, args) => abortRaised = true;
            detector.Exit += (sender, args) => Assert.Fail("Exit not expected");
            detector.Pause += (sender, args) => Assert.Fail("Pause not expected");
            detector.Resume += (sender, args) => Assert.Fail("Resume not expected");

            var entryImages = Enumerable.Range(2, 3)
                .Select(i => new Image<Rgb, byte>($@"Resources\train-{i}.jpg"))
                .Select(train => train.Convert<Gray, byte>())
                .ToArray();

            var exitImages = Enumerable.Range(1, 4)
                .Select(i => new Image<Rgb, byte>($@"Resources\train-{i}.jpg"))
                .Select(image => image.Convert<Gray, byte>())
                .Reverse() // just by reversing, the train exits :)
                .ToArray();

            timeProvider.CurrentTime = Year2000;
            detector.Tick(entryImages);
            Assert.AreEqual(DetectorState.Entry, detector.CurrentState);
            Assert.IsTrue(enterRaised);
            Assert.IsFalse(abortRaised);

            timeProvider.CurrentTime = timeProvider.CurrentTime.AddSeconds(EntryDetector.MinTimeAfterEntry - 1);
            detector.Tick(exitImages);
            Assert.AreEqual(DetectorState.Exit, detector.CurrentState);
            Assert.IsTrue(abortRaised);
        }

        /// <summary>
        /// This test provokes an abort because the exit follows next after the entry.
        /// </summary>
        [TestMethod]
        public void TestNoAbort()
        {
            var timeProvider = new ExternalTimeProvider();
            var detector = new EntryDetector(Background, timeProvider);
            var enterRaised = false;
            var exitRaised = false;

            detector.Enter += (sender, args) => enterRaised = true;
            detector.Exit += (sender, args) => exitRaised = true;
            detector.Abort += (sender, args) => Assert.Fail("Abort not expected");
            detector.Pause += (sender, args) => Assert.Fail("Pause not expected");
            detector.Resume += (sender, args) => Assert.Fail("Resume not expected");

            var entryImages = Enumerable.Range(2, 3)
                .Select(i => new Image<Rgb, byte>($@"Resources\train-{i}.jpg"))
                .Select(train => train.Convert<Gray, byte>())
                .ToArray();

            var exitImages = Enumerable.Range(1, 4)
                .Select(i => new Image<Rgb, byte>($@"Resources\train-{i}.jpg"))
                .Select(image => image.Convert<Gray, byte>())
                .Reverse() // just by reversing, the train exits :)
                .ToArray();

            timeProvider.CurrentTime = Year2000;
            detector.Tick(entryImages);
            Assert.AreEqual(DetectorState.Entry, detector.CurrentState);
            Assert.IsTrue(enterRaised);
            Assert.IsFalse(exitRaised);

            timeProvider.CurrentTime = timeProvider.CurrentTime.AddSeconds(EntryDetector.MinTimeAfterEntry);
            detector.Tick(exitImages);
            Assert.AreEqual(DetectorState.Exit, detector.CurrentState);
            Assert.IsTrue(exitRaised);
        }

        /// <summary>
        /// Create entry and then nothing happens, the train just keeps moving. This should provoke a stop.
        /// </summary>
        [TestMethod]
        public void TestStopTooLong()
        {
            var timeProvider = new ExternalTimeProvider();
            var detector = new EntryDetector(Background, timeProvider);
            var enterRaised = false;
            var exitRaised = false;

            detector.Enter += (sender, args) => enterRaised = true;
            detector.Exit += (sender, args) => exitRaised = true;
            detector.Abort += (sender, args) => Assert.Fail("Abort not expected");
            detector.Pause += (sender, args) => Assert.Fail("Pause not expected");
            detector.Resume += (sender, args) => Assert.Fail("Resume not expected");

            var entryImages = Enumerable.Range(2, 3)
                .Select(i => new Image<Rgb, byte>($@"Resources\train-{i}.jpg"))
                .Select(train => train.Convert<Gray, byte>())
                .ToArray();

            var slowlyMovingTrain = Enumerable.Range(0, 1)
                .Select(i => new Image<Rgb, byte>(@"Resources\train-4.jpg"))
                .Select(image => image.Convert<Gray, byte>())
                .ToArray();

            timeProvider.CurrentTime = Year2000;
            detector.Tick(entryImages);
            Assert.AreEqual(DetectorState.Entry, detector.CurrentState);
            Assert.IsTrue(enterRaised);
            Assert.IsFalse(exitRaised);

            timeProvider.CurrentTime = timeProvider.CurrentTime.AddSeconds(EntryDetector.MaxRecordingDuration + 1);
            detector.Tick(slowlyMovingTrain);
            Assert.AreEqual(DetectorState.Exit, detector.CurrentState);
            Assert.IsTrue(exitRaised);
        }

        /// <summary>
        /// Create entry and then nothing happens, the train just keeps moving. This should provoke a stop 
        /// even if there is a long pause in between.
        /// </summary>
        [TestMethod]
        public void TestStopTooLongWithPause()
        {
            var timeProvider = new ExternalTimeProvider();
            var detector = new EntryDetector(Background, timeProvider);
            var enterRaised = false;
            var exitRaised = false;

            detector.Enter += (sender, args) => enterRaised = true;
            detector.Exit += (sender, args) => exitRaised = true;
            detector.Abort += (sender, args) => Assert.Fail("Abort not expected");
            detector.Pause += (sender, args) => Assert.Fail("Pause not expected");
            detector.Resume += (sender, args) => Assert.Fail("Resume not expected");

            var entryImages = Enumerable.Range(2, 3)
                .Select(i => new Image<Rgb, byte>($@"Resources\train-{i}.jpg"))
                .Select(train => train.Convert<Gray, byte>())
                .ToArray();

            var slowlyMovingTrain = Enumerable.Range(0, 1)
                .Select(i => new Image<Rgb, byte>(@"Resources\train-4.jpg"))
                .Select(image => image.Convert<Gray, byte>())
                .ToArray();

            timeProvider.CurrentTime = Year2000;
            detector.Tick(entryImages);
            Assert.AreEqual(DetectorState.Entry, detector.CurrentState);
            Assert.IsTrue(enterRaised);
            Assert.IsFalse(exitRaised);

            timeProvider.CurrentTime = timeProvider.CurrentTime.AddSeconds(EntryDetector.MaxRecordingDuration + 1);
            detector.Tick(slowlyMovingTrain);
            Assert.AreEqual(DetectorState.Exit, detector.CurrentState);
            Assert.IsTrue(exitRaised);
        }

        /// <summary>
        /// Test if the train stops moving, that the recording is paused.
        /// </summary>
        [TestMethod]
        public void TestPause()
        {
            var timeProvider = new ExternalTimeProvider();
            var detector = new EntryDetector(Background, timeProvider);
            var enterRaised = false;
            var pauseRaised = false;

            detector.Enter += (sender, args) => enterRaised = true;
            detector.Exit += (sender, args) => Assert.Fail("Exit not expected");
            detector.Abort += (sender, args) => Assert.Fail("Abort not expected");
            detector.Pause += (sender, args) => pauseRaised = true;
            detector.Resume += (sender, args) => Assert.Fail("Resume not expected");

            var entryImages = Enumerable.Range(2, 3)
                .Select(i => new Image<Rgb, byte>($@"Resources\train-{i}.jpg"))
                .Select(train => train.Convert<Gray, byte>())
                .ToArray();

            var noMotionImages = Enumerable.Range(0, 1)
                .Select(i => new Image<Rgb, byte>(@"Resources\train-3.jpg"))
                .Select(image => image.Convert<Gray, byte>())
                .ToArray();

            timeProvider.CurrentTime = Year2000;
            detector.Tick(entryImages);
            Assert.AreEqual(DetectorState.Entry, detector.CurrentState);
            Assert.IsTrue(enterRaised);
            Assert.IsFalse(pauseRaised);

            // tick with no motion (1)
            detector.Tick(noMotionImages);
            Assert.AreEqual(DetectorState.Entry, detector.CurrentState);
            Assert.IsFalse(pauseRaised);

            // tick with no motion (2)
            timeProvider.CurrentTime = timeProvider.CurrentTime.AddSeconds(EntryDetector.NoMotionPauseThreshold + 1);
            detector.Tick(noMotionImages);
            Assert.AreEqual(DetectorState.Entry, detector.CurrentState);
            Assert.IsTrue(pauseRaised);
        }

        [TestMethod]
        public void TestLongPause()
        {
            var timeProvider = new ExternalTimeProvider();
            var detector = new EntryDetector(Background, timeProvider);
            var enterRaised = false;
            var pauseRaised = false;
            var resumeRaised = false;

            detector.Enter += (sender, args) => enterRaised = true;
            detector.Exit += (sender, args) => Assert.Fail("Exit not expected");
            detector.Abort += (sender, args) => Assert.Fail("Abort not expected");
            detector.Pause += (sender, args) => pauseRaised = true;
            detector.Resume += (sender, args) => resumeRaised = true;

            var entryImages = Enumerable.Range(2, 3)
                .Select(i => new Image<Rgb, byte>($@"Resources\train-{i}.jpg"))
                .Select(train => train.Convert<Gray, byte>())
                .ToArray();

            var noMotionImages = Enumerable.Range(0, 1)
                .Select(i => new Image<Rgb, byte>(@"Resources\train-3.jpg"))
                .Select(image => image.Convert<Gray, byte>())
                .ToArray();

            var resumingImages = Enumerable.Range(3, 2)
                .Select(i => new Image<Rgb, byte>($@"Resources\train-{i}.jpg"))
                .Select(image => image.Convert<Gray, byte>())
                .ToArray();

            timeProvider.CurrentTime = Year2000;
            detector.Tick(entryImages);
            Assert.AreEqual(DetectorState.Entry, detector.CurrentState);
            Assert.IsTrue(enterRaised);
            Assert.IsFalse(pauseRaised);

            // tick with no motion (1)
            detector.Tick(noMotionImages);
            Assert.AreEqual(DetectorState.Entry, detector.CurrentState);
            Assert.IsFalse(pauseRaised);

            // tick with no motion (2)
            timeProvider.CurrentTime = timeProvider.CurrentTime.AddSeconds(EntryDetector.NoMotionPauseThreshold + 1);
            detector.Tick(noMotionImages);
            Assert.AreEqual(DetectorState.Entry, detector.CurrentState);
            Assert.IsTrue(pauseRaised);
            pauseRaised = false;

            // after a long pause the video should not be stopped
            timeProvider.CurrentTime = timeProvider.CurrentTime.AddSeconds(EntryDetector.MaxRecordingDuration);
            detector.Tick(resumingImages);
            timeProvider.CurrentTime = timeProvider.CurrentTime.AddSeconds(2);
            detector.Tick(resumingImages);
            Assert.AreEqual(DetectorState.Entry, detector.CurrentState);
            Assert.IsFalse(pauseRaised);
            Assert.IsTrue(resumeRaised);
        }

        /// <summary>
        /// Tests if the recording was paused, that the recording is resumed after the train picks up speed again.
        /// </summary>
        [TestMethod]
        public void TestResume()
        {
            var timeProvider = new ExternalTimeProvider();
            var detector = new EntryDetector(Background, timeProvider);
            var enterRaised = false;
            var pauseRaised = false;
            var resumeRaised = false;

            detector.Enter += (sender, args) => enterRaised = true;
            detector.Exit += (sender, args) => Assert.Fail("Exit not expected");
            detector.Abort += (sender, args) => Assert.Fail("Abort not expected");
            detector.Pause += (sender, args) => pauseRaised = true;
            detector.Resume += (sender, args) => resumeRaised = true;

            var entryImages = Enumerable.Range(2, 3)
                .Select(i => new Image<Rgb, byte>($@"Resources\train-{i}.jpg"))
                .Select(train => train.Convert<Gray, byte>())
                .ToArray();

            var noMotionImages = Enumerable.Range(0, 1)
                .Select(i => new Image<Rgb, byte>(@"Resources\train-3.jpg"))
                .Select(image => image.Convert<Gray, byte>())
                .ToArray();

            var resumingImages = Enumerable.Range(3, 2)
                .Select(i => new Image<Rgb, byte>($@"Resources\train-{i}.jpg"))
                .Select(image => image.Convert<Gray, byte>())
                .ToArray();

            timeProvider.CurrentTime = Year2000;
            detector.Tick(entryImages);
            Assert.AreEqual(DetectorState.Entry, detector.CurrentState);
            Assert.IsTrue(enterRaised);
            Assert.IsFalse(pauseRaised);

            // tick with no motion (1)
            detector.Tick(noMotionImages);
            Assert.AreEqual(DetectorState.Entry, detector.CurrentState);
            Assert.IsFalse(pauseRaised);

            // tick with no motion (2)
            timeProvider.CurrentTime = timeProvider.CurrentTime.AddSeconds(EntryDetector.NoMotionPauseThreshold + 1);
            detector.Tick(noMotionImages);
            Assert.AreEqual(DetectorState.Entry, detector.CurrentState);
            Assert.IsTrue(pauseRaised);
            pauseRaised = false;

            // tick with motion again (3)
            timeProvider.CurrentTime = timeProvider.CurrentTime.AddSeconds(5);
            detector.Tick(resumingImages);
            timeProvider.CurrentTime = timeProvider.CurrentTime.AddSeconds(5);
            detector.Tick(resumingImages);
            Assert.AreEqual(DetectorState.Entry, detector.CurrentState);
            Assert.IsFalse(pauseRaised);
            Assert.IsTrue(resumeRaised);
        }

        /// <summary>
        /// Test that no new recording is started if shortly after a stop a new train is picked up.
        /// </summary>
        [TestMethod]
        public void TestFastExitEntryRefusal()
        {
            var timeProvider = new ExternalTimeProvider();
            var detector = new EntryDetector(Background, timeProvider);
            var enterRaised = false;
            var exitRaised = false;

            detector.Enter += (sender, args) => enterRaised = true;
            detector.Exit += (sender, args) => exitRaised = true;
            detector.Abort += (sender, args) => Assert.Fail("Abort not expected");
            detector.Pause += (sender, args) => Assert.Fail("Pause not expected");
            detector.Resume += (sender, args) => Assert.Fail("Resume not expected");

            var entryImages = Enumerable.Range(2, 3)
                .Select(i => new Image<Rgb, byte>($@"Resources\train-{i}.jpg"))
                .Select(train => train.Convert<Gray, byte>())
                .ToArray();

            var exitImages = Enumerable.Range(1, 4)
                .Select(i => new Image<Rgb, byte>($@"Resources\train-{i}.jpg"))
                .Select(image => image.Convert<Gray, byte>())
                .Reverse() // just by reversing, the train exits :)
                .ToArray();

            timeProvider.CurrentTime = Year2000;
            detector.Tick(entryImages);
            Assert.AreEqual(DetectorState.Entry, detector.CurrentState);
            Assert.IsTrue(enterRaised);
            Assert.IsFalse(exitRaised);

            timeProvider.CurrentTime = timeProvider.CurrentTime.AddSeconds(EntryDetector.MinTimeAfterEntry + 1);
            detector.Tick(exitImages);
            Assert.AreEqual(DetectorState.Exit, detector.CurrentState);
            Assert.IsTrue(exitRaised);
            enterRaised = false;

            timeProvider.CurrentTime = timeProvider.CurrentTime.AddSeconds(EntryDetector.MinTimeAfterExit - 1);
            detector.Tick(entryImages);
            Assert.AreEqual(DetectorState.Exit, detector.CurrentState);
            Assert.IsFalse(enterRaised);
        }

        /// <summary>
        /// Full scenario test. The train enters, then stops within the frame for 15 minutes, then starts moving again
        /// and eventually exits. And does it again shortly after. Testing if the state is correctly reset.
        /// </summary>
        [TestMethod]
        public void TestFull()
        {
            var timeProvider = new ExternalTimeProvider {CurrentTime = Year2000};
            var detector = new EntryDetector(Background, timeProvider);

            var entryImages = Enumerable.Range(2, 3)
                .Select(i => new Image<Rgb, byte>($@"Resources\train-{i}.jpg"))
                .Select(train => train.Convert<Gray, byte>())
                .ToArray();

            var noMotionImages = Enumerable.Range(0, 1)
                .Select(i => new Image<Rgb, byte>(@"Resources\train-3.jpg"))
                .Select(image => image.Convert<Gray, byte>())
                .ToArray();

            var resumingImages = Enumerable.Range(3, 2)
                .Select(i => new Image<Rgb, byte>($@"Resources\train-{i}.jpg"))
                .Select(image => image.Convert<Gray, byte>())
                .ToArray();

            var exitImages = Enumerable.Range(1, 4)
                .Select(i => new Image<Rgb, byte>($@"Resources\train-{i}.jpg"))
                .Select(image => image.Convert<Gray, byte>())
                .Reverse() // just by reversing, the train exits :)
                .ToArray();

            for (var k = 0; k < 2; k++)
            {
                var enterRaised = false;
                var exitRaised = false;
                var pauseRaised = false;
                var resumeRaised = false;

                detector.Enter += (sender, args) => enterRaised = true;
                detector.Exit += (sender, args) => exitRaised = true;
                detector.Abort += (sender, args) => Assert.Fail("Abort not expected");
                detector.Pause += (sender, args) => pauseRaised = true;
                detector.Resume += (sender, args) => resumeRaised = true;

                // entry
                timeProvider.CurrentTime = timeProvider.CurrentTime.AddSeconds(5);
                detector.Tick(entryImages);
                Assert.AreEqual(DetectorState.Entry, detector.CurrentState);
                Assert.IsTrue(enterRaised);
                Assert.IsFalse(pauseRaised);

                // no motion
                detector.Tick(noMotionImages);
                Assert.AreEqual(DetectorState.Entry, detector.CurrentState);
                Assert.IsFalse(pauseRaised);

                // pause
                timeProvider.CurrentTime =
                    timeProvider.CurrentTime.AddSeconds(EntryDetector.NoMotionPauseThreshold + 1);
                detector.Tick(noMotionImages);
                Assert.AreEqual(DetectorState.Entry, detector.CurrentState);
                Assert.IsTrue(pauseRaised);
                pauseRaised = false;

                // resume
                timeProvider.CurrentTime = timeProvider.CurrentTime.AddSeconds(15 * 60);
                detector.Tick(resumingImages);
                timeProvider.CurrentTime = timeProvider.CurrentTime.AddSeconds(2);
                detector.Tick(resumingImages);
                Assert.AreEqual(DetectorState.Entry, detector.CurrentState);
                Assert.IsFalse(pauseRaised);
                Assert.IsTrue(resumeRaised);

                // exit
                timeProvider.CurrentTime = timeProvider.CurrentTime.AddSeconds(5);
                detector.Tick(exitImages);
                Assert.AreEqual(DetectorState.Exit, detector.CurrentState);
                Assert.IsFalse(pauseRaised);
                Assert.IsTrue(exitRaised);
            }
        }

        /// <summary>
        /// Test that the background gets initialized lazily if not provided directly.
        /// </summary>
        [TestMethod]
        public void TestBackgrounLazilyInitialized()
        {
            var detector = new EntryDetector();

            var images = Enumerable.Range(0, 4)
                .Select(i => new Image<Rgb, byte>($@"Resources\train-{i}.jpg"))
                .Select(train => train.Convert<Gray, byte>())
                .ToArray();

            detector.Tick(images);

            Assert.AreEqual(DetectorState.Entry, detector.CurrentState);
        }

        /// <summary>
        /// Test if some bounding box appears in the middle, like a bird, that the algorithm does not something weird.
        /// </summary>
        [TestMethod]
        public void TestBird()
        {
            var detector = new EntryDetector(Background);

            var images = Enumerable.Range(0, 1)
                .Select(i => new Image<Rgb, byte>(@"Resources\train-simulated-bird.jpg"))
                .Select(train => train.Convert<Gray, byte>())
                .ToArray();

            detector.Tick(images);

            Assert.AreEqual(DetectorState.Nothing, detector.CurrentState);
        }

        /// <summary>
        /// Provoke a reinitialization of the background after nothing happened for a long time.
        /// </summary>
        [TestMethod]
        public void TestReinitializationOfBackground()
        {
            var background = Background;
            var timeProvider = new ExternalTimeProvider();
            var detector = new EntryDetector(background, timeProvider);

            var images = Enumerable.Range(0, 1)
                .Select(i => new Image<Rgb, byte>($@"Resources\train-0.jpg"))
                .Select(train => train.Convert<Gray, byte>())
                .ToArray();

            // initialize
            timeProvider.CurrentTime = Year2000;
            detector.Tick(images);

            // no motion => schedule reset
            timeProvider.CurrentTime =
                timeProvider.CurrentTime.AddSeconds(EntryDetector.NoBoundingBoxBackgroundThreshold + 1);
            detector.Tick(images);

            // do reset
            timeProvider.CurrentTime = timeProvider.CurrentTime.AddSeconds(EntryDetector.AutoExposureTimeout + 1);
            detector.Tick(images);

            Assert.AreNotEqual(background, detector.MotionFinder.Background);
        }

        /// <summary>
        /// Provoke a reinitialization of the background after nothing happened for a long time. But 
        /// it should happen since the state is "Entry".
        /// </summary>
        [TestMethod]
        public void TestReinitializationOfBackgroundNotWhenTrainEntry()
        {
            var background = Background;
            var timeProvider = new ExternalTimeProvider();
            var detector = new EntryDetector(background, timeProvider);

            var images = Enumerable.Range(0, 1)
                .Select(i => new Image<Rgb, byte>($@"Resources\train-0.jpg"))
                .Select(train => train.Convert<Gray, byte>())
                .ToArray();

            timeProvider.CurrentTime = Year2000;
            detector.Tick(images);

            detector.CurrentState = DetectorState.Entry;

            timeProvider.CurrentTime =
                timeProvider.CurrentTime.AddSeconds(EntryDetector.NoBoundingBoxBackgroundThreshold + 1);
            detector.Tick(images);

            Assert.AreEqual(background, detector.MotionFinder.Background);
        }

        /// <summary>
        /// Test that resuming doesn't happen too fast so the slightest difference does not trigger.
        /// </summary>
        [TestMethod]
        public void TestNoFastResume()
        {
            var timeProvider = new ExternalTimeProvider();
            var detector = new EntryDetector(Background, timeProvider);
            var enterRaised = false;
            var pauseRaised = false;

            detector.Enter += (sender, args) => enterRaised = true;
            detector.Exit += (sender, args) => Assert.Fail("Exit not expected");
            detector.Abort += (sender, args) => Assert.Fail("Abort not expected");
            detector.Pause += (sender, args) => pauseRaised = true;
            detector.Resume += (sender, args) => Assert.Fail("Resume not expected");

            var entryImages = Enumerable.Range(2, 3)
                .Select(i => new Image<Rgb, byte>($@"Resources\train-{i}.jpg"))
                .Select(train => train.Convert<Gray, byte>())
                .ToArray();

            var noMotionImages = Enumerable.Range(0, 1)
                .Select(i => new Image<Rgb, byte>(@"Resources\train-3.jpg"))
                .Select(image => image.Convert<Gray, byte>())
                .ToArray();

            var resumingImages = Enumerable.Range(3, 2)
                .Select(i => new Image<Rgb, byte>($@"Resources\train-{i}.jpg"))
                .Select(image => image.Convert<Gray, byte>())
                .ToArray();

            timeProvider.CurrentTime = Year2000;
            detector.Tick(entryImages);
            Assert.AreEqual(DetectorState.Entry, detector.CurrentState);
            Assert.IsTrue(enterRaised);
            Assert.IsFalse(pauseRaised);

            // tick with no motion (1)
            detector.Tick(noMotionImages);
            Assert.AreEqual(DetectorState.Entry, detector.CurrentState);
            Assert.IsFalse(pauseRaised);

            // tick with no motion (2)
            timeProvider.CurrentTime = timeProvider.CurrentTime.AddSeconds(EntryDetector.NoMotionPauseThreshold + 1);
            detector.Tick(noMotionImages);
            Assert.AreEqual(DetectorState.Entry, detector.CurrentState);
            Assert.IsTrue(pauseRaised);

            // motion that should not trigger since not enough time has passed to trigger a resume
            detector.Tick(resumingImages);
            Assert.AreEqual(DetectorState.Entry, detector.CurrentState);
        }
    }
}
