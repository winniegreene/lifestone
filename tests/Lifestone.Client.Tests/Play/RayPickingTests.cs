using System.Numerics;
using Lifestone.Client.Play;
using Lifestone.Client.Rendering;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Tests.PortalSpace;
using Lifestone.Data.Geometry;

namespace Lifestone.Client.Tests.Play;

/// <summary>Picking and selection (G5 W7): the ray from a window point, the nearest hit, the player left out, the projection back, the selection's clearing.</summary>
public sealed class RayPickingTests
{
    private const float Width = 800f;
    private const float Height = 600f;
    private const float Tolerance = 1e-3f;

    // A camera at the origin looking along +Z (right +X, up +Y), 60 degrees vertical over an 800x600 view.
    private static PickView View(CameraLens? lens = null) =>
        new(lens ?? CameraLens.Perspective(60f, 0.1f), Width, Height, Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);

    [Fact]
    public void The_ray_through_the_centre_of_the_view_is_the_view_axis()
    {
        var ray = View().Ray(Width / 2f, Height / 2f);

        Assert.Equal(Vector3.Zero, ray.Origin);
        Assert.True(Vector3.Distance(Vector3.UnitZ, ray.Direction) < Tolerance, $"{ray.Direction}");
    }

    [Fact]
    public void The_rays_through_the_corners_follow_the_lens_sides()
    {
        // 60 degrees vertical: the half height at unit distance is tan 30; the half width is 4/3 of it.
        var halfHeight = MathF.Tan(MathF.PI / 6f);
        var halfWidth = halfHeight * Width / Height;
        var view = View();

        var topLeft = view.Ray(0f, 0f).Direction;
        var bottomRight = view.Ray(Width, Height).Direction;

        Assert.True(Vector3.Distance(Vector3.Normalize(new Vector3(-halfWidth, halfHeight, 1f)), topLeft) < Tolerance, $"{topLeft}");
        Assert.True(Vector3.Distance(Vector3.Normalize(new Vector3(halfWidth, -halfHeight, 1f)), bottomRight) < Tolerance, $"{bottomRight}");
    }

    [Fact]
    public void An_off_centre_frustum_moves_the_axis_off_the_middle_of_the_view_and_the_projection_agrees()
    {
        // The D40 hook: the view axis sits at 40 % of the width and 30 % of the height.
        const float frustumHeight = 0.2f;
        var (ox, oy) = CameraLens.OffsetForCentre(frustumHeight, Width / Height, 0.4f, 0.3f);
        var view = View(CameraLens.Frustum(frustumHeight, 0.05f, ox, oy));

        var axis = view.Ray(Width * 0.4f, Height * 0.3f).Direction;
        var middle = view.Ray(Width / 2f, Height / 2f).Direction;

        Assert.True(Vector3.Distance(Vector3.UnitZ, axis) < Tolerance, $"axis ray {axis}");
        Assert.True(middle.X > 0f && middle.Y < 0f, $"the middle of the window looks right of and below the axis: {middle}");
        Assert.True(view.TryProject(new Vector3(0f, 0f, 10f), out var screen));
        Assert.True(Vector2.Distance(new Vector2(Width * 0.4f, Height * 0.3f), screen) < 0.05f, $"{screen}");
    }

    [Fact]
    public void Projecting_the_point_a_ray_passes_through_gives_the_pixel_the_ray_came_from()
    {
        var view = View();
        foreach (var (x, y) in new[] { (0f, 0f), (Width, Height), (123f, 456f), (Width / 2f, Height / 2f), (700f, 20f) })
        {
            var ray = view.Ray(x, y);
            Assert.True(view.TryProject(ray.Origin + (ray.Direction * 7f), out var screen));
            Assert.True(Vector2.Distance(new Vector2(x, y), screen) < 0.05f, $"({x}, {y}) came back as {screen}");
        }
    }

    [Fact]
    public void A_point_behind_the_camera_does_not_project()
    {
        Assert.False(View().TryProject(new Vector3(0f, 0f, -5f), out _));
        Assert.False(View().TryProjectSphere(new Vector3(0f, 0f, -5f), 1f, out var rect, out var onScreen));
        Assert.False(onScreen);
        Assert.Equal(default, rect);
    }

    [Fact]
    public void A_sphere_projects_to_a_rectangle_round_its_centre_and_is_off_screen_when_it_misses_the_view()
    {
        var view = View();

        Assert.True(view.TryProjectSphere(new Vector3(0f, 0f, 10f), 1f, out var rect, out var onScreen));
        Assert.True(onScreen);
        Assert.True(MathF.Abs(rect.X + (rect.Width / 2f) - (Width / 2f)) < 0.05f && MathF.Abs(rect.Y + (rect.Height / 2f) - (Height / 2f)) < 0.05f, $"{rect}");
        Assert.True(rect.Width > 0f && rect.Height > 0f);
        Assert.True(view.TryProjectSphere(new Vector3(100f, 0f, 10f), 1f, out _, out var farRight));
        Assert.False(farRight);
    }

    // Shapes (GfxObj ids): a sphere with no polygons, a quad filling the middle of its sphere, and a small triangle pair off to one side of its sphere.
    private const uint SphereOnly = 0x01000001;
    private const uint Quad = 0x01000002;
    private const uint Corner = 0x01000003;
    private const uint TwoLayers = 0x01000004;

    private static readonly Dictionary<uint, PickShape> Shapes = new()
    {
        [SphereOnly] = new PickShape(Vector3.Zero, 1f, []),
        [Quad] = new PickShape(Vector3.Zero, 1.5f, [FaceTowardsMinusZ(new Vector3(-1f, -1f, 0f), new Vector3(1f, 1f, 0f))]),
        [Corner] = new PickShape(Vector3.Zero, 2.2f, [FaceTowardsMinusZ(new Vector3(1f, 1f, 0f), new Vector3(1.4f, 1.4f, 0f))]),

        // Two quads in one part: the far one (z = 1.5) is listed first, the near one (z = -1.5) second.
        [TwoLayers] = new PickShape(Vector3.Zero, 2.5f, [FaceTowardsMinusZ(new Vector3(-1f, -1f, 1.5f), new Vector3(1f, 1f, 1.5f)), FaceTowardsMinusZ(new Vector3(-1f, -1f, -1.5f), new Vector3(1f, 1f, -1.5f))]),
    };

    private static readonly PickRay Forward = new(Vector3.Zero, Vector3.UnitZ);

    // The rectangle min..max in the z = 0 plane as two counter-clockwise triangles whose normal is -Z (the front face is towards a camera at -Z).
    private static SubMesh FaceTowardsMinusZ(Vector3 min, Vector3 max)
    {
        var z = min.Z;
        Vector3[] positions = [new(min.X, min.Y, z), new(min.X, max.Y, z), new(max.X, min.Y, z), new(max.X, max.Y, z)];
        return new SubMesh(0x08000001, MeshSide.Positive, TextureAddressMode.Wrap, positions, new Vector3[4], new Vector2[4], [0, 1, 2, 1, 3, 2]);
    }

    private static PickShape? ShapeOf(uint id) => Shapes.GetValueOrDefault(id);

    private static PickPart Part(uint guid, uint gfxObj, Vector3 origin, Quaternion? orientation = null, float scale = 1f, int index = 0) =>
        new(guid, index, gfxObj, origin, orientation ?? Quaternion.Identity, new Vector3(scale));

    private static PickHit? Pick(in PickRay ray, PickPart[] parts, uint exclude = 0, bool polygons = true, bool retail = false) =>
        RayPicking.Pick(ray, parts, ShapeOf, exclude, polygons, retail);

    [Fact]
    public void The_retail_picking_flag_and_the_all_sizes_switch_are_off_by_default()
    {
        Assert.False(PlayOptions.Parse([]).RetailPicking);
        Assert.False(PlayOptions.Parse([]).AllSizes);
        Assert.True(PlayOptions.Parse(["--retail-picking"]).RetailPicking);
        Assert.True(PlayOptions.Parse(["--all-sizes"]).AllSizes);
        Assert.Contains(Lifestone.Client.Options.OptionCatalog.Registry.Groups.SelectMany(g => g.Options), o => o.Name == "--retail-picking");
    }

    [Fact]
    public void A_part_offers_its_nearest_polygon_by_default_and_the_first_one_under_retail_picking()
    {
        // Object 1's part lists a far quad (world z 11.5) before a near one (8.5); object 2's quad is at 10. Retail stops at the first polygon the ray hits,
        // so the far quad's time loses to object 2 (the flag, --retail-picking); the default tests every polygon and the nearest surface of object 1 wins.
        var layered = Part(1, TwoLayers, new Vector3(0f, 0f, 10f));
        var between = Part(2, Quad, new Vector3(0f, 0f, 10f));

        var fixedPick = Pick(Forward, [layered, between]);
        var retailPick = Pick(Forward, [layered, between], retail: true);

        Assert.Equal(1u, fixedPick?.Guid);
        Assert.True(Math.Abs(fixedPick!.Value.Distance - 8.5f) < Tolerance, $"{fixedPick}");
        Assert.Equal(2u, retailPick?.Guid);
        Assert.True(Math.Abs(retailPick!.Value.Distance - 10f) < Tolerance, $"{retailPick}");
        Assert.Equal(1u, Pick(Forward, [layered], retail: true)?.Guid); // alone, either mode picks it
    }

    [Fact]
    public void Hit_times_are_world_units_by_default_and_each_parts_local_units_under_retail_picking()
    {
        // Object 1's quad is 8 m ahead at scale 1; object 2's is 10 m ahead at scale 2. In world units 1 is nearer; in object 2's local units (a ray unit in its
        // scaled frame) it is 5, nearer than 8, so retail's comparison picks object 2.
        var unscaled = Part(1, Quad, new Vector3(0f, 0f, 8f));
        var scaled = Part(2, Quad, new Vector3(0f, 0f, 10f), scale: 2f);

        var fixedPick = Pick(Forward, [unscaled, scaled]);
        var retailPick = Pick(Forward, [unscaled, scaled], retail: true);

        Assert.Equal(1u, fixedPick?.Guid);
        Assert.True(Math.Abs(fixedPick!.Value.Distance - 8f) < Tolerance, $"{fixedPick}");
        Assert.Equal(2u, retailPick?.Guid);
        Assert.True(Math.Abs(retailPick!.Value.Distance - 5f) < Tolerance, $"{retailPick}");
    }

    [Fact]
    public void The_two_modes_agree_when_nothing_is_scaled_and_each_part_has_one_polygon_on_the_ray()
    {
        var near = Part(1, Quad, new Vector3(0f, 0f, 6f));
        var far = Part(2, Quad, new Vector3(0f, 0f, 12f));

        Assert.Equal(Pick(Forward, [far, near]), Pick(Forward, [far, near], retail: true));
        Assert.Equal(1u, Pick(Forward, [far, near], retail: true)?.Guid);
    }

    [Fact]
    public void A_polygon_hit_beats_the_nearer_sphere_that_the_ray_passes_through_without_touching_its_polygons()
    {
        // Object 1's sphere is entered first, but its only polygon is off to the side; object 2 further on has a polygon on the ray.
        PickPart[] parts = [Part(1, Corner, new Vector3(0f, 0f, 5f)), Part(2, Quad, new Vector3(0f, 0f, 10f))];

        var click = Pick(Forward, parts);
        var reversed = Pick(Forward, [parts[1], parts[0]]);

        Assert.Equal(2u, click?.Guid);
        Assert.True(click!.Value.Polygon);
        Assert.True(MathF.Abs(click.Value.Distance - 10f) < Tolerance, $"{click}");
        Assert.Equal(2u, reversed?.Guid);
    }

    [Fact]
    public void A_hover_search_tests_the_spheres_only_so_the_nearest_sphere_wins()
    {
        PickPart[] parts = [Part(1, Corner, new Vector3(0f, 0f, 5f)), Part(2, Quad, new Vector3(0f, 0f, 10f))];

        var hover = Pick(Forward, parts, polygons: false);

        Assert.Equal(1u, hover?.Guid);
        Assert.False(hover!.Value.Polygon);
        Assert.True(MathF.Abs(hover.Value.Distance - 2.8f) < Tolerance, $"{hover}");
    }

    [Fact]
    public void With_no_polygon_hit_the_nearest_sphere_wins_whatever_the_order_of_the_list()
    {
        PickPart[] parts = [Part(1, Corner, new Vector3(0f, 0f, 5f)), Part(2, Corner, new Vector3(0f, 0f, 12f)), Part(3, SphereOnly, new Vector3(0f, 0f, 8f))];

        var forward = Pick(Forward, parts);
        var reversed = Pick(Forward, [parts[2], parts[1], parts[0]]);

        Assert.Equal(1u, forward?.Guid);
        Assert.Equal(1u, reversed?.Guid);
        Assert.False(forward!.Value.Polygon);
        Assert.True(MathF.Abs(forward.Value.Distance - 2.8f) < Tolerance, $"{forward}");
    }

    [Fact]
    public void The_nearest_polygon_wins_whatever_the_order_of_the_list_and_a_sphere_hit_cannot_beat_it()
    {
        PickPart[] parts = [Part(1, Quad, new Vector3(0f, 0f, 5f)), Part(2, Quad, new Vector3(0f, 0f, 9f)), Part(3, SphereOnly, new Vector3(0f, 0f, 3f))];

        // The sphere-only object is nearest by sphere, but a polygon is hit, so the nearer quad (object 1) wins.
        Assert.Equal(1u, Pick(Forward, parts)?.Guid);
        Assert.Equal(1u, Pick(Forward, [parts[1], parts[0], parts[2]])?.Guid);
        Assert.Equal(1u, Pick(Forward, [parts[2], parts[1], parts[0]])?.Guid);
    }

    [Fact]
    public void A_part_is_hit_where_its_frame_puts_it_not_at_the_objects_origin()
    {
        // One object whose part sits 5 m to the right of where the object is; the object's own origin is on the forward axis.
        var part = Part(4, Quad, new Vector3(5f, 0f, 10f), index: 3);
        var towardsPart = new PickRay(Vector3.Zero, Vector3.Normalize(new Vector3(5f, 0f, 10f)));

        var hit = Pick(towardsPart, [part]);

        Assert.Equal(4u, hit?.Guid);
        Assert.Equal(3, hit!.Value.PartIndex);
        Assert.Null(Pick(Forward, [part]));
    }

    [Fact]
    public void The_parts_orientation_and_scale_place_its_polygons()
    {
        // The corner rectangle spans (1, 1)..(1.4, 1.4) in the part frame: scaled by 2 it is at (2, 2)..(2.8, 2.8), 20 m ahead.
        var scaled = Part(1, Corner, new Vector3(0f, 0f, 20f), scale: 2f);
        var unscaled = Part(2, Corner, new Vector3(0f, 0f, 20f));
        var at = new PickRay(Vector3.Zero, Vector3.Normalize(new Vector3(2.4f, 2.4f, 20f)));

        var hit = Pick(at, [scaled]);
        Assert.Equal(1u, hit?.Guid);
        Assert.True(hit!.Value.Polygon);
        Assert.Null(Pick(at, [unscaled])); // outside the unscaled part's sphere altogether

        // Turned half way round the vertical, the quad faces away from the eye: its sphere is entered, its polygons are back faces, the sphere is the hit.
        var turned = Part(3, Quad, new Vector3(0f, 0f, 10f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI));
        var back = Pick(Forward, [turned]);
        Assert.Equal(3u, back?.Guid);
        Assert.False(back!.Value.Polygon);
    }

    [Fact]
    public void The_player_is_not_pickable_and_the_object_behind_it_is()
    {
        PickPart[] parts = [Part(0x50000001, Quad, new Vector3(0f, 0f, 3f)), Part(0x80000A02, Quad, new Vector3(0f, 0f, 9f))];

        Assert.Equal(0x80000A02u, Pick(Forward, parts, exclude: 0x50000001)?.Guid);
        Assert.Null(Pick(Forward, [parts[0]], exclude: 0x50000001));
    }

    [Fact]
    public void The_ray_is_ten_thousand_long_and_a_sphere_the_eye_is_inside_or_behind_it_is_not_hit()
    {
        Assert.Equal(1u, Pick(Forward, [Part(1, Quad, new Vector3(0f, 0f, RayPicking.RayLength - 10f))])?.Guid);
        Assert.Null(Pick(Forward, [Part(2, Quad, new Vector3(0f, 0f, RayPicking.RayLength + 10f))]));
        Assert.Null(Pick(Forward, [Part(3, Quad, new Vector3(0f, 0f, 0.5f))])); // the eye is inside the sphere: retail's origin test
        Assert.Null(Pick(Forward, [Part(4, Quad, new Vector3(0f, 0f, -6f))]));
        Assert.Null(Pick(Forward, []));
        Assert.Null(Pick(Forward, [Part(5, 0x01FFFFFF, new Vector3(0f, 0f, 6f))])); // no shape: cannot be hit
    }

    [Fact]
    public void A_click_beside_every_object_picks_nothing_and_a_click_at_the_projected_point_of_a_part_finds_it()
    {
        var view = View();
        var part = Part(7, SphereOnly, new Vector3(2f, -1f, 8f));

        Assert.Null(Pick(view.Ray(10f, 10f), [part]));
        Assert.True(view.TryProjectSphere(part.Origin, 1f, out var rect, out _));
        Assert.Equal(7u, Pick(view.Ray(rect.X + (rect.Width / 2f), rect.Y + (rect.Height / 2f)), [part])?.Guid);
    }

    [Fact]
    public void Selecting_tells_the_listeners_once_and_selecting_the_same_object_again_does_not()
    {
        var selection = new ObjectSelection();
        var changes = new List<(uint, uint)>();
        selection.Changed += (previous, current) => changes.Add((previous, current));

        Assert.True(selection.Select(5));
        Assert.False(selection.Select(5));
        Assert.True(selection.Select(6));
        Assert.True(selection.Clear());
        Assert.False(selection.Clear());

        Assert.Equal([(0u, 5u), (5u, 6u), (6u, 0u)], changes);
        Assert.False(selection.HasSelection);
    }

    [Fact]
    public void The_selection_clears_when_its_object_is_deleted_and_not_when_another_is()
    {
        var bus = new FakeEventBus();
        using var selection = new ObjectSelection();
        selection.Attach(bus);
        selection.Select(0x80000A03);

        bus.Publish(new ObjectDeleted(0x80000A01));
        Assert.Equal(0x80000A03u, selection.Selected);

        bus.Publish(new ObjectDeleted(0x80000A03));
        Assert.False(selection.HasSelection);
    }

    [Fact]
    public void The_selection_clears_on_a_teleport_and_stops_listening_when_disposed()
    {
        var bus = new FakeEventBus();
        var selection = new ObjectSelection();
        selection.Attach(bus);
        selection.Select(9);

        bus.Publish(new PlayerTeleportAnnounced(1));
        Assert.False(selection.HasSelection);

        selection.Select(9);
        selection.Dispose();
        bus.Publish(new ObjectDeleted(9));
        Assert.Equal(9u, selection.Selected);
        Assert.Equal(0, bus.Subscribers<ObjectDeleted>());
    }
}
