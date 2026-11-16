# Trusted reverse proxies

Bookshelf accepts forwarded client addresses and schemes only from trusted
reverse proxies. An empty setting trusts loopback proxies only. A proxy running
on another machine or a container network needs its actual peer address listed.
This setting controls incoming requests; the outbound **Proxy Settings** section
for indexer, metadata and notification connections remains separate.

## Configure proxy addresses

In **Settings → General**, enable **Show Advanced**, then set **Trusted Reverse
Proxies** under **Host**. Enter a comma-separated list of IP addresses or CIDR
networks. Prefer each proxy's exact address over a network containing ordinary
clients. For example:

```text
192.0.2.10, 198.51.100.20, 2001:db8::10
```

These are documentation addresses; replace them with the addresses Bookshelf
actually sees as its immediate peers. Container and NAT deployments may expose
a different peer address from the proxy's advertised hostname.

Both IPv4 and IPv6 are supported. A CIDR must identify the network itself, with
no host bits set: `192.0.2.0/28` is valid, while `192.0.2.10/28` is rejected.
IPv4 addresses must use four decimal components without leading zeros. Prefix
lengths start at 1; a `/0` network is rejected. Hostnames, wildcards, bracketed
IPv6 addresses and IPv6 scope IDs are rejected. An IPv4-mapped IPv6 CIDR must
have a prefix length from 97 to 128; `/96` would trust every mapped IPv4 address.
Other IPv6 networks that cover the entire mapped IPv4 range, such as `::/64`,
are also rejected. Spaces around entries and empty entries between commas are
ignored.

Save the setting, then restart Bookshelf to apply it. Clearing the field restores
loopback-only trust. Changing trusted proxies does not enable authentication
bypass or change your authentication method. CGNAT LAN bypass remains disabled
by default.

The same setting is available through:

| Configuration source | Name | Example |
| --- | --- | --- |
| Host settings API | `trustedNetworks` | `192.0.2.10,2001:db8::10` |
| Home's `config.xml` | `TrustedNetworks` | `<TrustedNetworks>192.0.2.10,2001:db8::10</TrustedNetworks>` |
| Environment | `Readarr__Server__TrustedNetworks` | `192.0.2.10,2001:db8::10` |

The environment value takes precedence over XML and saved UI settings. Remove
the environment override if you want the UI or XML value to control trust.
Changes through any source require a restart. Host API updates that omit
`trustedNetworks` preserve its saved value; an explicit empty string clears it.
The API validates the complete list before saving. If an invalid entry is
supplied through XML or the environment, startup logs a warning and ignores that
entry, retaining loopback trust and any other valid entries.

## Upgrade an existing reverse proxy deployment

Older versions accepted forwarded headers from arbitrary peers. Configure the
exact proxy peer addresses before starting the upgraded server:

1. Stop Bookshelf and back up the home, including `config.xml` and the databases,
   to a private location.
2. Add or update the `TrustedNetworks` element inside the existing `<Config>`
   element of `/path/to/config/config.xml`. Preserve the other settings and
   credentials. Alternatively, configure `Readarr__Server__TrustedNetworks` in
   the service or container environment before starting the upgraded version.
3. Start the upgraded server and check access through the proxy. Confirm that
   HTTPS redirects and authentication use the intended public address.

Keep the old server stopped after editing XML, so it cannot overwrite the new
setting. If you configure trust through the upgraded UI instead, use an
accessible administrative connection, save the setting and restart before
expecting forwarded headers to work. A pre-upgrade backup is also required for
rolling back database upgrades.

## Forwarded headers and authentication

For a TLS-terminating proxy, preserve the public `Host` header and supply
`X-Forwarded-For` with the verified client address and `X-Forwarded-Proto` with
the public scheme, usually `https`. At the public edge, overwrite untrusted
client-supplied forwarding headers. Do not append an unverified client-supplied
address chain.

For multiple proxies, each downstream proxy appends its verified immediate
peer to the trusted forwarding chain. List every proxy whose entries Bookshelf
must consume. Trust is evaluated from the immediate peer back through that
chain. If any `X-Forwarded-For` entries remain unconsumed, LAN authentication
bypass is refused and normal login is required when authentication is enabled.
Empty or malformed client-address headers also require normal login. A request
without a known transport peer cannot replace its address using forwarded headers.
With authentication disabled, the setting does not introduce a login screen.

A trusted proxy must be responsible for the headers it sends. Listing an entire
LAN permits clients on that LAN to supply forwarding information themselves;
use exact proxy addresses or the smallest dedicated proxy network instead.
