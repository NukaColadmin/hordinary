using Hordinary.AI.Navigation;
using Hordinary.World;

namespace Hordinary.Tests;

public class PathFollowerTests
{
    private static void Simulate(PathFollower follower, float seconds, Func<GridPosition, bool>? traversable = null)
    {
        const float step = 1f / 60f;
        for (float t = 0f; t < seconds; t += step) follower.Advance(step, traversable);
    }

    [Fact]
    public void Le_survivant_parcourt_le_chemin_jusqu_a_la_destination()
    {
        var path = new List<GridPosition>
        {
            new(0, 0), new(1, 0), new(2, 0), new(2, 1), new(2, 2),
        };

        var follower = new PathFollower { Speed = 4f };
        follower.Teleport(path[0]);

        GridPosition? arrival = null;
        follower.Arrived += cell => arrival = cell;

        follower.SetPath(path);
        Assert.False(follower.IsFinished);

        Simulate(follower, 2f); // 4 pas a 4 cellules/seconde

        Assert.True(follower.IsFinished);
        Assert.Equal(new GridPosition(2, 2), follower.CurrentCell);
        Assert.Equal(new GridPosition(2, 2), arrival);
    }

    [Fact]
    public void Le_survivant_progresse_sans_sauter_de_case()
    {
        var path = new List<GridPosition> { new(0, 0), new(1, 0), new(2, 0) };

        var follower = new PathFollower { Speed = 1f };
        follower.Teleport(path[0]);
        follower.SetPath(path);

        Simulate(follower, 0.5f);

        // A mi-parcours de la premiere case : entre le depart et la case suivante.
        Assert.True(follower.Position.X is > 0.3f and < 0.7f);
        Assert.False(follower.IsFinished);
    }

    [Fact]
    public void Un_obstacle_decouvert_interrompt_le_chemin()
    {
        var path = new List<GridPosition> { new(0, 0), new(1, 0), new(2, 0) };
        var blockedCell = new GridPosition(2, 0);

        var follower = new PathFollower { Speed = 4f };
        follower.Teleport(path[0]);
        follower.SetPath(path);

        GridPosition? reported = null;
        follower.Blocked += cell => reported = cell;

        Simulate(follower, 1f, cell => cell != blockedCell);

        Assert.Equal(blockedCell, reported);
        Assert.False(follower.HasPath);
        Assert.Equal(new GridPosition(1, 0), follower.CurrentCell);
    }

    [Fact]
    public void Un_chemin_reduit_a_la_case_de_depart_est_immediatement_termine()
    {
        var follower = new PathFollower();
        follower.Teleport(new GridPosition(3, 3));

        GridPosition? arrival = null;
        follower.Arrived += cell => arrival = cell;

        follower.SetPath([new GridPosition(3, 3)]);
        follower.Advance(1f / 60f);

        Assert.True(follower.IsFinished);
        Assert.Equal(new GridPosition(3, 3), arrival);
    }
}
