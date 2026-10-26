# SkiaSharp 4.153.1 redistribution notices

Retain this directory alongside redistributed backend binaries and in the backend runtime image. The two notice files are copied verbatim from the official NuGet packages; they have not been shortened, translated, or rewritten. The local `.gitattributes` preserves their exact bytes across platforms.

| Retained file | Official package and archive entry | SHA-256 of retained file |
| --- | --- | --- |
| `LICENSE.txt` | [SkiaSharp 4.153.1](https://www.nuget.org/packages/SkiaSharp/4.153.1), root `LICENSE.txt` | `89101e35a8c66fd4d6dffc1763259161d35cb564c169714ec227a768c89f2938` |
| `THIRD-PARTY-NOTICES.txt` | [SkiaSharp.NativeAssets.Linux.NoDependencies 4.153.1](https://www.nuget.org/packages/SkiaSharp.NativeAssets.Linux.NoDependencies/4.153.1), root `THIRD-PARTY-NOTICES.txt` | `21504c46c4c58aa64c1055bd2dcbc5f9a136b4b8c412ed3cc6740e22c5b127f5` |

`LICENSE.txt` contains the SkiaSharp MIT copyright, permission notice and disclaimer. The native package contains the upstream aggregate third-party notices, including the licenses and acknowledgments for bundled native components. Preserve that complete package-provided file even where it describes components beyond the avatar code path. It is an aggregate notice, not an inventory of enabled codecs; for example, its retained DNG/PIEX text does not mean those codecs are enabled in this release. The corresponding 4.153.1 Linux NoDependencies, macOS and Win32 package license files are identical; their aggregate third-party notice files are also identical, as checked during collection.

The source release is [SkiaSharp v4.153.1](https://github.com/mono/SkiaSharp/releases/tag/v4.153.1). Its [source license](https://github.com/mono/SkiaSharp/blob/v4.153.1/LICENSE.md) has the same license wording, but the retained file comes from the package so its packaging and line endings remain intact. The [pinned native dependency guidance](https://github.com/mono/SkiaSharp/blob/v4.153.1/documentation/dev/dependencies.md) describes bundled components; it does not replace their notices.

Collection used the official packages restored to the NuGet cache. Each cached archive's SHA-512 matched its recorded `.nupkg.sha512`, and the copied files matched the package files byte for byte. Archive provenance:

| Official archive | SHA-512, base64 |
| --- | --- |
| [skiasharp.4.153.1.nupkg](https://api.nuget.org/v3-flatcontainer/skiasharp/4.153.1/skiasharp.4.153.1.nupkg) | `quzaoxV/g+5AizlwOf6XzdALi3cgCEy6rQA06SW1fezA+Lrj0FKFmG5v65hyDM26qD+SdL7u0HjWadN9hOZwFA==` |
| [skiasharp.nativeassets.linux.nodependencies.4.153.1.nupkg](https://api.nuget.org/v3-flatcontainer/skiasharp.nativeassets.linux.nodependencies/4.153.1/skiasharp.nativeassets.linux.nodependencies.4.153.1.nupkg) | `9pcoCFjGDhkSLAJlgJXU9PpFzFbxwSDsJVSv4jOJm+EmPrP7b9FnRPC3R9I+qQXjw4JhLdfHPRbi+6630mT2MA==` |

When updating the managed/native packages, collect and review their notices again rather than assuming these files cover a later version. These are dependency redistribution notices, not a claim that all repository dependencies or production media have been audited.
