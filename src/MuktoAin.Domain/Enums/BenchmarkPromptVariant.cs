namespace MuktoAin.Domain.Enums;

// S-3.2/S-3.3: which prompt-assembly strategy the benchmark runner drives.
// FewShotIrac is added by S-3.3 (Task 6 of the QA benchmark chain plan).
public enum BenchmarkPromptVariant
{
    ZeroShot = 0,
    FewShotIrac = 1
}
