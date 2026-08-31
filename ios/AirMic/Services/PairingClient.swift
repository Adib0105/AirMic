import CryptoKit
import Foundation
import Network
import Security
import UIKit

final class PairingClient {
    enum PairingError: LocalizedError {
        case invalidEndpoint, invalidResponse, rejected(String), certificateChanged, connection(String)
        var errorDescription: String? {
            switch self {
            case .invalidEndpoint: "The selected PC address is invalid."
            case .invalidResponse: "The PC returned an invalid pairing response."
            case .rejected(let message): message
            case .certificateChanged: "This PC's security identity changed. Remove the saved pairing before reconnecting."
            case .connection(let message): message
            }
        }
    }

    private struct Request: Codable {
        let type = "pair"
        let protocolVersion = 1
        let deviceId: String
        let deviceName: String
        let pin: String
    }
    private struct Response: Codable {
        let ok: Bool
        let error: String?
        let sessionId: UInt32?
        let audioPort: Int?
        let audioHost: String?
        let key: String?
        let certificateFingerprint: String?
    }

    private let queue = DispatchQueue(label: "com.airmic.pairing")
    private let fingerprintLock = NSLock()
    private var observedFingerprint: String?

    func pair(endpoint: NWEndpoint, pin: String) async throws -> PairedSession {
        let tls = NWProtocolTLS.Options()
        sec_protocol_options_set_min_tls_protocol_version(tls.securityProtocolOptions, .TLSv12)
        let storageKey = "airmic.cert.\(String(describing: endpoint))"
        let pinned = UserDefaults.standard.string(forKey: storageKey)
        sec_protocol_options_set_verify_block(tls.securityProtocolOptions, { [weak self] _, trust, complete in
            guard let self, let fingerprint = Self.fingerprint(trust: trust) else { complete(false); return }
            self.fingerprintLock.lock(); self.observedFingerprint = fingerprint; self.fingerprintLock.unlock()
            complete(pinned == nil || pinned?.lowercased() == fingerprint)
        }, queue)
        let parameters = NWParameters(tls: tls, tcp: NWProtocolTCP.Options())
        let connection = NWConnection(to: endpoint, using: parameters)
        try await waitUntilReady(connection)
        defer { connection.cancel() }

        let request = Request(deviceId: deviceIdentifier(), deviceName: UIDevice.current.name, pin: pin)
        let payload = try JSONEncoder().encode(request)
        let started = ContinuousClock.now
        try await sendFramed(payload, on: connection)
        let responseData = try await receiveFramed(on: connection)
        let elapsed = started.duration(to: .now)
        let response = try JSONDecoder().decode(Response.self, from: responseData)
        guard response.ok else { throw PairingError.rejected(response.error ?? "Pairing was rejected.") }
        guard let sessionId = response.sessionId, let portValue = response.audioPort,
              let audioHost = response.audioHost, !audioHost.isEmpty,
              let keyText = response.key, let key = Data(base64Encoded: keyText), key.count == 32,
              let returnedFingerprint = response.certificateFingerprint,
              let port = NWEndpoint.Port(rawValue: UInt16(portValue)) else { throw PairingError.invalidResponse }
        fingerprintLock.lock(); let observed = observedFingerprint; fingerprintLock.unlock()
        guard observed == returnedFingerprint.lowercased() else { throw PairingError.certificateChanged }
        UserDefaults.standard.set(returnedFingerprint.lowercased(), forKey: storageKey)
        let audioEndpoint: NWEndpoint.Host
        if case .hostPort(let manualHost, _) = endpoint { audioEndpoint = manualHost }
        else { audioEndpoint = NWEndpoint.Host(audioHost) }
        return PairedSession(sessionId: sessionId, key: key, endpoint: audioEndpoint, audioPort: port,
                             certificateFingerprint: returnedFingerprint, approximateLatencyMs: elapsed.seconds * 500)
    }

    private func waitUntilReady(_ connection: NWConnection) async throws {
        try await withCheckedThrowingContinuation { continuation in
            let gate = ContinuationGate()
            connection.stateUpdateHandler = { state in
                switch state {
                case .ready: gate.resume { continuation.resume() }
                case .failed(let error): gate.resume { continuation.resume(throwing: PairingError.connection(Self.friendly(error))) }
                case .cancelled: gate.resume { continuation.resume(throwing: CancellationError()) }
                default: break
                }
            }
            connection.start(queue: queue)
        }
    }

    private func sendFramed(_ payload: Data, on connection: NWConnection) async throws {
        var size = UInt32(payload.count).bigEndian
        var frame = Data(bytes: &size, count: 4); frame.append(payload)
        try await withCheckedThrowingContinuation { continuation in
            connection.send(content: frame, completion: .contentProcessed { error in
                if let error { continuation.resume(throwing: PairingError.connection(Self.friendly(error))) }
                else { continuation.resume() }
            })
        }
    }

    private func receiveFramed(on connection: NWConnection) async throws -> Data {
        let lengthData = try await receiveExactly(4, on: connection)
        let length = lengthData.reduce(UInt32(0)) { ($0 << 8) | UInt32($1) }
        guard length > 0 && length <= 65_536 else { throw PairingError.invalidResponse }
        return try await receiveExactly(Int(length), on: connection)
    }

    private func receiveExactly(_ length: Int, on connection: NWConnection) async throws -> Data {
        var result = Data()
        while result.count < length {
            let part: Data = try await withCheckedThrowingContinuation { continuation in
                connection.receive(minimumIncompleteLength: 1, maximumLength: length - result.count) { data, _, complete, error in
                    if let error { continuation.resume(throwing: PairingError.connection(Self.friendly(error))) }
                    else if let data, !data.isEmpty { continuation.resume(returning: data) }
                    else if complete { continuation.resume(throwing: PairingError.invalidResponse) }
                    else { continuation.resume(throwing: PairingError.invalidResponse) }
                }
            }
            result.append(part)
        }
        return result
    }

    private func deviceIdentifier() -> String {
        if let stored = UserDefaults.standard.string(forKey: "airmic.deviceId") { return stored }
        let value = UUID().uuidString; UserDefaults.standard.set(value, forKey: "airmic.deviceId"); return value
    }

    private static func fingerprint(trust: sec_trust_t) -> String? {
        let secTrust = sec_trust_copy_ref(trust).takeRetainedValue()
        guard let certificate = SecTrustGetCertificateAtIndex(secTrust, 0) else { return nil }
        let digest = SHA256.hash(data: SecCertificateCopyData(certificate) as Data)
        return digest.map { String(format: "%02x", $0) }.joined()
    }

    private static func friendly(_ error: NWError) -> String {
        switch error {
        case .posix(.ECONNREFUSED): "AirMic is not running on the selected PC."
        case .posix(.ETIMEDOUT): "The PC did not respond. Check Wi-Fi and Windows Firewall."
        default: "Could not connect to the PC on the local network."
        }
    }
}

private final class ContinuationGate: @unchecked Sendable {
    private let lock = NSLock(); private var resumed = false
    func resume(_ action: () -> Void) { lock.lock(); defer { lock.unlock() }; guard !resumed else { return }; resumed = true; action() }
}

private extension Duration {
    var seconds: Double { Double(components.seconds) + Double(components.attoseconds) / 1e18 }
}
