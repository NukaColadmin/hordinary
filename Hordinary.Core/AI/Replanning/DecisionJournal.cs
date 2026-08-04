using System.Globalization;
using Hordinary.AI.Planning;

namespace Hordinary.AI.Replanning;

/// <summary>Nature d'une entree du journal, pour filtrer l'affichage du panneau debug.</summary>
public enum DecisionKind
{
    /// <summary>Un evenement percu qui n'a pas encore de consequence.</summary>
    Event = 0,

    /// <summary>La Utility AI a retenu un objectif.</summary>
    Goal,

    /// <summary>Une planification, reussie ou non.</summary>
    Plan,

    /// <summary>Une replanification : un plan a ete abandonne pour un autre.</summary>
    Replan,

    /// <summary>Une action du plan a commence, s'est achevee ou a echoue.</summary>
    Action,
}

/// <summary>Une ligne du journal des decisions de l'IA.</summary>
public readonly record struct DecisionEntry(double At, string Agent, DecisionKind Kind, string Message)
{
    /// <summary>Horodatage de jeu, au format de la specification : <c>[10:42:13]</c>.</summary>
    public string Timestamp => DecisionJournal.Clock(At);

    public override string ToString() => $"{Timestamp} {Agent} — {Message}";
}

/// <summary>
/// Ce qu'une replanification doit produire, mot pour mot d'apres la specification (livrable 6,
/// « Journalisation ») :
///
/// <code>
/// [10:42:13] Alice replans
/// Reason: Front door is locked
/// Previous goal: Collect medicine
/// New goal: Find alternate entrance
/// </code>
///
/// Les quatre champs sont ceux-la, dans cet ordre ; seule la langue suit celle du reste du projet.
/// Une cinquieme ligne donne le plan retenu — la specification ne la demande pas, mais le panneau
/// debug du livrable 8 doit pouvoir dire ce que l'agent a decide de faire a la place.
/// </summary>
public sealed record ReplanRecord(
    double At,
    string Agent,
    ReplanTrigger Trigger,
    string? PreviousGoal,
    string? NewGoal,
    Plan? Plan,
    string PlanReason)
{
    public string Timestamp => DecisionJournal.Clock(At);

    /// <summary>Vrai si la replanification a effectivement produit un plan.</summary>
    public bool Succeeded => Plan is not null;

    public string Header => $"{Timestamp} {Agent} replanifie";

    public string ReasonLine => $"Raison : {Trigger.Reason}";

    public string PreviousGoalLine => $"Objectif precedent : {PreviousGoal ?? "aucun"}";

    public string NewGoalLine => $"Nouvel objectif : {NewGoal ?? "aucun"}";

    public string PlanLine => $"Nouveau plan : {(Plan is { } plan ? plan.Describe() : PlanReason)}";

    /// <summary>Le bloc complet, une ligne par element.</summary>
    public IReadOnlyList<string> Lines => [Header, ReasonLine, PreviousGoalLine, NewGoalLine, PlanLine];

    /// <summary>Resume d'une ligne, pour le HUD, ou la place manque.</summary>
    public override string ToString()
        => $"{Timestamp} {Agent} : {PreviousGoal ?? "aucun"} -> {NewGoal ?? "aucun"} ({Trigger.Reason})";
}

/// <summary>
/// Journal des decisions de l'IA — l'un des livrables finaux attendus, et la source du panneau debug
/// du livrable 8.
///
/// C'est une memoire bornee : les entrees les plus anciennes sont perdues, celles qui comptent sont
/// les dernieres. Les replanifications sont en outre conservees a part, sous leur forme structuree :
/// un panneau debug doit pouvoir les compter par cause sans relire des chaines de caracteres.
/// </summary>
public sealed class DecisionJournal
{
    private readonly Queue<DecisionEntry> _entries = [];
    private readonly List<ReplanRecord> _replans = [];
    private readonly int _capacity;

    public DecisionJournal(int capacity = 200)
        => _capacity = Math.Max(1, capacity);

    public IReadOnlyCollection<DecisionEntry> Entries => _entries;

    /// <summary>Replanifications conservees, de la plus ancienne a la plus recente.</summary>
    public IReadOnlyList<ReplanRecord> Replans => _replans;

    public int ReplanCount { get; private set; }

    /// <summary>Emis a chaque ligne ecrite : c'est par la que la couche Godot alimente sa console.</summary>
    public event Action<DecisionEntry>? Written;

    public void Add(double at, string agent, DecisionKind kind, string message)
    {
        var entry = new DecisionEntry(at, agent, kind, message);

        _entries.Enqueue(entry);
        while (_entries.Count > _capacity) _entries.Dequeue();

        Written?.Invoke(entry);
    }

    /// <summary>Ecrit une replanification : le bloc de quatre lignes, et l'enregistrement structure.</summary>
    public void AddReplan(ReplanRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        ReplanCount++;

        _replans.Add(record);
        while (_replans.Count > _capacity) _replans.RemoveAt(0);

        foreach (var line in record.Lines) Add(record.At, record.Agent, DecisionKind.Replan, line);
    }

    /// <summary>Les <paramref name="count"/> dernieres lignes, de la plus ancienne a la plus recente.</summary>
    public IEnumerable<string> Tail(int count)
        => _entries.Reverse().Take(Math.Max(0, count)).Reverse().Select(entry => entry.ToString());

    /// <summary>Compte des replanifications par cause, pour le panneau debug.</summary>
    public IReadOnlyDictionary<ReplanCause, int> CountsByCause()
    {
        var counts = new Dictionary<ReplanCause, int>();
        foreach (var record in _replans) counts[record.Trigger.Cause] = counts.GetValueOrDefault(record.Trigger.Cause) + 1;
        return counts;
    }

    /// <summary>
    /// Temps de jeu au format <c>[hh:mm:ss]</c> impose par la specification. Les deux-points sont
    /// echappes : sans cela, ils seraient remplaces par le separateur horaire de la machine.
    /// </summary>
    public static string Clock(double seconds)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"[{TimeSpan.FromSeconds(Math.Max(0d, seconds)):hh\\:mm\\:ss}]");
}
