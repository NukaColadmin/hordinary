namespace Hordinary.AI.Utility;

/// <summary>
/// Reglages de la Utility AI (specification section 11.4 et annexe B), charges depuis
/// <c>config/game_config.json</c>, section <c>utility_ai</c>. Les deux premieres valeurs sont celles
/// du format impose ; les suivantes normalisent les considerations primitives.
/// </summary>
public sealed class UtilityAiSettings
{
    // --- Cadence et stabilite des decisions (section 11.4) --------------------------------

    /// <summary>Periode entre deux reevaluations, en secondes (1 a 2 fois par seconde).</summary>
    public float EvaluationInterval { get; init; } = 0.75f;

    /// <summary>Hysteresis : marge dont un objectif doit depasser l'objectif courant pour le remplacer.</summary>
    public float SwitchThreshold { get; init; } = 0.15f;

    /// <summary>Seuil minimum : en dessous, un objectif ne vaut pas la peine d'etre poursuivi.</summary>
    public float MinimumScore { get; init; } = 0.05f;

    // --- Normalisation des considerations --------------------------------------------------

    /// <summary>Distance, en cellules, au-dela de laquelle une menace ne pese plus sur une cellule.</summary>
    public float DangerRadius { get; init; } = 8f;

    /// <summary>Distance, en cellules, qui vaut 1 pour <c>distance_to(...)</c>. Au-dela, « c'est loin ».</summary>
    public float DistanceScale { get; init; } = 12f;

    /// <summary>
    /// Poids d'un bruit dans le danger percu, face a un zombie reellement vu. Entendre n'est pas voir :
    /// un fracas inquiete, il ne prouve pas qu'une horde arrive.
    /// </summary>
    public float NoiseDangerWeight { get; init; } = 0.5f;

    /// <summary>Intensite percue a partir de laquelle un bruit compte comme fort (bareme section 18.1).</summary>
    public float LoudNoiseIntensity { get; init; } = 6f;

    /// <summary>Cout, en distance, d'un pas de fuite : au-dela, mieux vaut une cachette plus proche.</summary>
    public float FleeTravelPenalty { get; init; } = 0.5f;

    /// <summary>Priorite attribuee par defaut a un ordre du joueur (entree <c>player_priority</c>).</summary>
    public float PlayerOrderPriority { get; init; } = 0.7f;

    /// <summary>Les reperes et les faits de l'entraide, sous-section <c>utility_ai.cooperation</c>.</summary>
    public CooperationSettings Cooperation { get; init; } = new();

    public static UtilityAiSettings Default { get; } = new();
}

/// <summary>
/// Ce que « blesse », « a portee de jet » et « diversion en cours » veulent dire, en chiffres.
///
/// <para>
/// Ces trois valeurs vivent sous <c>utility_ai</c> et non dans une section a part parce qu'elles
/// servent a <b>resoudre des cibles</b> — <c>patient</c>, <c>decoy</c> — exactement comme
/// <see cref="DangerRadius"/> sert a resoudre <c>away_from_danger</c>. Le planificateur les relit
/// ensuite par le contexte, sans qu'une seconde source de verite puisse diverger.
/// </para>
///
/// <para>
/// Aucune ne declenche rien. Les trois gestes qu'elles parametrent — soigner un camarade, lancer un
/// petard, emprunter une arme — n'ont pas de demandeur dans les objectifs d'utilite : c'est une
/// consigne du joueur qui les rend necessaires (livrable 15).
/// </para>
/// </summary>
public sealed class CooperationSettings
{
    /// <summary>
    /// Sous ce rapport de sante, un survivant compte comme blesse : c'est le repere <c>patient</c>, et
    /// ce qui rend <c>band_tended</c> faux. Au-dessus, il se debrouille seul.
    /// </summary>
    public float WoundedRatio { get; init; } = 0.5f;

    /// <summary>
    /// Distance, en cases, a laquelle on lance un petard. Le repere <c>decoy</c> est choisi dans ce
    /// rayon : une diversion ne vaut que si on peut la poser loin de soi.
    /// </summary>
    public int ThrowRange { get; init; } = 14;

    /// <summary>
    /// Distance, en cases, a laquelle un zombie doit pouvoir entendre le petard pour qu'il serve. Un
    /// bruit se percoit a son intensite moins la distance (section 18.2) : un petard de 10 s'entend a
    /// neuf cases en terrain degage, et on garde une marge.
    /// </summary>
    public float LureReach { get; init; } = 8f;

    /// <summary>
    /// Pendant combien de secondes une diversion entendue compte comme « en cours ». Au-dela, la
    /// horde a eu le temps de revenir, et une consigne qui l'exige en demande une nouvelle.
    /// </summary>
    public float DiversionMemory { get; init; } = 25f;

    /// <summary>
    /// Poids d'un ordre donne par un camarade (action <c>OrderAlly</c>), entree <c>player_priority</c>
    /// du survivant qui le recoit. Au-dessus d'un ordre du joueur, qu'il met de cote le temps d'etre
    /// rempli ; au-dessous du rappel du van (<c>mission.recall_priority</c>), qu'il ne supplante
    /// jamais. Ce n'est pas lui qui fait passer l'ordre devant les objectifs propres — c'est la
    /// precondition <c>obeying</c> — mais lui qui le fait ceder devant le danger.
    /// </summary>
    public float OrderPriority { get; init; } = 0.9f;
}
