using Godot;
using Hordinary.Agents;

namespace Hordinary.UI;

/// <summary>
/// Le panneau d'inspection d'un survivant (specification section A : <c>UI/AgentPanel</c>, et
/// livrable 8 : « Informations visibles »).
///
/// Il suit exactement la maquette de la specification — nom, objectif, action actuelle, plan,
/// derniere replanification — et y ajoute les autres grandeurs qu'elle exige : sante, panique,
/// prochain point de deplacement, scores Utility AI et ce que l'agent sait.
///
/// Il n'affiche jamais « ce que le groupe sait », parce que cela n'existe pas : tout ce qui est ecrit
/// ici est lu dans la memoire d'<b>un</b> survivant. Changer de point de vue change donc les
/// informations connues, les scores et le plan — c'est le livrable 7 rendu lisible.
/// </summary>
public partial class AgentPanel : InfoPanel
{
    public AgentPanel() : base("AgentPanel") { }

    /// <summary>Deplie le detail des scores, consideration par consideration (touche F4).</summary>
    public bool ShowScoreDetail { get; set; }

    public void Refresh(SurvivorAgent? agent, double now)
    {
        if (agent is null)
        {
            Write("Aucun survivant.");
            return;
        }

        var survivor = agent.Survivor;

        Write($"""
            {survivor.Name} — {survivor.Profile.Role}
            {StateLine(survivor)}

            Objectif :
            {GoalLine(agent, now)}

            Action actuelle :
            {ActionLine(agent)}

            Plan :
            {PlanLines(agent)}

            Derniere replanification :
            {ReplanLine(agent)}

            Scores :
            {ScoreLines(agent)}

            Connaissances :
            {KnowledgeLines(agent)}
            """);
    }

    // --- Etat -------------------------------------------------------------------------------

    /// <summary>
    /// Sante, motivation et panique. La panique porte la mention du mode debug quand elle est
    /// neutralisee : sans cela, un zero permanent passerait pour un survivant imperturbable au lieu
    /// d'un interrupteur qu'on a oublie de remettre.
    ///
    /// La motivation figure ici sous la meme forme que sur la fiche du bas — <c>SANITY 150/200</c> —
    /// pour qu'un joueur n'ait jamais a convertir mentalement entre deux affichages du meme chiffre.
    /// </summary>
    private static string StateLine(Survivor survivor)
    {
        if (survivor.Returned) return $"rentre au camp a t = {survivor.ReturnedAt:0.0} s";
        if (!survivor.Alive) return "hors de combat";

        string panic = survivor.PanicEnabled
            ? $"panique {survivor.Panic:P0}"
            : "panique neutralisee (debug)";

        string carrying = survivor.Carries(ItemKind.Medicine) ? ", porte les medicaments" : string.Empty;
        string recalled = survivor.Recalled ? $"\nrappele : « {survivor.RecallMessage} »" : string.Empty;

        return $"HP {survivor.Health:0}/{survivor.Settings.MaxHealth:0}, "
             + $"SANITY {survivor.Sanity:0}/{survivor.Settings.MaxSanity:0}, {panic}{carrying}{recalled}";
    }

    private static string GoalLine(SurvivorAgent agent, double now)
    {
        if (!agent.Survivor.Alive) return "  aucun";
        if (agent.Utility.CurrentGoal is not { } goal) return "  aucun (rien ne depasse le seuil)";

        return $"  {goal} ({agent.Utility.CurrentScore:0.00}), depuis {now - agent.Utility.LastChangeAt:0.0} s";
    }

    /// <summary>
    /// L'action en cours et, quand elle consiste a marcher, le prochain point de deplacement — que
    /// la specification demande explicitement. C'est la seule facon de voir qu'un survivant avance
    /// vraiment, et non qu'il pietine sur une action qui ne finit jamais.
    /// </summary>
    private static string ActionLine(SurvivorAgent agent)
    {
        if (agent.Plan?.Current is not { } action) return "  aucune";

        string step = $"  {action.Describe()} ({agent.Executor.ElapsedInStep:0.0}/{action.Duration:0.#} s)";

        return agent.Follower.NextCell is { } next
            ? $"{step}\n  prochain pas : {next} vers {agent.Follower.Destination}"
            : step;
    }

    private static string PlanLines(SurvivorAgent agent)
    {
        if (agent.Plan is not { } plan) return $"  aucun — {agent.Brain.LastReason}";

        var steps = plan.DescribeSteps().Select(line => $"  {line}");

        string header = $"  {plan.Goal}{(plan.IsFallback ? " (repli)" : string.Empty)} — "
            + $"{plan.CurrentActionIndex}/{plan.StepCount}, cout {plan.TotalCost:0.##}, bruit {plan.TotalNoise:0.##}";

        return $"{header}\n{string.Join("\n", steps)}";
    }

    /// <summary>La derniere cause de replanification, telle que la specification l'exige.</summary>
    private static string ReplanLine(SurvivorAgent agent)
    {
        var brain = agent.Brain;

        if (brain.LastReplan is not { } record) return $"  aucune ({brain.ReplanCount} au total)";

        return $"  {record.Trigger.Reason}\n  a t = {record.Timestamp}, {brain.ReplanCount} au total";
    }

    private string ScoreLines(SurvivorAgent agent)
    {
        var scores = agent.Utility.LastScores;
        if (scores.Count == 0) return "  en attente de la premiere evaluation";

        if (ShowScoreDetail) return string.Join("\n", scores.Select(score => $"  {score.Describe()}"));

        var top = scores.Where(score => !score.IsBlocked).Take(3).Select(score => $"  {score}");
        return string.Join("\n", top);
    }

    /// <summary>
    /// Ce que l'agent sait — la ligne que la specification appelle « informations connues par
    /// l'agent ». Elle compte des croyances, pas des faits : deux survivants cote a cote affichent
    /// des nombres differents, et c'est le propos du livrable 2.
    /// </summary>
    private static string KnowledgeLines(SurvivorAgent agent)
    {
        var knowledge = agent.Survivor.Knowledge;
        var orders = agent.Survivor.Orders;

        string mission = agent.Survivor.Mission is { } order ? order.ToString() : "aucun";

        return $"""
              terrain {knowledge.ExploredRatio:P0} explore, {knowledge.KnownCells.Count} cellules
              {knowledge.KnownEntities.Count} entites, {knowledge.KnownItems.Count} objets, {knowledge.KnownNoises.Count} bruits
              ordre du joueur : {mission}
              consignes : {orders}
            """;
    }
}
