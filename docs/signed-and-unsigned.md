---
title: Signed and unsigned
nav_order: 8
---

# Signed and unsigned modules

A program and a library each have an unsigned form and a signed form, so four file kinds appear:

| Extension | What it is | Signed |
|---|---|---|
| `.elf` | A program (an executable). | No |
| `.self` | A program wrapped in a signed container. | Yes |
| `.prx` | A library or module. | No |
| `.sprx` | A library or module wrapped in a signed container. | Yes |

`.prx` and `.sprx` are libraries: you load one at run time or link against it. `.elf` and `.self` are
programs: the application's `eboot.bin` is one. The leading `s` marks the signed form of each.

## Unsigned and signed

An **unsigned** file is a plain ELF. The linker produces this form: an `eboot.bin` is an ELF program,
and a library the linker builds is a `.prx`, also an ELF.

A **signed** file wraps that ELF in a container: a header, a segment table, the ELF header and
program headers, extended information, and the program's segment data. There are two signed forms:

- **Developer-accepted (readable).** The container's digest and signature slots are zero-filled and
  its segment data is plaintext. A development console accepts it. The toolchain produces and reads
  this form.
- **Retail (encrypted).** The container is signed with a certificate and its segment data is
  encrypted. A retail console requires this form. Its contents cannot be read without its key, so the
  tools report the form but cannot open it.

## Which form a console runs

A development console runs an unsigned ELF wrapped in a developer-accepted signed container. A retail
console runs only a retail signed, encrypted container.

The normal build produces an unsigned ELF, and the packager wraps it in a developer-accepted signed
container while it assembles the package. You rarely sign a file by hand; you do so only to produce a
loose `.self` or `.sprx` outside a package.

## Reading any readable form

The inspector and the module reader take either a plain ELF or a developer-accepted signed container.
A signed container is unwrapped to its ELF first, so a `.sprx` reads the same way as a `.prx`:

```
sharpprospero-bindgen elf --file mylib.sprx
sharpprospero-bindgen prx --module mylib.sprx --inspect
```

The `elf` command prints the container line so the form is clear:

```
File:      mylib.sprx
Container: signed (.self / .sprx)
Class:     ELF64
Type:      SCE dynamic module
```

`self --inspect` reports which of the forms a file is, including a retail encrypted container it
cannot open:

```
sharpprospero-bindgen self --inspect --file mylib.sprx
```

## Converting between the forms

```
sharpprospero-bindgen self --sign    --in app.elf  --out app.self
sharpprospero-bindgen self --extract --in app.self --out app.elf
```

`self --sign` wraps an unsigned ELF in a developer-accepted signed container. `--app-version`,
`--fw-version` and `--authority` set the extended-info fields (hexadecimal); `--no-normalize` keeps
the ELF header exactly as supplied instead of adjusting the machine, OS/ABI and type for the module
loader.

`self --extract` recovers the ELF from a developer-accepted signed container. A signed container
stores only the loadable program segments, so the recovered ELF carries the same headers and loadable
content, which is what the inspector reads; it is not a byte copy of the original file, because
section headers and the padding between segments are not stored.

A retail signed, encrypted container cannot be signed or extracted here: signing it needs a
certificate, and extracting it needs its key.
