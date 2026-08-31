import Foundation
import Network

enum ConnectionStatus: Equatable {
    case idle
    case discovering
    case pairing
    case connected(String)
    case reconnecting
    case failed(String)

    var title: String {
        switch self {
        case .idle: "Not connected"
        case .discovering: "Looking for your PC"
        case .pairing: "Pairing securely"
        case .connected(let name): "Connected to \(name)"
        case .reconnecting: "Reconnecting"
        case .failed(let message): message
        }
    }
}

struct DiscoveredPC: Identifiable, Hashable {
    let id: String
    let name: String
    let endpoint: NWEndpoint

    static func == (lhs: DiscoveredPC, rhs: DiscoveredPC) -> Bool { lhs.id == rhs.id }
    func hash(into hasher: inout Hasher) { hasher.combine(id) }
}

struct PairedSession {
    let sessionId: UInt32
    let key: Data
    let endpoint: NWEndpoint.Host
    let audioPort: NWEndpoint.Port
    let certificateFingerprint: String
    let approximateLatencyMs: Double
}

enum SampleRateOption: Int, CaseIterable, Identifiable {
    case voice = 16_000
    case balanced = 24_000
    case full = 48_000
    var id: Int { rawValue }
    var title: String { "\(rawValue / 1_000) kHz" }
}
