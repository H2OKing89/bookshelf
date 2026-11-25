# Managed package license notices

These files accompany the pinned libraries used by Bookshelf:

| Package | Version | Distributed notice text |
| --- | --- | --- |
| Magick.NET-Q8-AnyCPU / Magick.NET.Core | 15.1.0 | [Apache license](Magick.NET/LICENSE.txt), [native and third-party notices](Magick.NET/NOTICE.txt) |
| PdfPig | 0.1.16 | [License and included component terms](PdfPig/LICENSE.txt) |
| FluentMigrator and its runner components | 8.0.1 | [Apache license](FluentMigrator/LICENSE.txt) |
| Npgsql | 10.0.4 | [PostgreSQL license](Npgsql/LICENSE.txt) |
| System.IO.Abstractions / TestableIO abstraction and wrapper packages | 23.1.0 | [MIT license](System.IO.Abstractions/LICENSE.txt), [abstraction package attribution](System.IO.Abstractions/TestableIO.System.IO.Abstractions.NOTICE.nuspec), [wrapper package attribution](System.IO.Abstractions/TestableIO.System.IO.Abstractions.Wrappers.NOTICE.nuspec) |
| Testably.Abstractions.FileSystem.Interface | 11.1.0 | [MIT license](Testably.Abstractions/LICENSE.txt) |
| System.Data.SQLite | 2.0.4 | [Public-domain declaration and attribution](System.Data.SQLite/NOTICE.nuspec), [upstream copyright notice](System.Data.SQLite/COPYRIGHT.wiki) |
| SourceGear.sqlite3 | 3.53.4 | [SQLite public-domain notice](SourceGear.sqlite3/LICENSE.txt), [package attribution](SourceGear.sqlite3/NOTICE.nuspec) |
| Ical.Net | 5.2.4 | [MIT license](Ical.Net/LICENSE.md) |
| NodaTime | 3.2.2 | [Apache-2.0 license](NodaTime/LICENSE.txt), [attribution notice](NodaTime/NOTICE.txt) |

The Magick.NET notice comes from the original NuGet package and includes
ImageMagick 7.1.2-35 and its bundled codec notices. The complete PdfPig license
includes its external component notices. Files retain upstream bytes and
formatting; [sources.json](sources.json) records the pinned source, package
checksum, repository revision and file checksum.

Copy this directory into application output and publish packages. Updating a
package requires updating its corresponding notice files and checksums from
the same upstream version.

The FluentMigrator and Npgsql notices come from the exact repository commits
recorded in their NuGet package metadata. Their source URLs and checksums are
included in the same manifest.

System.Data.SQLite's package declares its copyright as Public Domain but does
not include a separate license file or identify a source revision. Its original
package metadata is preserved alongside the official project's copyright notice,
pinned by the Fossil file artifact recorded in the manifest. That copyright
artifact is not claimed to identify the package's source revision. The notice's
MS-PL exception concerns the separate LINQ and EF6 assemblies; this package
contains only System.Data.SQLite.dll.

SourceGear.sqlite3 supplies the native SQLite builds used by the managed
provider. Its license file and package metadata are copied from the exact NuGet
archive, preserving the SQLite public-domain declaration and the package's
author and copyright attribution. The package does not identify a source
revision.

The iCal.NET and NodaTime notices come from the exact repository commits
recorded in their restored NuGet package metadata.

The System.IO.Abstractions compatibility package and split TestableIO runtime
packages share the exact upstream MIT license. The two new runtime package
metadata notices retain their package attribution and source revision.
The resolved Testably file-system interface dependency has its own pinned
upstream MIT notice.
