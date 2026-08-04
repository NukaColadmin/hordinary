using System.Text.Json;
using Hordinary.Config;
using Hordinary.World;

namespace Hordinary.Agents;

/// <summary>
/// Ce qu'un survivant sait faire mieux — ou moins bien — que les autres, exprime sur une action du
/// planificateur (specification, livrable 7 : « actions speciales » d'Alice, Bob et Claire).
///
/// Il n'existe volontairement aucune classe <c>ForceDoorSkill</c> ni aucun <c>if (survivor is Bob)</c> :
/// une competence est une ponderation des trois grandeurs qu'une action porte deja — son cout, son
/// bruit, sa duree — plus le droit de l'interdire. « Bob force les portes » est donc la meme phrase
/// que « ForceDoor coute deux fois moins cher a Bob », et elle s'ecrit dans
/// <c>config/survivors.json</c>.
///
/// C'est ce qui fait que les trois profils se distinguent reellement dans les plans produits : GBFS
/// minimise le cout, et ce n'est pas le meme cout pour tout le monde.
/// </summary>
public sealed class SurvivorSkill
{
    /// <summary>Phrase courte a l'usage d'un lecteur humain. Comme ailleurs, c'est une vraie propriete JSON.</summary>
    public string? Description { get; init; }

    /// <summary>Multiplie le cout de l'action pour ce survivant. Sous 1, il la choisit plus volontiers.</summary>
    public float Cost { get; init; } = 1f;

    /// <summary>Multiplie le bruit produit. Alice force une porte aussi bruyamment ; elle marche moins fort.</summary>
    public float Noise { get; init; } = 1f;

    /// <summary>Multiplie la duree d'execution. C'est l'adresse, pas l'envie.</summary>
    public float Duration { get; init; } = 1f;

    /// <summary>A <c>false</c>, l'action sort du domaine de ce survivant : il ne peut pas la planifier.</summary>
    public bool Enabled { get; init; } = true;

    public static SurvivorSkill Neutral { get; } = new();

    public override string ToString()
        => $"cout x{Cost:0.##}, bruit x{Noise:0.##}, duree x{Duration:0.##}{(Enabled ? string.Empty : ", desactivee")}";
}

/// <summary>
/// Un des trois survivants de la specification (livrable 7) : ses caracteristiques, sa personnalite,
/// ses competences et ses aptitudes.
///
/// Le profil est une <b>donnee</b>. Alice l'eclaireuse, Bob le bricoleur et Claire la secouriste ne
/// sont pas trois sous-classes de <see cref="Survivor"/> — ce sont trois entrees de
/// <c>config/survivors.json</c>. Ajouter un quatrieme survivant, ou echanger les roles pour un test,
/// reste une modification de configuration.
///
/// Les valeurs absentes ne sont pas des zeros : elles signifient « comme tout le monde », et
/// <see cref="ApplyTo(SurvivorSettings)"/> retombe alors sur la section <c>survivor</c> de
/// <c>config/game_config.json</c>. Un profil ne redit donc que ce qui le distingue.
/// </summary>
public sealed class SurvivorProfile
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>Role affiche : eclaireuse, bricoleur, secouriste.</summary>
    public string? Role { get; init; }

    public string? Description { get; init; }

    /// <summary>Coefficients de personnalite (section 10.2), lus par la Utility AI et par A*.</summary>
    public Personality Personality { get; init; } = new();

    // --- Caracteristiques, en ecart a la ligne de base -----------------------------------------

    public float? MaxHealth { get; init; }

    /// <summary>Motivation maximale — la jauge <c>SANITY</c>. Tout le monde ne tient pas pareil.</summary>
    public float? MaxSanity { get; init; }

    public float? WalkSpeed { get; init; }
    public float? RunSpeed { get; init; }
    public float? SearchRadius { get; init; }
    public int? VisionRange { get; init; }
    public float? FieldOfViewDegrees { get; init; }

    /// <summary>Sante rendue par une boite de medicaments entre ses mains. Claire en tire davantage.</summary>
    public float? HealAmount { get; init; }

    /// <summary>Panique retiree quand ce survivant en rassure un autre.</summary>
    public float? CalmAmount { get; init; }

    /// <summary>
    /// Discretion generale : multiplie tout bruit que le survivant produit. Alice se deplace
    /// silencieusement (0,6) la ou Bob fait le bruit de tout le monde.
    /// </summary>
    public float NoiseMultiplier { get; init; } = 1f;

    // --- Savoir-faire ---------------------------------------------------------------------------

    /// <summary>
    /// Competences par nom d'action du domaine (<c>ForceDoor</c>, <c>HealAlly</c>, <c>Observe</c>…).
    /// Une action absente de ce dictionnaire garde le cout, le bruit et la duree du domaine commun.
    /// </summary>
    public Dictionary<string, SurvivorSkill> Skills { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Aptitudes lues par la Utility AI via la consideration <c>aptitude(nom)</c> : <c>exploration</c>,
    /// <c>support</c>, <c>breaching</c>… Ce vocabulaire est libre — il appartient aux deux fichiers de
    /// configuration qui doivent s'accorder, pas au code. Une aptitude non declaree vaut
    /// <see cref="NeutralAptitude"/>, c'est-a-dire « ni doue ni maladroit ».
    /// </summary>
    public Dictionary<string, float> Aptitudes { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Valeur d'une aptitude jamais declaree : la moyenne, qui ne favorise ni ne penalise.</summary>
    public const float NeutralAptitude = 0.5f;

    /// <summary>Profil sans caractere : celui d'un survivant cree sans fiche.</summary>
    public static SurvivorProfile Default { get; } = new() { Id = "survivant", Name = "Survivant" };

    public SurvivorSkill SkillFor(string action)
        => Skills.TryGetValue(action, out var skill) ? skill : SurvivorSkill.Neutral;

    public float AptitudeFor(string name)
        => Aptitudes.TryGetValue(name, out float value) ? Math.Clamp(value, 0f, 1f) : NeutralAptitude;

    /// <summary>Le bareme de la section 18.1, passe au filtre de la discretion du survivant.</summary>
    public float NoiseOf(float level) => level * Math.Max(0f, NoiseMultiplier);

    /// <summary>Etat physique du survivant : la ligne de base, corrigee de ce que le profil redit.</summary>
    public SurvivorSettings ApplyTo(SurvivorSettings baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);

        return new SurvivorSettings
        {
            MaxHealth = MaxHealth ?? baseline.MaxHealth,
            MaxSanity = MaxSanity ?? baseline.MaxSanity,
            SanityDrainPerSecond = baseline.SanityDrainPerSecond,
            SanityRecoveryPerSecond = baseline.SanityRecoveryPerSecond,
            SanityPerWound = baseline.SanityPerWound,
            WalkSpeed = WalkSpeed ?? baseline.WalkSpeed,
            RunSpeed = RunSpeed ?? baseline.RunSpeed,
            SearchRadius = SearchRadius ?? baseline.SearchRadius,
            HealAmount = HealAmount ?? baseline.HealAmount,
            CalmAmount = CalmAmount ?? baseline.CalmAmount,
            PanicRisePerSecond = baseline.PanicRisePerSecond,
            PanicDecayPerSecond = baseline.PanicDecayPerSecond,
            PanicPerWound = baseline.PanicPerWound,
            RearGlanceThreshold = baseline.RearGlanceThreshold,
            RearGlanceInterval = baseline.RearGlanceInterval,
            RearGlanceDuration = baseline.RearGlanceDuration,
            Personality = Personality,
        };
    }

    public PerceptionSettings ApplyTo(PerceptionSettings baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);

        return new PerceptionSettings
        {
            VisionRange = VisionRange ?? baseline.VisionRange,
            FieldOfViewDegrees = FieldOfViewDegrees ?? baseline.FieldOfViewDegrees,
            UpdateInterval = baseline.UpdateInterval,
        };
    }

    public override string ToString()
        => $"{Name}{(Role is { } role ? $" ({role})" : string.Empty)} — {Personality}";
}

/// <summary>
/// L'equipe du MVP, chargee depuis <c>config/survivors.json</c>.
///
/// Comme <c>UtilityGoalSet</c> pour les objectifs et <c>PlanningDomain</c> pour les actions, le
/// fichier est valide des le chargement : un profil sans identifiant, un doublon ou une competence
/// aux coefficients negatifs doivent se voir tout de suite, et non produire un survivant
/// silencieusement inerte.
/// </summary>
public sealed class SurvivorRoster
{
    public List<SurvivorProfile> Survivors { get; init; } = [];

    public static SurvivorRoster Empty { get; } = new();

    public SurvivorProfile? Find(string? id)
        => id is null
            ? null
            : Survivors.FirstOrDefault(profile => profile.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static SurvivorRoster FromJson(string json)
    {
        var roster = JsonSerializer.Deserialize<SurvivorRoster>(json, GameConfig.JsonOptions) ?? Empty;
        roster.Validate();
        return roster;
    }

    public void Validate()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in Survivors)
        {
            if (string.IsNullOrWhiteSpace(profile.Id))
                throw new InvalidOperationException("Un survivant sans identifiant a ete declare.");

            if (!seen.Add(profile.Id))
                throw new InvalidOperationException($"Le survivant « {profile.Id} » est declare deux fois.");

            if (string.IsNullOrWhiteSpace(profile.Name))
                throw new InvalidOperationException($"Le survivant « {profile.Id} » n'a pas de nom.");

            if (profile.NoiseMultiplier < 0f)
                throw new InvalidOperationException($"Le survivant « {profile.Id} » a une discretion negative.");

            foreach (var (action, skill) in profile.Skills)
            {
                if (skill.Cost >= 0f && skill.Noise >= 0f && skill.Duration >= 0f) continue;

                throw new InvalidOperationException(
                    $"La competence « {action} » de « {profile.Id} » a un cout, un bruit ou une duree negatif.");
            }

            foreach (var (aptitude, value) in profile.Aptitudes)
            {
                if (value is >= 0f and <= 1f) continue;

                throw new InvalidOperationException(
                    $"L'aptitude « {aptitude} » de « {profile.Id} » vaut {value} : elle doit rester entre 0 et 1.");
            }
        }
    }
}
