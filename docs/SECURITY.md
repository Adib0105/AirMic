# Security model

AirMic assumes both devices are on a trusted private LAN. It does not expose a
cloud endpoint, open a router port, perform UPnP, or store microphone audio.

## Controls

- TLS 1.2+ protects pairing and session negotiation.
- The six-digit PIN is generated on Windows, expires, and is rate-limited per
  source address.
- Each connection receives a random 256-bit audio key and 32-bit session ID.
- Every UDP packet is authenticated and encrypted with AES-256-GCM.
- Sequence validation rejects duplicate and stale packets.
- A paired iPhone pins the Windows TLS certificate fingerprint.
- Logs exclude PINs, keys, raw audio, and full packet payloads.

## First-pair limitation

The first connection accepts the receiver's self-signed certificate and then
authenticates the user-entered PIN. This blocks passive listeners and online
guessing, but a sophisticated active relay on a hostile LAN is outside v1's
threat model. Pair only on a trusted home or office Wi-Fi network. A future
version may use a PAKE such as SPAKE2+ to remove this limitation.

## Firewall

The installer adds inbound rules only for the AirMic executable's TCP 51243 and
UDP 51244 listeners on Private profiles. Public-profile rules are not created.
