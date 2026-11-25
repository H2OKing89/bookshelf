"""Generate or verify Bookshelf's versioned OpenAPI from an owned runtime.

No running user's server or configuration is used. The committed documents
are normalized only for their origin-relative server path.
"""
import argparse
import json
from pathlib import Path

from . import app, common

ROOT = Path(__file__).resolve().parents[2]
DOCUMENTS = {
    "v1": ROOT / "src/Readarr.Api.V1/openapi.json",
    "v2": ROOT / "src/Readarr.Api.V2/openapi.json",
}
V2_ROUTES = {"/api/v2/queue", "/api/v2/queue/status", "/api/v2/queue/grab", "/api/v2/queue/remove"}


def normalize(document):
    return {**document, "servers": [{"url": "/"}]}


def run(runtime, output, *, generate=False):
    runtime, output = common.validate_output(runtime, output)
    common.private_directory(output)
    fixture = app.Fixture(runtime, output / "application")
    completed, failure, stage = False, None, "configure"
    try:
        fixture.configure_home()
        fixture.start()
        docs = {}
        stage = "document_routes"
        for version in DOCUMENTS:
            route = f"{fixture.URL_BASE}/docs/{version}/openapi.json"
            status, _, _ = fixture.request(route)
            fixture.check(f"{version}_document_requires_api_key", status == 401)
            status, _, body = fixture.request(route, key=True)
            fixture.check(f"{version}_document_is_served_by_package", status == 200)
            docs[version] = json.loads(body)
            fixture.check(f"{version}_document_uses_actual_listener_base_path",
                          docs[version]["servers"] == [{"url": fixture.URL_BASE}])
            fixture.check(f"{version}_security_describes_header_query_and_bearer",
                          docs[version]["security"] == [{"X-Api-Key": []}, {"apikey": []}, {"BearerApiKey": []}])
            common.write_json(output / f"{version}-openapi.json", normalize(docs[version]))
        paths_v1 = docs["v1"]["paths"]
        paths_v2 = docs["v2"]["paths"]
        fixture.check("v1_retains_existing_queue_and_discovery_operations",
                      {"/api", "/api/v1/queue", "/api/v1/queue/{id}", "/api/v1/queue/bulk",
                       "/api/v1/queue/grab/{id}", "/api/v1/queue/grab/bulk", "/api/v1/queue/details",
                       "/api/v1/queue/status"} <= set(paths_v1))
        fixture.check("versions_publish_only_their_owned_routes",
                      not any(path.startswith("/api/v2/") for path in paths_v1) and set(paths_v2) == V2_ROUTES)
        fixture.check("anonymous_ping_and_login_not_documented_as_api_key_required",
                      paths_v1["/ping"]["get"]["security"] == [{}] and paths_v1["/login"]["post"]["security"] == [{}])
        schemas_v1 = docs["v1"]["components"]["schemas"]
        schemas_v2 = docs["v2"]["components"]["schemas"]
        fixture.check("v1_numeric_identity_preserved_v2_explicit_download_identity",
                      schemas_v1["QueueResource"]["properties"]["id"]["type"] == "integer" and
                      schemas_v2["DownloadQueueResource"]["properties"]["id"]["type"] == "string" and
                      schemas_v2["DownloadQueueResource"]["properties"]["recordings"]["type"] == "array")
        fixture.check("v2_schema_explicitly_declares_coverage_source_and_action_revision",
                      set(schemas_v2["QueueCoverageSource"]["enum"]) == {"recordedGrab", "pendingRelease", "mappedCandidates", "unknown"} and
                      schemas_v2["DownloadQueueTarget"]["properties"]["revision"]["type"] == "string")
        fixture.check("unsupported_version_and_nonexistent_v2_legacy_routes_not_served",
                      all(fixture.request(fixture.URL_BASE + path, key=True)[0] == 404 for path in
                          ("/api/v3/queue", "/api/v2/book", "/docs/v3/openapi.json")))
        stage = "actual_contracts"
        status, _, body = fixture.request(fixture.URL_BASE + "/api", key=True)
        discovery = json.loads(body)
        fixture.check("existing_discovery_current_version_is_preserved",
                      status == 200 and discovery["current"] == "v1" and discovery["available"] == ["v1", "v2"])
        for version in DOCUMENTS:
            route = f"{fixture.URL_BASE}/api/{version}/queue"
            unauthorized, _, _ = fixture.request(route)
            status, _, body = fixture.request(route, key=True)
            page = json.loads(body)
            fixture.check(f"{version}_actual_queue_api_requires_api_key", unauthorized == 401)
            fixture.check(f"{version}_query_and_bearer_api_key_forms_remain_supported",
                          fixture.request(route + "?apikey=" + fixture.api_key)[0] == 200 and
                          fixture.request(route, headers={"Authorization": "Bearer " + fixture.api_key})[0] == 200)
            fixture.check(f"{version}_actual_queue_response_contains_camel_case_paged_contract",
                          status == 200 and page["totalRecords"] == 0 and page["records"] == [] and "sortDirection" in page)
        stage = "static_reference_docs"
        if not generate:
            for version, destination in DOCUMENTS.items():
                fixture.check(f"{version}_committed_document_matches_actual_listener",
                              destination.is_file() and json.loads(destination.read_text()) == normalize(docs[version]))
        completed = True
    except Exception as error:
        failure = app.safe_failure(stage, error)
    finally:
        fixture.cleanup()
        fixture.checks["owned_process_stopped"] = fixture.process is None and fixture.forced_stops == 0
    value = app.result(fixture, completed, failure=failure)
    common.write_json(output / "result.json", value)
    return value


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("runtime", type=Path, help="Built Bookshelf runtime (no application home)")
    parser.add_argument("output", type=Path, help="Fresh directory for generated document and fixture evidence")
    parser.add_argument("--generate", action="store_true", help="Generate documents without comparing committed references")
    arguments = parser.parse_args()
    result = run(arguments.runtime, arguments.output, generate=arguments.generate)
    print(json.dumps(result, indent=2))
    raise SystemExit(0 if result.get("completed") else 1)
