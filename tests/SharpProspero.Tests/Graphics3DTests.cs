using SharpProspero.Graphics;
using SharpProspero.Numerics;
using System.Numerics;
using Xunit;

namespace SharpProspero.Tests;

public sealed class Graphics3DTests
{
    private const float Tol = 1e-3f;

    [Fact]
    public void Perspective_MapsNearToZeroAndFarToOne()
    {
        Matrix4x4 p = Camera3D.Perspective(MathUtil.DegreesToRadians(60), 1.6f, 0.5f, 100f);
        Vector4 near = Vector4.Transform(new Vector4(0, 0, -0.5f, 1), p);
        Vector4 far = Vector4.Transform(new Vector4(0, 0, -100f, 1), p);
        Assert.Equal(0f, near.Z / near.W, 3);
        Assert.Equal(1f, far.Z / far.W, 3);
    }

    [Fact]
    public void Camera_WorldToScreen_TargetLandsAtCenter()
    {
        var cam = new Camera3D { Position = new(0, 0, 5), Target = Vector3.Zero, AspectRatio = 16f / 9f };
        Vector3 screen = cam.WorldToScreen(Vector3.Zero, 1920, 1080, out bool visible);
        Assert.True(visible);
        Assert.Equal(960f, screen.X, 0);
        Assert.Equal(540f, screen.Y, 0);
    }

    [Fact]
    public void Camera_ScreenToRay_CenterPointsForward()
    {
        var cam = new Camera3D { Position = new(0, 0, 5), Target = Vector3.Zero };
        Ray ray = cam.ScreenToRay(960, 540, 1920, 1080);
        Assert.True(Vector3.Dot(ray.Direction, new Vector3(0, 0, -1)) > 0.999f);
    }

    [Fact]
    public void Transform_ComposesAndDirectionsAreRightHanded()
    {
        var t = new Transform(new(1, 2, 3), Quaternion.Identity, Vector3.One);
        Assert.Equal(new Vector3(1, 2, 3), t.TransformPoint(Vector3.Zero));
        Assert.True(Vector3.Distance(t.Forward, new Vector3(0, 0, -1)) < Tol);
        Assert.True(Vector3.Distance(t.Right, new Vector3(1, 0, 0)) < Tol);
        Assert.True(Vector3.Distance(t.Up, new Vector3(0, 1, 0)) < Tol);
    }

    [Fact]
    public void Ray_HitsSphereBoxPlaneAndTriangle()
    {
        var ray = new Ray(new(0, 0, 5), new(0, 0, -1));
        Assert.Equal(4f, ray.IntersectSphere(new BoundingSphere(Vector3.Zero, 1f)), 3);
        Assert.Equal(4f, ray.IntersectBox(new BoundingBox(new(-1, -1, -1), new(1, 1, 1))), 3);
        Assert.Equal(5f, ray.IntersectPlane(new Plane(0, 0, 1, 0)), 3);
        Assert.Equal(5f, ray.IntersectTriangle(new(-1, -1, 0), new(1, -1, 0), new(0, 1, 0)), 3);
        Assert.True(new Ray(new(0, 0, 5), new(0, 1, 0)).IntersectSphere(new BoundingSphere(Vector3.Zero, 1f)) < 0);
    }

    [Fact]
    public void BoundingBox_FromPointsContainsAndTransforms()
    {
        Vector3[] pts = [new(-1, -2, -3), new(4, 5, 6), new(0, 0, 0)];
        BoundingBox box = BoundingBox.FromPoints(pts);
        Assert.Equal(new Vector3(-1, -2, -3), box.Min);
        Assert.Equal(new Vector3(4, 5, 6), box.Max);
        Assert.True(box.Contains(new(0, 0, 0)));
        Assert.False(box.Contains(new(10, 0, 0)));
        BoundingBox moved = box.Transform(Matrix4x4.CreateTranslation(10, 0, 0));
        Assert.Equal(9f, moved.Min.X, 3);
    }

    [Fact]
    public void Frustum_CullsWhatIsBehindTheCamera()
    {
        var cam = new Camera3D { Position = new(0, 0, 5), Target = Vector3.Zero, NearPlane = 0.1f, FarPlane = 50f };
        var frustum = new Frustum(cam.ViewProjection);
        Assert.True(frustum.Contains(Vector3.Zero));                       // in front
        Assert.False(frustum.Contains(new Vector3(0, 0, 20)));             // behind the camera
        Assert.True(frustum.Intersects(new BoundingSphere(Vector3.Zero, 1f)));
        Assert.False(frustum.Intersects(new BoundingSphere(new(0, 0, 20), 0.5f)));
    }

    [Fact]
    public void Cube_HasSixQuadsWithOutwardNormals()
    {
        MeshData cube = MeshData.Cube(2f);
        Assert.Equal(24, cube.Vertices.Length);
        Assert.Equal(36, cube.Indices.Length);
        Assert.Equal(12, cube.TriangleCount);
        BoundingBox b = cube.Bounds();
        Assert.Equal(-1f, b.Min.X, 3);
        Assert.Equal(1f, b.Max.Y, 3);
        // Every normal is unit length and points away from the center at its face.
        foreach (Vertex v in cube.Vertices)
        {
            Assert.Equal(1f, v.Normal.Length(), 3);
            Assert.True(Vector3.Dot(v.Normal, v.Position) > 0f);
        }
    }

    [Fact]
    public void Sphere_VerticesLieOnTheRadiusWithUnitNormals()
    {
        MeshData s = MeshData.Sphere(2f, 8, 12);
        Assert.True(s.Vertices.Length > 0);
        Assert.True(s.Indices.Length % 3 == 0);
        foreach (Vertex v in s.Vertices)
        {
            Assert.Equal(2f, v.Position.Length(), 2);
            Assert.Equal(1f, v.Normal.Length(), 2);
        }
    }

    [Fact]
    public void RecalculateNormals_ProducesUnitNormals()
    {
        MeshData plane = MeshData.Plane(4f, 3);
        plane.RecalculateNormals();
        foreach (Vertex v in plane.Vertices)
            Assert.True(Vector3.Distance(v.Normal, Vector3.UnitY) < 1e-2f);
    }
}
