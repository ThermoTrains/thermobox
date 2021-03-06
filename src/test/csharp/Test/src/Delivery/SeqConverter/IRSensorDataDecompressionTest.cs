using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SebastianHaeni.ThermoBox.SeqConverter;

namespace Test.Delivery.SeqConverter
{
    [TestClass]
    public class IRSensorDataDecompressionTest
    {
        [TestMethod]
        public void TestDecompress()
        {
            const string compressedVideo = @"Resources\book.mp4";
            const string decompressedVideo = @"book-decompressed.seq";

            IRSensorDataDecompression.Decompress(compressedVideo, decompressedVideo);
            Assert.IsTrue(File.Exists(decompressedVideo));
        }
    }
}
