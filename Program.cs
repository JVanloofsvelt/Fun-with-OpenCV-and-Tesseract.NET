using System;
using System.IO;
using System.Drawing;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;

namespace Emgu
{
    using CV;
    using CV.OCR;
    using CV.Structure;
    using CV.CvEnum;
    using Accord.MachineLearning;
    using Accord.Statistics.Distributions.DensityKernels;
    using Levenshtein = Fastenshtein.Levenshtein;
    using static Direction;

    static class Program
    {
        static readonly string tessdata = Path.Combine(Directory.GetCurrentDirectory(), "tessdata");
        static readonly Tesseract TessNumber;
        static readonly Tesseract TessAbbreviation;
        static readonly Tesseract TessDescription;

        static readonly Inter ResizeInterpolationType = Inter.Linear;
        static readonly int cellborderMargin = 5;
        static readonly int minLineWidth = 30;

        struct Product
        {
            public Product(string code, string description/*, int upperBound, int lowerBound*/)
            {
                Description = description;
                Code = code;
                //UpperBound = upperBound;
                //LowerBound = lowerBound;
            }

            public string Description;
            public string Code;
            //public int UpperBound;
            //public int LowerBound;
        }

        #region Products
        static readonly Product[] OrderedProducts = 
        {
            new Product("BK", "diksmuidse boterkoek"),
            new Product("CK", "diksmuidse cremekoek"),
            new Product("CH", "diksmuidse chocokoek"),
            new Product("CHCR", "chococreme"),
            new Product("CHCH", "chocochoco"),
            new Product("CHNA", "choconatuur"),
            new Product("BKR", "boterkoek rozijn"),
            new Product("AMR", "apres midi rond"),
            new Product("AMCH", "apres-midi choco"),
            new Product("CU", "curryrol"),
            new Product("AF", "appelflap"),
            new Product("ABF", "abrikozenflap"),
            new Product("KF", "kersenflap"),
            new Product("T", "torsade"),
            new Product("BOCH", "boekjes choco"),
            new Product("CR", "croissant"),
            new Product("FPK", "frangipannekoek"),
            new Product("A", "achtjes"),
            new Product("B", "berlijnse bol"),
            new Product("RH", "roomhoorn"),
            new Product("EW", "eclair wit"),
            new Product("E", "eclair bruin"),
            new Product("EM", "eclair mokka"),
            new Product("EB", "eclair banaan"),
            new Product("MA", "mattetaart"),
            new Product("R", "rijsttaart"),
            new Product("KT", "konfituurtaartje"),
            new Product("KK", "klaaskoeken"),
            new Product("FP", "frangipanne"),
            new Product("DONA", "donuts natuur"),
            new Product("DOCH", "donuts choco"),
            new Product("DOPI", "Donut Pinky"),
            new Product("DOPA", "Donut Party"),
            new Product("DOHA", "DONUT HAZELNOOT"),
            new Product("DOCHCH", "DONUT CHOCO CHOCO"),
            new Product("RS", "roomsoesjes 2 kg"),
            new Product("RSCH", "roomsoesjes choco"),
            new Product("PAG", "papier groot P8")
        };
        #endregion

        static Program()
        {
            try
            {
                var abbreviationCharacters = string.Join("", OrderedProducts.SelectMany(p => p.Code.ToCharArray()).Distinct());
                var descriptionCharacters = string.Join("", OrderedProducts.SelectMany(p => p.Description.ToCharArray()).Distinct());

                TessNumber = new Tesseract(tessdata, null, OcrEngineMode.TesseractOnly);
                TessNumber.SetVariable("tessedit_char_whitelist", "0123456789.");

                TessAbbreviation = new Tesseract(tessdata, null, OcrEngineMode.TesseractOnly);
                TessAbbreviation.SetVariable("tessedit_char_whitelist", abbreviationCharacters);

                TessDescription = new Tesseract(tessdata, null, OcrEngineMode.TesseractOnly);
                TessDescription.SetVariable("tessedit_char_whitelist", descriptionCharacters);
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception.Message);
                throw;
            }
        }

        static void Main(string[] args)
        {
            try
            {
                Run();
            }
            catch (Exception)
            {
                throw;
            }
        }

        static void Run()
        {
            var image = new Image<Bgr, byte>("table.jpg");
            var gray = image.Convert<Gray, byte>();


            // Find lines
            var binary = gray.ConvertToBinary();
            binary.Save("binary.png");

            var lineYCoordinates = binary.DetectLineCoordinates(Horizontal, linewidth: 6);
            Console.WriteLine($"{lineYCoordinates.Length} horizontal line(s) found");

            var lineXCoordinates = binary.DetectLineCoordinates(Vertical, linewidth: 4);
            Console.WriteLine($"{lineXCoordinates.Length} vertical line(s) found");


            // Draw lines
            foreach (var y in lineYCoordinates)
                image.Draw(new LineSegment2D(new Point(0, y), new Point(image.Size.Width, y)), new Bgr(Color.Red), 1);

            foreach (var x in lineXCoordinates)
                image.Draw(new LineSegment2D(new Point(x, 0), new Point(x, image.Size.Height)), new Bgr(Color.Red), 1);

            image.Save("detected lines.png");

            Console.WriteLine("Press enter to continue");
            Console.ReadLine();


            // Read cells
            for (int i = -1; i < lineYCoordinates.Length - 1; i++)
            {
                int top = 0, bottom = image.Size.Height, margin_top = 0, margin_bottom = 0;

                if (i != -1)
                { 
                    top = lineYCoordinates[i];
                    margin_top = cellborderMargin;
                }

                if (i + 1 != lineYCoordinates.Length)
                {
                    bottom = lineYCoordinates[i + 1];
                    margin_bottom = cellborderMargin;
                }

                var cellHeight = bottom - top;

                for (int j = -1; j < lineXCoordinates.Length - 1; j++)
                {
                    int left = 0, right = image.Size.Width, margin_left = 0, margin_right = 0;

                    if (j != -1)
                    {
                        left = lineXCoordinates[j];
                        margin_left = cellborderMargin;
                    }

                    if (j + 1 != lineXCoordinates.Length)
                    {
                        right = lineXCoordinates[j + 1];
                        margin_right = cellborderMargin;
                    }

                    var cellWidth = right - left;

                    var cellRegion = new Rectangle(
                        new Point(left + margin_left, top + margin_top),
                        new Size(cellWidth - margin_left - margin_bottom, cellHeight - margin_top - margin_bottom)
                    );

                    var cell = binary.GetSubRect(cellRegion);

                    

                    Tesseract tessEngine = null;

                    if (j == 2)
                        tessEngine = TessAbbreviation;

                    if (j == 1)
                        tessEngine = TessNumber;

                    if (j == 3)
                        tessEngine = TessDescription;

                    if (tessEngine == null)
                        continue;

                    tessEngine.Recognize(cell);
                    var text = tessEngine.GetText().Trim();

                    if (text.Length > 0 || j == 3)
                    {
                        string bestMatch;

                        if (j == 2)
                        {
                            int distance = OrderedProducts.Select(p => p.Code).Skip(i).GetBestMatch(text, out bestMatch);
                            Console.Write(bestMatch + $"\t\t{text}");
                        }
                        else if (j == 3)
                        {
                            if (string.IsNullOrWhiteSpace(text))
                                Console.Write("Couldn't read this");
                            else
                            {
                                int distance = OrderedProducts.Select(p => p.Description).Skip(i).GetBestMatch(text, out bestMatch);
                                Console.Write(bestMatch + $"\t\t{text}");
                            }
                        }
                        else
                            Console.Write(text);

                        Console.Write("\t");
                        cell.Save($"cells/cell {i + 1} {j + 1}.png");
                    }
                }

                Console.WriteLine();
            }

            Console.WriteLine("Press enter to exit");
            Console.ReadLine();
        }

        static Image<Gray, byte> ConvertToBinary<TDepth>(this Image<Gray, TDepth> image)
            where TDepth : new()
        {
            var output = new Image<Gray, byte>(image.Size);
            CvInvoke.Threshold(image, output, 0, 255, ThresholdType.Binary | ThresholdType.Otsu);
            return output;
        }

        /// <summary>
        /// Finds coordinates of all lines in a binary image. Warning: Might modify image
        /// </summary>
        /// <param name="image">Binary image</param>
        /// <param name="scale">Smaller scale improves performance but decreases accuracy</param>
        /// <returns>Coordinates, ordered</returns>
        static int[] DetectLineCoordinates<TDepth>(this Image<Gray, TDepth> image, Direction direction, int linewidth, double scale = 1.0) 
            where TDepth : new()
        {
            if (scale != 1.0)
                image = image.Resize(scale, ResizeInterpolationType);
            
            #if DEBUG
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            #endif

            var result = image.Not().HoughLinesBinary(
                1,                  // rho resolution
                90 * Math.PI / 180, // theta resolution
                Math.Max(Convert.ToInt32(minLineWidth * scale), 2),    // threshold
                Math.Max(Convert.ToInt32(minLineWidth * scale), 1),  // Minimum line width
                Convert.ToInt32(0 * scale)                           // Maximum gap between lines
            );

            #if DEBUG
            stopwatch.Stop();
            Console.WriteLine($"Executed HoughLines() in {stopwatch.ElapsedMilliseconds}ms");
            #endif

            var lines = result[0].Where(l => l.DirectionEquals(direction)).ToArray();

            #if DEBUG
            Console.WriteLine($"{lines.Length} {direction.ToString().ToLower()} hough lines found");

            var houghlines = image.Convert<Bgr, byte>();

            foreach (var line in lines)
                houghlines.Draw(line, new Bgr(Color.Red), 1);
            
            houghlines.Save($"houghlines {direction.ToString().ToLower()}.png");
            #endif

            if (lines.Length == 0)
                return new int[] { };

            Func<Point, int> GetCoordinate = null;

            if (direction == Vertical)
                GetCoordinate = point => point.X;

            if (direction == Horizontal)
                GetCoordinate = point => point.Y;

            Debug.Assert(GetCoordinate != null);

            var lineCoordinates = Cluster(
                // Weigh each coordinate by multiplying its occurence frequency by the line's length/minLineWidth-ratio
                lines.SelectMany(l => 
                    Enumerable.Range(0, Convert.ToInt32(l.Length / minLineWidth)).Select(_ => GetCoordinate(l.P1))
                ),  
                bandwidth: Convert.ToInt32(linewidth * scale)
            );

            return lineCoordinates.OrderBy(l => l).Select(y => Convert.ToInt32(y / scale)).ToArray();
        }

        static int[] Cluster(IEnumerable<int> integers, int bandwidth)
        {
            #if DEBUG
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            #endif

            var kernel = new GaussianKernel(1);
            var meanshift = new MeanShift(1, kernel, bandwidth);
            meanshift.UseParallelProcessing = false;

            var points = integers.Select(i => new[] { Convert.ToDouble(i) }).ToArray();

            try
            {
                var labels = meanshift.Compute(points);
            }
            catch (Exception exception)
            {
                throw;
            }

            #if DEBUG
            stopwatch.Stop();
            Console.WriteLine($"Performed meanshift on {points.Length} points in {stopwatch.ElapsedMilliseconds}ms");
            #endif

            return meanshift.Clusters.Modes.Select(m => Convert.ToInt32(m[0])).ToArray();
        }

        static bool DirectionEquals(this LineSegment2D line, Direction direction)
        {
            if (direction == Horizontal)
                return Math.Abs(line.Direction.X) == 1 && Math.Abs(line.Direction.Y) == 0;

            if (direction == Vertical)
                return Math.Abs(line.Direction.Y) == 1 && Math.Abs(line.Direction.X) == 0;

            throw new ArgumentException($"Only {Direction.Vertical} and {Direction.Horizontal} are supported", nameof(direction));
        }

        static int GetBestMatch(this IEnumerable<string> candidates, string text, out string best)
        {
            int? smallestDistance = null;
            string bestMatch = null;

            foreach (var candidate in candidates)
            {
                var distance = Levenshtein.Distance(text, candidate);

                if (smallestDistance == null || distance < smallestDistance)
                {
                    smallestDistance = distance;
                    bestMatch = candidate;
                }
            }

            if (smallestDistance == null)
                throw new InvalidOperationException("Sequence is empty");

            best = bestMatch;
            return (int)smallestDistance;
        }
    }

    enum Direction
    {
        Horizontal,
        Vertical
    }
}
