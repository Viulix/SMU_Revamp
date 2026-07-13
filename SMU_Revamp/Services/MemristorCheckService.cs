using System;
using System.Collections.Generic;
using System.Linq;
using SMU_Revamp.Models;
using SMU_Revamp.ViewModels;

namespace SMU_Revamp.Services
{
    public sealed class MemristorCheckResult
    {
        public double Score01 { get; set; }          // 0..1
        public double Score3 { get; set; }           // 0..3, kompatibel zur alten Idee

        public double Snr { get; set; }
        public double NoiseStd { get; set; }
        public double CurrentScale { get; set; }

        public double Nonlinearity { get; set; }
        public double Hysteresis { get; set; }
        public double BranchSeparationSnr { get; set; }

        public double PinchError { get; set; }       // kleiner ist besser
        public double OriginOffset { get; set; }     // kleiner ist besser
        public double Roughness { get; set; }        // kleiner ist meist besser

        public int UpSweepCount { get; set; }
        public int DownSweepCount { get; set; }

        public string Classification { get; set; } = "";
        public List<string> Flags { get; set; } = new();

        // Helper properties for easy UI bindings without converters
        public string FormattedScore01 => double.IsNaN(Score01) ? "-" : Score01.ToString("0.00");
        public string FormattedScore3 => double.IsNaN(Score3) ? "-" : Score3.ToString("0.00");
        public string FormattedSnr => double.IsNaN(Snr) ? "-" : Snr.ToString("0.0");
        public string FormattedNoiseStd => double.IsNaN(NoiseStd) ? "-" : ResultContactViewModel.FormatValue(NoiseStd) + " A";
        public string FormattedCurrentScale => double.IsNaN(CurrentScale) ? "-" : ResultContactViewModel.FormatValue(CurrentScale) + " A";
        public string FormattedNonlinearity => double.IsNaN(Nonlinearity) ? "-" : Nonlinearity.ToString("0.00");
        public string FormattedHysteresis => double.IsNaN(Hysteresis) ? "-" : Hysteresis.ToString("0.00");
        public string FormattedBranchSeparationSnr => double.IsNaN(BranchSeparationSnr) ? "-" : BranchSeparationSnr.ToString("0.0");
        public string FormattedPinchError => double.IsNaN(PinchError) ? "-" : PinchError.ToString("0.00");
        public string FormattedOriginOffset => double.IsNaN(OriginOffset) ? "-" : OriginOffset.ToString("0.00");
        public string FormattedRoughness => double.IsNaN(Roughness) ? "-" : Roughness.ToString("0.00");
        public string FormattedSweepCounts => $"{UpSweepCount} / {DownSweepCount}";

        public List<string> UserFlags
        {
            get
            {
                var list = new List<string>();
                foreach (var flag in Flags)
                {
                    list.Add(MapFlagToUserFriendly(flag));
                }
                return list;
            }
        }

        private static string MapFlagToUserFriendly(string flag)
        {
            return flag switch
            {
                "InsufficientData" => "Too few data points (< 12)",
                "InsufficientValidData" => "Too few valid data points (< 12)",
                "NoVoltageRange" => "No significant voltage variation",
                "VoltageDoesNotCrossZero" => "Voltage does not cross 0 V",
                "NoCurrentSignal" => "No measurable current signal",
                "WeakSignal" => "Weak signal (SNR < 3)",
                "MostlyLinear" => "Curve is mostly linear",
                "WeakHysteresis" => "Weak hysteresis (< 5%)",
                "HysteresisNotAboveNoise" => "Hysterese below noise floor",
                "WeakPinch" => "Weak pinch at origin",
                "HighOriginOffset" => "High origin offset",
                "NoCommonZeroVoltage" => "No common 0 V point",
                "NoCommonSweepRange" => "No common sweep range",
                "InterpolationFailed" => "Interpolation failed",
                "CouldNotSeparateSweeps" => "Sweeps could not be separated",
                "NoisyOrJaggedCurve" => "High noise / jagged curve",
                _ => flag
            };
        }
    }

    public static class MemristorCheckService
    {
        private struct XY
        {
            public double Voltage;
            public double Current;

            public XY(double voltage, double current)
            {
                Voltage = voltage;
                Current = current;
            }
        }

        public static MemristorCheckResult Calculate(List<CurvePoint> points)
        {
            var result = new MemristorCheckResult();

            if (points == null || points.Count < 12)
            {
                result.Score01 = double.NaN;
                result.Score3 = double.NaN;
                result.Classification = "Too few data points";
                result.Flags.Add("InsufficientData");
                return result;
            }

            var clean = points
                .Where(p => double.IsFinite(p.Voltage) && double.IsFinite(p.Current))
                .Select(p => new XY(p.Voltage, p.Current))
                .ToList();

            if (clean.Count < 12)
            {
                result.Score01 = double.NaN;
                result.Score3 = double.NaN;
                result.Classification = "Too few valid data points";
                result.Flags.Add("InsufficientValidData");
                return result;
            }

            var V = clean.Select(p => p.Voltage).ToArray();
            var I = clean.Select(p => p.Current).ToArray();
            var absV = V.Select(Math.Abs).ToArray();
            var absI = I.Select(Math.Abs).ToArray();

            double vMin = V.Min();
            double vMax = V.Max();
            double vSpan = vMax - vMin;
            double maxAbsV = absV.Max();

            if (vSpan <= 0 || maxAbsV <= 0)
            {
                result.Score01 = 0;
                result.Score3 = 0;
                result.Classification = "No significant voltage variation";
                result.Flags.Add("NoVoltageRange");
                return result;
            }

            bool crossesZero = vMin <= 0 && vMax >= 0;
            if (!crossesZero)
            {
                result.Flags.Add("VoltageDoesNotCrossZero");
            }

            // Robuste Stromskala: viel besser als Max(), weil einzelne Spikes weniger dominieren.
            double currentScale = Percentile(absI, 95);
            double maxAbsI = absI.Max();

            // Falls fast alles 0 ist, fallback auf Max.
            if (currentScale <= 0)
                currentScale = maxAbsI;

            if (currentScale <= 0)
            {
                result.Score01 = 0;
                result.Score3 = 0;
                result.Classification = "No measurable current signal";
                result.Flags.Add("NoCurrentSignal");
                return result;
            }

            result.CurrentScale = currentScale;

            // 1. Robuste Noise-Schätzung nahe 0 V.
            // Statt fixer 0.05 V: relativ zum Sweepbereich.
            double vNoise = 0.05 * maxAbsV;

            var noiseRegion = clean
                .Where(p => Math.Abs(p.Voltage) <= vNoise)
                .Select(p => p.Current)
                .ToList();

            // Falls nahe 0 V zu wenige Punkte liegen, nimm die niedrigsten |V|-Punkte.
            int minNoiseCount = Math.Max(6, clean.Count / 20);

            if (noiseRegion.Count < minNoiseCount)
            {
                noiseRegion = clean
                    .OrderBy(p => Math.Abs(p.Voltage))
                    .Take(Math.Max(minNoiseCount, clean.Count / 10))
                    .Select(p => p.Current)
                    .ToList();
            }

            double noiseStd = RobustStdFromMad(noiseRegion);

            // Absoluter numerischer Floor, damit Divisionen stabil bleiben.
            double noiseFloor = Math.Max(noiseStd, currentScale * 1e-12);
            result.NoiseStd = noiseFloor;

            double snr = currentScale / noiseFloor;
            result.Snr = snr;

            if (snr < 3)
                result.Flags.Add("WeakSignal");

            // 2. Robustere Nichtlinearität.
            // Low-Strom wird mindestens auf den Noise-Floor gesetzt, damit reines Rauschen
            // keine riesige Nonlinearity erzeugt.
            var iHigh = clean
                .Where(p => Math.Abs(p.Voltage) >= 0.75 * maxAbsV)
                .Select(p => Math.Abs(p.Current))
                .ToList();

            var iLow = clean
                .Where(p => Math.Abs(p.Voltage) <= 0.25 * maxAbsV)
                .Select(p => Math.Abs(p.Current))
                .ToList();

            double medianHigh = iHigh.Count > 0 ? Median(iHigh) : 0;
            double medianLow = iLow.Count > 0 ? Median(iLow) : 0;

            double effectiveLow = Math.Max(medianLow, 2.0 * noiseFloor);
            double nonlinearity = medianHigh / effectiveLow;

            result.Nonlinearity = nonlinearity;

            if (nonlinearity < 2)
                result.Flags.Add("MostlyLinear");

            // 3. Sweeps robuster trennen.
            double voltageTolerance = Math.Max(vSpan * 1e-6, 1e-15);

            SplitSweeps(clean, voltageTolerance, out var upSweepRaw, out var downSweepRaw);

            result.UpSweepCount = upSweepRaw.Count;
            result.DownSweepCount = downSweepRaw.Count;

            double hysteresis = double.NaN;
            double branchSepSnr = double.NaN;
            double pinchError = double.NaN;
            double originOffset = double.NaN;

            if (upSweepRaw.Count >= 6 && downSweepRaw.Count >= 6)
            {
                var upSweep = PrepareForInterpolation(upSweepRaw, 800);
                var downSweep = PrepareForInterpolation(downSweepRaw, 800);

                if (upSweep.Count >= 4 && downSweep.Count >= 4)
                {
                    double commonMin = Math.Max(upSweep.First().Voltage, downSweep.First().Voltage);
                    double commonMax = Math.Min(upSweep.Last().Voltage, downSweep.Last().Voltage);

                    if (commonMax > commonMin)
                    {
                        int gridPoints = Math.Min(400, Math.Max(80, clean.Count));
                        double step = (commonMax - commonMin) / (gridPoints - 1);

                        var diffs = new List<double>(gridPoints);
                        double hystArea = 0;
                        double normArea = 0;

                        for (int i = 0; i < gridPoints; i++)
                        {
                            double v = commonMin + i * step;

                            double iUp = Interpolate(upSweep, v);
                            double iDown = Interpolate(downSweep, v);

                            double diff = Math.Abs(iUp - iDown);

                            diffs.Add(diff);

                            hystArea += diff;
                            normArea += Math.Abs(iUp) + Math.Abs(iDown);
                        }

                        hysteresis = normArea > 0 ? hystArea / normArea : 0;
                        branchSepSnr = Median(diffs) / noiseFloor;

                        // Pinch am Ursprung nur bewerten, wenn 0 V im gemeinsamen Sweepbereich liegt.
                        if (commonMin <= 0 && commonMax >= 0)
                        {
                            double iUp0 = Interpolate(upSweep, 0);
                            double iDown0 = Interpolate(downSweep, 0);

                            pinchError = Math.Abs(iUp0 - iDown0) / currentScale;
                            originOffset = ((Math.Abs(iUp0) + Math.Abs(iDown0)) / 2.0) / currentScale;
                        }
                        else
                        {
                            result.Flags.Add("NoCommonZeroVoltage");
                        }
                    }
                    else
                    {
                        result.Flags.Add("NoCommonSweepRange");
                    }
                }
                else
                {
                    result.Flags.Add("InterpolationFailed");
                }
            }
            else
            {
                result.Flags.Add("CouldNotSeparateSweeps");
            }

            result.Hysteresis = hysteresis;
            result.BranchSeparationSnr = branchSepSnr;
            result.PinchError = pinchError;
            result.OriginOffset = originOffset;

            if (!double.IsNaN(hysteresis) && hysteresis < 0.05)
                result.Flags.Add("WeakHysteresis");

            if (!double.IsNaN(branchSepSnr) && branchSepSnr < 3)
                result.Flags.Add("HysteresisNotAboveNoise");

            if (!double.IsNaN(pinchError) && pinchError > 0.2)
                result.Flags.Add("WeakPinch");

            if (!double.IsNaN(originOffset) && originOffset > 0.2)
                result.Flags.Add("HighOriginOffset");

            // 4. Roughness: niedrig gewichtet.
            double roughUp = ComputeRoughness(upSweepRaw, currentScale);
            double roughDown = ComputeRoughness(downSweepRaw, currentScale);

            double roughness;
            if (double.IsNaN(roughUp)) roughness = roughDown;
            else if (double.IsNaN(roughDown)) roughness = roughUp;
            else roughness = 0.5 * (roughUp + roughDown);

            result.Roughness = roughness;

            if (!double.IsNaN(roughness) && roughness > 1.5)
                result.Flags.Add("NoisyOrJaggedCurve");

            // 5. Einzelmetriken in Scores 0..1 umwandeln.
            double snrScore = ScoreRatio(snr, bad: 3, good: 20);
            double nonlinearityScore = ScoreRatio(nonlinearity, bad: 1.5, good: 10);

            double hysteresisScore = double.IsNaN(hysteresis)
                ? double.NaN
                : ScoreLinear(hysteresis, bad: 0.03, good: 0.25);

            double branchSepScore = double.IsNaN(branchSepSnr)
                ? double.NaN
                : ScoreRatio(branchSepSnr, bad: 2, good: 10);

            double pinchScore = double.NaN;

            if (!double.IsNaN(pinchError) && !double.IsNaN(originOffset))
            {
                double pinchPenalty = 0.7 * pinchError + 0.3 * originOffset;
                pinchScore = 1.0 - ScoreLinear(pinchPenalty, bad: 0.03, good: 0.25);
                pinchScore = Clamp01(pinchScore);
            }

            double smoothnessScore = double.IsNaN(roughness)
                ? double.NaN
                : 1.0 - ScoreLinear(roughness, bad: 0.3, good: 2.0);

            smoothnessScore = Clamp01(smoothnessScore);

            // Hysterese nur voll zählen, wenn sie über dem Rauschen liegt.
            if (!double.IsNaN(hysteresisScore) && !double.IsNaN(branchSepScore))
            {
                hysteresisScore *= Math.Max(0.2, branchSepScore);
            }

            // 6. Gewichteter Gesamtscore.
            var weightedScores = new List<(double score, double weight)>
            {
                (snrScore, 0.20),
                (nonlinearityScore, 0.15),
                (hysteresisScore, 0.25),
                (branchSepScore, 0.15),
                (pinchScore, 0.20),
                (smoothnessScore, 0.05)
            };

            double score01 = WeightedMeanIgnoringNaN(weightedScores);

            // Wenn die Spannung 0 V nicht kreuzt, kann Pinch nicht bewertet werden.
            // Dann nicht komplett bestrafen, aber leicht abwerten.
            if (!crossesZero)
                score01 *= 0.85;

            result.Score01 = Clamp01(score01);
            result.Score3 = 3.0 * result.Score01;

            result.Classification = ClassifyScore(result.Score01, result.Flags);

            return result;
        }

        private static double ComputeRoughness(List<XY> branch, double currentScale)
        {
            if (branch == null || branch.Count < 5 || currentScale <= 0)
                return double.NaN;

            var sorted = branch
                .Where(p => double.IsFinite(p.Voltage) && double.IsFinite(p.Current))
                .OrderBy(p => p.Voltage)
                .ToList();

            if (sorted.Count < 5)
                return double.NaN;

            double sumSecond = 0;
            double sumFirst = 0;

            for (int i = 1; i < sorted.Count - 1; i++)
            {
                double yPrev = sorted[i - 1].Current / currentScale;
                double y = sorted[i].Current / currentScale;
                double yNext = sorted[i + 1].Current / currentScale;

                double first1 = y - yPrev;
                double first2 = yNext - y;

                double second = yNext - 2.0 * y + yPrev;

                sumSecond += Math.Abs(second);
                sumFirst += Math.Abs(first1) + Math.Abs(first2);
            }

            if (sumFirst <= 0)
                return 0;

            return sumSecond / sumFirst;
        }

        private static double ScoreLinear(double value, double bad, double good)
        {
            if (double.IsNaN(value))
                return double.NaN;

            if (Math.Abs(good - bad) <= 0)
                return value >= good ? 1 : 0;

            return Clamp01((value - bad) / (good - bad));
        }

        private static double ScoreRatio(double value, double bad, double good)
        {
            if (double.IsNaN(value) || value <= 0)
                return double.NaN;

            bad = Math.Max(bad, 1e-30);
            good = Math.Max(good, bad * 1.000001);

            double logValue = Math.Log10(Math.Max(value, 1e-30));
            double logBad = Math.Log10(bad);
            double logGood = Math.Log10(good);

            return Clamp01((logValue - logBad) / (logGood - logBad));
        }

        private static double WeightedMeanIgnoringNaN(List<(double score, double weight)> values)
        {
            double weightedSum = 0;
            double weightSum = 0;

            foreach (var item in values)
            {
                if (double.IsNaN(item.score))
                    continue;

                if (item.weight <= 0)
                    continue;

                weightedSum += Clamp01(item.score) * item.weight;
                weightSum += item.weight;
            }

            if (weightSum <= 0)
                return 0;

            return weightedSum / weightSum;
        }

        private static string ClassifyScore(double score01, List<string> flags)
        {
            if (double.IsNaN(score01))
                return "Not evaluable";

            double score3 = score01 * 3.0;

            if (flags.Contains("WeakSignal") && score3 < 1.2)
                return "Poor / probably noise";

            if (score3 < 0.75)
                return "Poor / probably noise";

            if (score3 < 1.35)
                return "Signal, but weak";

            if (score3 < 1.95)
                return "Possible candidate";

            if (score3 <= 2.40)
                return "Good candidate";

            return "Very good candidate";
        }

        private static double Median(IEnumerable<double> source)
        {
            var sorted = source.OrderBy(x => x).ToList();
            if (sorted.Count == 0) return 0;
            int mid = sorted.Count / 2;
            if (sorted.Count % 2 == 0) return (sorted[mid - 1] + sorted[mid]) / 2.0;
            return sorted[mid];
        }

        private static double RobustStdFromMad(List<double> values)
        {
            if (values == null || values.Count == 0) return 0;
            double median = Median(values);
            var absoluteDeviations = values.Select(v => Math.Abs(v - median)).ToList();
            double mad = Median(absoluteDeviations);
            return 1.4826 * mad;
        }

        private static double Percentile(double[] sequence, double excelPercentile)
        {
            if (sequence == null || sequence.Length == 0) return 0;
            var sorted = sequence.OrderBy(x => x).ToArray();
            double percent = excelPercentile / 100.0;
            double realIndex = percent * (sorted.Length - 1);
            int index = (int)realIndex;
            double frac = realIndex - index;
            if (index + 1 < sorted.Length)
                return sorted[index] * (1.0 - frac) + sorted[index + 1] * frac;
            return sorted[index];
        }

        private static void SplitSweeps(List<XY> clean, double voltageTolerance, out List<XY> upSweep, out List<XY> downSweep)
        {
            upSweep = new List<XY>();
            downSweep = new List<XY>();
            if (clean.Count == 0) return;

            bool isUp = true;
            if (clean.Count > 1)
            {
                isUp = clean[1].Voltage >= clean[0].Voltage;
            }

            for (int i = 0; i < clean.Count; i++)
            {
                if (i > 0)
                {
                    double diff = clean[i].Voltage - clean[i - 1].Voltage;
                    if (Math.Abs(diff) > voltageTolerance)
                    {
                        isUp = diff > 0;
                    }
                }

                if (isUp)
                    upSweep.Add(clean[i]);
                else
                    downSweep.Add(clean[i]);
            }
        }

        private static List<XY> PrepareForInterpolation(List<XY> branch, int targetCount)
        {
            if (branch == null || branch.Count == 0)
                return new List<XY>();

            var sorted = branch.OrderBy(p => p.Voltage).ToList();
            var unique = new List<XY>();
            int i = 0;
            while (i < sorted.Count)
            {
                double v = sorted[i].Voltage;
                double sumI = sorted[i].Current;
                int count = 1;
                int j = i + 1;
                while (j < sorted.Count && Math.Abs(sorted[j].Voltage - v) < 1e-12)
                {
                    sumI += sorted[j].Current;
                    count++;
                    j++;
                }
                unique.Add(new XY(v, sumI / count));
                i = j;
            }
            return unique;
        }

        private static double Interpolate(List<XY> sortedPoints, double targetV)
        {
            if (sortedPoints == null || sortedPoints.Count == 0) return 0;
            if (targetV <= sortedPoints[0].Voltage) return sortedPoints[0].Current;
            if (targetV >= sortedPoints[^1].Voltage) return sortedPoints[^1].Current;

            for (int i = 0; i < sortedPoints.Count - 1; i++)
            {
                var p1 = sortedPoints[i];
                var p2 = sortedPoints[i + 1];

                if (targetV >= p1.Voltage && targetV <= p2.Voltage)
                {
                    if (Math.Abs(p2.Voltage - p1.Voltage) < 1e-12) return p1.Current;
                    double fraction = (targetV - p1.Voltage) / (p2.Voltage - p1.Voltage);
                    return p1.Current + fraction * (p2.Current - p1.Current);
                }
            }
            return 0;
        }

        private static double Clamp01(double val)
        {
            if (double.IsNaN(val)) return 0;
            if (val < 0) return 0;
            if (val > 1) return 1;
            return val;
        }
    }
}
