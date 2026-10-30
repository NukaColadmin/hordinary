using Godot;
using Hordinary.AI.Zombies;
using Hordinary.Agents;
using Hordinary.Commands;
using Hordinary.Game;
using Hordinary.World;

// Godot expose un noeud 3D nomme GridMap : on desambigue en faveur de notre grille de jeu.
using GridMap = Hordinary.World.GridMap;

namespace Hordinary.UI;

/// <summary>
/// Le HUD (specification section A : <c>UI/HUD</c>) : l'etat de la partie, pas celui d'un survivant.
///
/// La repartition avec <see cref="AgentPanel"/> tient en une phrase : ce panneau-ci montre ce que le
/// <b>joueur</b> sait — l'heure, le groupe, la horde, l'issue de la partie, les ordres qu'il a
/// donnes —, l'autre montre ce qu'un <b>survivant</b> sait. Le joueur voit la horde entiere ; Alice
/// ne voit que ce qu'elle a croise. Melanger les deux ferait mentir tout le livrable 2.
/// </summary>
public partial class Hud : InfoPanel
{
    public Hud() : base("Hud") { }

    /// <summary>Deplie les intentions connues et les frontieres candidates (touche F7).</summary>
    public bool ShowBandDetail { get; set; }

    /// <summary>En demonstration, le chrono et la consigne gardent la carte lisible.</summary>
    public bool Compact { get; set; }

    /// <summary>
    /// Ce que la victoire a rapporte (livrable 16), une fois le verdict rendu. Le jeu n'a pas d'ecran
    /// de resultat separe : le verdict s'affiche la ou il s'est toujours affiche, et ce qui a ete
    /// verse s'affiche avec lui. Un ecran de plus pour trois lignes ferait une transition a
    /// traverser, pas une information de plus.
    /// </summary>
    public IReadOnlyList<MissionReward> Awarded { get; set; } = [];

    public void Refresh(
        GridMap map,
        WorldState world,
        SurvivorBand band,
        CommandManager commands,
        MissionTracker mission,
        IReadOnlyList<Zombie> horde,
        bool running)
    {
        if (Compact && !ShowBandDetail)
        {
            Write($"Hordinary — {world.Time:0.0} s\n{InstructionLine(band)}\n"
                + (mission.IsOver ? OutcomeLine(mission) : string.Empty));
            return;
        }

        Write($"""
            Hordinary — {mission.Mission.Name}
            Carte {map.Width} x {map.Height}, origine {map.Origin} — t = {world.Time:0.0} s{ClockLine(mission, world.Time)}
            {OutcomeLine(mission)}
            {BandLine(band, commands)}
            {InstructionLine(band)}
            {ObjectiveLine(mission)}
            {HordeLine(horde)}
            {ExplorationLine(commands.Selected)}
            {CommandLine(commands)}
            {Controls()}
            """);
    }

    /// <summary>
    /// Le temps qui reste avant que le van ne parte. C'est le seul chiffre du HUD sur lequel le
    /// joueur a une decision a prendre — aller voir un batiment de plus, ou rentrer —, alors il tient
    /// sur la ligne de l'horloge et pas ailleurs. Une mission sans limite n'affiche rien : un
    /// compteur immobile ferait croire a un chronometre en panne.
    /// </summary>
    private static string ClockLine(MissionTracker mission, double now)
    {
        if (mission.TimeLeft(now) is not { } left) return string.Empty;
        if (mission.Recalled) return " — temps ecoule, retour au van";

        return $" — depart du van dans {(int)(left / 60)}:{left % 60:00}";
    }

    /// <summary>
    /// L'issue de la partie. Tant qu'elle court, la ligne rappelle ce qu'on attend du joueur : le
    /// joueur ne devrait jamais avoir a ouvrir la specification pour savoir ce qu'il fait la — y
    /// compris qu'on ne laisse personne derriere, puisque c'est le groupe au complet qui conclut.
    /// </summary>
    private string OutcomeLine(MissionTracker mission)
    {
        if (mission.IsOver)
        {
            string spoils = Awarded.Count == 0
                ? mission.Mission.Rewards.Count > 0 && mission.Outcome != MissionOutcome.Victory
                    ? "\nAucune recompense : la mission n'a pas ete remplie."
                    : string.Empty
                : $"\nRecompenses versees au camp : {string.Join(", ", Awarded)}";

            return $"*** {mission.Verdict} ***{spoils}";
        }

        int home = mission.Home.Count();
        string tally = home == 0 ? string.Empty : $" (rentres : {home})";

        // Une mission generee (livrable 16) annonce son type et sa zone plutot que l'objet unique
        // du MVP : « rapporter medikit » ne dit rien d'une mission d'elimination.
        if (mission.Objectives is { } objectives)
        {
            string where = mission.Mission.ObjectiveZone ?? mission.Mission.Objective.ToString();

            return $"Mission : {MissionObjectiveNames.Of(objectives.Kind)} — {where}, "
                 + $"puis rentrer au complet{tally}";
        }

        return $"Mission : rapporter {mission.Mission.Item} au van, et y rentrer au complet{tally}";
    }

    /// <summary>
    /// Les instructions tactiques actives (livrable 15). Le joueur doit pouvoir relire ce qu'il a
    /// demande pendant qu'il regarde ce que ca donne : une contrainte n'est visible nulle part
    /// ailleurs sur la carte — elle n'ajoute aucun ordre, elle retire des plans — et un comportement
    /// qu'on ne sait plus avoir demande passe pour un defaut.
    ///
    /// Sans instruction, la ligne n'existe pas : annoncer « aucune consigne » a chaque frame d'une
    /// partie qui n'en utilise pas ferait du livrable un bruit permanent.
    /// </summary>
    private static string InstructionLine(SurvivorBand band)
    {
        if (band.Instructions is not { Active.Count: > 0 } instructions) return string.Empty;

        var lines = instructions.Active.Select(constraint => $"  • {constraint.Utterance ?? constraint.Text}");

        return $"Consignes ({instructions.Active.Count}) :\n{string.Join("\n", lines)}";
    }

    private static string BandLine(SurvivorBand band, CommandManager commands)
    {
        var selected = commands.Selected;

        var parts = band.Agents.Select(agent =>
        {
            var survivor = agent.Survivor;
            string mark = ReferenceEquals(agent, selected) ? ">" : " ";

            string state = survivor switch
            {
                { Alive: false } => "hors de combat",
                _ => $"{survivor.HealthRatio:P0}, {survivor.CurrentGoal ?? "sans objectif"}",
            };

            // « a portee » : ce que le survivant affiche sait, celui-ci peut l'apprendre a la
            // prochaine seconde. Hors portee, ils sont aussi etrangers que deux inconnus.
            string reach = selected is null
                || ReferenceEquals(agent, selected)
                || !band.Sharing.CanTalk(selected.Survivor, survivor)
                    ? string.Empty
                    : " (a portee)";

            return $"{mark}{survivor.Name} [{state}]{reach}";
        });

        string addressee = commands.GroupSelected ? "groupe" : selected?.Survivor.Name ?? "personne";

        return $"Groupe (ordres au {addressee}) : {string.Join("  ", parts)}";
    }

    /// <summary>
    /// Ce que la mission envoie chercher, et qui le porte. Le HUD ne nomme pas plus les medicaments
    /// que le reste du jeu : il affiche l'identifiant que la mission a choisi.
    /// </summary>
    private static string ObjectiveLine(MissionTracker mission)
    {
        if (mission.Objectives is { } objectives) return ProgressLine(mission, objectives);

        var carriers = mission.Carriers.ToList();
        string what = mission.Mission.Item;

        return carriers.Count == 0
            ? $"{what} : pas encore ramasse"
            : $"{what} : porte par {string.Join(", ", carriers.Select(survivor => survivor.Name))}";
    }

    /// <summary>
    /// La progression d'une mission generee : chaque exigence avec son compteur, les recompenses
    /// promises, et l'etat de livraison au camp.
    ///
    /// « Livre » ne veut pas dire « pose au camp » : les objets restent dans les sacs, et c'est le
    /// retour du groupe qui les rentre. La ligne dit donc ou en est le <b>retour</b>, qui est la
    /// seconde moitie de toute mission et celle que le joueur oublie.
    /// </summary>
    private static string ProgressLine(MissionTracker mission, MissionObjectiveTracker objectives)
    {
        string counters = string.Join("   ", objectives.Progress().Select(line => line.ToString()));

        string rewards = mission.Mission.Rewards.Count == 0
            ? string.Empty
            : $"   Recompenses : {string.Join(", ", mission.Mission.Rewards)}";

        string delivery = objectives.Fulfilled
            ? mission.Home.Any() ? "au camp" : "reste a rentrer au camp"
            : "objectif inacheve";

        return $"Objectif : {counters}   [{delivery}]{rewards}";
    }

    private static string HordeLine(IReadOnlyList<Zombie> horde)
    {
        int hunting = 0, curious = 0, searching = 0;

        foreach (var zombie in horde)
        {
            switch (zombie.State)
            {
                case ZombieState.Chase or ZombieState.Attack: hunting++; break;
                case ZombieState.InvestigateNoise: curious++; break;
                case ZombieState.SearchLastKnownPosition: searching++; break;
            }
        }

        return $"Horde : {horde.Count} zombies — {hunting} en chasse, {curious} sur un bruit, {searching} en fouille";
    }

    /// <summary>
    /// La frontiere retenue par le survivant affiche, et sous F7 le detail de sa note. C'est ce qui
    /// explique pourquoi celui-ci part au nord pendant qu'un autre part au sud (livrable 7).
    /// </summary>
    private string ExplorationLine(SurvivorAgent? agent)
    {
        if (agent?.Context is not { } context) return "Exploration : en attente d'une perception";
        if (context.BestFrontier is not { } frontier) return "Exploration : plus rien a decouvrir d'accessible";

        if (!ShowBandDetail) return $"Exploration : {frontier}";

        var ranked = context.RankFrontiers().Take(5).Select(candidate => $"  {candidate}");
        return $"Exploration : {frontier}\n{string.Join("\n", ranked)}";
    }

    private static string CommandLine(CommandManager commands)
    {
        if (commands.History.Count == 0) return "Dernier ordre : aucun";

        var recent = commands.History.TakeLast(3).Select(result => $"  {result}");
        return $"Derniers ordres :\n{string.Join("\n", recent)}";
    }

    private static string Controls() => """

        Tab / clic / fiche : changer de point de vue    Maj + ordre : au groupe entier
        Clic gauche : donner un objectif                Clic droit : cycler le danger
        1 pharmacie  2 retour au camp  4 explorer  5 regrouper
        B : interdire / autoriser le bruit              Z : interdire / autoriser la zone visee
        R : marcher / courir     Espace : distraction     C : effacer le danger
        F1 chemin A*  F2 carte mentale  F3 bruits  F4 scores  F5 plan  F6 journal  F7 groupe
        """;
}
