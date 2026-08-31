import Foundation
import Network

final class DiscoveryService {
    private let queue = DispatchQueue(label: "com.airmic.discovery")
    private var browser: NWBrowser?
    var onResults: (([DiscoveredPC]) -> Void)?
    var onError: ((String) -> Void)?

    func start() {
        stop()
        let parameters = NWParameters.tcp
        parameters.includePeerToPeer = false
        let browser = NWBrowser(for: .bonjour(type: "_airmic._tcp", domain: "local."), using: parameters)
        browser.browseResultsChangedHandler = { [weak self] results, _ in
            let devices = results.compactMap { result -> DiscoveredPC? in
                guard case let .service(name, _, _, _) = result.endpoint else { return nil }
                return DiscoveredPC(id: String(describing: result.endpoint), name: name, endpoint: result.endpoint)
            }.sorted { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }
            DispatchQueue.main.async { self?.onResults?(devices) }
        }
        browser.stateUpdateHandler = { [weak self] state in
            if case .failed(let error) = state {
                DispatchQueue.main.async { self?.onError?(Self.friendly(error)) }
            }
        }
        self.browser = browser
        browser.start(queue: queue)
    }

    func stop() { browser?.cancel(); browser = nil }

    private static func friendly(_ error: NWError) -> String {
        if case .dns(let code) = error, code == -65570 {
            return "Local Network access is off. Enable it in iPhone Settings → AirMic."
        }
        return "PC not found. Make sure both devices use the same Wi-Fi network."
    }
}
