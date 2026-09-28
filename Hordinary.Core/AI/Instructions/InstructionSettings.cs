namespace Hordinary.AI.Instructions;

/// <summary>
/// Section <c>instructions</c> de <c>config/game_config.json</c> (livrable 15).
///
/// <para>
/// Livree a <c>enabled: false</c> : le jeu est integralement jouable sans service de langage, par les
/// instructions pre-ecrites de <c>config/instruction_presets.json</c>. Ce qui est desactive ici, c'est
/// la <b>traduction</b> d'une phrase libre en contrainte, pas les contraintes elles-memes — les quatre
/// portillons et le compilateur tournent exactement pareil dans les deux cas.
/// </para>
///
/// <para>
/// Un record, pour que <see cref="LocallyEnabled"/> puisse le recopier avec un seul champ change.
/// </para>
/// </summary>
public sealed record InstructionSettings
{
    /// <summary>Appel a un modele de langage pour traduire une phrase libre. Les contraintes fonctionnent sans.</summary>
    public bool Enabled { get; init; }

    /// <summary>L'argument de ligne de commande qui allume le service pour cette session : <c>-- --ia</c>.</summary>
    public const string Argument = "--ia";

    /// <summary>La variable d'environnement qui fait de meme, pour un jeu lance depuis l'editeur.</summary>
    public const string EnvironmentVariable = "HORDINARY_IA";

    /// <summary>
    /// Le service allume pour cette session si l'argument <see cref="Argument"/> est passe ou si
    /// <see cref="EnvironmentVariable"/> vaut <c>1</c>, <c>on</c> ou <c>true</c> ; sinon, tel quel.
    ///
    /// <para>
    /// Pourquoi un reglage de session et non le fichier. Le fichier livre doit dire <c>false</c> — deux
    /// tests l'exigent, parce que le livrable promet un jeu jouable sans service —, et le seul moyen
    /// d'essayer la traduction etait de le modifier, donc de passer la suite au rouge. Ecrire
    /// <c>true</c> dans un fichier versionne pour un essai local, c'est aussi le livrer au prochain
    /// commit. Le sens n'est qu'unique : rien ici n'eteint un service que le fichier allume.
    /// </para>
    /// </summary>
    public InstructionSettings LocallyEnabled(IEnumerable<string> arguments, string? environment)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        bool asked = arguments.Contains(Argument, StringComparer.Ordinal)
                     || environment?.Trim().ToLowerInvariant() is "1" or "on" or "true";

        return asked && !Enabled ? this with { Enabled = true } : this;
    }

    /// <summary>Longueur maximale d'une phrase de joueur, coupee au-dela.</summary>
    public int MaxChars { get; init; } = 200;

    /// <summary>
    /// Nombre de contraintes actives simultanement. Chacune reecrit le domaine ; au-dela d'une
    /// poignee, plus personne — ni le joueur, ni la recherche du coupable minimal — ne sait ce que le
    /// domaine autorise encore.
    /// </summary>
    public int MaxActiveConstraints { get; init; } = 5;

    /// <summary>Bornes du multiplicateur de <c>penalize</c>. En dehors, le portillon 1 refuse.</summary>
    public float MinPenalty { get; init; } = 0.1f;

    public float MaxPenalty { get; init; } = 10f;

    /// <summary>Les instructions pre-ecrites restent proposees quand le service est coupe.</summary>
    public bool PresetsWhenDisabled { get; init; } = true;

    /// <summary>Le fournisseur de modeles. <c>openrouter</c> est le seul prevu.</summary>
    public string Provider { get; init; } = "openrouter";

    /// <summary>L'adresse du service FastAPI local, qui parle au fournisseur a notre place.</summary>
    public string Endpoint { get; init; } = "http://127.0.0.1:8001/instruction";

    /// <summary>
    /// Huit secondes, contre huit cents millisecondes pour un dialogue.
    ///
    /// <para>
    /// Ce n'est pas la meme architecture avec un autre chiffre, c'est le meme budget applique a une
    /// autre situation : compiler une instruction n'est pas dans la boucle de jeu. Le joueur a tape
    /// une phrase et attend une reponse, il regarde l'ecran. Une replique de bavardage en retard
    /// arrive apres la scene qu'elle commentait ; une traduction en retard n'arrive nulle part, elle
    /// arrive.
    /// </para>
    /// </summary>
    public int TimeoutMs { get; init; } = 8000;

    /// <summary>Une reprise, puis les instructions pre-ecrites. Insister plus longtemps ne traduit rien.</summary>
    public int Retries { get; init; } = 1;

    /// <summary>
    /// Le cache <c>(phrase normalisee, graine, empreinte du domaine)</c>.
    ///
    /// <para>
    /// C'est l'une des deux choses qui rendent une partie reproductible malgre un fournisseur distant
    /// non deterministe — l'autre etant qu'une contrainte acceptee est figee dans la mission. Le
    /// determinisme du jeu ne vient jamais du modele.
    /// </para>
    /// </summary>
    public bool CacheEnabled { get; init; } = true;

    public OpenRouterSettings OpenRouter { get; init; } = new();
}

/// <summary>
/// Le fournisseur de modeles, sous-section <c>instructions.openrouter</c>.
///
/// <para>
/// L'inference locale est retiree de la conception : la machine cible ne dispose pas de la VRAM
/// necessaire pour l'executer de facon fiable. OpenRouter donne acces a plusieurs familles de modeles
/// derriere une seule API, ce qui est exactement ce que demande le tableau d'evaluation du livrable —
/// comparer des modeles, pas en heberger un.
/// </para>
/// </summary>
public sealed class OpenRouterSettings
{
    public string Model { get; init; } = "deepseek/deepseek-v4.1-flash";

    /// <summary>
    /// Le <b>nom de la variable d'environnement</b> qui porte la cle, jamais la cle. Un fichier de
    /// configuration est versionne ; une cle qui y entre est publiee.
    /// </summary>
    public string ApiKeyEnv { get; init; } = "OPENROUTER_API_KEY";

    public string BaseUrl { get; init; } = "https://openrouter.ai/api/v1";

    /// <summary>
    /// Plafond dur de depense, en dollars, pour une session de jeu. Depasse, les requetes sont
    /// refusees et le client bascule sur les instructions pre-ecrites <b>en le signalant</b> : un
    /// repli silencieux ferait croire au joueur que le modele a compris sa phrase.
    /// </summary>
    public double MaxCostUsdPerSession { get; init; } = 0.50d;
}
