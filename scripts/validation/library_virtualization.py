"""Measure the packaged library tables with a large disposable catalog."""

import json
from pathlib import Path
import sqlite3
from string import ascii_uppercase

from . import app, common, identity, restore, upgrade


BROWSER = Path(__file__).with_name("library-virtualization-browser.cjs")
AUTHORS_PER_LETTER = 20
BOOKS_PER_AUTHOR = 8


def seed_catalog(fixture):
    restore.seed_library(fixture, seed_user=False)
    fixture.stop()
    with sqlite3.connect(fixture.home / "readarr.db") as database:
        database.row_factory = sqlite3.Row
        templates = {table: dict(database.execute(f'SELECT * FROM "{table}" LIMIT 1').fetchone())
                     for table in ("AuthorMetadata", "Authors", "Books", "Editions")}
        records = {table: [] for table in templates}
        for author_offset in range(len(ascii_uppercase) * AUTHORS_PER_LETTER):
            author_id = 1000 + author_offset
            letter = ascii_uppercase[author_offset // AUTHORS_PER_LETTER]
            name = f"{letter} Author {author_offset:04d}"
            records["AuthorMetadata"].append(dict(templates["AuthorMetadata"], Id=author_id,
                ForeignAuthorId=f"local:author:{author_id:032x}", TitleSlug=f"owned-author-{author_id}",
                Name=name, SortName=name.lower(), NameLastFirst=name, SortNameLastFirst=name.lower()))
            records["Authors"].append(dict(templates["Authors"], Id=author_id, AuthorMetadataId=author_id,
                CleanName=name.lower().replace(" ", ""), SortName=name.lower(),
                Path=str(fixture.home / "owned-media" / f"author-{author_id}")))
            for book_offset in range(BOOKS_PER_AUTHOR):
                book_id = 10000 + author_offset * BOOKS_PER_AUTHOR + book_offset
                title = f"{letter} Recording {author_offset:04d}-{book_offset}"
                foreign_id = f"local:recording:{book_id:032x}"
                ebook = book_offset == 7
                if author_offset == 0 and book_offset in (0, 1):
                    title = "A Alternate Recording"
                    foreign_id = f"audible:{'us' if book_offset == 0 else 'uk'}:B000000001"
                records["Books"].append(dict(templates["Books"], Id=book_id, AuthorMetadataId=author_id,
                    ForeignBookId=foreign_id, TitleSlug=f"owned-recording-{book_id}",
                    Title=title, CleanTitle=title.lower().replace(" ", "")))
                editions = dict(templates["Editions"], Id=book_id, BookId=book_id,
                    ForeignEditionId=foreign_id, Title=title, TitleSlug=f"owned-edition-{book_id}",
                    Format="EPUB" if ebook else "M4B", IsEbook=int(ebook),
                    RuntimeLengthMinutes=0 if ebook else 1)
                if "AudibleRegion" in editions:
                    editions["AudibleRegion"] = foreign_id.split(":")[1] if foreign_id.startswith("audible:") else None
                    editions["Asin"] = "B000000001" if foreign_id.startswith("audible:") else None
                if "PageCount" in editions:
                    editions["PageCount"] = 321 if ebook else 0
                records["Editions"].append(editions)
        for table, rows in records.items():
            names = list(templates[table])
            columns = ", ".join(f'"{name}"' for name in names)
            values = ", ".join("?" for _ in names)
            database.executemany(f'INSERT INTO "{table}" ({columns}) VALUES ({values})',
                                 ([row[name] for name in names] for row in rows))
    fixture.start()
    return {
        "authors": len(records["Authors"]) + 1,
        "books": len(records["Books"]) + 1,
    }


def run(runtime, output, playwright_module):
    runtime, output = common.validate_output(runtime, output)
    common.private_directory(output)
    fixture = app.Fixture(runtime, output / "application")
    playwright_module = Path(playwright_module).resolve(strict=True)
    completed, failure, stage = False, None, "configure"
    metrics = {}
    try:
        fixture.configure_home()
        fixture.start()
        upgrade.seed_current_account(fixture)
        fixture.check("fixture_has_no_download_clients_or_indexers", not fixture.api("downloadclient")
                      and not fixture.api("indexer"))
        stage = "catalog"
        sizes = seed_catalog(fixture)
        fixture.check("large_owned_catalog", len(fixture.api("author")) == sizes["authors"]
                      and len(fixture.api("book")) == sizes["books"])
        fixture.check("alternate_marketplace_recordings_preserve_distinct_local_ids",
                      fixture.api("book/10000")["foreignBookId"] == "audible:us:B000000001"
                      and fixture.api("book/10001")["foreignBookId"] == "audible:uk:B000000001")
        fixture.check("owned_ebook_has_retained_pages", fixture.api("book/10007")["pageCount"] == 321)
        stage = "browser"
        config = {"url": "http://" + fixture.backend_host + fixture.URL_BASE,
                  "username": fixture.current_username, "password": fixture.current_password,
                  "output": str(common.private_directory(output / "screenshots")),
                  "playwrightModule": str(playwright_module), "sizes": sizes}
        execution = identity._browser_process(config, common.private_directory(output / "browser-process"), BROWSER)
        report = json.loads(execution.stdout)
        checks = report.get("checks")
        if not isinstance(checks, dict) or not checks or not all(type(value) is bool for value in checks.values()):
            raise common.ValidationFailure("library_virtualization_browser_checks_invalid")
        fixture.checks.update(checks)
        metrics = report.get("metrics", {})
        common.write_json(output / "browser-result.json", report)
        fixture.check("browser_completed", execution.returncode == 0 and report.get("completed") is True and all(checks.values()))
        fixture.check("owned_media_unchanged", restore.media_unchanged(fixture))
        completed = True
    except Exception as error:
        failure = app.safe_failure(stage, error)
    finally:
        fixture.cleanup()
        fixture.checks["owned_process_stopped"] = fixture.process is None and fixture.forced_stops == 0
    value = app.result(fixture, completed, failure=failure, metrics=metrics)
    common.write_json(output / "result.json", value)
    return value
