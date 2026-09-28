using Hordinary.AI.Instructions;
using Hordinary.AI.Planning;
using Hordinary.AI.Planning.Pddl;
using Hordinary.Config;

namespace Hordinary.Tests;

/// <summary>
/// Instructions tactiques (livrable 15), partie PDDL 3 : la grammaire fermee (portillon 1), la
/// compilation des modalites de trajectoire en preconditions (portillon 2) et la verification de la
/// trajectoire produite (portillon 4).
///
/// Le portillon 3 — la satisfiabilite par Fast Downward — n'est pas ici : il est <b>hors ligne</b> par
/// construction, aucun planificateur tiers ne tourne pendant une partie. Ces tests-ci ne demandent ni
/// reseau, ni binaire, ni modele de langage : tout ce qu'ils exercent est deterministe et vit dans
/// <c>Hordinary.Core</c>.
///
/// Comme partout ailleurs, le domaine teste est le domaine livre. Une action retiree de
/// <c>config/planner_domain.json</c> doit faire echouer le test qui la nomme — c'est l'interet.
/// </summary>
public class InstructionTests
{
    private static readonly PlanningDomain DeliveredDomain =
        PlanningDomain.FromJson(DemoMap.ReadConfig("planner_domain.json"));

    private static readonly GameConfig DeliveredConfig =
        GameConfig.FromJson(DemoMap.ReadConfig("game_config.json"));

    private static readonly InstructionPresetCatalogue DeliveredPresets =
        InstructionPresetCatalogue.FromJson(DemoMap.ReadConfig("instruction_presets.json"));

    private static readonly PddlDomain Domain = PddlDomainBuilder.From(DeliveredDomain);

    private static InstructionSettings Settings => DeliveredConfig.Instructions;

    private static TacticalConstraint Parse(string text)
    {
        var result = ConstraintGrammar.Parse(text, Domain, Settings);
        Assert.True(result.Accepted, $"« {text} » : {result.Reason}");
        return result.Constraint!;
    }

    // --- Portillon 1 : la grammaire ---------------------------------------------------------------

    /// <summary>
    /// Les instructions pre-ecrites livrees passent le portillon 1, et se relisent identiques : la
    /// chaine du fichier est deja la forme canonique. Les cinq formes sont couvertes — sans quoi le
    /// livrable serait demontrable a moitie.
    /// </summary>
    [Fact]
    public void Les_instructions_pre_ecrites_livrees_passent_la_grammaire()
    {
        Assert.NotEmpty(DeliveredPresets.Presets);

        var forms = new HashSet<ConstraintForm>();

        foreach (var preset in DeliveredPresets.Presets)
        {
            var result = ConstraintGrammar.Parse(preset.Constraint, Domain, Settings);

            Assert.True(result.Accepted, $"« {preset.Constraint} » : {result.Reason}");
            Assert.Equal(preset.Constraint, result.Constraint!.Text);
            Assert.NotEmpty(preset.Label);

            forms.Add(result.Constraint.Form);
        }

        Assert.Equal(Enum.GetValues<ConstraintForm>().Length, forms.Count);
    }

    /// <summary>
    /// Le cas d'ecole de la specification : un modele propose un predicat qui n'existe pas. Le
    /// vocabulaire de reference est le domaine PDDL, pas une liste tenue a la main — il n'y a donc
    /// aucun endroit ou oublier de le mettre a jour.
    /// </summary>
    [Fact]
    public void Un_predicat_inconnu_est_refuse_par_la_grammaire()
    {
        var result = ConstraintGrammar.Parse("require_before(barricaded(porte), enter:building)", Domain, Settings);

        Assert.False(result.Accepted);
        Assert.Contains("barricaded", result.Reason, StringComparison.Ordinal);
    }

    /// <summary>Un schema retire du domaine cesse d'etre nommable : c'est ce qui a motive l'entree de <c>BarricadeDoor</c> dans le fichier.</summary>
    [Fact]
    public void Un_schema_inconnu_est_refuse_par_la_grammaire()
    {
        var result = ConstraintGrammar.Parse("forbid(CreateNoiseDistraction)", Domain, Settings);

        Assert.False(result.Accepted);
        Assert.Contains("CreateNoiseDistraction", result.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Une contrainte nomme des objets, jamais des variables : personne ne saurait par quoi les lier.
    /// C'est la difference entre une instruction du joueur et un schema d'action.
    /// </summary>
    [Fact]
    public void Une_variable_dans_une_contrainte_est_refusee()
    {
        var result = ConstraintGrammar.Parse("require_always(searched(?location))", Domain, Settings);

        Assert.False(result.Accepted);
        Assert.Contains("variable", result.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// L'arite est verifiee, pas seulement le nom : <c>at</c> existe, <c>at(group)</c> n'est pas un
    /// fait de ce domaine.
    /// </summary>
    [Fact]
    public void Un_predicat_employe_avec_la_mauvaise_arite_est_refuse()
    {
        var result = ConstraintGrammar.Parse("require_always(at(group))", Domain, Settings);

        Assert.False(result.Accepted);
        Assert.Contains("argument", result.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Les bornes du multiplicateur viennent de <c>config/game_config.json</c> et de nulle part
    /// ailleurs : les deplacer dans le fichier deplace ce que la grammaire accepte.
    /// </summary>
    [Fact]
    public void Un_multiplicateur_hors_des_bornes_de_la_configuration_est_refuse()
    {
        Assert.True(Settings.MinPenalty < Settings.MaxPenalty);

        float excessive = Settings.MaxPenalty * 2f;
        var result = ConstraintGrammar.Parse($"penalize(ForceDoor, {excessive.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)})", Domain, Settings);

        Assert.False(result.Accepted);
        Assert.Contains("bornes", result.Reason, StringComparison.Ordinal);
    }

    /// <summary>Une phrase qui n'est d'aucune des cinq formes est refusee en rappelant les cinq.</summary>
    [Fact]
    public void Une_phrase_hors_grammaire_est_refusee_en_rappelant_les_formes()
    {
        var result = ConstraintGrammar.Parse("restez groupes les gars", Domain, Settings);

        Assert.False(result.Accepted);
        Assert.Contains("require_before", result.Reason, StringComparison.Ordinal);
    }

    // --- Portillon 2 : la compilation --------------------------------------------------------------

    /// <summary>
    /// Les cinq formes compilent en un domaine bien forme — et « bien forme » veut dire deux choses :
    /// qu'il reste dans le fragment de la spec G, et qu'il reste <b>jouable</b>, c'est-a-dire que
    /// chaque schema produit sait encore par quel verbe on l'execute.
    /// </summary>
    [Fact]
    public void Les_cinq_formes_compilent_en_un_domaine_jouable()
    {
        foreach (var preset in DeliveredPresets.Presets)
        {
            var compiled = DomainCompiler.Compile(Domain, [Parse(preset.Constraint)]);

            Assert.True(compiled.Accepted, $"« {preset.Constraint} » : {compiled}");
            Assert.NotEmpty(compiled.Domain.Actions);

            // Le rendu vers GBFS jette si un schema a perdu sa couche d'execution : c'est la
            // verification qu'une scission herite bien de l'origine de ce qu'elle scinde.
            compiled.Domain.ToPlanningDomain().Validate();
        }
    }

    /// <summary>
    /// Le domaine compile reste dans exactement le meme fragment que celui de la spec G : memes
    /// <c>:requirements</c>, aucun bloc <c>(:constraints …)</c>. Ni GBFS ni Fast Downward ne prennent
    /// PDDL 3 en entree — c'est toute la raison de compiler plutot que de declarer.
    /// </summary>
    [Fact]
    public void Le_domaine_compile_reste_dans_le_fragment_de_la_spec_G()
    {
        var compiled = DomainCompiler.Compile(
            Domain,
            [.. DeliveredPresets.Presets.Take(3).Select(preset => Parse(preset.Constraint))]);

        Assert.True(compiled.Accepted, compiled.ToString());
        Assert.Equal(Domain.Requirements, compiled.Domain.Requirements);
        Assert.Empty(compiled.Domain.Constraints);
        Assert.DoesNotContain("(:constraints", PddlWriter.Render(compiled.Domain), StringComparison.Ordinal);
    }

    /// <summary>
    /// La scission d'un schema par une classe de lieux : l'union des preconditions des deux moities
    /// couvre exactement les instances du schema d'origine, et leur intersection est vide. C'est ce
    /// qui rend la reecriture sure — un deplacement qui ne va pas dans un batiment reste jouable
    /// exactement comme avant.
    /// </summary>
    [Fact]
    public void La_scission_couvre_les_instances_d_origine_sans_les_recouvrir()
    {
        var original = Domain.FindAction("MoveToLocation");
        Assert.NotNull(original);

        var compiled = DomainCompiler.Compile(Domain, [Parse("require_before(at(agent, group), enter:building)")]);
        Assert.True(compiled.Accepted, compiled.ToString());

        var halves = compiled.Domain.Actions
            .Where(action => action.Origin is { } origin && origin.Name == original!.Name)
            .ToList();

        Assert.Equal(2, halves.Count);

        var category = new PddlAtom("building", "?location");
        var inside = halves.Single(action => action.Precondition.Literals.Contains(category));
        var outside = halves.Single(action => action.Precondition.Literals.Contains(new PddlNegation(category)));

        // Union : tout ce que le schema d'origine exigeait, chaque moitie l'exige encore.
        foreach (var literal in original!.Precondition.Literals)
        {
            Assert.Contains(literal, inside.Precondition.Literals);
            Assert.Contains(literal, outside.Precondition.Literals);
        }

        // Intersection : aucune instance ne peut tomber dans les deux.
        Assert.DoesNotContain(category, outside.Precondition.Literals);
        Assert.DoesNotContain(new PddlNegation(category), inside.Precondition.Literals);

        // Et la contrainte est bien dans la moitie qui entre, pas dans l'autre.
        Assert.Contains(new PddlAtom("at", "agent", "group"), inside.Precondition.Literals);
        Assert.DoesNotContain(new PddlAtom("at", "agent", "group"), outside.Precondition.Literals);
        Assert.Equal("require_before(at(agent, group), enter:building)", inside.GeneratedBy);
    }

    /// <summary>
    /// Deux consignes sur la meme classe se composent au lieu de se rescinder. Avant, la seconde
    /// scindait a nouveau la moitie « dedans » de la premiere : il en sortait
    /// <c>EnterBuilding_EnterBuilding</c>, qui portait les deux exigences, et un <c>EnterBuilding</c>
    /// mort, qui exigeait a la fois <c>building(?location)</c> et sa negation — c'est la prise
    /// « Armez-vous, puis faites diversion » qui l'a montre, son journal ne nommant jamais le geste
    /// annonce.
    /// </summary>
    [Fact]
    public void Deux_consignes_sur_la_meme_classe_se_composent_sans_rescinder()
    {
        var compiled = DomainCompiler.Compile(Domain,
        [
            Parse("require_before(armed, enter:building)"),
            Parse("require_before(diversion_made, enter:building)"),
        ]);
        Assert.True(compiled.Accepted, compiled.ToString());

        var category = new PddlAtom("building", "?location");
        var inside = compiled.Domain.Actions.Where(action => action.Precondition.Literals.Contains(category)).ToList();

        // Une seule moitie « dedans » par schema de deplacement, sous son nom court pour le premier.
        Assert.Contains(inside, action => action.Name == "EnterBuilding");
        Assert.DoesNotContain(compiled.Domain.Actions, action => action.Name.Contains("EnterBuilding_EnterBuilding", StringComparison.Ordinal));

        foreach (var action in inside)
        {
            Assert.Contains(new PddlAtom("armed"), action.Precondition.Literals);
            Assert.Contains(new PddlAtom("diversion_made"), action.Precondition.Literals);
            Assert.Contains("require_before(armed, enter:building)", action.GeneratedBy);
            Assert.Contains("require_before(diversion_made, enter:building)", action.GeneratedBy);
        }

        // Aucune moitie morte : pas de schema qui exige une categorie et son contraire.
        Assert.DoesNotContain(compiled.Domain.Actions, action =>
            action.Precondition.Literals.Contains(category) && action.Precondition.Literals.Contains(new PddlNegation(category)));

        // Et autant de schemas qu'avec une seule consigne : la seconde n'en ajoute aucun.
        var single = DomainCompiler.Compile(Domain, [Parse("require_before(armed, enter:building)")]);
        Assert.Equal(single.Domain.Actions.Count, compiled.Domain.Actions.Count);
    }

    /// <summary>
    /// « A doit poser P » quand A ne touche pas a P : P devient une precondition. Ce qui surprend pour
    /// une contrainte dite « apres » ne l'est pas — une action qui ne touche pas a P le laisse vrai
    /// apres si et seulement s'il l'etait avant.
    /// </summary>
    [Fact]
    public void Require_after_pose_une_precondition_quand_le_schema_ne_touche_pas_au_fait()
    {
        var compiled = DomainCompiler.Compile(Domain, [Parse("require_after(threat_handled, EscapeLocation)")]);

        Assert.True(compiled.Accepted, compiled.ToString());

        var escape = compiled.Domain.FindAction("EscapeLocation");

        Assert.NotNull(escape);
        Assert.Contains(new PddlAtom("threat_handled"), escape!.Precondition.Literals);
        Assert.Equal("require_after(threat_handled, EscapeLocation)", escape.GeneratedBy);
    }

    /// <summary>
    /// Exiger d'une action ce qu'elle fait deja n'est pas une erreur, mais ce n'est pas non plus un
    /// effet : le compilateur accepte et le dit, plutot que de laisser croire que la contrainte a
    /// change quelque chose.
    /// </summary>
    [Fact]
    public void Require_after_sur_une_action_qui_pose_deja_le_fait_est_signalee_sans_effet()
    {
        var compiled = DomainCompiler.Compile(Domain, [Parse("require_after(threat_handled, AttackZombie)")]);

        Assert.True(compiled.Accepted, compiled.ToString());
        Assert.Contains(compiled.Warnings, warning => warning.Contains("etablit deja", StringComparison.Ordinal));

        var before = Domain.FindAction("AttackZombie")!;
        var after = compiled.Domain.FindAction("AttackZombie")!;

        Assert.Equal(before.Precondition.Canonical, after.Precondition.Canonical);
    }

    /// <summary>
    /// <c>require_always</c> sur un fait qu'une action retire pour <b>toutes</b> ses instances est
    /// refusee, avec sa raison. C'est le cas de l'effet universel de la spec G : exiger
    /// <c>at(agent, group)</c> a chaque etat interdirait tout deplacement. Le compilateur doit le dire
    /// plutot que de produire un domaine mort ou le survivant reste plante sans que personne sache
    /// pourquoi.
    /// </summary>
    [Fact]
    public void Require_always_sur_un_fait_supprime_universellement_est_refuse()
    {
        var compiled = DomainCompiler.Compile(Domain, [Parse("require_always(at(agent, group))")]);

        Assert.False(compiled.Accepted);
        Assert.Contains(compiled.Errors, error => error.Contains("effet universel", StringComparison.Ordinal));
        Assert.Same(Domain, compiled.Domain);
    }

    /// <summary>
    /// <c>require_always</c> sur un fait ordinaire traverse tout le domaine : chaque action qui ne le
    /// pose pas le prend en precondition, et celle qui le defait sort du domaine.
    /// </summary>
    [Fact]
    public void Require_always_ajoute_le_fait_en_precondition_partout()
    {
        var required = new PddlAtom("extraction_known");
        var compiled = DomainCompiler.Compile(Domain, [Parse("require_always(extraction_known)")]);

        Assert.True(compiled.Accepted, compiled.ToString());
        Assert.NotEmpty(compiled.Domain.Actions);

        Assert.All(
            compiled.Domain.Actions,
            action => Assert.True(
                action.Precondition.Literals.Contains(required) || action.Effect.Literals.Contains(required),
                $"« {action.Name} » n'exige ni ne pose « {required.AsFact} »."));
    }

    /// <summary>
    /// Interdire la derniere action qui produit ce qu'un objectif exige rend cet objectif
    /// inatteignable : refus, en nommant le fait perdu. Sans ce controle, le joueur obtiendrait un
    /// groupe qui n'echoue pas — il ne planifie simplement plus rien.
    /// </summary>
    [Fact]
    public void Un_forbid_qui_rend_le_but_inatteignable_est_refuse()
    {
        var goal = DeliveredDomain.FindGoal("ReachExtraction");
        Assert.NotNull(goal);

        var compiled = DomainCompiler.Compile(
            Domain,
            [Parse("forbid(ReachExtraction)")],
            new CompilationContext(Goal: goal));

        Assert.False(compiled.Accepted);
        Assert.Contains(compiled.Errors, error => error.Contains("extracted", StringComparison.Ordinal));
    }

    /// <summary>
    /// La meme interdiction, sans objectif qui en depende, passe : le compilateur ne refuse pas une
    /// contrainte au nom d'un but que personne ne poursuit.
    /// </summary>
    [Fact]
    public void Un_forbid_retire_le_schema_du_domaine()
    {
        var compiled = DomainCompiler.Compile(Domain, [Parse("forbid(BreakWindow)")]);

        Assert.True(compiled.Accepted, compiled.ToString());
        Assert.Null(compiled.Domain.FindAction("BreakWindow"));
        Assert.NotNull(Domain.FindAction("BreakWindow"));
    }

    /// <summary>
    /// Interdire une <b>classe</b> ne retire pas le schema : il n'en reste que la moitie qui ne va pas
    /// la ou le joueur ne veut pas qu'on aille. Interdire les ruelles ne doit pas interdire de marcher.
    /// </summary>
    [Fact]
    public void Un_forbid_de_classe_ne_retire_que_la_moitie_visee()
    {
        var compiled = DomainCompiler.Compile(Domain, [Parse("forbid(enter:alley)")]);

        Assert.True(compiled.Accepted, compiled.ToString());

        var move = compiled.Domain.FindAction("MoveToLocation");

        Assert.NotNull(move);
        Assert.Contains(new PddlNegation(new PddlAtom("alley", "?location")), move!.Precondition.Literals);
    }

    /// <summary>
    /// Une categorie de lieu qu'aucun lieu de la carte ne porte n'est pas un refus : la contrainte est
    /// acceptee et signalee sans effet ici. C'est la difference entre « impossible » et « sans objet
    /// sur cette carte », et le joueur a droit a la seconde phrase.
    /// </summary>
    [Fact]
    public void Une_categorie_absente_de_la_carte_est_signalee_sans_etre_refusee()
    {
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "street" };
        var compiled = DomainCompiler.Compile(
            Domain,
            [Parse("forbid(enter:alley)")],
            new CompilationContext(AvailableKinds: present));

        Assert.True(compiled.Accepted, compiled.ToString());
        Assert.Contains(compiled.Warnings, warning => warning.Contains("alley", StringComparison.Ordinal));
    }

    /// <summary>
    /// Deux contraintes qui se contredisent sont refusees ensemble, en nommant les deux. Se contenter
    /// de « impossible » laisserait le joueur retirer la mauvaise.
    /// </summary>
    [Fact]
    public void Deux_contraintes_contradictoires_sont_refusees_en_nommant_les_deux()
    {
        var compiled = DomainCompiler.Compile(Domain, [Parse("forbid(ForceDoor)"), Parse("penalize(ForceDoor, 2)")]);

        Assert.False(compiled.Accepted);
        Assert.Contains(compiled.Errors, error =>
            error.Contains("forbid(ForceDoor)", StringComparison.Ordinal)
            && error.Contains("penalize(ForceDoor, 2)", StringComparison.Ordinal));
    }

    /// <summary>La compilation est une fonction pure : le domaine d'origine n'est jamais touche.</summary>
    [Fact]
    public void La_compilation_ne_modifie_pas_le_domaine_d_origine()
    {
        var names = Domain.Actions.Select(action => action.Name).ToList();
        int actions = Domain.Actions.Count;

        DomainCompiler.Compile(Domain, [Parse("forbid(BreakWindow)"), Parse("require_before(at(agent, group), enter:building)")]);

        Assert.Equal(actions, Domain.Actions.Count);
        Assert.Equal(names, Domain.Actions.Select(action => action.Name));
    }

    // --- penalize et la recherche active -----------------------------------------------------------

    /// <summary>
    /// Sous une recherche qui ordonne sur <c>h</c> seul, le cout ne departage que les ex aequo :
    /// <c>penalize</c> est exact dans le PDDL et sans effet sur le plan choisi. C'est un
    /// <b>avertissement</b>, pas un refus — le domaine exporte reste juste, et c'est lui que Fast
    /// Downward verifie.
    /// </summary>
    [Fact]
    public void Un_penalize_sous_gbfs_avertit_sans_refuser()
    {
        var compiled = DomainCompiler.Compile(
            Domain,
            [Parse("penalize(ForceDoor, 3)")],
            new CompilationContext(PlannerSearch.Gbfs));

        Assert.True(compiled.Accepted, compiled.ToString());
        Assert.Contains(compiled.Warnings, warning => warning.Contains("ignore g", StringComparison.Ordinal));
        Assert.Equal(Domain.FindAction("ForceDoor")!.Cost * 3f, compiled.Domain.FindAction("ForceDoor")!.Cost);
    }

    /// <summary>
    /// Sous A*, la priorite est <c>g + h</c> : le cout d'une action change le plan choisi, donc
    /// <c>penalize</c> agit et le compilateur n'a rien a signaler. L'avertissement est conditionne a
    /// la recherche active, il n'est pas une propriete de la forme.
    /// </summary>
    [Fact]
    public void Un_penalize_sous_astar_agit_et_n_avertit_pas()
    {
        var compiled = DomainCompiler.Compile(
            Domain,
            [Parse("penalize(ForceDoor, 3)")],
            new CompilationContext(PlannerSearch.AStar));

        Assert.True(compiled.Accepted, compiled.ToString());
        Assert.Empty(compiled.Warnings);
        Assert.Equal(Domain.FindAction("ForceDoor")!.Cost * 3f, compiled.Domain.FindAction("ForceDoor")!.Cost);
    }

    /// <summary>
    /// La question se lit dans <c>planner.search</c> et nulle part ailleurs. Le test tient quelle que
    /// soit la recherche livree : basculer le fichier ne le casse pas, il fait basculer ce qu'il
    /// attend — et le cout reste multiplie dans les deux cas, parce que le PDDL doit rester exact meme
    /// quand le moteur de jeu l'ignore.
    /// </summary>
    [Fact]
    public void La_recherche_livree_decide_si_penalize_avertit()
    {
        var search = DeliveredConfig.Planner.Search;
        var compiled = DomainCompiler.Compile(Domain, [Parse("penalize(ForceDoor, 3)")], new CompilationContext(search));

        Assert.True(compiled.Accepted, compiled.ToString());
        Assert.Equal(
            !search.UsesCost(),
            compiled.Warnings.Any(warning => warning.Contains("ignore g", StringComparison.Ordinal)));
        Assert.Equal(Domain.FindAction("ForceDoor")!.Cost * 3f, compiled.Domain.FindAction("ForceDoor")!.Cost);
    }

    // --- La forme declarative PDDL 3 ---------------------------------------------------------------

    /// <summary>
    /// Les trois formes qui sont des modalites PDDL 3 se rendent comme telles ; les deux qui n'en sont
    /// pas le disent en commentaire plutot que de faire semblant. Ce rendu n'est donne a aucun
    /// planificateur : il existe pour le portillon 4 et pour VAL.
    /// </summary>
    [Fact]
    public void Les_contraintes_se_rendent_dans_les_modalites_de_pddl3()
    {
        var before = Parse("require_before(at(agent, group), enter:building)").Declarative(Domain);
        Assert.True(before.IsPddl3);
        Assert.Contains("sometime-before", before.Canonical, StringComparison.Ordinal);
        Assert.Contains("(building ?x)", before.Canonical, StringComparison.Ordinal);
        Assert.StartsWith("(forall (?x - location)", before.Canonical, StringComparison.Ordinal);

        var after = Parse("require_after(threat_handled, EscapeLocation)").Declarative(Domain);
        Assert.True(after.IsPddl3);
        Assert.Contains("sometime-after", after.Canonical, StringComparison.Ordinal);

        var always = Parse("require_always(extraction_known)").Declarative(Domain);
        Assert.True(always.IsPddl3);
        Assert.Equal("(always (extraction_known))", always.Canonical);

        foreach (string text in new[] { "forbid(BreakWindow)", "penalize(ForceDoor, 3)" })
        {
            var declarative = Parse(text).Declarative(Domain);

            Assert.False(declarative.IsPddl3);
            Assert.StartsWith("; ", declarative.Canonical, StringComparison.Ordinal);
            Assert.Contains(text, declarative.Canonical, StringComparison.Ordinal);
        }
    }

    /// <summary>Le bloc <c>(:constraints …)</c> n'est ecrit que sur demande, et il contient ce qu'on y a mis.</summary>
    [Fact]
    public void Le_bloc_de_contraintes_n_est_rendu_que_sur_demande()
    {
        var declarative = Parse("require_always(extraction_known)").Declarative(Domain);
        var annotated = Domain.With(constraints: [declarative]);

        Assert.Contains("(:constraints (and", PddlWriter.Render(annotated, includeConstraints: true), StringComparison.Ordinal);
        Assert.Contains(declarative.Canonical, PddlWriter.Render(annotated, includeConstraints: true), StringComparison.Ordinal);
        Assert.DoesNotContain("(:constraints", PddlWriter.Render(annotated), StringComparison.Ordinal);
    }

    // --- Portillon 4 : la conformite de la trajectoire ---------------------------------------------

    private static readonly ActionSchema MoveSchema = DeliveredDomain.FindAction("MoveToLocation")!;

    private static PlanningAction MoveTo(string place)
        => new(MoveSchema, new Dictionary<string, string>(StringComparer.Ordinal) { ["location"] = place });

    /// <summary>Une carte minuscule ou l'on sait ce qui est un batiment, une rue et une ruelle.</summary>
    private static SymbolicState Quarter(params string[] extra)
        => new(new[]
        {
            "building(entrepot)",
            "street(rue_nord)",
            "alley(ruelle)",
            "known(entrepot)",
            "known(rue_nord)",
            "known(ruelle)",
            "known(group)",
            "at(agent, depart)",
        }.Concat(extra));

    /// <summary>
    /// Le verificateur lit la contrainte du joueur, pas sa traduction. Un plan qui rejoint le groupe,
    /// traverse une rue, puis entre dans le batiment satisfait <c>sometime-before</c> et <b>non</b> la
    /// forme compilee, plus stricte. Il doit etre accepte : sans quoi le portillon 4 ne verifierait
    /// plus la contrainte mais la compilation, et l'incompletude assumee du livrable deviendrait
    /// invisible.
    /// </summary>
    [Fact]
    public void Le_verificateur_accepte_un_regroupement_anterieur_a_l_entree()
    {
        var trace = TrajectoryTrace.Replay(Quarter(), [MoveTo("group"), MoveTo("rue_nord"), MoveTo("entrepot")]);
        var verdict = TrajectoryChecker.Check(Domain, [Parse("require_before(at(agent, group), enter:building)")], trace);

        Assert.True(verdict.Satisfied, verdict.ToString());
    }

    /// <summary>
    /// Le meme plan sans le regroupement est refuse, et la violation dit a quel pas. C'est le filet
    /// qui attrape une compilation fausse : un plan peut etre valide au regard du domaine reecrit et
    /// faux au regard de la phrase du joueur, et rien d'autre dans la chaine ne le voit.
    /// </summary>
    [Fact]
    public void Le_verificateur_refuse_une_entree_sans_regroupement()
    {
        var trace = TrajectoryTrace.Replay(Quarter(), [MoveTo("rue_nord"), MoveTo("entrepot")]);
        var verdict = TrajectoryChecker.Check(Domain, [Parse("require_before(at(agent, group), enter:building)")], trace);

        Assert.False(verdict.Satisfied);
        Assert.Contains(verdict.Violations, violation => violation.Contains("at(agent, group)", StringComparison.Ordinal));
    }

    /// <summary>
    /// Une carte qui n'a jamais pose <c>building(...)</c> ne fait tomber aucun pas sous la contrainte :
    /// c'est la traduction exacte de « acceptee, sans effet ici ».
    /// </summary>
    [Fact]
    public void Un_lieu_sans_categorie_ne_tombe_pas_sous_la_contrainte()
    {
        var trace = TrajectoryTrace.Replay(new SymbolicState(["known(hangar)"]), [MoveTo("hangar")]);
        var verdict = TrajectoryChecker.Check(Domain, [Parse("require_before(at(agent, group), enter:building)")], trace);

        Assert.True(verdict.Satisfied, verdict.ToString());
    }

    /// <summary>Un plan qui joue ce que le joueur a interdit est refuse, categorie comprise.</summary>
    [Fact]
    public void Le_verificateur_refuse_un_plan_qui_passe_par_la_classe_interdite()
    {
        var trace = TrajectoryTrace.Replay(Quarter(), [MoveTo("ruelle")]);
        var verdict = TrajectoryChecker.Check(Domain, [Parse("forbid(enter:alley)")], trace);

        Assert.False(verdict.Satisfied);
        Assert.Contains(verdict.Violations, violation => violation.Contains("ruelle", StringComparison.Ordinal));
    }

    /// <summary><c>always</c> porte sur chaque etat, l'etat initial compris.</summary>
    [Fact]
    public void Le_verificateur_exige_le_fait_a_chaque_etat()
    {
        var constraints = new[] { Parse("require_always(extraction_known)") };

        var ignorant = TrajectoryTrace.Replay(Quarter(), [MoveTo("rue_nord")]);
        Assert.False(TrajectoryChecker.Check(Domain, constraints, ignorant).Satisfied);

        var informe = TrajectoryTrace.Replay(Quarter("extraction_known"), [MoveTo("rue_nord")]);
        Assert.True(TrajectoryChecker.Check(Domain, constraints, informe).Satisfied);
    }

    /// <summary>
    /// Une moitie issue d'une scission reste une instance du schema qu'elle scinde : une contrainte
    /// qui vise <c>MoveToLocation</c> doit voir <c>EnterBuilding</c>. C'est la filiation que
    /// <c>PddlAction.Origin</c> conserve.
    /// </summary>
    [Fact]
    public void Le_verificateur_reconnait_une_moitie_scindee_comme_le_schema_d_origine()
    {
        var compiled = DomainCompiler.Compile(Domain, [Parse("require_before(at(agent, group), enter:building)")]);
        Assert.True(compiled.Accepted, compiled.ToString());

        var split = compiled.Domain.Actions.Single(action => action.GeneratedBy is not null);
        var rendered = compiled.Domain.ToPlanningDomain().FindAction(split.Name)!;

        var trace = TrajectoryTrace.Replay(
            Quarter("at(agent, group)"),
            [new PlanningAction(rendered, new Dictionary<string, string>(StringComparer.Ordinal) { ["location"] = "entrepot" })]);

        var verdict = TrajectoryChecker.Check(compiled.Domain, [Parse("forbid(MoveToLocation)")], trace);

        Assert.False(verdict.Satisfied);
        Assert.Contains(verdict.Violations, violation => violation.Contains(split.Name, StringComparison.Ordinal));
    }

    /// <summary>
    /// Une preference de cout ne dit rien de la trajectoire : le verificateur n'a rien a y trouver, et
    /// pretendre le contraire serait le seul moyen de le rendre menteur.
    /// </summary>
    [Fact]
    public void Un_penalize_ne_produit_aucune_violation_de_trajectoire()
    {
        var trace = TrajectoryTrace.Replay(Quarter(), [MoveTo("entrepot")]);

        Assert.True(TrajectoryChecker.Check(Domain, [Parse("penalize(move:street, 2)")], trace).Satisfied);
    }

    // --- Le cycle complet ---------------------------------------------------------------------------

    /// <summary>
    /// Retirer toutes les instructions rend le jeu bit a bit identique a ce qu'il etait : le domaine
    /// d'origine n'est jamais modifie, et le domaine compile repart de lui a chaque changement. C'est
    /// ce qui rend le livrable 15 retirable, comme la specification l'exige de tout post-MVP.
    /// </summary>
    [Fact]
    public void Retirer_les_instructions_rend_le_domaine_d_origine()
    {
        var instructions = new TacticalInstructions(Domain, Settings);

        Assert.True(instructions.Add("forbid(BreakWindow)").Accepted);
        Assert.True(instructions.Add("require_before(at(agent, group), enter:building)").Accepted);
        Assert.NotSame(Domain, instructions.Compiled);

        Assert.True(instructions.Remove("forbid(BreakWindow)"));
        Assert.NotNull(instructions.Compiled.FindAction("BreakWindow"));

        instructions.Clear();

        Assert.Same(Domain, instructions.Compiled);
        Assert.Empty(instructions.Active);
        Assert.Equal(
            Domain.Actions.Select(action => action.Name),
            instructions.Compiled.Actions.Select(action => action.Name));
    }

    /// <summary>
    /// Le journal garde les refus autant que les acceptations, avec le portillon qui a tranche : un
    /// systeme qui n'affiche que ses succes ne prouve rien.
    /// </summary>
    [Fact]
    public void Le_journal_garde_le_portillon_qui_a_refuse()
    {
        var instructions = new TacticalInstructions(Domain, Settings);

        instructions.Add("require_before(barricaded(porte), enter:building)", utterance: "Barricade avant d'entrer.");
        instructions.Add("require_always(at(agent, group))", utterance: "Restez toujours groupes.");
        instructions.Add("forbid(BreakWindow)", utterance: "Ne cassez aucune vitre.");

        var entries = instructions.Log.Entries;

        Assert.Equal(3, entries.Count);
        Assert.Equal(InstructionGate.Grammar, entries[0].Gate);
        Assert.Equal(InstructionGate.Compilation, entries[1].Gate);
        Assert.Null(entries[2].Gate);
        Assert.True(entries[2].Accepted);
        Assert.Single(instructions.Active);
        Assert.All(entries, entry => Assert.NotEmpty(entry.Reason));
    }

    /// <summary>
    /// Le service s'allume pour une session, par la ligne de commande ou l'environnement, sans qu'on
    /// ecrive <c>true</c> dans le fichier versionne — et rien ne l'eteint quand le fichier l'allume.
    /// </summary>
    [Fact]
    public void Le_service_s_allume_pour_une_session_sans_toucher_au_fichier()
    {
        Assert.False(Settings.Enabled);

        Assert.True(Settings.LocallyEnabled(["--demo", InstructionSettings.Argument], null).Enabled);
        Assert.True(Settings.LocallyEnabled([], "1").Enabled);
        Assert.True(Settings.LocallyEnabled([], " On ").Enabled);
        Assert.Same(Settings, Settings.LocallyEnabled([], null));
        Assert.Same(Settings, Settings.LocallyEnabled(["--ia=non"], "0"));

        var lit = Settings.LocallyEnabled([InstructionSettings.Argument], null);
        Assert.Equal(Settings.Endpoint, lit.Endpoint);
        Assert.Equal(Settings.TimeoutMs, lit.TimeoutMs);

        var shipped = new InstructionSettings { Enabled = true };
        Assert.Same(shipped, shipped.LocallyEnabled([], "0"));
    }

    /// <summary>
    /// Le service de langage est livre coupe, et ne change rien aux portillons : les instructions
    /// pre-ecrites suivent exactement le meme chemin qu'une phrase traduite par un modele. C'est ce
    /// qui rend le livrable demontrable sans reseau.
    /// </summary>
    [Fact]
    public void Le_livrable_est_jouable_sans_modele_de_langage()
    {
        Assert.False(Settings.Enabled);
        Assert.True(Settings.PresetsWhenDisabled);

        var instructions = new TacticalInstructions(Domain, Settings);
        var preset = DeliveredPresets.Find("regroupement_avant_entree");

        Assert.NotNull(preset);

        var entry = instructions.Add(preset!.Constraint, utterance: preset.Label);

        Assert.True(entry.Accepted, entry.Reason);
        Assert.Equal(preset.Label, entry.Utterance);
        Assert.Equal(preset.Constraint, entry.Constraint);
    }
}
