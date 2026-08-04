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
    private readonly List<ItemKind> _inventory = [];

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

    /// <summary>Coefficients de personnalite (section 10.2), lus par la Utility AI.</summary>
    public Personality Personality { get; set; }

    /// <summary>Sante, de 0 a <see cref="SurvivorSettings.MaxHealth"/> (section 19.1).</summary>
    public float Health { get; private set; }

    /// <summary>Panique, de 0 a 1 (section 19.3). Elle monte pres du danger et retombe au calme.</summary>
    public float Panic { get; private set; }

    /// <summary>
    /// Motivation, de 0 a <see cref="SurvivorSettings.MaxSanity"/> : la jauge <c>SANITY</c> de la
    /// fiche de personnage. La panique est une emotion de l'instant, qui retombe des que la rue est
    /// vide ; la motivation est ce qu'il en reste une fois la peur passee, et elle ne se refait pas
    /// dans la meme minute. C'est elle qui distingue un survivant frais d'un survivant qui revient
    /// de trois missions.
    ///
    /// A zero, le survivant n'a plus de raison de rester dehors : <see cref="Motivated"/> passe a
    /// faux, et <c>MissionRecall</c> le renvoie au camp.
    /// </summary>
    public float Sanity { get; private set; }

    public bool Alive => Health > 0f;

    /// <summary>Vrai tant qu'il reste de quoi tenir. A faux, le survivant abandonne la mission.</summary>
    public bool Motivated => Sanity > 0f;

    public float HealthRatio => Settings.MaxHealth <= 0f ? 0f : Math.Clamp(Health / Settings.MaxHealth, 0f, 1f);

    public float SanityRatio => Settings.MaxSanity <= 0f ? 0f : Math.Clamp(Sanity / Settings.MaxSanity, 0f, 1f);

    // --- Rappel et retour au camp ---------------------------------------------------------------

    /// <summary>
    /// Vrai quand quelqu'un lui a dit de rentrer — l'agent qui a fini la mission, ou sa propre
    /// motivation tombee a zero. Ce n'est pas un ordre de deplacement : c'est le fait d'avoir
    /// <b>recu le message</b>. Le trajet, lui, passe par <see cref="Mission"/> comme n'importe quel
    /// autre objectif, et reste donc negociable face a une horde.
    /// </summary>
    public bool Recalled { get; private set; }

    /// <summary>Ce qu'on lui a dit, mot pour mot. Le HUD et le journal l'affichent tel quel.</summary>
    public string? RecallMessage { get; private set; }

    /// <summary>
    /// Vrai une fois rentre au camp. Le survivant quitte alors le terrain : il ne percoit plus, ne
    /// decide plus, et ne recoit plus d'ordre. Il n'est pas mort pour autant — <see cref="Alive"/>
    /// reste vrai, et sa sante comme sa motivation sont celles avec lesquelles il repartira.
    /// </summary>
    public bool Returned { get; private set; }

    /// <summary>Date de jeu du retour, pour le debrief.</summary>
    public double ReturnedAt { get; private set; }

    /// <summary>Recoit le message de rappel. Vrai la premiere fois seulement.</summary>
    public bool Recall(string message)
    {
        if (Recalled || Returned) return false;

        Recalled = true;
        RecallMessage = message;
        return true;
    }

    /// <summary>Rentre au camp. Vrai la premiere fois seulement.</summary>
    public bool ComeHome(double now)
    {
        if (Returned) return false;

        Returned = true;
        ReturnedAt = now;
        Mission = null;
        return true;
    }

    /// <summary>Objets transportes. Un simple sac : l'inventaire avance est hors perimetre du MVP.</summary>
    public IReadOnlyList<ItemKind> Inventory => _inventory;

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
        SurvivorProfile? profile = null)
    {
        var vision = perception ?? PerceptionSettings.Default;

        Id = id;
        Name = name;
        Knowledge = knowledge ?? throw new ArgumentNullException(nameof(knowledge));
        Settings = settings ?? SurvivorSettings.Default;
        Profile = profile ?? SurvivorProfile.Default;
        Personality = Settings.Personality;
        Health = Settings.MaxHealth;
        Sanity = Settings.MaxSanity;
        VisionRange = vision.VisionRange;
        FieldOfViewDegrees = vision.FieldOfViewDegrees;
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
        SurvivorSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return new Survivor(
            profile.Id,
            profile.Name,
            knowledge,
            profile.ApplyTo(perception ?? PerceptionSettings.Default),
            profile.ApplyTo(settings ?? SurvivorSettings.Default),
            profile);
    }

    /// <summary>Oriente le regard vers une cellule. Une direction nulle laisse l'orientation intacte.</summary>
    public void LookAt(GridPosition target)
    {
        var direction = new Vector2(target.X - Position.X, target.Y - Position.Y);
        if (direction.LengthSquared() > 0f) Facing = Vector2.Normalize(direction);
    }

    // --- Sante et panique (section 19) ------------------------------------------------------

    /// <summary>Encaisse un coup. Etre blesse fait aussi monter la panique (section 19.3).</summary>
    public void TakeDamage(float amount)
    {
        if (amount <= 0f || !Alive) return;

        float lost = Math.Min(amount, Health);
        Health -= lost;

        // Encaisser un coup entame la motivation, meme en mode debug : c'est un fait, pas une
        // emotion. Ce que l'interrupteur neutralise, c'est la peur — pas les consequences.
        DrainSanity(Settings.MaxSanity * Settings.SanityPerWound * lost / Settings.MaxHealth);

        if (!PanicEnabled) return;

        Panic = Math.Clamp(Panic + Settings.PanicPerWound * lost / Settings.MaxHealth, 0f, 1f);
    }

    public void Heal(float amount)
    {
        if (amount <= 0f || !Alive) return;
        Health = Math.Min(Settings.MaxHealth, Health + amount);
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

        // Rassurer quelqu'un lui rend aussi l'envie de continuer, dans la meme proportion. C'est ce
        // qui fait de Claire autre chose qu'une infirmiere : elle est la raison pour laquelle le
        // groupe ne rentre pas a mi-chemin.
        RestoreSanity(amount * Settings.MaxSanity);
    }

    // --- Motivation -----------------------------------------------------------------------------

    public void DrainSanity(float amount)
    {
        if (amount <= 0f) return;
        Sanity = Math.Max(0f, Sanity - amount);
    }

    public void RestoreSanity(float amount)
    {
        if (amount <= 0f) return;
        Sanity = Math.Min(Settings.MaxSanity, Sanity + amount);
    }

    /// <summary>
    /// Fait glisser la motivation avec la peur du moment : elle se vide a proportion de la panique
    /// et se refait au calme, plus lentement qu'elle ne se vide.
    ///
    /// Elle depend de la panique et non du danger percu, et la nuance porte tout le sens de la
    /// jauge : voir un zombie de loin sans s'affoler ne coute rien. C'est d'avoir eu peur qui coute.
    /// </summary>
    public void UpdateSanity(float deltaSeconds)
    {
        if (deltaSeconds <= 0f || !Alive) return;

        float drain = Settings.SanityDrainPerSecond * Panic;
        float recovery = Settings.SanityRecoveryPerSecond * (1f - Panic);

        Sanity = Math.Clamp(Sanity + (recovery - drain) * deltaSeconds, 0f, Settings.MaxSanity);
    }

    public void AddToInventory(ItemKind kind) => _inventory.Add(kind);

    public bool RemoveFromInventory(ItemKind kind) => _inventory.Remove(kind);

    public bool Carries(ItemKind kind) => _inventory.Contains(kind);

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
