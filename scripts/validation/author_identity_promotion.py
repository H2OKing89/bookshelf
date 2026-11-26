"""Exercise a name-only author upgrade using only an owned loopback catalog.

Both the stored first recording and newly released second recording have an
explicit catalog author ASIN. An unrelated homonym must never be attached.
"""
from datetime import date, timedelta
import json
from pathlib import Path
import secrets
import sqlite3
from urllib.parse import parse_qs, quote

from . import app, common, restore
from .provider_policies import OwnedEndpoint

NAME = 'Synthetic Author'
AUTHOR_ASIN = 'B000000001'
FIRST_ASIN = 'B000000002'
SECOND_ASIN = 'B000000003'
HOMONYM_ASIN = 'B000000004'
OTHER_AUTHOR = 'B000000009'
LEGACY_ID = 'audible:us:name:' + quote(NAME)
AUTHOR_ID = 'audible:us:' + AUTHOR_ASIN
FIRST_ID = 'audible:us:' + FIRST_ASIN
SECOND_ID = 'audible:us:' + SECOND_ASIN


class Catalog(OwnedEndpoint):
    def __init__(self):
        self.token = secrets.token_urlsafe(24)
        self.products = {
            asin: {'asin': asin, 'title': title, 'language': 'english',
                   'runtime_length_min': 1, 'publisher_summary': 'Owned recording metadata',
                   'content_type': 'Product', 'content_delivery_type': 'MultiPartBook',
                   'authors': [{'name': NAME, 'asin': author}], 'narrators': [{'name': 'Fixture Narrator'}],
                   'release_date': release_date}
            for asin, title, author, release_date in (
                (FIRST_ASIN, 'Synthetic Recording', AUTHOR_ASIN, '2020-01-01'),
                (SECOND_ASIN, 'Synthetic Upcoming Recording', AUTHOR_ASIN, (date.today() + timedelta(days=10)).isoformat()),
                (HOMONYM_ASIN, 'Unrelated Same Name Recording', OTHER_AUTHOR, '2020-01-01'))}
        super().__init__()

    def response(self, method, route, headers, stream):
        if method == 'POST' and route.path == '/v1/api/request':
            if headers.get('Authorization') != 'Bearer ' + self.token or headers.get('X-Audible-Marketplace') != 'us':
                return self.reject()
            size = int(headers.get('Content-Length', '0'))
            if not 0 < size <= 32768:
                return self.reject()
            request = json.loads(stream.read(size))
            path = request.get('path', '')
            if request.get('method') != 'GET':
                return self.reject()
            if path == '/1.0/catalog/products' and request.get('query', {}).get('author') == NAME:
                return self.json(200, {'status': 200, 'body': {'total_results': len(self.products), 'products': list(self.products.values())}})
            asin = path.removeprefix('/1.0/catalog/products/')
            if asin in self.products:
                return self.json(200, {'status': 200, 'body': {'product': self.products[asin]}})
            return self.reject()
        if method != 'GET' or parse_qs(route.query).get('region') != ['us']:
            return self.reject()
        if route.path == '/authors/' + AUTHOR_ASIN:
            return self.json(200, {'asin': AUTHOR_ASIN, 'name': NAME, 'region': 'us'})
        parts = route.path.split('/')
        if len(parts) >= 3 and parts[1] == 'books' and parts[2] in self.products:
            product = self.products[parts[2]]
            if len(parts) == 4 and parts[3] == 'chapters':
                # Missing optional chapter details does not invalidate identity.
                return self.json(404, {})
            return self.json(200, {'asin': product['asin'], 'region': 'us', 'title': product['title'],
                                   'language': product['language'], 'runtimeLengthMin': 1,
                                   'releaseDate': product['release_date'], 'description': product['publisher_summary'],
                                   'authors': product['authors'], 'narrators': product['narrators']})
        return self.reject()


def projection(fixture):
    with sqlite3.connect(f'file:{fixture.home / "readarr.db"}?mode=ro', uri=True) as db:
        return {
            'authors': db.execute('SELECT Id, AuthorMetadataId, Path, Monitored, MonitorNewItems, MetadataProfileId, QualityProfileId, Tags FROM Authors ORDER BY Id').fetchall(),
            'metadata': db.execute('SELECT Id, ForeignAuthorId, TitleSlug, Name FROM AuthorMetadata ORDER BY Id').fetchall(),
            'books': db.execute('SELECT Id, AuthorMetadataId, ForeignBookId, Monitored FROM Books ORDER BY Id').fetchall(),
            'editions': db.execute('SELECT Id, BookId, ForeignEditionId, Asin, AudibleRegion FROM Editions ORDER BY Id').fetchall(),
            'files': db.execute('SELECT Id, EditionId, Path, Size FROM BookFiles ORDER BY Id').fetchall(),
            'history': db.execute('SELECT Id, AuthorId, BookId FROM History ORDER BY Id').fetchall(),
        }


def verify(fixture, before):
    after = projection(fixture)
    fixture.check('same_author_numeric_identity_path_and_preferences', after['authors'] == before['authors'])
    fixture.check('identity_promoted_in_place_and_slug_retained', after['metadata'] == [(7, AUTHOR_ID, before['metadata'][0][2], NAME)])
    fixture.check('existing_book_and_future_book_only', len(after['books']) == 2 and after['books'][0] == before['books'][0] and
                  after['books'][1][1:3] == (7, SECOND_ID))
    fixture.check('existing_edition_preserved', after['editions'][0] == before['editions'][0] and len(after['editions']) == 2)
    fixture.check('files_and_history_preserved', after['files'] == before['files'] and after['history'] == before['history'] and restore.media_unchanged(fixture))
    author = fixture.api('author/11')
    books = fixture.api('book?authorId=11')
    fixture.check('api_shows_new_release_on_original_author', author['foreignAuthorId'] == AUTHOR_ID and
                  {book['foreignBookId'] for book in books} == {FIRST_ID, SECOND_ID})
    new = next(book for book in books if book['foreignBookId'] == SECOND_ID)
    fixture.check('future_release_remains_upcoming', new['releaseDate'][:10] > date.today().isoformat())


def run(runtime: Path, output: Path):
    fixture = app.Fixture(runtime, output)
    provider = Catalog()
    completed, failure, stage = False, None, 'setup'
    try:
        fixture.configure_home()
        fixture.start()
        token_path = fixture.home / 'owned-catalog-token'
        token_path.write_text(provider.token)
        token_path.chmod(0o600)
        settings = fixture.api('config/metadataprovider/1')
        settings.update({'audnexusUrl': provider.url, 'audnexusRegion': 'us', 'audibleRsEnabled': True,
                         'audibleRsUrl': provider.url, 'audibleRsTokenFile': str(token_path), 'audibleRsAccount': '',
                         'writeAudioTags': 'no', 'scrubAudioTags': False, 'updateCovers': False, 'embedMetadata': False})
        fixture.api('config/metadataprovider/1', 'PUT', settings)
        restore.seed_library(fixture)
        fixture.stop()
        with sqlite3.connect(fixture.home / 'readarr.db') as db:
            db.execute('UPDATE AuthorMetadata SET ForeignAuthorId=? WHERE Id=7', (LEGACY_ID,))
            db.execute('UPDATE Books SET ForeignBookId=? WHERE Id=13', (FIRST_ID,))
            db.execute('UPDATE Editions SET ForeignEditionId=?, Asin=?, AudibleRegion=? WHERE Id=17', (FIRST_ID, FIRST_ASIN, 'us'))
            db.execute('INSERT OR REPLACE INTO Config (Key, Value) VALUES (?, ?)', ('rescanafterrefresh', 'Never'))
        fixture.start()
        before = projection(fixture)
        stage = 'read_only_catalog'
        matches = fixture.api('book/lookup?term=' + quote('author:' + LEGACY_ID, safe=''))
        fixture.check('catalog_discovers_upcoming_and_excludes_homonym', {book['foreignBookId'] for book in matches} == {FIRST_ID, SECOND_ID})
        fixture.check('catalog_get_does_not_rekey_database', projection(fixture) == before)
        stage = 'refresh'
        command = fixture.api('command', 'POST', {'name': 'RefreshAuthor', 'authorId': 11})
        restore.wait_command(fixture, command['id'])
        verify(fixture, before)
        stage = 'restart_refresh'
        fixture.stop()
        fixture.start()
        command = fixture.api('command', 'POST', {'name': 'RefreshAuthor', 'authorId': 11})
        restore.wait_command(fixture, command['id'])
        verify(fixture, before)
        fixture.check('repeat_refresh_does_not_duplicate_records', len(projection(fixture)['books']) == 2)
        fixture.check('only_owned_provider_routes_used', provider.unexpected == 0 and provider.requests[('POST', '/v1/api/request')] > 0)
        completed = True
    except Exception as error:
        fixture.checks[stage + '_completed'] = False
        failure = app.safe_failure(stage, error)
    finally:
        fixture.stop()
        provider.close()
    receipt = app.result(fixture, completed, failure=failure)
    common.write_json(output / 'result.json', receipt)
    return receipt
