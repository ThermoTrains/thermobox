package ch.sebastianhaeni.thermotrains.internals;


import java.util.function.Function;
import java.util.function.Predicate;

import org.opencv.core.Mat;
import org.opencv.videoio.VideoCapture;
import org.opencv.videoio.Videoio;

import static ch.sebastianhaeni.thermotrains.util.FileUtil.emptyFolder;
import static ch.sebastianhaeni.thermotrains.util.FileUtil.saveMat;
import static org.opencv.core.Core.flip;

public final class ExtractFrames {
  private ExtractFrames() {
  }

  public enum Direction {
    REVERSE,
    FORWARD
  }

  public static void extractFrames(int framesToExtract, Direction direction, String inputVideoFilename, String outputFolder) {
    emptyFolder(outputFolder);
    VideoCapture capture = new VideoCapture();

    if (!capture.open(inputVideoFilename)) {
      throw new IllegalStateException("Cannot open the video file");
    }

    int frameCount = (int) capture.get(Videoio.CAP_PROP_FRAME_COUNT);
    int frameCounter = 0;

    Predicate<Integer> termination = direction == Direction.FORWARD ?
      i -> i < frameCount :
      i -> i > 0;
    Function<Integer, Integer> increment = direction == Direction.FORWARD ?
      i -> i + 1 :
      i -> i - 1;

    int i = direction == Direction.FORWARD ? 0 : frameCount;

    while (termination.test(i)) {
      i = increment.apply(i);

      Mat frame = new Mat();
      boolean success = capture.read(frame);
      if (!success) {
        System.out.println("Cannot read frame " + i);
        continue;
      }

      if (i == 0 || i % (frameCount / framesToExtract) != 0) {
        // do not extract every frame, but once in a while so we have a fixed number of frames
        // not correlated to the frame count
        continue;
      }

      if (direction == Direction.REVERSE) {
        flip(frame, frame, 1);
      }

      saveMat(outputFolder, frame, ++frameCounter);
    }
  }
}