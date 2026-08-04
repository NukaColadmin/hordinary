using Hordinary.Agents;
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
}
