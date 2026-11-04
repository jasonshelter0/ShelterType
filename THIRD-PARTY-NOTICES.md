# Third-party notices

## Kingsoft TypeEasy 2008 course and teaching materials

The `Data/` and `Teaching/` directories contain course text, dictionaries, teaching images and converted data originating from Kingsoft TypeEasy 2008. The project maintainer has confirmed permission from the original author to include these materials in ShelterType. Original rights remain with their respective owners. See [ACKNOWLEDGEMENTS.md](ACKNOWLEDGEMENTS.md).

Some introductory exercises and the application code were newly written for ShelterType. The repository currently has no selected open-source license for its own code. Public source availability is not a statement that all code and bundled course materials share one license.

## Microsoft .NET and WPF

The Windows portable package includes the Microsoft .NET 8 runtime and Windows Desktop/WPF runtime so users do not need to install .NET separately. These components retain their own licenses and notices. The build copies the runtime packs' original `LICENSE`/`LICENSE.TXT` and available `THIRD-PARTY-NOTICES.TXT` into `licenses/`, and retains any notices emitted by the SDK.

- .NET runtime source and license: https://github.com/dotnet/runtime
- WPF source and license: https://github.com/dotnet/wpf

The application uses Windows system fonts and does not redistribute font files.

## Public-domain practice excerpts

`Courses/public-articles.json` contains 32 excerpts from works published before 1931. English sources: Project Gutenberg ebooks 11 (Lewis Carroll) and 1661 (Arthur Conan Doyle). Chinese sources: Wikisource transcriptions of 朱自清's 背影, 荷塘月色, 匆匆 (author died 1948); the source pages identify the originals as public domain in the USA and countries with life-plus-75 or shorter terms. Individual course metadata records source links and copyright status. Public-domain status is jurisdiction-specific. No website navigation, modern editorial commentary, or Project Gutenberg trademarked boilerplate is included as lesson text.

English punctuation and whitespace are normalized for keyboard practice; Chinese text uses Wikisource's simplified view. See ACKNOWLEDGEMENTS.md for source links.

## Resource provenance and eventual Kingsoft removal

`Course.Origin` distinguishes `kingsoft-2008`, `public-domain`, and `sheltertype`. `Courses/provenance.json` also covers non-course assets and the Jason-Wubi86 dictionary. Shuffled copies preserve their source metadata.

The current root keyboard draws vector outlines traced from existing `Teaching/glyphs86` / `glyphs98` images. It does not display the old keyboard screenshot, but the outlines are still Kingsoft-derived assets, not independently authored roots. Replacing the VectorRoots loader is required before claiming independent root artwork.

Default builds retain Kingsoft materials. `package.ps1 -ExcludeKingsoft` omits all `Data/` and `Teaching/` assets from a new package; the application then uses authored fallback lessons and public-domain articles. Existing progress files are never removed. `CourseCatalog.Load(root, includeKingsoft: false)` provides the same course-origin filter for migration checks. Original root practice is unavailable in an excluded-assets package until replacement roots are authored.
