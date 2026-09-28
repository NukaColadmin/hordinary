using System.Text.RegularExpressions;
using Hordinary.Agents;
using Hordinary.Config;
using Hordinary.World;

namespace Hordinary.Tests;

/// <summary>
/// Systeme de bruit (specification section 18) : bareme, propagation, attenuation, et surtout
/// localisation approximative — un bruit ne dit jamais exactement d'ou il vient.
/// </summary>
public class NoiseTests
{
    private static NoiseSystem System(NoiseSettings? settings = null) => new(settings);

    // --- Bareme (section 18.1) ------------------------------------------------------------

    /// <summary>
    /// Les pas du groupe, que le jeu et le harnais deroulent par le meme code
    /// (<see cref="SurvivorBand.EmitFootsteps"/>) : un bruit par case franchie et non par passe, et
    /// l'allure de course qui s'entend comme telle.
    /// </summary>
    [Fact]
    public void Un_pas_sonne_une_fois_par_case_et_la_course_s_entend()
    {
        // Un groupe tout juste enrole, sans passe de la boucle : personne n'a encore pose le pied.
        var run = new MissionRun(team: true);

        var first = run.Band.EmitFootsteps();
        Assert.Equal(run.Band.Standing.Count(), first.Count);
        Assert.All(first, step => Assert.Equal(step.Agent.Survivor.Id, step.Noise.SourceId));
        Assert.All(first, step => Assert.Equal(NoiseAction.Walk, step.Noise.Action));

        // Personne n'a bouge : rien ne sonne une seconde fois.
        Assert.Empty(run.Band.EmitFootsteps());

        // La course : les corps avancent, et chaque case franchie sonne comme une course.
        IReadOnlyList<(SurvivorAgent Agent, NoiseEvent Noise)> running = [];
        Assert.True(run.RunUntil(() =>
        {
            run.Band.Move(MissionRun.Step);
            running = run.Band.EmitFootsteps(running: true);
            return running.Count > 0;
        }), run.Trace());

        Assert.All(running, step => Assert.True(step.Noise.Action is NoiseAction.Run or NoiseAction.OpenDoor, step.Noise.ToString()));
    }

    [Fact]
    public void Le_bareme_de_la_specification_est_respecte()
    {
        var settings = NoiseSettings.Default;

        Assert.Equal(1f, settings.LevelOf(NoiseAction.Walk), 3);
        Assert.Equal(3f, settings.LevelOf(NoiseAction.Run), 3);
        Assert.Equal(2f, settings.LevelOf(NoiseAction.OpenDoor), 3);
        Assert.Equal(6f, settings.LevelOf(NoiseAction.ForceDoor), 3);
        Assert.Equal(8f, settings.LevelOf(NoiseAction.BreakWindow), 3);
        Assert.Equal(10f, settings.LevelOf(NoiseAction.Distraction), 3);

        // Barricader fait le bruit de forcer : meme bois, meme outil.
        Assert.Equal(6f, settings.LevelOf(NoiseAction.BarricadeDoor), 3);
    }

    // --- Propagation (section 18.2) -------------------------------------------------------

    [Fact]
    public void L_intensite_percue_diminue_avec_la_distance()
    {
        var noises = System();
        var noise = noises.Emit(NoiseAction.Distraction, GridPosition.Zero, 0d);

        // perceived_intensity = intensity - distance, sans obstacle sur le chemin.
        Assert.Equal(10f, noises.PerceivedIntensity(noise, GridPosition.Zero), 3);
        Assert.Equal(7f, noises.PerceivedIntensity(noise, new GridPosition(3, 0)), 3);
        Assert.Equal(1f, noises.PerceivedIntensity(noise, new GridPosition(9, 0)), 3);

        // Au-dela de sa portee le bruit ne parvient plus.
        Assert.True(noises.PerceivedIntensity(noise, new GridPosition(12, 0)) < 0f);
    }

    [Fact]
    public void Un_obstacle_reduit_l_intensite()
    {
        var map = TestMaps.Parse(
            ".....",
            "..#..",
            ".....");

        var noises = System(new NoiseSettings { ObstacleAttenuation = 2f });
        var noise = noises.Emit(NoiseAction.Distraction, new GridPosition(0, 1), 0d);

        var behindWall = new GridPosition(4, 1);

        float open = noises.PerceivedIntensity(noise, behindWall);
        float muffled = noises.PerceivedIntensity(noise, behindWall, map);

        Assert.Equal(6f, open, 3);
        Assert.Equal(4f, muffled, 3);
    }

    [Fact]
    public void Un_bruit_trop_faible_n_est_pas_entendu()
    {
        var noises = System();
        var walk = noises.Emit(NoiseAction.Walk, GridPosition.Zero, 0d);

        // Marcher (niveau 1) ne s'entend qu'au contact.
        Assert.True(noises.CanHear(walk, GridPosition.Zero, 0d));
        Assert.False(noises.CanHear(walk, new GridPosition(3, 0), 0d));

        var distraction = noises.Emit(NoiseAction.Distraction, GridPosition.Zero, 0d);
        Assert.True(noises.CanHear(distraction, new GridPosition(9, 0), 0d));
    }

    [Fact]
    public void Un_bruit_expire_apres_sa_duree_de_vie()
    {
        var noises = System(new NoiseSettings { Lifetime = 6f });
        var noise = noises.Emit(NoiseAction.Distraction, GridPosition.Zero, 0d);

        Assert.True(noises.CanHear(noise, GridPosition.Zero, 5d));
        Assert.False(noises.CanHear(noise, GridPosition.Zero, 7d));

        Assert.Equal(0, noises.Expire(5d));
        Assert.Equal(1, noises.Expire(7d));
        Assert.Empty(noises.Active);
    }

    [Fact]
    public void Le_plus_fort_des_bruits_audibles_est_retourne_en_premier()
    {
        var noises = System();
        noises.Emit(NoiseAction.Distraction, new GridPosition(9, 0), 0d);
        var near = noises.Emit(NoiseAction.Run, new GridPosition(1, 0), 0d);

        var loudest = noises.Loudest(GridPosition.Zero, 0d);

        Assert.NotNull(loudest);
        Assert.Equal(near.Id, loudest.Value.Noise.Id);
        Assert.Equal(2, noises.Audible(GridPosition.Zero, 0d).Count());
    }

    // --- Localisation approximative (section 18.3, etape 1) --------------------------------

    [Fact]
    public void Un_bruit_proche_est_parfaitement_situe()
    {
        var noises = System();
        var noise = noises.Emit(NoiseAction.Distraction, new GridPosition(1, 0), 0d);
        float perceived = noises.PerceivedIntensity(noise, GridPosition.Zero);

        Assert.Equal(0, noises.LocalisationRadius(noise, perceived));
        Assert.Equal(noise.Position, noises.Localize(noise, perceived, new Random(1)));
    }

    [Fact]
    public void Un_bruit_lointain_ou_etouffe_est_mal_situe()
    {
        var map = TestMaps.Parse(
            "..........",
            "....#.....",
            "..........");

        var noises = System();
        var noise = noises.Emit(NoiseAction.Distraction, new GridPosition(9, 1), 0d);

        var listener = new GridPosition(0, 1);
        float perceived = noises.PerceivedIntensity(noise, listener, map);

        // 9 cases de distance et un mur : il ne reste presque rien, et l'incertitude est maximale.
        int radius = noises.LocalisationRadius(noise, perceived);
        Assert.True(radius > 0, "Un bruit venu de loin devrait etre mal situe.");

        var believed = noises.Localize(noise, perceived, new Random(7));
        Assert.True(
            believed.ChebyshevDistanceTo(noise.Position) <= radius,
            $"{believed} devrait rester dans le rayon d'erreur de {radius}.");
    }

    [Fact]
    public void L_erreur_de_localisation_croit_avec_la_distance()
    {
        var noises = System();
        var noise = noises.Emit(NoiseAction.Distraction, GridPosition.Zero, 0d);

        int near = noises.LocalisationRadius(noise, noises.PerceivedIntensity(noise, new GridPosition(2, 0)));
        int far = noises.LocalisationRadius(noise, noises.PerceivedIntensity(noise, new GridPosition(9, 0)));

        Assert.True(far > near, $"Le rayon d'erreur devrait croitre avec la distance ({near} -> {far}).");
        Assert.True(far <= noises.Settings.MaxLocalisationError);
    }

    // --- Ecoute par un agent --------------------------------------------------------------

    [Fact]
    public void Un_agent_memorise_le_bruit_comme_une_croyance_de_source_Sound()
    {
        var map = TestMaps.Parse("..........", "..........");
        var world = new WorldState(map);
        world.Noises.Emit(NoiseAction.Distraction, new GridPosition(6, 0), 0d, "alice");

        var bob = new Survivor("bob", "Bob", new AgentKnowledge("bob", map.Width, map.Height));

        var heard = new PerceptionSystem().Listen(bob, world, new Random(3), 0d);

        var belief = Assert.Single(bob.Knowledge.KnownNoises.Values);
        Assert.Equal(KnowledgeSource.Sound, belief.Source);
        Assert.Single(heard);

        // Il croit savoir d'ou vient le bruit, mais rien ne lui apprend qui l'a fait.
        Assert.Empty(bob.Knowledge.KnownEntities);
    }

    [Fact]
    public void On_n_entend_pas_ses_propres_bruits()
    {
        var map = TestMaps.Parse("..........");
        var world = new WorldState(map);
        world.Noises.Emit(NoiseAction.Distraction, new GridPosition(2, 0), 0d, "alice");

        var alice = new Survivor("alice", "Alice", new AgentKnowledge("alice", map.Width, map.Height));

        Assert.Empty(new PerceptionSystem().Listen(alice, world, new Random(1), 0d));
        Assert.Empty(alice.Knowledge.KnownNoises);
    }

    [Fact]
    public void La_confiance_dans_un_bruit_decroit_puis_il_est_oublie()
    {
        var map = TestMaps.Parse("..........");
        var world = new WorldState(map);
        world.Noises.Emit(NoiseAction.Distraction, new GridPosition(1, 0), 0d, "alice");

        var settings = new Hordinary.Config.KnowledgeSettings { NoiseConfidenceDecay = 0.1f };
        var bob = new Survivor("bob", "Bob", new AgentKnowledge("bob", map.Width, map.Height, default, settings));

        new PerceptionSystem().Listen(bob, world, new Random(1), 0d);
        var belief = Assert.Single(bob.Knowledge.KnownNoises.Values);

        // confidence = max(0, 1 - 0,1 * temps ecoule) : dix secondes de memoire.
        Assert.Equal(1f, bob.Knowledge.ConfidenceIn(belief, 0d), 2);
        Assert.Equal(0.5f, bob.Knowledge.ConfidenceIn(belief, 5d), 2);
        Assert.Equal(0f, bob.Knowledge.ConfidenceIn(belief, 20d), 3);

        Assert.Null(bob.Knowledge.MostRecentNoise(20d));
        Assert.Equal(1, bob.Knowledge.ForgetFadedNoises(20d));
        Assert.Empty(bob.Knowledge.KnownNoises);
    }

    [Fact]
    public void Un_meme_bruit_n_est_pas_rafraichi_a_chaque_ecoute()
    {
        var map = TestMaps.Parse("..........");
        var world = new WorldState(map);
        world.Noises.Emit(NoiseAction.Distraction, new GridPosition(1, 0), 0d, "alice");

        var bob = new Survivor("bob", "Bob", new AgentKnowledge("bob", map.Width, map.Height));
        var perception = new PerceptionSystem();

        Assert.Single(perception.Listen(bob, world, new Random(1), 0d));

        // Le bruit dure, mais il ne redevient pas neuf : sinon un vacarme continu resterait
        // eternellement « recent » et figerait les zombies dessus.
        Assert.Empty(perception.Listen(bob, world, new Random(1), 2d));
        Assert.Equal(0d, Assert.Single(bob.Knowledge.KnownNoises.Values).LastObservedAt, 3);
    }

    // --- La graine du bruit ---------------------------------------------------------------

    private static GameConfig Config => MissionRun.Config;

    /// <summary>
    /// Une rue droite, sans obstacle, et un bruit emis a l'autre bout : il arrive trop affaibli pour
    /// etre bien situe — <c>10 - 9 = 1</c>, soit un rayon d'erreur de trois cases — mais assez fort
    /// pour etre entendu, ce qui est exactement la fenetre ou la graine se voit. De pres,
    /// <c>Localize</c> rend la position exacte et tous les generateurs se valent ; un mur de plus et
    /// le bruit n'est plus audible du tout.
    /// </summary>
    private static GridMap Rue() => TestMaps.Parse(
        "..........",
        "..........",
        "..........");

    /// <summary>Ou un survivant place le bruit qu'il vient d'entendre, avec ce generateur-la.</summary>
    private static GridPosition Believed(int seed)
    {
        var world = new WorldState(Rue());
        var band = new SurvivorBand(world, Config);

        var agent = band.Enlist(
            MissionRun.Roster.Survivors[0], new GridPosition(0, 1), MissionRun.Goals, MissionRun.Domain);

        world.Noises.Emit(NoiseAction.Distraction, new GridPosition(9, 1), 0d, "quelqu_un_d_autre");

        band.Perceive(0d, new Random(seed));

        return Assert.Single(agent.Survivor.Knowledge.KnownNoises.Values).Value;
    }

    /// <summary>
    /// Le generateur donne au groupe arrive bien jusqu'a <see cref="NoiseSystem.Localize"/>.
    ///
    /// <para>
    /// Le test porte sur la chaine entiere — <c>SurvivorBand.Perceive</c>, <c>SurvivorAgent.Perceive</c>,
    /// <c>PerceptionSystem.Listen</c>, <c>Localize</c> — parce que c'est la ou le parametre peut se
    /// perdre : chacun de ces maillons le declare optionnel, et un maillon qui oublie de le
    /// transmettre retombe sur <see cref="Random.Shared"/> sans rien casser. Douze graines qui
    /// donnent douze fois le meme endroit voudraient dire que le generateur n'est pas lu ; deux
    /// passages sur la meme graine qui different voudraient dire qu'il ne sert a rien.
    /// </para>
    /// </summary>
    [Fact]
    public void Le_generateur_donne_au_groupe_situe_reellement_les_bruits()
    {
        Assert.Equal(Believed(4), Believed(4));

        var places = Enumerable.Range(1, 12).Select(Believed).Distinct().ToList();

        Assert.True(
            places.Count > 1,
            "Douze graines donnent la meme position : le generateur passe au groupe n'atteint plus Localize.");
    }

    /// <summary>
    /// Le jeu, lui, en passe un — et c'est ce qui ferme l'invariant « a graine egale, resultat egal »
    /// du cote de la partie jouee, pas seulement du cote des tests.
    ///
    /// <para>
    /// Ce test lit du <b>texte source</b>, ce qu'aucun autre ne fait ici, et c'est un pis-aller
    /// assume : le cablage vit dans <c>Scripts/</c>, que cette suite ne compile pas — elle ne
    /// reference pas Godot, c'est tout son interet. Le choix est donc entre lire le fichier et ne
    /// rien verrouiller du tout. Ce qu'il attrape est precisement ce qui est arrive une fois :
    /// quelqu'un retire le parametre, le jeu compile, la partie cesse d'etre rejouable, et aucune
    /// ligne rouge ne le dit. Ce qu'il n'attrape pas : un cablage qui compile et se trompe — le
    /// generateur reseme a chaque frame, par exemple. Seule une partie rejouee le montrerait.
    /// </para>
    /// </summary>
    [Fact]
    public void Le_jeu_seme_son_generateur_de_bruit_et_le_passe_a_la_perception()
    {
        string path = Path.Combine(DemoMap.RepositoryRoot, "Scripts", "Game", "GameRoot.cs");
        string source = File.ReadAllText(path);

        Assert.True(
            source.Contains("_noiseRng = new Random(", StringComparison.Ordinal),
            "GameRoot ne seme plus son generateur de bruit : une mission rejouee ne sera plus identique.");

        var calls = Regex.Matches(source, @"_band\.(?:Perceive|Advance)\(([^;]*)\);");

        Assert.True(calls.Count > 0, "Aucune passe de perception du groupe dans GameRoot : le motif a change, ce test aussi.");

        Assert.All(calls, call => Assert.True(
            call.Groups[1].Value.Contains("_noiseRng", StringComparison.Ordinal),
            $"« {call.Value.Trim()} » ne passe aucun generateur : NoiseSystem.Localize retombera sur Random.Shared."));
    }
}
