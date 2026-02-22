using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace ReelsGenerator;

public class GAConfig
{
    public int PopSize { get; set; } = 5;
    public int Generations { get; set; } = 3;
    public float CrossoverRate { get; set; } = 0.9f;
    public float MutationRate { get; set; } = 0.02f;
    public int Elitism { get; set; } = 2;
    public int TournamentK { get; set; } = 3;
    public int Seed { get; set; } = 123;
    public float CrossoverAlpha { get; set; } = 0.5f;
    public double MutationSigma { get; set; } = 3.0;
    public bool VerboseProgress { get; set; } = false;
    public double SymbolRtpUnevennessWeight { get; set; } = 0.1;
}

public class GAResult
{
    public Individual? BestIndividual { get; set; }
    public FitnessBreakdown BestFitness { get; set; }
    public List<double> HistoryBest { get; set; } = new();
}

public class Individual
{
    public List<Dictionary<int, List<int>>> Items { get; set; } = new();
    public List<List<int>> Reels { get; set; } = new();
}

public readonly record struct FitnessBreakdown(
    double Total,
    double RtpDelta,
    double HitFrequencyDelta,
    double BonusGameFrequencyDelta,
    double SymbolRtpTargetErrorPenalty,
    double SymbolRtpTargetError,
    double Rtp,
    double HitFrequency,
    double BonusGameFrequency);

public class GeneticAlgorithm
{
    private const int MaxGenerateAttemptsPerReel = 250;
    private List<Dictionary<int, List<int>>> LOWS;
    private List<Dictionary<int, List<int>>> HIGHS;
    private List<int[]> SYMBOLS_BY_REEL;
    private List<int> REEL_RADII;
    private List<int> REEL_SEEDS;
    private List<ReelGenerator> REEL_GENERATORS;
    private int REEL_COUNT;

    private double MUT_SIGMA;
    private float CROSSOVER_ALPHA;
    private double TARGET_RTP;
    private double TARGET_HIT_FREQUENCY;
    private double TARGET_BONUS_GAME_FREQUENCY;
    private Dictionary<int, double> SYMBOL_RTP_TARGETS;
    private int SPIN_NUMBER;
    private SlotMachineConfig SLOT_CONFIG;
    private IGameEngineFactory GAME_FACTORY;
    private ISlotEngineFactory SLOT_FACTORY;

    private GAConfig config;
    private Random rng;

    public GeneticAlgorithm(
        GAConfig gaConfig,
        List<ReelConfig> reelConfigs,
        double targetRtp,
        double targetHitFrequency,
        double targetBonusGameFrequency,
        IReadOnlyDictionary<int, double> symbolRtpTargets,
        int spinNumber,
        SlotMachineConfig slotConfig,
        IGameEngineFactory gameFactory,
        ISlotEngineFactory slotFactory)
    {
        config = gaConfig;
        rng = new Random(config.Seed);

        LOWS = new List<Dictionary<int, List<int>>>();
        HIGHS = new List<Dictionary<int, List<int>>>();
        SYMBOLS_BY_REEL = new List<int[]>();
        REEL_RADII = new List<int>();
        REEL_SEEDS = new List<int>();
        REEL_GENERATORS = new List<ReelGenerator>();
        var specialSymbols = slotConfig.IconWild.Concat(slotConfig.IconScatter);

        for (int reelIndex = 0; reelIndex < reelConfigs.Count; reelIndex++)
        {
            var reelConfig = reelConfigs[reelIndex];
            var low = reelConfig.SymbolStacks.Low;
            var high = reelConfig.SymbolStacks.High;

            var symbols = low.Keys.Union(high.Keys).OrderBy(x => x).ToArray();
            foreach (var symbol in symbols)
            {
                if (!low.ContainsKey(symbol) || !high.ContainsKey(symbol))
                {
                    throw new InvalidOperationException($"Reel {reelIndex}: symbol {symbol} must exist in both low and high stacks.");
                }

                if (low[symbol].Count != high[symbol].Count)
                {
                    throw new InvalidOperationException($"Reel {reelIndex}: symbol {symbol} low/high stack lengths must match.");
                }
            }

            LOWS.Add(low);
            HIGHS.Add(high);
            SYMBOLS_BY_REEL.Add(symbols);
            REEL_RADII.Add(reelConfig.Radius);
            REEL_SEEDS.Add(reelConfig.Seed);
            REEL_GENERATORS.Add(new ReelGenerator(specialSymbols));
        }

        REEL_COUNT = reelConfigs.Count;
        TARGET_RTP = targetRtp;
        TARGET_HIT_FREQUENCY = targetHitFrequency;
        TARGET_BONUS_GAME_FREQUENCY = targetBonusGameFrequency;
        SYMBOL_RTP_TARGETS = symbolRtpTargets
            .OrderBy(x => x.Key)
            .ToDictionary(x => x.Key, x => x.Value);
        SPIN_NUMBER = spinNumber;
        SLOT_CONFIG = slotConfig;
        GAME_FACTORY = gameFactory;
        SLOT_FACTORY = slotFactory;

        MUT_SIGMA = config.MutationSigma;
        CROSSOVER_ALPHA = config.CrossoverAlpha;
    }

    private FitnessBreakdown FitnessSphere(Individual ind)
    {
        IGameEngine game = GAME_FACTORY.Create(ind.Reels, SPIN_NUMBER, SLOT_CONFIG, SLOT_FACTORY);
        game.Run();
        var (rtp, hitFrequency) = game.GetStats();
        double bonusGameFrequency = game.GetBonusGameFrequency();
        var winSums = game.GetWinningCombinationWinSums();
        double symbolRtpTargetError = GetSymbolRtpTargetError(winSums, SYMBOL_RTP_TARGETS, SPIN_NUMBER);
        double symbolRtpTargetErrorPenalty = config.SymbolRtpUnevennessWeight * symbolRtpTargetError;
        double rtpDelta = RelativeDelta(TARGET_RTP, rtp);
        double hitDelta = RelativeDelta(TARGET_HIT_FREQUENCY, hitFrequency);
        double bonusDelta = RelativeDelta(TARGET_BONUS_GAME_FREQUENCY, bonusGameFrequency);

        double fitness =
            rtpDelta +
            hitDelta +
            bonusDelta +
            symbolRtpTargetErrorPenalty;

        return new FitnessBreakdown(
            Total: fitness,
            RtpDelta: rtpDelta,
            HitFrequencyDelta: hitDelta,
            BonusGameFrequencyDelta: bonusDelta,
            SymbolRtpTargetErrorPenalty: symbolRtpTargetErrorPenalty,
            SymbolRtpTargetError: symbolRtpTargetError,
            Rtp: rtp,
            HitFrequency: hitFrequency,
            BonusGameFrequency: bonusGameFrequency);
    }

    private static double RelativeDelta(double target, double actual)
    {
        double denominator = Math.Abs(target) + Math.Abs(actual);
        if (denominator < 1e-12)
        {
            return 0;
        }

        return Math.Abs(target - actual) / denominator;
    }

    private static double GetSymbolRtpTargetError(
        IReadOnlyDictionary<(int Symbol, int Length), long> winSums,
        IReadOnlyDictionary<int, double> symbolRtpTargets,
        int spinNumber)
    {
        if (spinNumber <= 0 || symbolRtpTargets.Count == 0)
        {
            return 0;
        }

        var symbolWinSums = new Dictionary<int, long>();
        foreach (var kvp in winSums)
        {
            symbolWinSums.TryGetValue(kvp.Key.Symbol, out long current);
            symbolWinSums[kvp.Key.Symbol] = current + kvp.Value;
        }

        double error = 0;
        foreach (var target in symbolRtpTargets)
        {
            symbolWinSums.TryGetValue(target.Key, out long sumWin);
            double actual = sumWin / (double)spinNumber;
            error += RelativeDelta(target.Value, actual);
        }

        return error / symbolRtpTargets.Count;
    }

    private Individual TournamentSelect(List<Individual> population, List<FitnessBreakdown> fitnesses)
    {
        int bestIdx = -1;

        for (int i = 0; i < config.TournamentK; i++)
        {
            int idx = rng.Next(population.Count);

            if (bestIdx == -1)
            {
                bestIdx = idx;
            }
            else
            {
                bool isBetter = fitnesses[idx].Total < fitnesses[bestIdx].Total;

                if (isBetter)
                {
                    bestIdx = idx;
                }
            }
        }

        return population[bestIdx];
    }

    private List<FitnessBreakdown> EvaluatePopulation(List<Individual> population, bool showProgress)
    {
        var fitnesses = new List<FitnessBreakdown>(population.Count);
        for (int i = 0; i < population.Count; i++)
        {
            if (showProgress)
            {
                Console.Write($"[E{i}]");
                if (i % 10 == 9)
                {
                    Console.WriteLine();
                }
            }

            fitnesses.Add(FitnessSphere(population[i]));
        }

        if (showProgress)
        {
            Console.WriteLine();
        }

        return fitnesses;
    }

    private Dictionary<int, List<int>> CloneItem(Dictionary<int, List<int>> item)
    {
        var result = new Dictionary<int, List<int>>();
        foreach (var kvp in item)
        {
            result[kvp.Key] = new List<int>(kvp.Value);
        }
        return result;
    }

    private Individual CloneIndividual(Individual ind)
    {
        var clone = new Individual();
        foreach (var item in ind.Items)
        {
            clone.Items.Add(CloneItem(item));
        }
        foreach (var reel in ind.Reels)
        {
            clone.Reels.Add(reel != null ? new List<int>(reel) : null!);
        }
        return clone;
    }

    private static void PrintBestIndividualItems(Individual individual, string header)
    {
        Console.WriteLine(header);
        for (int reelIndex = 0; reelIndex < individual.Items.Count; reelIndex++)
        {
            var item = individual.Items[reelIndex];
            foreach (var kvp in item)
            {
                Console.WriteLine($"{reelIndex + 1}, {kvp.Key}, {string.Join(", ", kvp.Value)}");
            }
            Console.WriteLine();
        }
    }

    private static void PrintBestIndividualReels(Individual individual)
    {
        Console.WriteLine("Best Individual Reels:");
        for (int i = 0; i < individual.Reels.Count; i++)
        {
            Console.WriteLine($"Reel {i + 1}: {string.Join(", ", individual.Reels[i])}");
        }
        Console.WriteLine();
    }

    private void PrintFitnessComponentsTable(FitnessBreakdown current)
    {
        string[] header = ["Metric", "Target", "Current", "Fitness"];
        string[][] rows =
        [
            [
                "RTP",
                FormatMetricValue(TARGET_RTP),
                FormatMetricValue(current.Rtp),
                FormatMetricValue(current.RtpDelta)
            ],
            [
                "Hit Frequency",
                FormatMetricValue(TARGET_HIT_FREQUENCY),
                FormatMetricValue(current.HitFrequency),
                FormatMetricValue(current.HitFrequencyDelta)
            ],
            [
                "Bonus game frequency",
                FormatMetricValue(TARGET_BONUS_GAME_FREQUENCY),
                FormatMetricValue(current.BonusGameFrequency),
                FormatMetricValue(current.BonusGameFrequencyDelta)
            ],
            [
                "symbol_rtp_target_error",
                "-",
                FormatMetricValue(current.SymbolRtpTargetError),
                FormatMetricValue(current.SymbolRtpTargetErrorPenalty)
            ]
        ];

        int[] widths = new int[header.Length];
        for (int i = 0; i < header.Length; i++)
        {
            widths[i] = header[i].Length;
        }

        for (int r = 0; r < rows.Length; r++)
        {
            for (int c = 0; c < rows[r].Length; c++)
            {
                widths[c] = Math.Max(widths[c], rows[r][c].Length);
            }
        }

        Console.WriteLine(RenderTableRow(header, widths));
        Console.WriteLine(RenderTableSeparator(widths));
        for (int r = 0; r < rows.Length; r++)
        {
            Console.WriteLine(RenderTableRow(rows[r], widths));
        }
    }

    private static string FormatMetricValue(double value)
    {
        return value.ToString("0.0000", CultureInfo.CurrentCulture);
    }

    private static string RenderTableRow(string[] row, int[] widths)
    {
        return string.Join(" | ", row.Select((cell, i) => cell.PadRight(widths[i])));
    }

    private static string RenderTableSeparator(int[] widths)
    {
        return string.Join("-+-", widths.Select(w => new string('-', w)));
    }

    private void PrintWinningCombinationCountsForIndividual(Individual individual, string header)
    {
        IGameEngine game = GAME_FACTORY.Create(individual.Reels, SPIN_NUMBER, SLOT_CONFIG, SLOT_FACTORY);
        game.Run();
        var counts = game.GetWinningCombinationCounts();
        var winSums = game.GetWinningCombinationWinSums();
        Console.WriteLine(header);
        var tableLines = WinningCombinationsTableFormatter.Format(counts, winSums, SPIN_NUMBER, SLOT_CONFIG.Lines.Count);
        foreach (var line in tableLines)
        {
            Console.WriteLine(line);
        }
        Console.WriteLine();
    }

    private ReelGenerator GetReelGenerator(int reelIndex) => REEL_GENERATORS[reelIndex];

    private Dictionary<int, List<int>> BlendReelItem(Individual a, Individual b, int reelIndex)
    {
        var low = LOWS[reelIndex];
        var high = HIGHS[reelIndex];
        var symbols = SYMBOLS_BY_REEL[reelIndex];

        var item = new Dictionary<int, List<int>>();

        foreach (int symbol in symbols)
        {
            var blended = new List<int>();
            for (int i = 0; i < a.Items[reelIndex][symbol].Count; i++)
            {
                int x = a.Items[reelIndex][symbol][i];
                int y = b.Items[reelIndex][symbol][i];
                int lo = (int)Math.Round(Math.Min(x, y) - CROSSOVER_ALPHA * Math.Abs(x - y));
                int hi = (int)Math.Round(Math.Max(x, y) + CROSSOVER_ALPHA * Math.Abs(x - y));

                int value = rng.Next(lo, hi + 1);
                value = Math.Min(high[symbol][i], Math.Max(low[symbol][i], value));
                blended.Add(value);
            }
            item[symbol] = blended;
        }

        return item;
    }

    private (Individual, Individual) CrossoverBlend(Individual a, Individual b)
    {
        var c1 = new Individual();
        var c2 = new Individual();

        for (int reelIndex = 0; reelIndex < REEL_COUNT; reelIndex++)
        {
            bool generated = false;
            int attempts = 0;
            while (!generated)
            {
                var item1 = BlendReelItem(a, b, reelIndex);
                var item2 = BlendReelItem(a, b, reelIndex);

                var reelGen = GetReelGenerator(reelIndex);
                var reel1 = reelGen.Generate(item1, REEL_RADII[reelIndex], REEL_SEEDS[reelIndex]);
                var reel2 = reelGen.Generate(item2, REEL_RADII[reelIndex], REEL_SEEDS[reelIndex]);

                if (reel1 != null && reel2 != null)
                {
                    c1.Items.Add(item1);
                    c1.Reels.Add(reel1);
                    c2.Items.Add(item2);
                    c2.Reels.Add(reel2);
                    generated = true;
                }
                else if (++attempts >= MaxGenerateAttemptsPerReel)
                {
                    throw new InvalidOperationException($"Failed to generate reel {reelIndex + 1} during crossover after {MaxGenerateAttemptsPerReel} attempts.");
                }
            }
        }

        return (c1, c2);
    }

    private Individual MutateReal(Individual ind)
    {
        var outInd = new Individual();

        for (int reelIndex = 0; reelIndex < REEL_COUNT; reelIndex++)
        {
            bool generated = false;
            int attempts = 0;
            while (!generated)
            {
                var low = LOWS[reelIndex];
                var high = HIGHS[reelIndex];
                var symbols = SYMBOLS_BY_REEL[reelIndex];
                var item = new Dictionary<int, List<int>>();

                foreach (int symbol in symbols)
                {
                    item[symbol] = new List<int>();
                    for (int index = 0; index < ind.Items[reelIndex][symbol].Count; index++)
                    {
                        int val = ind.Items[reelIndex][symbol][index];

                        if (rng.NextDouble() < config.MutationRate)
                        {
                            double gaussian = GetGaussian(0.0, MUT_SIGMA);
                            val += (int)gaussian;
                        }

                        val = Math.Min(high[symbol][index], Math.Max(low[symbol][index], val));
                        item[symbol].Add(val);
                    }
                }

                var reelGen = GetReelGenerator(reelIndex);
                var reel = reelGen.Generate(item, REEL_RADII[reelIndex], REEL_SEEDS[reelIndex]);
                if (reel != null)
                {
                    outInd.Items.Add(item);
                    outInd.Reels.Add(reel);
                    generated = true;
                }
                else if (++attempts >= MaxGenerateAttemptsPerReel)
                {
                    throw new InvalidOperationException($"Failed to generate reel {reelIndex + 1} during mutation after {MaxGenerateAttemptsPerReel} attempts.");
                }
            }
        }

        return outInd;
    }

    private Individual CreateReal()
    {
        var ind = new Individual();

        for (int reelIndex = 0; reelIndex < REEL_COUNT; reelIndex++)
        {
            bool generated = false;
            int attempts = 0;
            while (!generated)
            {
                var low = LOWS[reelIndex];
                var high = HIGHS[reelIndex];
                var symbols = SYMBOLS_BY_REEL[reelIndex];
                var item = new Dictionary<int, List<int>>();

                foreach (int symbol in symbols)
                {
                    item[symbol] = new List<int>();
                    for (int index = 0; index < low[symbol].Count; index++)
                    {
                        item[symbol].Add(rng.Next(low[symbol][index], high[symbol][index] + 1));
                    }
                }

                var reelGen = GetReelGenerator(reelIndex);
                var reel = reelGen.Generate(item, REEL_RADII[reelIndex], REEL_SEEDS[reelIndex]);
                if (reel != null)
                {
                    ind.Items.Add(item);
                    ind.Reels.Add(reel);
                    generated = true;
                }
                else
                {
                    Console.Write(".");
                }

                if (!generated && ++attempts >= MaxGenerateAttemptsPerReel)
                {
                    throw new InvalidOperationException($"Failed to generate reel {reelIndex + 1} for initial population after {MaxGenerateAttemptsPerReel} attempts.");
                }
            }
        }

        return ind;
    }

    public GAResult Run()
    {
        Console.WriteLine(">>> Initializing population...");
        var population = new List<Individual>();
        for (int i = 0; i < config.PopSize; i++)
        {
            if (config.VerboseProgress)
            {
                Console.Write($"[{i}]");
                if (i % 10 == 9)
                {
                    Console.WriteLine();
                }
            }
            population.Add(CreateReal());
        }
        if (config.VerboseProgress)
        {
            Console.WriteLine();
        }
        Console.WriteLine($">>> Population created with {population.Count} individuals");

        Console.WriteLine(">>> Evaluating initial population...");
        var fitnesses = EvaluatePopulation(population, config.VerboseProgress);
        Console.WriteLine($">>> Initial evaluation complete, {fitnesses.Count} fitnesses calculated");

        int bestIdx = 0;
        for (int i = 1; i < fitnesses.Count; i++)
        {
            bool isBetter = fitnesses[i].Total < fitnesses[bestIdx].Total;

            if (isBetter)
            {
                bestIdx = i;
            }
        }

        var bestInd = CloneIndividual(population[bestIdx]);
        var bestFit = fitnesses[bestIdx];
        var history = new List<double> { bestFit.Total };
        PrintBestIndividualItems(bestInd, "Current Best Individual Items:");

        for (int gen = 0; gen < config.Generations; gen++)
        {
            var generationStopwatch = Stopwatch.StartNew();

            var indexed = Enumerable.Range(0, config.PopSize).ToList();
            indexed.Sort((a, b) =>
            {
                return fitnesses[a].Total.CompareTo(fitnesses[b].Total);
            });

            var newPopulation = new List<Individual>();
            for (int i = 0; i < config.Elitism && i < indexed.Count; i++)
            {
                newPopulation.Add(CloneIndividual(population[indexed[i]]));
            }

            while (newPopulation.Count < config.PopSize)
            {
                var p1 = TournamentSelect(population, fitnesses);
                var p2 = TournamentSelect(population, fitnesses);

                Individual c1;
                Individual c2;
                if (rng.NextDouble() < config.CrossoverRate)
                {
                    (c1, c2) = CrossoverBlend(p1, p2);
                }
                else
                {
                    c1 = CloneIndividual(p1);
                    c2 = CloneIndividual(p2);
                }

                c1 = MutateReal(c1);
                if (newPopulation.Count < config.PopSize)
                {
                    newPopulation.Add(c1);
                }

                if (newPopulation.Count < config.PopSize)
                {
                    c2 = MutateReal(c2);
                    newPopulation.Add(c2);
                }
            }

            population = newPopulation;
            fitnesses = EvaluatePopulation(population, showProgress: false);

            int curBestIdx = 0;
            for (int i = 1; i < fitnesses.Count; i++)
            {
                bool isBetter = fitnesses[i].Total < fitnesses[curBestIdx].Total;

                if (isBetter)
                {
                    curBestIdx = i;
                }
            }

            var curBestFit = fitnesses[curBestIdx];
            var previousBestFit = bestFit;
            bool isImproved = curBestFit.Total < bestFit.Total;

            if (isImproved)
            {
                bestInd = CloneIndividual(population[curBestIdx]);
                bestFit = curBestFit;
            }
            double fitnessDelta = bestFit.Total - previousBestFit.Total;
            generationStopwatch.Stop();
            Console.WriteLine($"---- Generation {gen} ----");
            Console.WriteLine($"Generation time: {generationStopwatch.Elapsed.TotalSeconds:F2} s");
            Console.WriteLine(
                $"Best fitness: {bestFit.Total.ToString("0.000000", CultureInfo.CurrentCulture)} " +
                $"({fitnessDelta.ToString("0.000000", CultureInfo.CurrentCulture)})");
            Console.WriteLine();
            PrintFitnessComponentsTable(bestFit);
            Console.WriteLine();
            PrintBestIndividualItems(bestInd, "Current Best Individual Items:");
            Console.WriteLine();
            PrintBestIndividualReels(bestInd);
            Console.WriteLine();
            PrintWinningCombinationCountsForIndividual(bestInd, $"Winning combinations by symbol and length (generation {gen}):");

            history.Add(bestFit.Total);
        }

        return new GAResult
        {
            BestIndividual = bestInd,
            BestFitness = bestFit,
            HistoryBest = history
        };
    }

    private double GetGaussian(double mean, double stdDev)
    {
        double u1 = rng.NextDouble();
        double u2 = rng.NextDouble();
        double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        return mean + stdDev * z;
    }
}
