using Hordinary.Agents;

namespace Hordinary.AI.Utility;

/// <summary>
/// Le socle de considerations primitives que connait le code (specification, livrable 4).
///
/// Chacune est une fonction pure du contexte vers <c>[0, 1]</c>. Il n'y a volontairement pas de
/// <c>ComputeFleeScore()</c> ni de <c>ComputeHelpAllyScore()</c> : les objectifs de la specification
/// sont des compositions de ces entrees, decrites dans <c>config/utility_goals.json</c>.
/// </summary>
public static class UtilityInputs
{
    private const string DistancePrefix = "distance_to(";

    private static readonly Dictionary<string, Func<UtilityContext, float>> Readers =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Presence : donne a un objectif une valeur de base independante de la situation.
            ["constant"] = _ => 1f,

            // Danger et securite. « perceived_danger » est ce qu'elle voit a l'instant ;
            // « threat_pressure » y ajoute la panique pas encore retombee, et « personal_safety »
            // est son complement — le sentiment de securite, pas l'absence de zombie a l'ecran.
            ["perceived_danger"] = context => context.PerceivedDanger,
            ["threat_pressure"] = context => context.ThreatPressure,
            ["personal_safety"] = context => 1f - context.ThreatPressure,

            ["ally_danger"] = context => context.AllyDanger,
            ["heard_noise"] = context => context.LoudestNoise,

            // Ce que l'endroit a de mauvais souvenirs. « perceived_danger » dit ce qui menace
            // maintenant ; celle-ci dit qu'on a deja eu peur ici, meme si la rue est vide — et elle
            // s'efface toute seule (voir DangerMemory).
            ["remembered_danger"] = context => context.RememberedDanger,

            // « On me suit » : une menace proche dont le cap memorise pointe vers l'agent. Ce n'est
            // pas la meme information que le danger — on peut etre poursuivi de loin, ou cerne par
            // des zombies qui ne vous ont pas vu.
            ["pursued"] = context => context.PursuitPressure,

            // Etat du survivant.
            ["health_ratio"] = context => context.Self.HealthRatio,
            ["panic"] = context => context.Self.Panic,
            ["has_objective"] = context => context.Self.CarriesObjective ? 1f : 0f,

            // « J'ai de quoi soigner » : n'importe quel objet porte qui declare un effet de soin, pas
            // l'objet de la mission. Les deux se confondaient tant que toute mission ramenait une
            // trousse ; une mission qui va chercher un jerrican rend la distinction obligatoire.
            ["has_remedy"] = context => context.Self.Inventory.HasHealingItem ? 1f : 0f,

            // Groupe. « ally_wounded » et « ally_panicking » ne se voient pas : ils n'arrivent que
            // par la parole (livrable 7). Un survivant qui n'a parle a personne les lit a zero, et
            // ne part donc pas soigner un blesse qu'il aurait devine.
            ["has_ally"] = context => context.Allies.Count > 0 ? 1f : 0f,
            ["ally_wounded"] = context => 1f - context.AllyHealth,
            ["ally_panicking"] = context => context.AllyPanic,

            // Connaissances.
            ["objective_known"] = context => context.KnowsObjective ? 1f : 0f,

            // « J'y suis alle, il n'y avait rien. » Sans cette entree, un survivant qui a fouille le
            // lieu de la mission et n'y a rien trouve rescore la recherche a chaque decision et repart
            // vers une caisse imaginaire. Une fouille infructueuse doit pouvoir fermer un objectif.
            ["objective_zone_searched"] = context => context.SearchedObjectiveZoneInVain ? 1f : 0f,

            // « Je suis deja sorti » : sans elle, un survivant plante sur la case d'extraction se
            // redonne indefiniment un plan d'une action qui s'acheve aussitot.
            ["at_extraction"] = context => context.Locate("extraction") is { } exit
                && context.Self.Position.ChebyshevDistanceTo(exit) <= 1
                    ? 1f
                    : 0f,
            ["unexplored_ratio"] = context => 1f - context.Self.Knowledge.ExploredRatio,

            // « Il reste quelque part ou aller voir. » Ce n'est pas la meme question que
            // « reste-t-il de l'inexplore » : une carte aux trois quarts inconnue peut n'offrir
            // aucune frontiere atteignable — tout ce qui restait est derriere une porte verrouillee,
            // ou au-dela d'une cour fermee. Sans cette entree, l'objectif d'exploration se maintient
            // en tete des scores et le planificateur ne peut rien en faire : le survivant reste
            // plante, l'objectif affiche.
            ["frontier_known"] = context => context.BestFrontier is not null ? 1f : 0f,

            // Consignes du joueur et etat du plan.
            ["player_priority"] = context => context.Self.Mission?.Priority ?? 0f,
            ["plan_confidence"] = context => context.Self.PlanConfidence,

            // « L'ordre est rempli » : sans cette entree, un survivant arrive a destination
            // continuerait a scorer haut un objectif qu'il a deja atteint, et le planificateur lui
            // rendrait indefiniment un plan de zero action au lieu de le laisser passer a la suite.
            ["mission_reached"] = context => context.Self.Mission?.Destination is { } destination
                && context.Self.Position.ChebyshevDistanceTo(destination) <= 1
                    ? 1f
                    : 0f,
        };

    /// <summary>
    /// Entrees scalaires connues, hors l'entree parametrable <c>distance_to(cible)</c>.
    /// </summary>
    public static IReadOnlyCollection<string> Names => Readers.Keys;

    public static bool IsKnown(string input)
        => Readers.ContainsKey(input)
        || (TryParseArgument(input, DistancePrefix, out var target) && IsKnownTarget(target));

    /// <summary>Lit une consideration primitive. Le resultat est toujours dans <c>[0, 1]</c>.</summary>
    public static float Read(string input, UtilityContext context)
    {
        if (Readers.TryGetValue(input, out var read)) return Math.Clamp(read(context), 0f, 1f);

        if (TryParseArgument(input, DistancePrefix, out var target)) return context.NormalizedDistanceTo(target);

        throw new InvalidOperationException($"Consideration inconnue « {input} ». {Vocabulary()}");
    }

    /// <summary>Message d'aide liste dans les erreurs de configuration.</summary>
    public static string Vocabulary()
        => $"Entrees connues : {string.Join(", ", Names.Order())}, "
         + $"distance_to(cible) avec cible parmi {string.Join(", ", UtilityContext.TargetNames.Order())}.";

    /// <summary>
    /// Decoupe une entree parametrable, <c>prefixe(argument)</c>. Le vocabulaire des cibles est
    /// ferme et verifie : une cible inconnue est une erreur de configuration, pas un zero silencieux.
    /// </summary>
    private static bool TryParseArgument(string input, string prefix, out string argument)
    {
        argument = string.Empty;

        if (!input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        if (!input.EndsWith(')')) return false;

        argument = input[prefix.Length..^1].Trim();
        return argument.Length > 0;
    }

    private static bool IsKnownTarget(string target) => UtilityContext.TargetNames.Contains(target);
}
