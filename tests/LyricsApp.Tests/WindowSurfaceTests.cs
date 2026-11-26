using System.Windows;

namespace LyricsApp.Tests;

public class WindowSurfaceTests
{
    private static readonly Rect Start = new(100, 200, 460, 640);

    private static Rect Resize(string edges, double dx, double dy) =>
        WindowSurface.Resize(Start, edges.Contains('L'), edges.Contains('T'), edges.Contains('R'), edges.Contains('B'), dx, dy, minWidth: 240, minHeight: 120);

    [Fact]
    public void Right_and_bottom_edges_grow_the_window_in_place()
    {
        Assert.Equal(new Rect(100, 200, 500, 640), Resize("R", 40, 99));
        Assert.Equal(new Rect(100, 200, 460, 610), Resize("B", 99, -30));
        Assert.Equal(new Rect(100, 200, 500, 700), Resize("RB", 40, 60));
    }

    [Fact]
    public void Left_and_top_edges_keep_the_opposite_edge_still()
    {
        var left = Resize("L", -40, 99);
        Assert.Equal(new Rect(60, 200, 500, 640), left);
        Assert.Equal(Start.Right, left.Right);

        var topLeft = Resize("LT", 30, 50);
        Assert.Equal(new Rect(130, 250, 430, 590), topLeft);
        Assert.Equal(Start.Bottom, topLeft.Bottom);
    }

    [Fact]
    public void Mixed_corners_move_only_their_own_edges()
    {
        Assert.Equal(new Rect(100, 150, 520, 690), Resize("TR", 60, -50));
        Assert.Equal(new Rect(40, 200, 520, 690), Resize("LB", -60, 50));
    }

    [Fact]
    public void The_window_stops_at_its_minimum_size()
    {
        // Dragged far past the opposite edge: the size stops, and so does the edge being dragged.
        Assert.Equal(new Rect(100, 200, 240, 120), Resize("RB", -1000, -1000));
        var topLeft = Resize("LT", 1000, 1000);
        Assert.Equal(new Rect(320, 720, 240, 120), topLeft);
        Assert.Equal(Start.Right, topLeft.Right);
        Assert.Equal(Start.Bottom, topLeft.Bottom);
    }
}
