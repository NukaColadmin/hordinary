using System.Text.Json;
using System.Text.Json.Serialization;
using Hordinary.AI.Navigation;
using Hordinary.AI.Replanning;
using Hordinary.AI.Utility;
using Hordinary.AI.Zombies;
using Hordinary.Agents;
using Hordinary.World;

namespace Hordinary.Config;

/// <summary>
/// Configuration externe du jeu (specification section 32). Aucune de ces valeurs ne doit etre
/// dupliquee dans le code : elles proviennent de <c>config/game_config.json</c>.
/// Les sections non encore utilisees sont chargees des maintenant pour que le fichier reste unique.
///
/// C'est un <c>record</c> pour que <c>config with { Replanning = ... }</c> suffise a rejouer une
/// partie avec une seule cadence changee — sans recopier a la main une liste de sections qu'un
/// futur livrable allongera.
/// </summary>
public sealed record GameConfig
{
    public NavigationSettings Navigation { get; init; } = new();
    public PerceptionSettings Perception { get; init; } = new();
    public NoiseSettings Noise { get; init; } = new();
    public SurvivorSettings Survivor { get; init; } = new();
    public ZombieSettings Zombies { get; init; } = new();
    public UtilityAiSettings UtilityAi { get; init; } = new();
    public PlannerSettings Planner { get; init; } = new();
    public ReplanningSettings Replanning { get; init; } = new();
    public KnowledgeSettings Knowledge { get; init; } = new();
    public ExplorationSettings Exploration { get; init; } = new();
    public MissionSettings Mission { get; init; } = new();
    public DebugSettings Debug { get; init; } = new();

    public static GameConfig Default { get; } = new();

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static GameConfig FromJson(string json)
        => JsonSerializer.Deserialize<GameConfig>(json, JsonOptions) ?? Default;
}

/// <summary>
/// Deroulement d'une mission, section <c>mission</c> de <c>config/game_config.json</c> : ce qui se
/// passe une fois l'objectif rempli, et ce qui fait rentrer un survivant avant l'heure.
///
/// Une mission commence et finit au camp. Elle n'est donc jamais « gagnee puis terminee » d'un seul
/// coup : la caisse revient au camp, le groupe est rappele, et la partie ne s'arrete que lorsque
/// tout le monde est rentre. Les valeurs ci-dessous reglent cette seconde moitie.
/// </summary>
public sealed class MissionSettings
{
    /// <summary>
    /// Poids de l'ordre de retour dans les scores Utility AI. Volontairement plus eleve que
    /// <see cref="UtilityAiSettings.PlayerOrderPriority"/> : la mission est finie, plus rien
    /// au-dehors ne vaut la peine — mais ce reste un poids, pas une laisse. Un survivant poursuivi
    /// fuit d'abord et rentre apres.
    /// </summary>
    public float RecallPriority { get; init; } = 0.9f;

    /// <summary>
    /// Ecart, en cellules, au-dela duquel le messager refait son trajet vers un allie qui a bouge.
    /// A zero, il recalculerait a chaque passe et n'avancerait jamais.
    /// </summary>
    public int RecallReroute { get; init; } = 3;

    /// <summary>
    /// Rayon d'arrivee, en cellules, quand aucune zone de camp n'est declaree. Avec une zone — le
    /// cas normal — c'est le rectangle du camp qui fait foi.
    /// </summary>
    public int CampRadius { get; init; } = 1;

    /// <summary>
    /// A <c>true</c>, un survivant dont la motivation tombe a zero abandonne la mission et rentre.
    /// C'est ce qui donne une consequence a la jauge <c>SANITY</c> ; a <c>false</c>, elle redevient
    /// un simple indicateur, ce qui est parfois ce qu'on veut pour observer autre chose.
    /// </summary>
    public bool AbandonAtZeroSanity { get; init; } = true;

    public static MissionSettings Default { get; } = new();
}

/// <summary>
/// Reglages du mode debug (specification, livrable 8), section <c>debug</c> de
/// <c>config/game_config.json</c>.
///
/// Ce sont les seuls reglages du fichier qui ne decrivent pas le jeu mais son observation : ils ne
/// changent pas l'equilibre, ils retirent une variable pour rendre les autres lisibles.
///
/// Ils se reglent ici et nulle part ailleurs. Une partie se deroule donc entierement dans le regime
/// que le fichier a fixe, et deux parties lancees sur la meme configuration sont comparables — ce
/// qu'une touche de bascule en cours de route rendrait impossible a garantir.
///
/// <see cref="Enabled"/> est l'interrupteur general : a <c>false</c>, il n'y a plus ni console, ni
/// touches de debug, ni texte d'inspection, quelles que soient les valeurs des autres lignes. Ce
/// n'est pas un raccourci d'ecriture mais la difference entre deux publics — on livre le jeu avec
/// <c>enabled: false</c> et on le developpe avec <c>true</c>, sans avoir a se souvenir de la liste
/// des reglages a eteindre un par un.
/// </summary>
public sealed class DebugSettings
{
    /// <summary>
    /// Interrupteur general du mode debug. A <c>false</c>, tout ce qui suit est neutralise : la
    /// console reste muette, les touches de debug ne repondent plus, les panneaux de texte
    /// disparaissent et les overlays restent eteints. Reste a l'ecran ce qu'un joueur doit voir —
    /// la carte, les agents, leurs jauges.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Journal dans la console de sortie : plans, replanifications, bruits, ordres, partages. C'est
    /// le plus verbeux des trois, et le premier qu'on eteint quand on veut simplement jouer.
    /// </summary>
    public bool ConsoleOutput { get; init; } = true;

    /// <summary>
    /// Touches de debug : bascules d'overlay (F1 a F7), danger pose a la main, distraction, effacement
    /// du danger. Les commandes de jeu — selection, ordres, marche et course — ne sont pas
    /// concernees : ce sont celles du joueur, pas celles de l'observateur.
    /// </summary>
    public bool Commands { get; init; } = true;

    /// <summary>
    /// Panneaux de texte (HUD, fiche d'agent, plan et journal). Sans eux l'ecran ne montre plus que
    /// la carte et la barre d'agents, qui est l'interface du joueur.
    /// </summary>
    public bool HudText { get; init; } = true;

    /// <summary>
    /// Saute l'ecran de preparation : la premiere mission du catalogue demarre aussitot avec toute
    /// l'equipe. C'est ce qui permet de relancer une partie en boucle en observant une seule chose,
    /// sans recliquer trois fois — et ce qui rend le jeu lancable sans interface, en integration.
    /// </summary>
    public bool SkipSetup { get; init; }

    /// <summary>
    /// A <c>false</c>, la panique et le stress sont neutralises des le demarrage : la panique reste
    /// a zero et cesse d'influencer les scores Utility AI, la fuite et le budget de decision.
    ///
    /// C'est l'interrupteur demande par le livrable 8. Il sert a observer la planification et la
    /// navigation seules : avec la panique, un survivant qui apercoit une silhouette lache sa
    /// mission, et l'on ne voit plus jamais un plan aller a son terme.
    /// </summary>
    public bool PanicEnabled { get; init; } = true;

    /// <summary>Overlays allumes au demarrage, pour ouvrir la partie directement en mode debug.</summary>
    public bool ShowNavigation { get; init; }

    public bool ShowMentalMap { get; init; }

    public bool ShowNoise { get; init; }

    // --- Ce qui est reellement allume ------------------------------------------------------------
    // Le reste du programme ne lit que ces quatre-la, jamais les proprietes brutes ci-dessus : c'est
    // le seul endroit ou l'interrupteur general s'applique, donc le seul endroit ou l'on peut
    // l'oublier.

    public bool ShowsConsole => Enabled && ConsoleOutput;

    public bool AcceptsCommands => Enabled && Commands;

    public bool ShowsText => Enabled && HudText;

    public bool SkipsSetup => Enabled && SkipSetup;

    public bool ShowsOverlay(bool wanted) => Enabled && wanted;

    public static DebugSettings Default { get; } = new();
}

/// <summary>
/// Limites et reglages du planificateur GBFS (specification, livrable 5 : « Limites de recherche »
/// et « Version enrichie » de l'heuristique). Toutes ces valeurs viennent de
/// <c>config/game_config.json</c>, section <c>planner</c>.
/// </summary>
public sealed class PlannerSettings
{
    /// <summary>Maximum de noeuds explores avant d'abandonner la recherche.</summary>
    public int MaxNodes { get; init; } = 1000;

    /// <summary>Profondeur maximale, en nombre d'actions du plan.</summary>
    public int MaxDepth { get; init; } = 20;

    /// <summary>Temps maximal accorde a une planification, en millisecondes.</summary>
    public int MaxTimeMs { get; init; } = 50;

    // --- Heuristique -------------------------------------------------------------------------
    // Mettre les trois derniers poids a zero ramene exactement a l'heuristique simple de la
    // specification : « heuristic = number_of_unsatisfied_goal_conditions ».

    /// <summary>Poids du nombre de conditions du but non satisfaites.</summary>
    public float UnsatisfiedWeight { get; init; } = 1f;

    /// <summary>Poids de la distance restante jusqu'aux lieux cites par le but.</summary>
    public float DistanceWeight { get; init; } = 0.6f;

    /// <summary>Poids du danger memorise sur le lieu ou l'agent se trouverait.</summary>
    public float DangerWeight { get; init; } = 1.5f;

    /// <summary>Poids du bruit deja produit par le debut du plan.</summary>
    public float NoiseWeight { get; init; } = 0.4f;

    /// <summary>Distance, en cellules, au-dela de laquelle le terme de distance est sature.</summary>
    public float DistanceScale { get; init; } = 12f;

    /// <summary>Bruit cumule, au bareme de la section 18.1, au-dela duquel le terme de bruit est sature.</summary>
    public float NoiseScale { get; init; } = 10f;

    // --- Construction de l'etat symbolique -----------------------------------------------------

    /// <summary>Danger percu a partir duquel un lieu recoit le fait <c>zombie_near</c>.</summary>
    public float DangerThreshold { get; init; } = 0.25f;

    /// <summary>
    /// Sante rapportee en dessous de laquelle un allie recoit le fait <c>ally_wounded</c> — ce qui
    /// donne enfin a <c>HealAlly</c> une precondition verifiable. Elle ne s'observe pas a distance :
    /// seul le partage de connaissances la transmet (livrable 7).
    /// </summary>
    public float AllyWoundedThreshold { get; init; } = 0.6f;

    /// <summary>Panique rapportee au-dela de laquelle un allie recoit le fait <c>ally_panicking</c>.</summary>
    public float AllyPanicThreshold { get; init; } = 0.5f;

    /// <summary>Distance, en cellules, en deca de laquelle une porte connue est rattachee a un lieu.</summary>
    public int DoorLinkRadius { get; init; } = 4;

    /// <summary>
    /// Distance, en cellules, en deca de laquelle l'agent est considere « sur place » : c'est ce qui
    /// fait apparaitre <c>at(agent, pharmacy)</c> et ce qui clot une action de deplacement.
    /// </summary>
    public int ArrivalRadius { get; init; } = 1;

    /// <summary>
    /// Objectif de repli quand aucun plan n'est trouve (specification : « l'agent choisit un objectif
    /// d'exploration »). Doit exister dans <c>config/planner_domain.json</c> ; vide desactive le repli.
    /// </summary>
    public string ExplorationFallbackGoal { get; init; } = "ExploreUnknownArea";
}

/// <summary>
/// Reglages de la frontier exploration (specification, livrable 7). Le score d'une frontiere y est
/// une soustraction de quatre termes, tous ramenes entre 0 et 1 :
///
/// <code>
/// score = gain d'information - cout de distance - danger percu - duplication entre allies
/// </code>
///
/// Les poids ci-dessous decident lequel des quatre l'emporte. Mettre <see cref="DuplicationWeight"/>
/// a zero ramene exactement au comportement du livrable 4 — les trois survivants visent alors la
/// meme frontiere, ce qui est le defaut que ce livrable corrige.
/// </summary>
public sealed class ExplorationSettings
{
    /// <summary>
    /// Rayon, en cellules, sur lequel on estime ce qu'une frontiere ferait decouvrir. C'est une
    /// mesure du potentiel, pas une promesse : un mur juste derriere la rendra decevante.
    /// </summary>
    public int InformationRadius { get; init; } = 4;

    /// <summary>Poids du gain d'information attendu — le seul terme positif du score.</summary>
    public float InformationWeight { get; init; } = 1f;

    /// <summary>Poids du prix du trajet. Trop fort, le survivant grignote le connu case par case.</summary>
    public float DistanceWeight { get; init; } = 0.5f;

    /// <summary>Poids du danger, percu et memorise, a l'arrivee.</summary>
    public float DangerWeight { get; init; } = 0.8f;

    /// <summary>Poids de la penalite quand un allie explore deja par la.</summary>
    public float DuplicationWeight { get; init; } = 0.9f;

    /// <summary>
    /// Distance, en cellules, en deca de laquelle deux explorations font double emploi. Au-dela, deux
    /// survivants qui avancent en parallele ne se genent pas.
    /// </summary>
    public float DuplicationRadius { get; init; } = 8f;

    /// <summary>
    /// Nombre de frontieres candidates reellement notees, les plus proches d'abord. Sans ce plafond,
    /// une carte a moitie decouverte en offre des centaines a chaque passe de decision.
    /// </summary>
    public int MaxCandidates { get; init; } = 24;

    public static ExplorationSettings Default { get; } = new();
}

/// <summary>
/// Fiabilite des connaissances (specification sections 9.4 et 9.5). Les deux premieres valeurs
/// sont celles du format impose ; les suivantes distinguent les autres informations dynamiques.
/// </summary>
public sealed class KnowledgeSettings
{
    /// <summary>Perte de confiance par seconde sur la position d'un zombie.</summary>
    public float ZombieConfidenceDecay { get; init; } = 0.08f;

    /// <summary>
    /// Portee du partage de connaissances entre allies, en cellules (livrable 7 : « a moins de cinq
    /// cellules les uns des autres »).
    /// </summary>
    public int CommunicationRange { get; init; } = 5;

    /// <summary>
    /// Delai entre deux echanges, en secondes. La specification recommande une fois par seconde
    /// (document A, « gestion des mises a jour ») : plus souvent ne ferait que recopier des
    /// croyances deja transmises, moins souvent laisserait deux survivants se croiser sans se parler.
    /// </summary>
    public float SharingInterval { get; init; } = 1f;

    /// <summary>
    /// Radio de groupe : le partage ignore alors la distance. La specification la propose en option
    /// pour simplifier le gameplay. A <c>false</c>, une information ne circule qu'a portee de voix —
    /// et un survivant isole reste reellement isole, ce qui est le comportement interessant.
    /// </summary>
    public bool RadioEnabled { get; init; }

    /// <summary>
    /// Duree, en secondes, pendant laquelle l'annonce d'un allie (« je vais voir au nord ») reste
    /// credible. Passe ce delai elle ne dit plus rien de ce qu'il fait, et cesse de peser sur la
    /// repartition de l'exploration.
    /// </summary>
    public float IntentMemory { get; init; } = 20f;

    /// <summary>Un allie previent de ses deplacements : sa position vieillit moins vite.</summary>
    public float AllyConfidenceDecay { get; init; } = 0.05f;

    /// <summary>Une porte peut etre ouverte ou refermee par quelqu'un d'autre.</summary>
    public float DoorStateConfidenceDecay { get; init; } = 0.02f;

    /// <summary>Un bruit ne dit rien de ce qui a bouge depuis : son souvenir se perime vite.</summary>
    public float NoiseConfidenceDecay { get; init; } = 0.1f;

    /// <summary>En dessous de ce seuil, une croyance n'est plus prise en compte par une decision.</summary>
    public float MinimumConfidence { get; init; } = 0.05f;

    /// <summary>Decote appliquee a une information rapportee par un allie plutot que vue.</summary>
    public float SharedConfidencePenalty { get; init; } = 0.2f;

    /// <summary>
    /// Duree pendant laquelle un cap observe prolonge une position vue. Passe ce delai, l'agent
    /// s'en tient au dernier endroit suppose au lieu de continuer a deviner.
    /// </summary>
    public float PredictionHorizon { get; init; } = 3f;

    /// <summary>
    /// Ecart maximal entre deux observations pour en deduire un cap. Deux apercus separes de dix
    /// secondes ne decrivent pas une trajectoire.
    /// </summary>
    public float HeadingMemory { get; init; } = 2f;

    /// <summary>Part du cap precedent conservee : une trajectoire se confirme, elle ne saute pas.</summary>
    public float HeadingSmoothing { get; init; } = 0.5f;

    /// <summary>Vitesse maximale creditee a une entite suivie, en cellules par seconde.</summary>
    public float MaximumTrackedSpeed { get; init; } = 3f;

    // --- Carte de chaleur des menaces (section 9.4 : « niveau de danger d'une zone ») -------------

    /// <summary>
    /// Portee, en pas sur les cases praticables, sur laquelle une menace apercue deborde autour
    /// d'elle. Le comptage se fait de proche en proche : un mur arrete la propagation.
    /// </summary>
    public int DangerSpreadRadius { get; init; } = 4;

    /// <summary>Menace perdue par seconde sur une case marquee. C'est l'oubli du danger.</summary>
    public float DangerDecayPerSecond { get; init; } = 0.08f;

    /// <summary>Poids accorde a un bruit : on ne sait pas ce qui l'a produit.</summary>
    public float DangerNoiseWeight { get; init; } = 0.5f;

    /// <summary>Menace memorisee a partir de laquelle une case devient « suspecte » pour A*.</summary>
    public float DangerSuspectedThreshold { get; init; } = 0.2f;

    /// <summary>Menace memorisee a partir de laquelle A* traite la case comme un zombie proche.</summary>
    public float DangerConfirmedThreshold { get; init; } = 0.6f;
}
