using System;

namespace HeroesReplay.Core.Retention;

/// <summary>
/// One kind of cleanup the housekeeping pass runs (#487). <see cref="Plan"/> lists what it would
/// remove and why, and changes nothing, so <c>storage report</c> can show it. <see cref="Apply"/>
/// removes what a plan names. A pruner never plans anything a live process or a non-terminal
/// state still needs.
/// </summary>
public interface IPruner
{
    /// <summary>
    /// Its key under <c>Housekeeping:Pruners</c> (the switch that turns it off) and its name in
    /// the report and the log.
    /// </summary>
    string Name { get; }

    /// <summary>What it removes, in one sentence, for <c>storage report</c>.</summary>
    string Description { get; }

    PrunePlan Plan(DateTimeOffset utcNow);

    RetentionSweep Apply(PrunePlan plan);
}

/// <summary>
/// A pruner from a plan function. Its apply removes every item of the plan in order
/// (<see cref="PrunePlan.DeleteAll"/>) unless it is given its own.
/// </summary>
public sealed class Pruner : IPruner
{
    private readonly Func<DateTimeOffset, PrunePlan> plan;
    private readonly Func<PrunePlan, RetentionSweep> apply;

    public Pruner(
        string name,
        string description,
        Func<DateTimeOffset, PrunePlan> plan,
        Func<PrunePlan, RetentionSweep> apply = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Description = description ?? string.Empty;
        this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
        this.apply = apply ?? (planned => planned.DeleteAll());
    }

    public string Name { get; }
    public string Description { get; }

    public PrunePlan Plan(DateTimeOffset utcNow) => plan(utcNow) ?? new PrunePlan(Name);

    public RetentionSweep Apply(PrunePlan plan) =>
        plan == null ? new RetentionSweep() : apply(plan);
}
