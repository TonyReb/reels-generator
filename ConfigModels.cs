using System.Collections.Generic;

namespace ReelsGenerator;

public class AppConfig
{
    public GeneticAlgorithmSection GeneticAlgorithm { get; set; } = new();
    public SimulationSection Simulation { get; set; } = new();
    public ReelGenerationSection ReelGeneration { get; set; } = new();
    public SlotMachineConfig SlotMachine { get; set; } = new();
}

public class GeneticAlgorithmSection
{
    public int PopSize { get; set; }
    public int Generations { get; set; }
    public float CrossoverRate { get; set; }
    public float MutationRate { get; set; }
    public float CrossoverAlpha { get; set; }
    public double MutationSigma { get; set; }
    public int Elitism { get; set; }
    public int TournamentK { get; set; }
    public bool Maximize { get; set; }
    public int Seed { get; set; }
}

public class SimulationSection
{
    public int SpinNumber { get; set; }
    public double TargetRtp { get; set; }
    public double TargetHitFrequency { get; set; }
}

public class ReelGenerationSection
{
    public int Radius { get; set; }
    public int Seed { get; set; }
    public SymbolStacksSection SymbolStacks { get; set; } = new();
}

public class SymbolStacksSection
{
    public Dictionary<int, List<int>> Low { get; set; } = new();
    public Dictionary<int, List<int>> High { get; set; } = new();
}

public class SlotMachineConfig
{
    public List<int> Window { get; set; } = new();
    public List<int> IconWild { get; set; } = new();
    public List<int> IconScatter { get; set; } = new();
    public Dictionary<int, List<int>> Paytable { get; set; } = new();
    public List<List<int>> Lines { get; set; } = new();
}
