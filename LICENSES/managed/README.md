# Managed package license notices

These files accompany the pinned image and PDF libraries used by Bookshelf:

| Package | Version | Distributed notice text |
| --- | --- | --- |
| Magick.NET-Q8-AnyCPU / Magick.NET.Core | 15.1.0 | [Apache license](Magick.NET/LICENSE.txt), [native and third-party notices](Magick.NET/NOTICE.txt) |
| PdfPig | 0.1.16 | [License and included component terms](PdfPig/LICENSE.txt) |

The Magick.NET notice comes from the original NuGet package and includes
ImageMagick 7.1.2-35 and its bundled codec notices. The complete PdfPig license
includes its external component notices. Files retain upstream bytes and
formatting; [sources.json](sources.json) records the pinned source, package
checksum, repository revision and file checksum.

Copy this directory into application output and publish packages. Updating a
package requires updating its corresponding notice files and checksums from
the same upstream version.
