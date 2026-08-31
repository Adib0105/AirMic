import CryptoKit
import Network
import XCTest
@testable import AirMic

final class AirMicProtocolTests: XCTestCase {
    func testEncryptedDatagramUsesV1HeaderAndStaysBelowEthernetMtu() throws {
        let session = PairedSession(sessionId: 0x12345678,
                                    key: Data((0..<32).map(UInt8.init)),
                                    endpoint: NWEndpoint.Host("192.168.1.10"),
                                    audioPort: 51244,
                                    certificateFingerprint: String(repeating: "a", count: 64),
                                    approximateLatencyMs: 1)
        let transport = AudioTransport(session: session)
        let pcm = Data(repeating: 0x5A, count: 960)
        let packet = try transport.makeDatagram(pcm: pcm, sampleRate: 48_000, sampleCount: 480, muted: false)
        XCTAssertEqual(packet.prefix(4), Data("AMIC".utf8))
        XCTAssertEqual(packet[4], 1)
        XCTAssertEqual(packet.count, 32 + 960 + 16)
        XCTAssertLessThan(packet.count, 1_500)
    }

    func testMutedFlagIsAuthenticated() throws {
        let session = PairedSession(sessionId: 1, key: Data(repeating: 7, count: 32),
                                    endpoint: NWEndpoint.Host("pc.local"), audioPort: 51244,
                                    certificateFingerprint: "fingerprint", approximateLatencyMs: 1)
        let packet = try AudioTransport(session: session).makeDatagram(
            pcm: Data(repeating: 0, count: 960), sampleRate: 48_000, sampleCount: 480, muted: true)
        XCTAssertEqual(packet[5], 3)
    }
}
