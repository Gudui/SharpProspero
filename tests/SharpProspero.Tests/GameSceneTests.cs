// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Graphics;
using SharpProspero.Numerics;
using System;
using Xunit;

namespace SharpProspero.Tests;

public sealed unsafe class GameSceneTests
{
    private static void WithSurface(int width, int height, Action<Surface, uint[]> action)
    {
        uint[] pixels = new uint[width * height];
        fixed (uint* p = pixels)
            action(new Surface(p, width, height), pixels);
    }

    // --- Blended shape fills ---

    [Fact]
    public void FillRectBlended_BlendsByAlphaAndTakesTheOpaqueFastPath()
    {
        WithSurface(4, 4, (surface, pixels) =>
        {
            surface.FillRectBlended(0, 0, 4, 4, Color.White.WithAlpha(128));
            Assert.Equal(0xFF808080u, pixels[0]); // white at half alpha over black is mid grey

            surface.FillRectBlended(0, 0, 4, 4, Color.Red); // opaque
            Assert.Equal(Color.Red.Value, pixels[0]);
        });
    }

    [Fact]
    public void FillCircleBlended_BlendsTheDiscAndLeavesTheCorners()
    {
        WithSurface(9, 9, (surface, pixels) =>
        {
            surface.FillCircleBlended(4, 4, 3, Color.White.WithAlpha(128));
            Assert.Equal(0xFF808080u, pixels[(4 * 9) + 4]); // centre blended
            Assert.Equal(0u, pixels[0]);                    // corner untouched
        });
    }

    // --- Camera2D ---

    [Fact]
    public void Camera_WorldAndScreenRoundTrip()
    {
        var camera = new Camera2D(200, 100) { Position = new Vector2(50f, 50f) };
        Assert.Equal(new Vector2(100f, 50f), camera.WorldToScreen(new Vector2(50f, 50f))); // centre
        Assert.Equal(new Vector2(110f, 50f), camera.WorldToScreen(new Vector2(60f, 50f)));
        Assert.Equal(new Vector2(60f, 50f), camera.ScreenToWorld(new Vector2(110f, 50f)));

        camera.Zoom = 2f;
        Assert.Equal(new Vector2(120f, 50f), camera.WorldToScreen(new Vector2(60f, 50f)));
    }

    [Fact]
    public void Camera_ZoomStaysAboveZero()
    {
        var camera = new Camera2D(200, 100) { Zoom = -5f };
        Assert.True(camera.Zoom > 0f);
    }

    [Fact]
    public void Camera_ZoomRejectsNonFinite()
    {
        // A non-finite zoom would spread NaN through every world/screen conversion; the setter clamps it.
        var camera = new Camera2D(200, 100) { Zoom = float.NaN };
        Assert.True(float.IsFinite(camera.Zoom) && camera.Zoom > 0f);

        camera.Zoom = float.PositiveInfinity;
        Assert.True(float.IsFinite(camera.Zoom) && camera.Zoom > 0f);

        // Mapping the camera's own centre must stay finite after the clamp.
        camera.Position = new Vector2(10f, 20f);
        Vector2 screen = camera.WorldToScreen(camera.Position);
        Assert.True(float.IsFinite(screen.X) && float.IsFinite(screen.Y));
    }

    [Fact]
    public void Camera_VisibleBoundsAndClamp()
    {
        var camera = new Camera2D(200, 100);
        Assert.Equal(new RectF(-100f, -50f, 200f, 100f), camera.VisibleWorldBounds);

        var world = new RectF(0f, 0f, 1000f, 1000f);
        camera.Position = new Vector2(0f, 0f);
        camera.ClampToBounds(world);
        Assert.Equal(new Vector2(100f, 50f), camera.Position); // pushed inside the edges

        camera.Position = new Vector2(2000f, 2000f);
        camera.ClampToBounds(world);
        Assert.Equal(new Vector2(900f, 950f), camera.Position);

        // A world smaller than the view centres on it.
        camera.ClampToBounds(new RectF(0f, 0f, 100f, 50f));
        Assert.Equal(new Vector2(50f, 25f), camera.Position);
    }

    // --- TileMap ---

    [Fact]
    public void TileMap_GetSetBoundsAndQueries()
    {
        var map = new TileMap(4, 3, 16, 16);
        Assert.Equal(64, map.WidthInPixels);
        Assert.Equal(48, map.HeightInPixels);
        Assert.Equal(TileMap.Empty, map.GetTile(1, 1));

        map.SetTile(1, 1, 5);
        Assert.Equal(5, map.GetTile(1, 1));
        Assert.Equal(TileMap.Empty, map.GetTile(-1, 0)); // out of range
        Assert.Equal(TileMap.Empty, map.GetTile(4, 0));

        Assert.Equal(new RectF(32f, 16f, 16f, 16f), map.TileBounds(2, 1));
        Assert.Equal((2, 1), map.WorldToTile(new Vector2(33f, 17f)));
    }

    [Fact]
    public void TileMap_Collides()
    {
        var map = new TileMap(4, 3, 16, 16);
        map.SetTile(1, 1, 5);
        bool IsSolid(int t) => t == 5;

        Assert.True(map.Collides(new RectF(20f, 20f, 4f, 4f), IsSolid)); // inside tile (1,1)
        Assert.False(map.Collides(new RectF(0f, 0f, 4f, 4f), IsSolid));  // empty tile (0,0)
        Assert.False(map.Collides(new RectF(20f, 20f, 4f, 4f), t => t == 9)); // not solid to this test
    }

    [Fact]
    public void TileMap_FromCsv()
    {
        TileMap map = TileMap.FromCsv("1,2,-1\n3,,5", tileWidth: 16, tileHeight: 16);
        Assert.Equal(3, map.Columns);
        Assert.Equal(2, map.Rows);
        Assert.Equal(1, map.GetTile(0, 0));
        Assert.Equal(TileMap.Empty, map.GetTile(2, 0));  // -1
        Assert.Equal(TileMap.Empty, map.GetTile(1, 1));  // blank cell
        Assert.Equal(5, map.GetTile(2, 1));
    }

    [Fact]
    public void TileMap_DrawsVisibleTiles()
    {
        uint[] sheetPixels = new uint[32 * 16];
        Array.Fill(sheetPixels, Color.White.Value); // two opaque 16x16 frames
        fixed (uint* sp = sheetPixels)
        {
            var sheet = new SpriteSheet(new Surface(sp, 32, 16), 16, 16);
            var map = new TileMap(4, 3, 16, 16);
            map.SetTile(0, 0, 0); // a tile at the world origin

            var camera = new Camera2D(64, 48) { Position = new Vector2(32f, 24f) };
            WithSurface(64, 48, (surface, pixels) =>
            {
                map.Draw(surface, sheet, camera);
                Assert.Equal(Color.White.Value, pixels[0]); // tile (0,0) lands at screen (0,0)
            });
        }
    }

    // --- ParticleSystem ---

    private static EmitParams FixedLife(float life) =>
        new(10f, 20f, 0f, MathUtil.TwoPi, life, life, 4f, 0f, Color.White, Color.White);

    [Fact]
    public void Particles_EmitUpdateAndRetire()
    {
        var system = new ParticleSystem(512, new GameRandom(1));
        system.Emit(new Vector2(10f, 10f), 10, FixedLife(0.5f));
        Assert.Equal(10, system.ActiveCount);

        system.Update(0.6f); // past the fixed 0.5s life
        Assert.Equal(0, system.ActiveCount);
    }

    [Fact]
    public void Particles_CapAtCapacityAndClear()
    {
        var system = new ParticleSystem(5, new GameRandom(2));
        system.Emit(Vector2.Zero, 20, FixedLife(1f));
        Assert.Equal(5, system.ActiveCount); // pool is full

        system.Clear();
        Assert.Equal(0, system.ActiveCount);
    }

    [Fact]
    public void Particles_UpdatePartialAndDrawDoNotThrow()
    {
        var system = new ParticleSystem(64, new GameRandom(3));
        system.Emit(new Vector2(32f, 32f), 30, FixedLife(1f)); // at the camera's centre
        system.Update(0.1f); // a short step, so all are still alive and near the centre
        Assert.Equal(30, system.ActiveCount);

        WithSurface(64, 64, (surface, pixels) =>
        {
            var camera = new Camera2D(64, 64) { Position = new Vector2(32f, 32f) };
            system.Draw(surface, camera);
            Assert.Contains(pixels, px => px != 0u); // something was drawn near the centre
        });
    }
}
