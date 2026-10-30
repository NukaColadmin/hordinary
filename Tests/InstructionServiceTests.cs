using Hordinary.AI.Instructions;
using Hordinary.AI.Planning;
using Hordinary.AI.Planning.Pddl;
using Hordinary.Config;

namespace Hordinary.Tests;

/// <summary>
/// Le service de langage du livrable 15, vu du cote deterministe : le digest qu'on lui envoie, et
/// tout ce que <see cref="InstructionBroker"/> decide autour de sa reponse.
///
/// <para>
/// <b>Aucun de ces tests n'ouvre une connexion.</b> Le transport est un delegue, et chaque test lui
/// donne la reponse qu'il veut eprouver — un service lent, un service muet, un service bavard, un
/// service qui invente un predicat. C'est la raison d'avoir mis ces decisions dans le coeur plutot
/// que dans le client HTTP : un repli sur les instructions pre-ecrites qui ne se declencherait qu'en
/// presence d'un vrai service en panne ne serait verifie par personne.
/// </para>
/// </summary>
public class InstructionServiceTests
{
    [Fact]
    public void Le_cache_change_quand_les_contraintes_actives_changent()
    {
        var digest = DomainDigest.From(Domain);
        var modified = digest with { ActiveConstraints = ["forbid(BreakWindow)"] };
        Assert.NotEqual(InstructionBroker.ContextHash(digest), InstructionBroker.ContextHash(modified));
    }

    [Fact]
    public void Le_cache_change_quand_le_glossaire_change()
    {
        var digest = DomainDigest.From(Domain);
        var glossary = digest.Glossary.ToDictionary(pair => pair.Key, pair => pair.Value);
        glossary["armed"] = "Sens modifie";
        Assert.NotEqual(InstructionBroker.ContextHash(digest), InstructionBroker.ContextHash(digest with { Glossary = glossary }));
        Assert.Equal(InstructionBroker.ContextHash(digest), InstructionBroker.ContextHash(digest with
        {
            Glossary = digest.Glossary.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value),
        }));
    }

    private static readonly PlanningDomain DeliveredDomain =
        PlanningDomain.FromJson(DemoMap.ReadConfig("planner_domain.json"));

    private static readonly GameConfig DeliveredConfig =
        GameConfig.FromJson(DemoMap.ReadConfig("game_config.json"));

    private static readonly PddlDomain Domain = PddlDomainBuilder.From(DeliveredDomain);

    /// <summary>La configuration livree, service allume : tout le reste vient du fichier.</summary>
    private static InstructionSettings Enabled(Action<Settings>? tweak = null)
    {
        var settings = new Settings(DeliveredConfig.Instructions);
        tweak?.Invoke(settings);
        return settings.Build();
    }

    /// <summary>Un montage lisible : on ne cite que ce qu'on change, le reste reste celui du fichier.</summary>
    private sealed class Settings(InstructionSettings source)
    {
        public int TimeoutMs { get; set; } = source.TimeoutMs;

        public int Retries { get; set; } = source.Retries;

        public bool CacheEnabled { get; set; } = source.CacheEnabled;

        public bool Active { get; set; } = true;

        public double MaxCost { get; set; } = source.OpenRouter.MaxCostUsdPerSession;

        public InstructionSettings Build() => new()
        {
            Enabled = Active,
            MaxChars = source.MaxChars,
            MaxActiveConstraints = source.MaxActiveConstraints,
            MinPenalty = source.MinPenalty,
            MaxPenalty = source.MaxPenalty,
            PresetsWhenDisabled = source.PresetsWhenDisabled,
            Provider = source.Provider,
            Endpoint = source.Endpoint,
            TimeoutMs = TimeoutMs,
            Retries = Retries,
            CacheEnabled = CacheEnabled,
            OpenRouter = new OpenRouterSettings
            {
                Model = source.OpenRouter.Model,
                ApiKeyEnv = source.OpenRouter.ApiKeyEnv,
                BaseUrl = source.OpenRouter.BaseUrl,
                MaxCostUsdPerSession = MaxCost,
            },
        };
    }

    private static InstructionReply Constraint(string form, string? predicate, string? action, float? factor = null, double cost = 0d)
        => new()
        {
            Status = InstructionReply.StatusConstraint,
            Constraint = new ServiceConstraint(form, predicate, action, factor),
            Model = "test/modele",
            CostUsd = cost,
        };

    // --- Le digest -------------------------------------------------------------------------------

    /// <summary>
    /// Le digest ne montre que ce que le domaine declare. C'est la propriete qui fait tenir tout le
    /// reste : le service et le portillon 1 lisent la meme source, donc un symbole vu dans le
    /// contexte ne peut pas etre un symbole refuse ensuite. Un digest redige a la main aurait pris du
    /// retard sur le domaine des le livrable suivant, et le joueur aurait vu des refus
    /// incomprehensibles.
    /// </summary>
    [Fact]
    public void Le_digest_ne_contient_que_des_symboles_declares()
    {
        var digest = DomainDigest.From(Domain);

        foreach (var predicate in digest.Predicates)
        {
            var declared = Domain.FindPredicate(predicate.Name);

            Assert.True(declared is not null, $"Le digest annonce le predicat « {predicate.Name} », absent du domaine.");
            Assert.Equal(declared!.Arity, predicate.Params.Count);
        }

        foreach (string action in digest.Actions)
            Assert.True(Domain.FindAction(action) is not null, $"Le digest annonce le schema « {action} », absent du domaine.");

        foreach (string kind in digest.LocationKinds)
            Assert.True(Domain.IsLocationKind(kind), $"Le digest annonce la categorie « {kind} », absente du domaine.");
    }

    /// <summary>
    /// Chaque symbole que le digest montre a sa definition dans <c>config/planner_domain.json</c>, et
    /// chaque definition a son symbole.
    ///
    /// <para>
    /// Les deux sens comptent. Un symbole sans definition revient a la situation qui a fait ecrire le
    /// glossaire : le modele voit <c>band_tended</c> et devine. Une definition sans symbole est un
    /// mot que le service croit pouvoir traduire et que le portillon 1 refusera — la meme derive que
    /// le digest existe pour empecher, par l'autre bout.
    /// </para>
    /// </summary>
    [Fact]
    public void Chaque_symbole_nommable_a_sa_definition()
    {
        var digest = DomainDigest.From(Domain);
        var names = DomainDigest.NamesOf(digest.Predicates, digest.Actions, digest.LocationKinds, digest.Objects).ToList();

        var undefined = names.Where(name => !digest.Glossary.TryGetValue(name, out string? meaning) || string.IsNullOrWhiteSpace(meaning)).ToList();
        Assert.True(undefined.Count == 0, $"Sans definition dans planner_domain.json (glossary) : {string.Join(", ", undefined)}.");

        var orphans = DeliveredDomain.Glossary.Keys.Except(names, StringComparer.Ordinal).ToList();
        Assert.True(orphans.Count == 0, $"Definitions sans symbole au domaine : {string.Join(", ", orphans)}.");
    }

    /// <summary>
    /// Tout objet que le digest montre est un objet que la grammaire accepte, et reciproquement pour
    /// l'exemple canonique du livrable.
    ///
    /// <para>
    /// C'est ce test qui a fait ajouter <c>objects</c> au contrat d'API. <c>group</c> n'est ni un
    /// type, ni une categorie de lieu : c'est un repere de <c>config/planner_domain.json</c>. Sans
    /// lui dans le digest, « regroupez-vous avant d'entrer dans un batiment » — la phrase que la
    /// specification donne en exemple du bout en bout — demandait au modele de deviner un symbole
    /// dont le portillon 1 exige pourtant l'exactitude.
    /// </para>
    /// </summary>
    [Fact]
    public void Le_digest_montre_les_objets_que_la_grammaire_exige()
    {
        var digest = DomainDigest.From(Domain);

        Assert.Contains(digest.Objects, entry => entry.Name == "group");
        Assert.Contains(digest.Objects, entry => entry.Name == "agent");

        var parsed = ConstraintGrammar.Parse("require_before(at(agent, group), enter:building)", Domain, Enabled());

        Assert.True(parsed.Accepted, parsed.Reason);
    }

    /// <summary>
    /// Le digest ne montre pas seulement quels symboles existent, mais de quel type ils sont — et ce
    /// type est celui que le portillon 1 exige ensuite.
    ///
    /// <para>
    /// Sans lui, <c>toolbox</c> et <c>agent</c> arrivaient au modele dans la meme liste plate, et
    /// rien dans le contexte ne disait que le premier s'equipe et pas le second. Montrer le
    /// vocabulaire sans son typage, c'est demander une exactitude qu'on n'a pas donne les moyens
    /// d'atteindre — le meme defaut que l'absence de <c>objects</c>, une couche plus bas.
    /// </para>
    /// </summary>
    [Fact]
    public void Le_digest_montre_le_type_de_chaque_objet()
    {
        var digest = DomainDigest.From(Domain);

        Assert.Equal(PlanningObjectTypes.Item, digest.Objects.Single(entry => entry.Name == "toolbox").Type);
        Assert.Equal(PlanningObjectTypes.Location, digest.Objects.Single(entry => entry.Name == "group").Type);

        // `agent` n'est type par aucune action : il reste au type racine. C'est exactement ce qui
        // rend `equipped(agent)` mal type, et c'est donc la valeur a verrouiller.
        Assert.Equal(PddlDomainBuilder.RootType, digest.Objects.Single(entry => entry.Name == "agent").Type);
    }

    /// <summary>
    /// Un argument du mauvais type est refuse, meme quand le symbole existe et que l'arite est bonne.
    ///
    /// <para>
    /// Le cas vient d'un vrai passage d'evaluation, pas d'une relecture :
    /// <c>nvidia/nemotron-3-super-120b-a12b</c> a produit
    /// <c>require_before(equipped(agent), AttackWithWeapon)</c>, et le portillon l'acceptait —
    /// <c>agent</c> est declare, <c>equipped</c> prend bien un argument. Il est de type <c>object</c>
    /// la ou <c>equipped</c> attend un <c>item</c>. Accepte, le fait partait en precondition dans le
    /// domaine donne a Fast Downward, qui l'aurait refuse pour type invalide.
    /// </para>
    /// </summary>
    [Fact]
    public void Un_argument_du_mauvais_type_est_refuse()
    {
        var mistyped = ConstraintGrammar.Parse("require_before(equipped(agent), AttackWithWeapon)", Domain, Enabled());

        Assert.False(mistyped.Accepted);
        Assert.Contains("item", mistyped.Reason);

        // Le meme fait, avec un objet du bon type, reste accepte : le controle ne ferme pas la forme.
        var typed = ConstraintGrammar.Parse("require_before(equipped(toolbox), BarricadeDoor)", Domain, Enabled());

        Assert.True(typed.Accepted, typed.Reason);
    }

    /// <summary>
    /// L'empreinte porte sur le domaine rendu, pas sur la projection : deux domaines differents ne
    /// peuvent pas partager une entree de cache. Sans cela, une contrainte traduite contre un
    /// vocabulaire disparu serait resservie telle quelle a la mission suivante.
    /// </summary>
    [Fact]
    public void L_empreinte_suit_le_domaine_et_pas_la_projection()
    {
        var reduced = Domain.With(actions: [.. Domain.Actions.Skip(1)]);

        Assert.NotEqual(DomainDigest.HashOf(Domain), DomainDigest.HashOf(reduced));
        Assert.Equal(DomainDigest.HashOf(Domain), DomainDigest.HashOf(Domain));
    }

    // --- Le courtier ------------------------------------------------------------------------------

    /// <summary>
    /// Service coupe : aucune requete, et le joueur l'apprend. C'est l'etat livre
    /// (<c>instructions.enabled: false</c>), donc le comportement par defaut du jeu — et
    /// <see cref="InstructionOutcome.Fallback"/> dit d'aller aux instructions pre-ecrites plutot que
    /// de laisser le panneau muet.
    /// </summary>
    [Fact]
    public async Task Service_coupe_aucune_requete_n_est_emise()
    {
        var broker = new InstructionBroker(
            Enabled(settings => settings.Active = false),
            (_, _) => throw new InvalidOperationException("Le courtier a appele le service alors qu'il est coupe."));

        var outcome = await broker.TranslateAsync("regroupez-vous avant d'entrer", 7, Domain);

        Assert.False(outcome.Accepted);
        Assert.Equal(InstructionSource.Disabled, outcome.Source);
        Assert.True(outcome.Fallback);
        Assert.Equal(0, broker.Calls);
    }

    /// <summary>
    /// La configuration livree coupe le service. Le test lit le fichier et non une valeur par
    /// defaut : c'est ce qui est livre qui compte, et le jour ou quelqu'un l'allumera pour essayer,
    /// cette ligne le lui rappellera avant le commit.
    /// </summary>
    [Fact]
    public void La_configuration_livree_n_appelle_aucun_service()
    {
        Assert.False(DeliveredConfig.Instructions.Enabled);
        Assert.True(DeliveredConfig.Instructions.PresetsWhenDisabled);
    }

    /// <summary>
    /// Un service qui ne repond pas n'empeche rien : le delai tombe, le courtier rend la main, et le
    /// joueur garde ses instructions pre-ecrites. Le budget de la mission n'est pas entame — une
    /// requete sans reponse ne rapporte aucun cout au jeu. Le service et le fournisseur gardent
    /// leurs propres garde-fous : un timeout client ne prouve pas une absence de facturation.
    /// </summary>
    [Fact]
    public async Task Un_service_muet_n_empeche_ni_la_mission_ni_les_instructions_pre_ecrites()
    {
        var broker = new InstructionBroker(
            Enabled(settings => { settings.TimeoutMs = 40; settings.Retries = 0; }),
            async (_, cancellation) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellation);
                return Constraint("forbid", null, "BreakWindow");
            });

        var outcome = await broker.TranslateAsync("ne cassez aucune vitre", 7, Domain);

        Assert.False(outcome.Accepted);
        Assert.Equal(InstructionSource.Timeout, outcome.Source);
        Assert.True(outcome.Fallback);
        Assert.Equal(0d, broker.Spent);
    }

    /// <summary>La reprise est unique : une de plus, puis on rend la main. Insister ne traduit rien.</summary>
    [Fact]
    public async Task Le_service_est_relance_une_fois_et_pas_davantage()
    {
        int calls = 0;

        var broker = new InstructionBroker(
            Enabled(settings => { settings.TimeoutMs = 40; settings.Retries = 1; }),
            (_, _) =>
            {
                calls++;
                throw new InvalidOperationException("connexion refusee");
            });

        var outcome = await broker.TranslateAsync("ne cassez aucune vitre", 7, Domain);

        Assert.Equal(2, calls);
        Assert.Equal(InstructionSource.Transport, outcome.Source);
    }

    /// <summary>
    /// Une contrainte hors grammaire est refusee par le portillon 1, exactement comme si un joueur
    /// l'avait tapee. Le predicat <c>barricaded</c> est le cas d'ecole de la specification : il
    /// n'existe pas, il sonne juste, et rien d'autre dans la chaine ne le verrait.
    /// </summary>
    [Fact]
    public async Task Une_contrainte_hors_grammaire_est_refusee_par_le_portillon_1()
    {
        var broker = new InstructionBroker(
            Enabled(),
            (_, _) => Task.FromResult<InstructionReply?>(
                Constraint("require_before", "barricaded(door_1)", "enter:building")));

        var outcome = await broker.TranslateAsync("barricadez avant d'entrer", 7, Domain);

        Assert.False(outcome.Accepted);
        Assert.Equal(InstructionSource.Grammar, outcome.Source);
        Assert.Contains("barricaded", outcome.Reason);
    }

    /// <summary>
    /// Une forme inconnue ne devient pas une contrainte vide. <c>ToGrammar</c> rend <c>null</c>, et
    /// c'est ce <c>null</c> qui est refuse — pas une chaine mal formee qu'on aurait laissee filer
    /// jusqu'a l'analyseur.
    /// </summary>
    [Fact]
    public async Task Une_forme_hors_des_cinq_est_refusee()
    {
        var broker = new InstructionBroker(
            Enabled(),
            (_, _) => Task.FromResult<InstructionReply?>(Constraint("prioritize", "healed(agent)", null)));

        var outcome = await broker.TranslateAsync("priorite aux blesses", 7, Domain);

        Assert.False(outcome.Accepted);
        Assert.Equal(InstructionSource.Grammar, outcome.Source);
    }

    /// <summary>
    /// Un refus du service n'est pas un refus de portillon, et ne renvoie pas aux instructions
    /// pre-ecrites : le service a compris la phrase et a explique pourquoi elle n'est pas une regle
    /// de plan. Proposer un repli ici laisserait croire a une panne.
    /// </summary>
    [Fact]
    public async Task Un_refus_du_service_est_rapporte_tel_quel()
    {
        var broker = new InstructionBroker(
            Enabled(),
            (_, _) => Task.FromResult<InstructionReply?>(new InstructionReply
            {
                Status = InstructionReply.StatusRejected,
                Reason = "preference_not_constraint",
                Message = "« priorite aux blesses » exprime une preference d'objectif, pas une regle de plan.",
            }));

        var outcome = await broker.TranslateAsync("priorite aux blesses", 7, Domain);

        Assert.False(outcome.Accepted);
        Assert.Equal(InstructionSource.ServiceRefusal, outcome.Source);
        Assert.False(outcome.Fallback);
        Assert.Contains("preference_not_constraint", outcome.Reason);
    }

    /// <summary>
    /// La meme phrase n'est traduite qu'une fois. C'est l'une des deux choses qui rendent une partie
    /// reproductible malgre un fournisseur distant non deterministe — l'autre etant qu'une contrainte
    /// acceptee est figee dans la mission.
    /// </summary>
    [Fact]
    public async Task La_meme_phrase_n_est_traduite_qu_une_fois()
    {
        var broker = new InstructionBroker(
            Enabled(),
            (_, _) => Task.FromResult<InstructionReply?>(Constraint("forbid", null, "BreakWindow", cost: 0.001d)));

        var first = await broker.TranslateAsync("Ne cassez aucune vitre", 7, Domain);
        var second = await broker.TranslateAsync("  ne   cassez aucune VITRE  ", 7, Domain);

        Assert.True(first.Accepted);
        Assert.True(second.Accepted);
        Assert.Equal(first.Constraint, second.Constraint);

        Assert.Equal(1, broker.Calls);
        Assert.Equal(InstructionSource.Cache, second.Source);
    }

    /// <summary>
    /// Deux graines ne partagent pas une traduction : le vocabulaire nommable depend de la carte
    /// tiree, et une contrainte qui citait un batiment du quartier d'hier n'a pas de sens aujourd'hui.
    /// </summary>
    [Fact]
    public async Task Deux_graines_ne_partagent_pas_une_traduction()
    {
        var broker = new InstructionBroker(
            Enabled(),
            (_, _) => Task.FromResult<InstructionReply?>(Constraint("forbid", null, "BreakWindow")));

        await broker.TranslateAsync("ne cassez aucune vitre", 7, Domain);
        await broker.TranslateAsync("ne cassez aucune vitre", 8, Domain);

        Assert.Equal(2, broker.Calls);
    }

    /// <summary>
    /// Le plafond coupe les appels et le dit. Un repli muet ferait croire au joueur que le modele a
    /// lu sa phrase alors que personne ne l'a lue — c'est la raison pour laquelle le montant figure
    /// dans le message.
    /// </summary>
    [Fact]
    public async Task Le_plafond_de_cout_coupe_les_appels_et_le_signale()
    {
        var broker = new InstructionBroker(
            Enabled(settings => { settings.MaxCost = 0.002d; settings.CacheEnabled = false; }),
            (_, _) => Task.FromResult<InstructionReply?>(Constraint("forbid", null, "BreakWindow", cost: 0.0015d)));

        var first = await broker.TranslateAsync("ne cassez aucune vitre", 7, Domain);
        var second = await broker.TranslateAsync("ne forcez aucune porte", 7, Domain);
        var third = await broker.TranslateAsync("restez a couvert", 7, Domain);

        Assert.True(first.Accepted);
        Assert.True(second.Accepted);

        Assert.False(third.Accepted);
        Assert.Equal(InstructionSource.Budget, third.Source);
        Assert.True(third.Fallback);
        Assert.Equal(2, broker.Calls);
        Assert.Contains("Plafond", third.Reason);
    }

    /// <summary>
    /// Un service injoignable ne se met pas en cache : il peut etre en train de demarrer, et figer
    /// « injoignable » condamnerait la phrase pour toute la partie. Un refus du service, lui, est une
    /// propriete de la phrase et se garde — d'ou les deux moities de ce test.
    /// </summary>
    [Fact]
    public async Task Une_panne_ne_se_met_pas_en_cache_un_refus_si()
    {
        bool broken = true;

        var broker = new InstructionBroker(
            Enabled(settings => settings.Retries = 0),
            (_, _) => broken
                ? throw new InvalidOperationException("connexion refusee")
                : Task.FromResult<InstructionReply?>(Constraint("forbid", null, "BreakWindow")));

        var down = await broker.TranslateAsync("ne cassez aucune vitre", 7, Domain);
        Assert.Equal(InstructionSource.Transport, down.Source);

        broken = false;

        var up = await broker.TranslateAsync("ne cassez aucune vitre", 7, Domain);
        Assert.True(up.Accepted);
        Assert.Equal(InstructionSource.Service, up.Source);
    }

    /// <summary>
    /// Le bout en bout, sans reseau : la phrase de la specification devient une contrainte, la
    /// contrainte passe le portillon 1, et le domaine compile scinde bien le schema de deplacement.
    /// C'est le critere de validation numero 2 du livrable, moins le modele.
    /// </summary>
    [Fact]
    public async Task L_exemple_du_livrable_traverse_toute_la_chaine()
    {
        var broker = new InstructionBroker(
            Enabled(),
            (_, _) => Task.FromResult<InstructionReply?>(
                Constraint("require_before", "at(agent, group)", "enter:building")));

        var outcome = await broker.TranslateAsync("Avant d'entrer dans un batiment, regroupez-vous.", 7, Domain);

        Assert.True(outcome.Accepted, outcome.Reason);
        Assert.Equal("require_before(at(agent, group), enter:building)", outcome.Constraint);

        var instructions = new TacticalInstructions(Domain, Enabled());
        var entry = instructions.Add(outcome.Constraint!, utterance: "Avant d'entrer dans un batiment, regroupez-vous.");

        Assert.True(entry.Accepted, entry.Reason);
        Assert.True(instructions.Compiled.Actions.Count > Domain.Actions.Count, "La scission du schema de deplacement n'a pas eu lieu.");
    }

    /// <summary>
    /// La phrase part tronquee a <c>max_chars</c>, et non entiere avec une troncature cote service :
    /// les jetons se comptent avant d'etre envoyes, pas apres.
    /// </summary>
    [Fact]
    public async Task La_phrase_est_coupee_avant_de_partir()
    {
        InstructionRequest? seen = null;

        var broker = new InstructionBroker(
            Enabled(),
            (request, _) =>
            {
                seen = request;
                return Task.FromResult<InstructionReply?>(Constraint("forbid", null, "BreakWindow"));
            });

        await broker.TranslateAsync(new string('a', 5_000), 7, Domain);

        Assert.NotNull(seen);
        Assert.Equal(DeliveredConfig.Instructions.MaxChars, seen!.Utterance.Length);
    }

    // --- Le corpus d'evaluation -------------------------------------------------------------------

    /// <summary>
    /// Chaque contrainte attendue par <c>service/evals/utterances.jsonl</c> — et par
    /// <c>heldout.jsonl</c>, les phrases tenues a l'ecart — passe le portillon 1.
    ///
    /// <para>
    /// Un corpus d'evaluation dont l'attendu est impossible ne mesure rien : il compte comme echec
    /// une traduction qui etait la bonne, et le taux publie est alors une propriete du corpus et non
    /// du modele. Cette assertion attache le corpus au domaine livre — retirer une action du jeu fait
    /// echouer ici, bruyamment, plutot que de fausser un tableau quelques semaines plus tard.
    /// </para>
    ///
    /// <para>
    /// Le <c>k</c> litteral de <c>penalize(A, k)</c> est remplace par 1 : le corpus ne fixe pas de
    /// multiplicateur attendu, parce qu'aucune phrase de joueur n'en designe un.
    /// </para>
    /// </summary>
    [Fact]
    public void Chaque_contrainte_attendue_du_corpus_passe_la_grammaire()
    {
        var settings = Enabled();
        int checkedCount = 0;

        foreach (var line in Corpus().Concat(Corpus("heldout.jsonl")))
        {
            if (line.GetProperty("expect").GetString() != "constraint") continue;

            string expected = line.GetProperty("constraint").GetString()!.Replace(", k)", ", 1)", StringComparison.Ordinal);
            string id = line.GetProperty("id").GetString()!;

            var result = ConstraintGrammar.Parse(expected, Domain, settings);

            Assert.True(result.Accepted, $"Corpus « {id} » attend « {expected} », que le portillon 1 refuse : {result.Reason}");
            checkedCount++;
        }

        Assert.True(checkedCount >= 30, $"Le corpus n'a que {checkedCount} contraintes attendues : trop peu pour mesurer quoi que ce soit.");
    }

    /// <summary>
    /// Le corpus contient des phrases irrecevables, et en nombre.
    ///
    /// <para>
    /// C'est la moitie qui compte. Un corpus qui ne contient que des cas faciles ne mesure rien, et
    /// le <b>taux de faux acceptes</b> — la seule mesure qui compte vraiment — ne se calcule que sur
    /// des phrases qu'un modele complaisant aurait traduites.
    /// </para>
    /// </summary>
    [Fact]
    public void Le_corpus_contient_des_phrases_irrecevables_de_chaque_famille()
    {
        var families = new HashSet<string>(StringComparer.Ordinal);
        int rejected = 0;

        foreach (var line in Corpus())
        {
            if (line.GetProperty("expect").GetString() != "rejected") continue;

            families.Add(line.GetProperty("reason").GetString()!);
            rejected++;
        }

        Assert.True(rejected >= 20, $"Seulement {rejected} phrases irrecevables au corpus.");

        Assert.Equal(
            new[] { "conditional_not_supported", "not_expressible", "out_of_scope", "preference_not_constraint", "unknown_symbol" },
            families.Order(StringComparer.Ordinal));
    }

    /// <summary>Chaque identifiant du corpus est unique : un doublon ferait compter une phrase deux fois.</summary>
    [Fact]
    public void Les_identifiants_du_corpus_sont_uniques()
    {
        var ids = Corpus().Concat(Corpus("heldout.jsonl")).Select(line => line.GetProperty("id").GetString()!).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    private static IEnumerable<System.Text.Json.JsonElement> Corpus(string file = "utterances.jsonl")
    {
        string path = Path.Combine(DemoMap.RepositoryRoot, "service", "evals", file);

        Assert.True(File.Exists(path), $"{path} est absent.");

        foreach (string line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            yield return System.Text.Json.JsonDocument.Parse(line).RootElement;
        }
    }

    // --- L'invariant -----------------------------------------------------------------------------

    /// <summary>
    /// <c>Hordinary.Core</c> ne reference aucun client HTTP.
    ///
    /// <para>
    /// L'invariant n'est pas une discipline : le transport est un delegue, donc il n'y a pas
    /// d'endroit ou en mettre un. Cette assertion existe pour le jour ou quelqu'un voudra « juste
    /// appeler le service ici » — elle le lui dira avant la revue, comme celle qui garde Godot hors
    /// du coeur.
    /// </para>
    /// </summary>
    [Fact]
    public void Le_coeur_ne_reference_aucun_client_http()
    {
        var assembly = typeof(InstructionBroker).Assembly;

        Assert.DoesNotContain(
            assembly.GetReferencedAssemblies(),
            reference => reference.Name?.Contains("System.Net.Http", StringComparison.OrdinalIgnoreCase) == true);

        Assert.DoesNotContain(
            assembly.GetReferencedAssemblies(),
            reference => reference.Name?.Contains("Godot", StringComparison.OrdinalIgnoreCase) == true);
    }
}
