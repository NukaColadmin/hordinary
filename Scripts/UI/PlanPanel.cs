using Godot;
using Hordinary.AI.Replanning;
using Hordinary.Agents;

namespace Hordinary.UI;

/// <summary>
/// Le panneau de planification (specification section A : <c>UI/PlanPanel</c>).
///
/// C'est la moitie « recherche » du mode debug du livrable 8 : les noeuds explores par GBFS, les
/// plans rejetes, la raison d'un echec de planification, le compte des replanifications par cause et
/// le journal des decisions.
///
/// Il repond a une question que le panneau d'agent ne pose pas : non pas « que fait-elle ? » mais
/// « pourquoi ne fait-elle pas autre chose ? ». Un survivant qui explore alors qu'on lui a demande
/// les medicaments a une raison — elle est ecrite ici, sous la forme du plan qui n'a pas abouti.
/// </summary>
public partial class PlanPanel : InfoPanel
{
    public PlanPanel() : base("PlanPanel", fontSize: 12) { }

    /// <summary>Deroule le journal des decisions de l'IA (touche F6).</summary>
    public bool ShowJournal { get; set; }

    public void Refresh(SurvivorAgent? agent)
    {
        if (agent is null)
        {
            Write(string.Empty);
            return;
        }

        Write($"""
            Planification GBFS
            {SearchLines(agent)}
            {RejectedLines(agent)}
            {ReplanLines(agent)}
            {JournalLines(agent)}
            """.TrimEnd());
    }

    /// <summary>
    /// La derniere recherche : noeuds explores, noeuds generes, temps passe, et les limites contre
    /// lesquelles elle bute. Les trois garde-fous du livrable 5 ne servent a rien si l'on ne voit
    /// jamais lequel a arrete la recherche.
    /// </summary>
    private static string SearchLines(SurvivorAgent agent)
    {
        var planner = agent.Planner;

        if (planner.Last is not { } last)
            return $"  aucune recherche lancee (limites : {planner.Settings.MaxNodes} noeuds, "
                 + $"profondeur {planner.Settings.MaxDepth}, {planner.Settings.MaxTimeMs} ms)";

        var search = last.Search;

        string outcome = last.Found
            ? $"plan de {search.Actions.Count} actions, cout {search.Cost:0.##}"
            : $"echec — {search.Reason}";

        return $"""
              {planner.SearchCount} recherches, derniere : {outcome}
              {search.ExploredNodes} noeuds explores, {search.GeneratedNodes} generes, {search.ElapsedMs:0.##} ms
              limites : {planner.Settings.MaxNodes} noeuds, profondeur {planner.Settings.MaxDepth}, {planner.Settings.MaxTimeMs} ms
            """;
    }

    /// <summary>
    /// Les plans rejetes. GBFS ne met pas des plans concurrents en balance : un plan rejete est une
    /// recherche dont le survivant n'a rien pu faire, et c'est cette liste-la qui explique un repli.
    /// </summary>
    private static string RejectedLines(SurvivorAgent agent)
    {
        var rejected = agent.Planner.Rejected;
        if (rejected.Count == 0) return "Plans rejetes : aucun";

        var tail = rejected
            .TakeLast(4)
            .Select(result => $"  {(result.UsedFallback ? "repli" : "echec")} — {result.Reason}");

        return $"Plans rejetes : {rejected.Count}\n{string.Join("\n", tail)}";
    }

    private static string ReplanLines(SurvivorAgent agent)
    {
        var brain = agent.Brain;

        string header = brain.LastReplan is { } record
            ? $"Replanifications : {brain.ReplanCount}, derniere {record.Timestamp} — {record.Trigger.Reason}"
            : "Replanifications : aucune";

        var causes = brain.Journal.CountsByCause()
            .OrderByDescending(pair => pair.Value)
            .Select(pair => $"{ReplanCauses.Label(pair.Key)} x{pair.Value}")
            .ToList();

        if (causes.Count > 0) header += $"\n  causes : {string.Join(", ", causes)}";

        if (brain.ConsecutiveFailures > 0)
            header += $"\n  {brain.ConsecutiveFailures} replanification(s) d'affilee sans solution";

        return header;
    }

    private string JournalLines(SurvivorAgent agent)
    {
        if (!ShowJournal) return "Journal : F6";

        var tail = agent.Brain.Journal.Tail(14).Select(line => $"  {line}");
        return $"Journal des decisions :\n{string.Join("\n", tail)}";
    }
}
