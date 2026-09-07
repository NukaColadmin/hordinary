using System.Numerics;
using Hordinary.Commands;
using Hordinary.World;

namespace Hordinary.Agents;

/// <summary>
/// Survivant du point de vue de la simulation (specification section A) : sa position, son regard,
/// sa carte mentale, son etat (sante, panique, inventaire) et l'objectif que lui a choisi la
/// Utility AI. Le plan et les actions arrivent aux livrables 5 et 6.
/// </summary>
public sealed class Survivor : IPerceivingAgent
{
    public string Id { get; }
    public string Name { get; }

    public GridPosition Position { get; set; }

    /// <summary>Direction du regard, utilisee comme axe du champ de vision.</summary>
    public Vector2 Facing { get; set; } = new(0f, 1f);

    /// <summary>Portee de vision, en cellules (specification section 9.2).</summary>
    public int VisionRange { get; set; }

    /// <summary>Ouverture du champ de vision, en degres.</summary>
    public float FieldOfViewDegrees { get; set; }

    public AgentKnowledge Knowledge { get; }

    public SurvivorSettings Settings { get; }

    /// <summary>
    /// Fiche du survivant (livrable 7) : son role, ses competences, sa discretion. C'est elle qui
    /// distingue Alice de Bob — il n'existe pas de sous-classe par survivant.
    /// </summary>
    public SurvivorProfile Profile { get; }

    /// <summary>Sante, de 0 a <see cref="SurvivorSettings.MaxHealth"/> (section 19.1).</summary>
    public float Health { get; private set; }

    /// <summary>Panique, de 0 a 1 (section 19.3). Elle monte pres du danger et retombe au calme.</summary>
    public float Panic { get; private set; }

    public bool Alive => Health > 0f;

    /// <summary>
    /// Sante maximale effective. La fiche donne la ligne de base ; la faim ou la soif la rabote
    /// (<c>camp.starvation_max_health_penalty</c>). Un survivant prive de ravitaillement ne se soigne
    /// donc plus jusqu'a la pleine forme, et c'est ce qui rend une disette couteuse au-dela du jour ou
    /// elle a lieu.
    /// </summary>
    public float MaxHealth => Math.Max(1f, Settings.MaxHealth - HealthPenalty);

    /// <summary>Plafond retire a la sante maximale, cumule par les journees de disette.</summary>
    public float HealthPenalty { get; private set; }

    /// <summary>
    /// Rabote la sante maximale et ramene la sante courante sous le nouveau plafond. C'est ce que la
    /// faim fait a un corps : elle ne blesse pas, elle enleve de la reserve.
    /// </summary>
    public void CapMaxHealth(float penalty)
    {
        if (penalty <= 0f) return;

        HealthPenalty += penalty;
        Health = Math.Min(Health, MaxHealth);
    }

    /// <summary>Rend ce que la faim avait pris, quand le camp remange a sa faim.</summary>
    public void RestoreMaxHealth()
        => HealthPenalty = 0f;

    public float HealthRatio => MaxHealth <= 0f ? 0f : Math.Clamp(Health / MaxHealth, 0f, 1f);

    /// <summary>
    /// Ce que le survivant porte, sous contrainte d'emplacements et de poids (livrable 11). Le sac
    /// ne connait aucun objet par son nom : il manipule des identifiants de <c>config/items.json</c>
    /// et de <c>config/weapons.json</c>, et ignore ce qu'ils font.
    /// </summary>
    public Inventory Inventory { get; }

    /// <summary>
    /// Ce qu'on l'a envoye chercher, par identifiant du catalogue. C'est la mission qui le fixe
    /// (<c>config/missions.json</c>) : le code ne nomme donc nulle part « les medicaments », il
    /// demande seulement au survivant s'il porte ce pour quoi il est parti.
    /// </summary>
    public string ObjectiveItem { get; set; } = DefaultObjectiveItem;

    /// <summary>
    /// Objectif de repli quand aucune mission n'a ete donnee — le cas d'un survivant fabrique en
    /// test. C'est la seule mention d'un identifiant d'objet dans le coeur, et elle ne decide de
    /// rien : toute partie reelle la remplace par celle de la mission.
    /// </summary>
    public const string DefaultObjectiveItem = "medikit";

    /// <summary>
    /// Ou on l'a envoye chercher : le lieu exact que la mission designe comme objectif. C'est le
    /// pendant de <see cref="ObjectiveItem"/> pour l'endroit, et c'est ce qui permet au domaine de
    /// planification de parler d'un <c>objective_zone</c> plutot que d'une pharmacie. Une mission qui
    /// envoie fouiller un entrepot devient alors la meme mission, sans une ligne de domaine en plus.
    ///
    /// Le <b>nom</b> compte autant que la categorie : la carte livree compte deux batiments, et une
    /// mission qui vise le hangar sud ne doit pas envoyer fouiller l'entrepot ouest.
    ///
    /// <c>null</c> pour un survivant fabrique en test, qui n'a ete envoye nulle part.
    /// </summary>
    public ObjectiveSite? ObjectiveZone { get; set; }

    /// <summary>Ordre courant du joueur, ou <c>null</c> si le survivant est livre a lui-meme.</summary>
    public MissionOrder? Mission { get; set; }

    /// <summary>
    /// Consignes permanentes du joueur (livrable 8) : bruit interdit, zones ou ne pas aller. A la
    /// difference de <see cref="Mission"/>, elles ne se negocient pas — elles amputent le domaine de
    /// planification et la carte de navigation avant que la decision ne soit prise.
    /// </summary>
    public StandingOrders Orders { get; } = new();

    /// <summary>
    /// Interrupteur du mode debug (livrable 8) : a <c>false</c>, la panique reste a zero et cesse
    /// d'influencer les scores. Cela permet d'observer la planification et la navigation seules,
    /// sans qu'un survivant lache sa mission a la premiere silhouette.
    ///
    /// Il vient de <c>debug.panic_enabled</c> dans <c>config/game_config.json</c>, applique au
    /// groupe entier au demarrage. Le desactiver fait retomber a zero la panique deja accumulee :
    /// neutraliser la peur ne veut rien dire si l'on garde celle d'avant.
    /// </summary>
    public bool PanicEnabled
    {
        get => _panicEnabled;
        set
        {
            _panicEnabled = value;
            if (!value) Panic = 0f;
        }
    }

    private bool _panicEnabled = true;

    /// <summary>Nom de l'objectif choisi par la Utility AI, pour l'affichage et le journal.</summary>
    public string? CurrentGoal { get; set; }

    /// <summary>
    /// Confiance dans le plan courant, entree <c>plan_confidence</c> des scores. Elle vaut 1 tant
    /// qu'aucun plan n'existe ; le planificateur du livrable 5 la fera varier.
    /// </summary>
    public float PlanConfidence { get; set; } = 1f;

    public Survivor(
        string id,
        string name,
        AgentKnowledge knowledge,
        PerceptionSettings? perception = null,
        SurvivorSettings? settings = null,
        SurvivorProfile? profile = null,
        ItemCatalogue? catalogue = null,
        InventorySettings? inventory = null)
    {
        var vision = perception ?? PerceptionSettings.Default;

        Id = id;
        Name = name;
        Knowledge = knowledge ?? throw new ArgumentNullException(nameof(knowledge));
        Settings = settings ?? SurvivorSettings.Default;
        Profile = profile ?? SurvivorProfile.Default;
        Health = Settings.MaxHealth;
        VisionRange = vision.VisionRange;
        FieldOfViewDegrees = vision.FieldOfViewDegrees;
        Inventory = new Inventory(catalogue ?? ItemCatalogue.Empty, inventory);
    }

    /// <summary>
    /// Cree un survivant depuis sa fiche : les caracteristiques du profil se posent sur la ligne de
    /// base de <c>config/game_config.json</c>, jamais l'inverse. C'est la seule facon de fabriquer
    /// Alice, Bob ou Claire — le code ne les nomme nulle part.
    /// </summary>
    public static Survivor FromProfile(
        SurvivorProfile profile,
        AgentKnowledge knowledge,
        PerceptionSettings? perception = null,
        SurvivorSettings? settings = null,
        ItemCatalogue? catalogue = null,
        InventorySettings? inventory = null)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return new Survivor(
            profile.Id,
            profile.Name,
            knowledge,
            perception ?? PerceptionSettings.Default,
            settings ?? SurvivorSettings.Default,
            profile,
            catalogue,
            inventory);
    }

    /// <summary>Oriente le regard vers une cellule. Une direction nulle laisse l'orientation intacte.</summary>
    public void LookAt(GridPosition target)
    {
        var direction = new Vector2(target.X - Position.X, target.Y - Position.Y);
        if (direction.LengthSquared() > 0f) Facing = Vector2.Normalize(direction);
    }

    // --- Sante et panique (section 19) ------------------------------------------------------

    /// <summary>
    /// Emis au moment ou le survivant tombe, une seule fois. C'est par la que son inventaire finit
    /// au sol : le survivant sait ce qu'il porte, pas ou il tombe — poser les objets sur la carte est
    /// l'affaire de qui connait le monde.
    /// </summary>
    public event Action<Survivor>? Died;

    /// <summary>Encaisse un coup. Etre blesse fait aussi monter la panique (section 19.3).</summary>
    public void TakeDamage(float amount)
    {
        if (amount <= 0f || !Alive) return;

        float lost = Math.Min(amount, Health);
        Health -= lost;

        if (!Alive) Died?.Invoke(this);

        if (!PanicEnabled) return;

        Panic = Math.Clamp(Panic + Settings.PanicPerWound * lost / Settings.MaxHealth, 0f, 1f);
    }

    public void Heal(float amount)
    {
        if (amount <= 0f || !Alive) return;
        Health = Math.Min(MaxHealth, Health + amount);
    }

    /// <summary>
    /// Faire redescendre la panique — l'action speciale de Claire (livrable 7). Elle ne soigne rien :
    /// elle rend au survivant la marge de decision que la peur lui prenait, ce qui suffit souvent a
    /// lui faire lacher la fuite pour reprendre la mission.
    ///
    /// L'apaisement ne tient pas contre les faits : <see cref="UpdatePanic"/> fera remonter la
    /// panique des la passe suivante si le danger, lui, est toujours la.
    /// </summary>
    public void Calm(float amount)
    {
        if (amount <= 0f || !Alive) return;

        Panic = Math.Clamp(Panic - amount, 0f, 1f);
    }

    /// <summary>Ramasser. Echoue si l'objet est inconnu du catalogue ou si le sac est plein.</summary>
    public bool AddToInventory(string itemId) => Inventory.Add(itemId);

    public bool RemoveFromInventory(string itemId) => Inventory.Remove(itemId);

    public bool Carries(string? itemId) => Inventory.Carries(itemId);

    /// <summary>Vrai quand il porte ce pour quoi il est parti.</summary>
    public bool CarriesObjective => Inventory.Carries(ObjectiveItem);

    /// <summary>
    /// Fait glisser la panique vers le danger percu (section 19.3). Elle monte vite pres des zombies
    /// et redescend lentement une fois au calme : c'est ce qui rend la fuite persistante quelques
    /// secondes apres la rencontre, plutot que d'osciller avec la perception.
    /// </summary>
    public void UpdatePanic(float perceivedDanger, float deltaSeconds)
    {
        if (deltaSeconds <= 0f) return;

        // Mode debug : la panique est neutralisee a la source plutot qu'ignoree par les scores.
        // Le HUD affiche donc bien 0, et non une valeur que personne ne lirait.
        if (!PanicEnabled)
        {
            Panic = 0f;
            return;
        }

        float target = Math.Clamp(perceivedDanger, 0f, 1f);
        float rate = target > Panic ? Settings.PanicRisePerSecond : Settings.PanicDecayPerSecond;
        float step = rate * deltaSeconds;

        Panic = target > Panic
            ? Math.Min(target, Panic + step)
            : Math.Max(target, Panic - step);
    }
}
