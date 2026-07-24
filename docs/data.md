---
title: Data and utilities
nav_order: 14
has_children: true
---

# Data and utilities

The building blocks for working with data: files and structured formats, vectors and randomness, byte
buffers, text, XML, and message digests. Each has its own page; this is the map.

| Page | Namespace | What it covers |
|---|---|---|
| [Files and storage](storage.md) | `SharpProspero.Storage` | Package files, directories, paths, an asset manager, INI, JSON, CSV, tables, versioned saves, and tar archives. |
| [Numerics and vectors](numerics.md) | `SharpProspero.Numerics` | `Vector2`, `RectF`, scalar math and smoothing, collision tests, a seedable random source, weighted tables, coherent noise, a quadtree, and a rectangle packer. |
| [Buffers and encodings](buffers.md) | `SharpProspero.Buffers` | Reading and writing binary data, bit fields, ring buffers, and hex/Base64/Base32. |
| [Text utilities](text.md) | `SharpProspero.Text` | Human-readable formatting and fuzzy string matching. |
| [XML](xml.md) | `SharpProspero.Xml` | A streaming reader and writer, and a small document model. |
| [Hashing and checksums](hashing.md) | `SharpProspero.Security` | CRC-32, MD5, SHA-1, SHA-2, SHA-3, and HMAC. |
| [Compression and archives](compression.md) | `SharpProspero.Compression` | DEFLATE, zlib and gzip decompression, and a ZIP archive reader. |

Most of these are self-contained: they compute in memory and touch no device service, so they work the
same on the console and in a unit test. The exception is the package-file and directory reading on the
storage page, which reads the mounted package.
