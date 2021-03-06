using System;
using System.Collections.Generic;
using System.IO;
using SebastianHaeni.ThermoBox.IRCompressor;
using SebastianHaeni.ThermoBox.SeqConverter.Color;

namespace SebastianHaeni.ThermoBox.SeqConverter
{
    internal static class Program
    {
        public static void Main(string[] args)
        {
            var (input, output, mode, palette) = ParseArguments(args);

            if (IsSeqFile(input) && IsMp4File(output))
            {
                IRSensorDataCompression.Compress(input, output, mode);
            }
            else if (IsMp4File(input) && IsSeqFile(output))
            {
                IRSensorDataDecompression.Decompress(input, output);
            }
            else if (IsMp4File(input) && IsMp4File(output))
            {
                ColorConverter.ConvertToColor(input, output, palette);
            }
            else
            {
                Console.WriteLine("Invalid combination of seq and mp4");
                Environment.Exit(1);
            }
        }

        private static bool IsSeqFile(string input)
        {
            return input != null && Path.GetExtension(input).ToLowerInvariant().Equals(".seq");
        }

        private static bool IsMp4File(string input)
        {
            return input != null && Path.GetExtension(input).ToLowerInvariant().Equals(".mp4");
        }

        private static (
            string input,
            string output,
            IRSensorDataCompression.Mode mode,
            ColorMap palette
            ) ParseArguments(IReadOnlyList<string> args)
        {
            if (args.Count != 2 && args.Count != 3)
            {
                Console.WriteLine(@"Usage:
seqconverter <input> <output> [mode]

Converting .seq to mp4:
seqconverter myseq.seq myseq.mp4

Converting a previously converted mp4 back to .seq:
seqconverter myseq.mp4 myseq.seq

Modes for .seq to .mp4:
- Other: uses the whole image
- Train: tries to find horizontal movement and limits compression on this area

Modes for .mp4 to .mp4 (Color Palette):
Iron, Hot");
                Environment.Exit(1);
            }

            var input = args[0];
            var output = args[1];
            var mode = IRSensorDataCompression.Mode.Other;
            var palette = ColorMap.Iron;

            if (args.Count != 3)
            {
                return (input, output, mode, palette);
            }

            if (!Enum.TryParse(args[2], out mode))
            {
                Enum.TryParse(args[2], out palette);
            }

            return (input, output, mode, palette);
        }
    }
}
