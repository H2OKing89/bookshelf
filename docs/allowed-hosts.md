# Allowed Hosts

Allowed Hosts optionally restricts the HTTP host names accepted by Bookshelf.
An empty value accepts any host, preserving existing access. The default does
not enable host filtering as a protection policy.

This setting checks the request's `Host` header. It is separate from **Bind
Address**, which controls the network interfaces on which the server listens,
and from authentication and API keys. It does not restrict clients by IP address.

## Configure the list

In **Settings → General**, enable **Show Advanced**, then set **Host → Allowed
Hosts**. Save and restart the server for the change to take effect.

Alternatively, stop the server and add `AllowedHosts` inside the existing
`<Config>` element in its `config.xml`:

```xml
<AllowedHosts>books.example.test, *.library.example.test, 192.0.2.10, [2001:db8::10]</AllowedHosts>
```

The environment variable `Readarr__Server__AllowedHosts` overrides the saved XML
value, including when the environment value is explicitly empty. For example:

```sh
export Readarr__Server__AllowedHosts='books.example.test, *.library.example.test'
```

Supply that variable through the server's existing service or container
configuration, then restart. It does not rewrite the XML value. Keep the existing
home, proxy settings, credentials and media paths.

## Supported values

Separate entries with commas or semicolons. Leading and trailing whitespace is
removed, matching is case insensitive, and duplicate normalized entries are
removed when saving through the settings API.

| Value | Behavior |
| --- | --- |
| `books.example.test` | Allows that exact host name. |
| `*.library.example.test` | Allows subdomains such as `audio.library.example.test`, including deeper subdomains. It does not allow `library.example.test` itself; add that separately if needed. |
| `192.0.2.10` | Allows that IPv4 address as the request host. |
| `2001:db8::10` or `[2001:db8::10]` | Saves as the normalized, bracketed IPv6 pattern `[2001:db8::10]`. |
| `bücher.example.test` | Normalizes to the ASCII IDN form `xn--bcher-kva.example.test`. The punycode form can also be entered directly. |

Do not include a URL scheme, port, path, query string, fragment, CIDR range or
IPv6 scope identifier. For example, `https://books.example.test`,
`books.example.test:8787`, `books.example.test/books`, `192.0.2.0/24` and
`[fe80::1%eth0]` are rejected. Bind addresses `0.0.0.0` and `::` are also rejected.
Use an empty value to accept any host; a standalone `*` and a list containing
only separators are invalid.

The port in an incoming request is ignored for matching. Configuring
`books.example.test` therefore permits `Host: books.example.test:8787` without
putting the port in the list.

## Local access and reverse proxies

With a configured list, `localhost`, `127.0.0.1`, `[::1]` and the server's valid
machine host name remain allowed as recovery routes. Requests with an empty
host are rejected. Authentication still applies to these host names; adding an
API key does not bypass host filtering.

Behind a reverse proxy, add the host name clients use to reach Bookshelf. The
[trusted reverse-proxy configuration](trusted-proxies.md) determines which peers
may supply `X-Forwarded-Host`. Forwarded headers are processed first, then the
effective host is filtered. An untrusted peer cannot use a forwarded host to
replace a rejected request host.

Host filtering runs before UI, API and SignalR handling, including login and
requests under a configured URL base. A valid list rejects unlisted hosts with
HTTP 400. This does not alter the existing Forms, Basic or API-key requirements.

## Validation and recovery

An invalid nonempty XML or environment list prevents normal server startup. The
server does not discard invalid entries or silently fall back to accepting every
host. Correct the list in the existing configuration source, or clear it
deliberately, before restarting.

The host-settings API exposes the `allowedHosts` string. An omitted or `null`
field preserves the saved value for older clients. An explicit empty or
whitespace-only string clears the list. Invalid supplied values return HTTP 400
before any settings are written. A successful save requires a restart before
the new host policy applies.
