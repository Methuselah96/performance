using DataTransferContracts;
using MarkdownLog;
using Perfolizer.Mathematics.SignificanceTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ResultsComparer
{
    internal static class TwoInputsComparer
    {
        internal static void Compare(TwoInputsOptions args)
        {
            var results = ReadResults(args)
                .Where(result => result.baseResults.All(benchmark => benchmark.Statistics != null) && result.diffResults.All(benchmark => benchmark.Statistics != null)).ToList();

            foreach (var result in results)
            {
                for (var i = 0; i < result.baseResults.Count; i++)
                {
                    Console.WriteLine($"{result.id} base {i + 1}: {FormatConfidenceInterval(result.baseResults[i].Statistics.ConfidenceInterval)}");
                }
                for (var i = 0; i < result.diffResults.Count; i++)
                {
                    Console.WriteLine($"{result.id} diff {i + 1}: {FormatConfidenceInterval(result.diffResults[i].Statistics.ConfidenceInterval)}");
                }

                var conclusions = new List<EquivalenceTestConclusion>();
                for (var i = 0; i < result.baseResults.Count; i++)
                {
                    for (var j = 0; j < result.diffResults.Count; j++)
                    {
                        var baseValues = result.baseResults[i].Statistics.OriginalValues.ToArray();
                        var diffValues = result.diffResults[j].Statistics.OriginalValues.ToArray();
                        var userTresholdResult = StatisticalTestHelper.CalculateTost(MannWhitneyTest.Instance, baseValues, diffValues, args.StatisticalTestThreshold);
                        Console.WriteLine($"Base {i + 1}, Diff {j + 1}: {userTresholdResult.Conclusion}");
                        conclusions.Add(userTresholdResult.Conclusion);
                    }
                }

                var baseConclusions = new List<EquivalenceTestConclusion>();
                for (var i = 0; i < result.baseResults.Count; i++)
                {
                    for (var j = i + 1; j < result.baseResults.Count; j++)
                    {
                        var baseValues1 = result.baseResults[i].Statistics.OriginalValues.ToArray();
                        var baseValues2 = result.baseResults[j].Statistics.OriginalValues.ToArray();
                        var userTresholdResult = StatisticalTestHelper.CalculateTost(MannWhitneyTest.Instance, baseValues1, baseValues2, args.StatisticalTestThreshold);
                        Console.WriteLine($"Base {i + 1}, Base {j + 1}: {userTresholdResult.Conclusion}");
                        baseConclusions.Add(userTresholdResult.Conclusion);
                    }
                }

                var diffConclusions = new List<EquivalenceTestConclusion>();
                for (var i = 0; i < result.diffResults.Count; i++)
                {
                    for (var j = i + 1; j < result.diffResults.Count; j++)
                    {
                        var diffValues1 = result.diffResults[i].Statistics.OriginalValues.ToArray();
                        var diffValues2 = result.diffResults[j].Statistics.OriginalValues.ToArray();
                        var userTresholdResult = StatisticalTestHelper.CalculateTost(MannWhitneyTest.Instance, diffValues1, diffValues2, args.StatisticalTestThreshold);
                        Console.WriteLine($"Diff {i + 1}, Diff {j + 1}: {userTresholdResult.Conclusion}");
                        diffConclusions.Add(userTresholdResult.Conclusion);
                    }
                }

                if (baseConclusions.Any(conclusion => conclusion != EquivalenceTestConclusion.Same))
                {
                    Console.WriteLine($"{result.id} conclusion: INCONCLUSIVE (Base were not all same)");
                }
                else if (diffConclusions.Any(conclusion => conclusion != EquivalenceTestConclusion.Same))
                {
                    Console.WriteLine($"{result.id} conclusion: INCONCLUSIVE (Diff were not all same)");
                }
                else if (conclusions.All(conclusion => conclusion == EquivalenceTestConclusion.Faster))
                {
                    Console.WriteLine($"{result.id} conclusion: FASTER");
                }
                else if (conclusions.All(conclusion => conclusion == EquivalenceTestConclusion.Slower))
                {
                    Console.WriteLine($"{result.id} conclusion: SLOWER");
                }
                else if (conclusions.All(conclusion => conclusion == EquivalenceTestConclusion.Same))
                {
                    Console.WriteLine($"{result.id} conclusion: SAME");
                }
                else
                {
                    Console.WriteLine($"{result.id} conclusion: INCONCLUSIVE (Not consistent conclusions)");
                }

                // var baseConfidenceInterval = result.baseResult.Statistics.ConfidenceInterval;
                // var diffConfidenceInterval = result.diffResult.Statistics.ConfidenceInterval;
                // Console.WriteLine($"{result.id} base: {FormatConfidenceInterval(baseConfidenceInterval)}");
                // Console.WriteLine($"{result.id} base: {FormatConfidenceInterval(diffConfidenceInterval)}");
                // if (baseConfidenceInterval.Mean >= diffConfidenceInterval.Lower &&
                //     baseConfidenceInterval.Mean <= diffConfidenceInterval.Upper)
                // {
                //     Console.WriteLine("Base is within diff confidence interval.");
                // }
                // else
                // {
                //     Console.WriteLine("Base is NOT within diff confidence interval.");
                // }
                //
                // if (diffConfidenceInterval.Mean >= baseConfidenceInterval.Lower &&
                //     diffConfidenceInterval.Mean <= baseConfidenceInterval.Upper)
                // {
                //     Console.WriteLine("Diff is within base confidence interval.");
                // }
                // else
                // {
                //     Console.WriteLine("Diff is NOT within base confidence interval.");
                // }

                Console.WriteLine();
            }

            if (results.Count == 0)
                return;

            var firstResult = results[0];

            for (var i = 0; i < firstResult.baseResults.Count; i++)
            {
                for (var j = 0; j < firstResult.diffResults.Count; j++)
                {
                    Console.WriteLine($"Base {i + 1}, Diff {j + 1} Results");

                    var resultsForTheRest = results.Select(result =>
                        (result.id, baseResult: result.baseResults[i], diffRefsult: result.diffResults[j]));

                    var notSame = GetNotSameResults(resultsForTheRest, args).ToArray();

                    if (!notSame.Any())
                    {
                        Console.WriteLine(
                            $"No differences found between the benchmark results with threshold {args.StatisticalTestThreshold}.");
                        return;
                    }

                    PrintSummary(notSame);

                    PrintTable(notSame, EquivalenceTestConclusion.Slower, args);
                    PrintTable(notSame, EquivalenceTestConclusion.Faster, args);
                }
            }
        }

        private static string FormatConfidenceInterval(ConfidenceInterval confidenceInterval) => $"Mean = {FormatNano(confidenceInterval.Mean)}, ConfidenceInterval = [{FormatNano(confidenceInterval.Lower)}; {FormatNano(confidenceInterval.Upper)}] (CI 99.9%), Margin = {FormatNano(confidenceInterval.Margin)} ({confidenceInterval.Margin / confidenceInterval.Mean * 100:F2}% of Mean)";

        private static string FormatNano(double nano) => $"{nano / 1000000:F3}";

        private static IEnumerable<(string id, Benchmark baseResult, Benchmark diffResult, EquivalenceTestConclusion conclusion)> GetNotSameResults(IEnumerable<(string id, Benchmark baseResult, Benchmark diffResult)> results, TwoInputsOptions args)
        {
            foreach ((string id, Benchmark baseResult, Benchmark diffResult) in results) // failures
            {
                var baseValues = baseResult.Statistics.OriginalValues.ToArray();
                var diffValues = diffResult.Statistics.OriginalValues.ToArray();

                var userTresholdResult = StatisticalTestHelper.CalculateTost(MannWhitneyTest.Instance, baseValues, diffValues, args.StatisticalTestThreshold);
                if (userTresholdResult.Conclusion == EquivalenceTestConclusion.Same)
                    continue;

                var noiseResult = StatisticalTestHelper.CalculateTost(MannWhitneyTest.Instance, baseValues, diffValues, args.NoiseThreshold);
                if (noiseResult.Conclusion == EquivalenceTestConclusion.Same)
                    continue;

                yield return (id, baseResult, diffResult, userTresholdResult.Conclusion);
            }
        }

        private static void PrintSummary((string id, Benchmark baseResult, Benchmark diffResult, EquivalenceTestConclusion conclusion)[] notSame)
        {
            var better = notSame.Where(result => result.conclusion == EquivalenceTestConclusion.Faster);
            var worse = notSame.Where(result => result.conclusion == EquivalenceTestConclusion.Slower);
            var betterCount = better.Count();
            var worseCount = worse.Count();

            // If the baseline doesn't have the same set of tests, you wind up with Infinity in the list of diffs.
            // Exclude them for purposes of geomean.
            worse = worse.Where(x => GetRatio(x) != double.PositiveInfinity);
            better = better.Where(x => GetRatio(x) != double.PositiveInfinity);

            Console.WriteLine("summary:");

            if (betterCount > 0)
            {
                var betterGeoMean = Math.Pow(10, better.Skip(1).Aggregate(Math.Log10(GetRatio(better.First())), (x, y) => x + Math.Log10(GetRatio(y))) / better.Count());
                Console.WriteLine($"better: {betterCount}, geomean: {betterGeoMean:F3}");
            }

            if (worseCount > 0)
            {
                var worseGeoMean = Math.Pow(10, worse.Skip(1).Aggregate(Math.Log10(GetRatio(worse.First())), (x, y) => x + Math.Log10(GetRatio(y))) / worse.Count());
                Console.WriteLine($"worse: {worseCount}, geomean: {worseGeoMean:F3}");
            }

            Console.WriteLine($"total diff: {notSame.Count()}");
            Console.WriteLine();
        }

        private static void PrintTable((string id, Benchmark baseResult, Benchmark diffResult, EquivalenceTestConclusion conclusion)[] notSame, EquivalenceTestConclusion conclusion, TwoInputsOptions args)
        {
            var data = notSame
                .Where(result => result.conclusion == conclusion)
                .OrderByDescending(result => GetRatio(conclusion, result.baseResult, result.diffResult))
                .Take(args.TopCount ?? int.MaxValue)
                .Select(result => new
                {
                    Id = (result.id.Length <= 80 || args.FullId) ? result.id : result.id.Substring(0, 80),
                    DisplayValue = GetRatio(conclusion, result.baseResult, result.diffResult),
                    BaseMedian = result.baseResult.Statistics.Median,
                    DiffMedian = result.diffResult.Statistics.Median,
                    Modality = Helper.GetModalInfo(result.baseResult) ?? Helper.GetModalInfo(result.diffResult)
                })
                .ToArray();

            if (!data.Any())
            {
                Console.WriteLine($"No {conclusion} results for the provided threshold = {args.StatisticalTestThreshold} and noise filter = {args.NoiseThreshold}.");
                Console.WriteLine();
                return;
            }

            var table = data.ToMarkdownTable().WithHeaders(conclusion.ToString(), conclusion == EquivalenceTestConclusion.Faster ? "base/diff" : "diff/base", "Base Median (ns)", "Diff Median (ns)", "Modality");

            foreach (var line in table.ToMarkdown().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
                Console.WriteLine($"| {line.TrimStart()}|"); // the table starts with \t and does not end with '|' and it looks bad so we fix it

            Console.WriteLine();
        }

        private static IEnumerable<(string id, List<Benchmark> baseResults, List<Benchmark> diffResults)> ReadResults(TwoInputsOptions args)
        {
            var baseTrialIndex = 1;
            var baseFilesTrials = new List<string[]>();
            while (Directory.Exists(Path.Join(args.BasePath, $"trial-{baseTrialIndex}")))
            {
                baseFilesTrials.Add(Helper.GetFilesToParse(Path.Join(args.BasePath, $"trial-{baseTrialIndex}")));
                baseTrialIndex += 1;
            }

            var diffTrialIndex = 1;
            var diffFilesTrials = new List<string[]>();
            while (Directory.Exists(Path.Join(args.DiffPath, $"trial-{baseTrialIndex}")))
            {
                diffFilesTrials.Add(Helper.GetFilesToParse(Path.Join(args.DiffPath, $"trial-{baseTrialIndex}")));
                diffTrialIndex += 1;
            }

            if (!baseFilesTrials.Any() || !diffFilesTrials.Any())
            {
                throw new ArgumentException("Found no trial directories.");
            }

            if (baseFilesTrials.Any(trialFiles => !trialFiles.Any()) || diffFilesTrials.Any(trialFiles => !trialFiles.Any()))
            {
                throw new ArgumentException($"Provided paths contained no {Helper.FullBdnJsonFileExtension} files.");
            }

            var baseResultsTrials =
                baseFilesTrials.Select(baseFilesTrial => baseFilesTrial.Select(Helper.ReadFromFile));
            var diffResultsTrials =
                diffFilesTrials.Select(diffFilesTrial => diffFilesTrial.Select(Helper.ReadFromFile));

            var benchmarkResultIds = baseResultsTrials.SelectMany(trialFiles => trialFiles.Select(trialFile => trialFile.Benchmarks))

            var benchmarkIdToDiffResultsTrial2 = diffResultsTrial2
                .SelectMany(result => result.Benchmarks)
                .Where(benchmarkResult => !args.Filters.Any() || args.Filters.Any(filter => filter.IsMatch(benchmarkResult.FullName)))
                .ToDictionary(benchmarkResult => benchmarkResult.FullName, benchmarkResult => benchmarkResult);

            var benchmarkIdToDiffResultsTrial1 = diffResultsTrial1
                .SelectMany(result => result.Benchmarks)
                .Where(benchmarkResult => !args.Filters.Any() || args.Filters.Any(filter => filter.IsMatch(benchmarkResult.FullName)))
                .ToDictionary(benchmarkResult => benchmarkResult.FullName, benchmarkResult => benchmarkResult);

            var benchmarkIdToBaseResultsTrial2 = baseResultsTrial2
                .SelectMany(result => result.Benchmarks)
                .Where(benchmarkResult => !args.Filters.Any() || args.Filters.Any(filter => filter.IsMatch(benchmarkResult.FullName)))
                .ToDictionary(benchmarkResult => benchmarkResult.FullName, benchmarkResult => benchmarkResult);

            return baseResultsTrial1
                .SelectMany(result => result.Benchmarks)
                .ToDictionary(benchmarkResult => benchmarkResult.FullName, benchmarkResult => benchmarkResult) // we use ToDictionary to make sure the results have unique IDs
                .Where(baseResult => benchmarkIdToDiffResultsTrial2.ContainsKey(baseResult.Key) && benchmarkIdToDiffResultsTrial1.ContainsKey(baseResult.Key) && benchmarkIdToBaseResultsTrial2.ContainsKey(baseResult.Key))
                .Select(baseResult => (baseResult.Key, new List<Benchmark> { baseResult.Value, benchmarkIdToBaseResultsTrial2[baseResult.Key] }, new List<Benchmark> { benchmarkIdToDiffResultsTrial1[baseResult.Key], benchmarkIdToDiffResultsTrial2[baseResult.Key] }));
        }

        private static double GetRatio((string id, Benchmark baseResult, Benchmark diffResult, EquivalenceTestConclusion conclusion) item) => GetRatio(item.conclusion, item.baseResult, item.diffResult);

        private static double GetRatio(EquivalenceTestConclusion conclusion, Benchmark baseResult, Benchmark diffResult)
            => conclusion == EquivalenceTestConclusion.Faster
                ? baseResult.Statistics.Median / diffResult.Statistics.Median
                : diffResult.Statistics.Median / baseResult.Statistics.Median;
    }
}
