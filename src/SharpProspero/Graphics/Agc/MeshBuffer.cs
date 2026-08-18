// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Memory;
using System;
using System.Runtime.InteropServices;

namespace SharpProspero.Graphics.Agc;

/// <summary>
/// A mesh uploaded to graphics-readable memory: a vertex buffer and a 32-bit index buffer the graphics
/// processor reads while drawing. Build one from <see cref="MeshData"/> once, then draw it every frame.
/// The buffers live in direct memory, so they stay put and the processor reads them without a copy.
/// </summary>
public sealed unsafe class MeshBuffer : IDisposable
{
    private DirectMemoryRegion? _vertices;
    private DirectMemoryRegion? _indices;
    private bool _disposed;

    private MeshBuffer(DirectMemoryRegion vertices, DirectMemoryRegion indices, int vertexCount, int indexCount)
    {
        _vertices = vertices;
        _indices = indices;
        VertexCount = vertexCount;
        IndexCount = indexCount;
    }

    /// <summary>The address of the vertex buffer in graphics-readable memory.</summary>
    public void* VertexAddress => _vertices!.Pointer;

    /// <summary>The address of the index buffer in graphics-readable memory.</summary>
    public void* IndexAddress => _indices!.Pointer;

    /// <summary>The number of vertices.</summary>
    public int VertexCount { get; }

    /// <summary>The number of indices (three per triangle).</summary>
    public int IndexCount { get; }

    /// <summary>The size of one vertex in bytes: the buffer stride.</summary>
    public static int VertexStride => Vertex.SizeInBytes;

    /// <summary>Uploads a mesh's vertices to graphics-readable memory.</summary>
    public static MeshBuffer Upload(MeshData mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        Vertex[] vertices;
        if (mesh.Indices is { Length: > 0 })
        {
            vertices = new Vertex[mesh.Indices.Length];
            for (int i = 0; i < mesh.Indices.Length; i++)
            {
                uint idx = mesh.Indices[i];
                vertices[i] = idx < (uint)mesh.Vertices.Length ? mesh.Vertices[idx] : default;
            }
        }
        else
        {
            vertices = mesh.Vertices;
        }

        int vertexBytes = vertices.Length * Vertex.SizeInBytes;
        var vertRegion = DirectMemoryRegion.Allocate((nuint)vertexBytes);
        MemoryMarshal.AsBytes(vertices.AsSpan()).CopyTo(new Span<byte>(vertRegion.Pointer, vertexBytes));
        return new MeshBuffer(vertRegion, null, vertices.Length, mesh.Indices.Length);
    }

    /// <summary>Releases both buffers.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _vertices?.Dispose();
        _indices?.Dispose();
        _vertices = null;
        _indices = null;
    }
}
