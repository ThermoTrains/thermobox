using System;
using System.Linq;
using Emgu.CV;
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

        private static readonly Image<Gray, byte>[] EntryImages = Enumerable.Range(0, 2)
            .Select(i => new Image<Rgb, byte>(@"Resources\train-full.jpg"))
            .Select(train => train.Convert<Gray, byte>())
            .ToArray();

        private static readonly Image<Gray, byte>[] EmptyImages = Enumerable.Range(0, 1)
            .Select(i => new Image<Rgb, byte>(@"Resources\train-0.jpg"))
            .Select(image => image.Convert<Gray, byte>())
            .ToArray();
        
        private static readonly Image<Gray, byte>[] SlowlyMovingTrain = Enumerable.Range(0, 1)
            .Select(i => new Image<Rgb, byte>(@"Resources\train-4.jpg"))
            .Select(image => image.Convert<Gray, byte>())
            .ToArray();

        [TestMethod]
        public void DetectEntryTest()
        {
            var detector = new EntryDetector(Background);

            var images = Enumerable.Range(0, 2)
                .Select(i => new Image<Rgb, byte>(@"Resources\train-full.jpg"))
                .Select(train => train.Convert<Gray, byte>())
                .ToArray();

            detector.Tick(images);

            Assert.AreEqual(DetectorState.Entry, detector.CurrentState);
        }

        [TestMethod]
        public void DetectExitTest()
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

            var emptyImages = Enumerable.Range(1, 1)
                .Select(i => new Image<Rgb, byte>(@"Resources\train-0.jpg"))
                .Select(image => image.Convert<Gray, byte>())
                .ToArray();

            detector.Tick(images);

            // Tick a few times with empty images should trigger exit
            for (var i = 0; i <= EntryDetector.ExitThreshold; i++)
            {
                detector.Tick(emptyImages);
            }

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

            timeProvider.CurrentTime = Year2000;
            detector.Tick(EntryImages);
            Assert.AreEqual(DetectorState.Entry, detector.CurrentState);
            Assert.IsTrue(enterRaised);
            Assert.IsFalse(abortRaised);

            timeProvider.CurrentTime = timeProvider.CurrentTime.AddSeconds(EntryDetector.MinTimeAfterEntry - 1);

            for (var i = 0; i <= EntryDetector.ExitThreshold; i++)
            {
                detector.Tick(EmptyImages);
            }

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

            timeProvider.CurrentTime = Year2000;
            detector.Tick(EntryImages);
            Assert.AreEqual(DetectorState.Entry, detector.CurrentState);
            Assert.IsTrue(enterRaised);
            Assert.IsFalse(exitRaised);

            timeProvider.CurrentTime = timeProvider.CurrentTime.AddSeconds(EntryDetector.MinTimeAfterEntry);

            for (var i = 0; i <= EntryDetector.ExitThreshold; i++)
            {
                detector.Tick(EmptyImages);
            }

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

            timeProvider.CurrentTime = Year2000;
            detector.Tick(EntryImages);
            Assert.AreEqual(DetectorState.Entry, detector.CurrentState);
            Assert.IsTrue(enterRaised);
            Assert.IsFalse(exitRaised);

            timeProvider.CurrentTime = timeProvider.CurrentTime.AddSeconds(EntryDetector.MaxRecordingDuration + 1);
            detector.Tick(SlowlyMovingTrain);
            Assert.AreEqual(DetectorState.Exit, detector.CurrentState);
            Assert.IsTrue(exitRaised);
        }

        /// <summary>
        /// Test that no new recording is started if shortly after a stop, a new train is picked up.
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

            timeProvider.CurrentTime = Year2000;
            detector.Tick(EntryImages);
            Assert.AreEqual(DetectorState.Entry, detector.CurrentState);
            Assert.IsTrue(enterRaised);
            Assert.IsFalse(exitRaised);

            timeProvider.CurrentTime = timeProvider.CurrentTime.AddSeconds(EntryDetector.MinTimeAfterEntry + 1);
            for (var i = 0; i <= EntryDetector.ExitThreshold; i++)
            {
                detector.Tick(EmptyImages);
            }
            Assert.AreEqual(DetectorState.Exit, detector.CurrentState);
            Assert.IsTrue(exitRaised);
            enterRaised = false;

            timeProvider.CurrentTime = timeProvider.CurrentTime.AddSeconds(EntryDetector.MinTimeAfterExit - 1);
            detector.Tick(EntryImages);
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

            for (var k = 0; k < 2; k++)
            {
                var enterRaised = false;
                var exitRaised = false;

                detector.Enter += (sender, args) => enterRaised = true;
                detector.Exit += (sender, args) => exitRaised = true;
                detector.Abort += (sender, args) => Assert.Fail("Abort not expected");

                // entry
                timeProvider.CurrentTime = timeProvider.CurrentTime.AddSeconds(5);
                detector.Tick(EntryImages);
                Assert.AreEqual(DetectorState.Entry, detector.CurrentState);
                Assert.IsTrue(enterRaised);

                // exit
                timeProvider.CurrentTime = timeProvider.CurrentTime.AddSeconds(5);
                for (var i = 0; i <= EntryDetector.ExitThreshold; i++)
                {
                    detector.Tick(EmptyImages);
                }
                Assert.AreEqual(DetectorState.Exit, detector.CurrentState);
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
                .Select(i => new Image<Rgb, byte>($@"Resources\train-{(i == 0 ? "0" : "full")}.jpg"))
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
                .Select(i => new Image<Rgb, byte>(@"Resources\train-0.jpg"))
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
    }
}
