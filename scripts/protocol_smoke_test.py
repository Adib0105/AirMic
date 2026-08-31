#!/usr/bin/env python3
"""Cross-platform AirMic protocol checks runnable without Windows or Xcode."""

from __future__ import annotations

import json
import pathlib
import struct
import unittest

from cryptography.hazmat.primitives.ciphers.aead import AESGCM

ROOT = pathlib.Path(__file__).resolve().parents[1]


def header(*, flags=1, sequence=42, timestamp=1_725_100_000_123_456,
           rate=48_000, samples=480, session=0x12345678) -> bytes:
    return struct.pack(">4sBBHIQIHBBI", b"AMIC", 1, flags, 32, sequence,
                       timestamp, rate, samples, 1, 1, session)


def nonce(session: int, sequence: int) -> bytes:
    return struct.pack(">IQ", session, sequence)


class ProtocolTests(unittest.TestCase):
    def test_golden_header(self) -> None:
        vector = json.loads((ROOT / "shared/test-vectors/audio-packet-v1.json").read_text())
        self.assertEqual(header(flags=0).hex(), vector["headerHex"])
        self.assertEqual(len(header()), 32)

    def test_aes_gcm_round_trip_and_tamper_rejection(self) -> None:
        key = bytes(range(32))
        pcm = bytes((index % 251 for index in range(960)))
        aad = header(sequence=7, timestamp=99, session=1234)
        encrypted = AESGCM(key).encrypt(nonce(1234, 7), pcm, aad)
        datagram = aad + encrypted
        self.assertEqual(AESGCM(key).decrypt(nonce(1234, 7), datagram[32:], datagram[:32]), pcm)
        damaged = bytearray(datagram); damaged[40] ^= 1
        with self.assertRaises(Exception):
            AESGCM(key).decrypt(nonce(1234, 7), damaged[32:], damaged[:32])

    def test_48khz_packet_stays_below_mtu(self) -> None:
        packet_size = 32 + (480 * 2) + 16
        self.assertEqual(packet_size, 1008)
        self.assertLess(packet_size, 1500)

    def test_source_has_no_cloud_media_endpoint(self) -> None:
        code_extensions = {".swift", ".cs"}
        forbidden = ("firebase", "amazonaws", "blob.core.windows.net", "webrtc.org")
        for base in (ROOT / "ios", ROOT / "windows"):
            for path in base.rglob("*"):
                if path.suffix not in code_extensions:
                    continue
                lowered = path.read_text(errors="ignore").lower()
                for value in forbidden:
                    self.assertNotIn(value, lowered, f"{value} found in {path}")


if __name__ == "__main__":
    unittest.main(verbosity=2)
