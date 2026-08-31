# AirMic LAN protocol v1

AirMic uses two LAN-only channels:

- TCP 51243 with TLS 1.2+ for pairing, capability negotiation, keepalive, and key delivery.
- UDP 51244 for AES-256-GCM authenticated audio packets.

The Windows receiver advertises `_airmic._tcp.local.` over mDNS. The TXT record
contains only `version=1`, `controlPort=51243`, and a human-readable PC name.
No PIN, key, device identifier, or IP is placed in mDNS.

## Pairing

Messages on the TLS control channel are UTF-8 JSON prefixed by a four-byte,
big-endian unsigned length. Receivers reject messages over 64 KiB.

The iPhone sends:

```json
{"type":"pair","protocolVersion":1,"deviceId":"uuid","deviceName":"Adib's iPhone","pin":"123456"}
```

On success Windows returns:

```json
{"ok":true,"sessionId":305419896,"audioPort":51244,"audioHost":"ADIB-PC.local","key":"base64-32-bytes","certificateFingerprint":"sha256-hex"}
```

The session key exists only in memory. A successful pairing resets the failure
counter. Five failures from one address within five minutes cause a 30-second
lockout. Previously paired clients pin the Windows certificate fingerprint.

## Audio datagram

All integer fields are network byte order. PCM samples are signed 16-bit little
endian. The 32-byte header is authenticated as AES-GCM associated data.

| Offset | Size | Field |
| ---: | ---: | --- |
| 0 | 4 | ASCII `AMIC` |
| 4 | 1 | Version (`1`) |
| 5 | 1 | Flags: bit 0 encrypted, bit 1 muted |
| 6 | 2 | Header length (`32`) |
| 8 | 4 | Sequence number |
| 12 | 8 | Capture timestamp, microseconds since Unix epoch |
| 20 | 4 | Sample rate (16000, 24000, or 48000) |
| 24 | 2 | Samples per channel |
| 26 | 1 | Channels (`1`) |
| 27 | 1 | Format (`1` = PCM16LE) |
| 28 | 4 | Session ID |

The encrypted payload is `ciphertext || 16-byte tag`. The 12-byte GCM nonce is
`sessionId (4 bytes) || sequence (8 bytes)`, both big-endian. A session must be
re-paired before its 32-bit sequence wraps; implementations reject replayed or
very old sequence numbers.

At 48 kHz AirMic sends 10 ms (480-sample) frames so a datagram stays below a
normal Ethernet MTU. At 16/24 kHz it may use 20 ms frames. The receiver uses a
small adaptive jitter buffer and inserts silence for packets that miss their
playout deadline.
