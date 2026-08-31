import AVFoundation
import Foundation
import Network
import UIKit

@MainActor
final class AirMicViewModel: ObservableObject {
    @Published var status: ConnectionStatus = .discovering
    @Published var devices: [DiscoveredPC] = []
    @Published var selectedDevice: DiscoveredPC?
    @Published var level: Float = 0
    @Published var latencyMs: Double?
    @Published var muted = false
    @Published var gain: Double = 1
    @Published var sampleRate: SampleRateOption = .full
    @Published var noiseSuppression = true
    @Published var autoGain = true
    @Published var noiseGate = false
    @Published var errorMessage: String?

    private let discovery = DiscoveryService()
    private let pairing = PairingClient()
    private let capture = AudioCaptureService()
    private var transport: AudioTransport?
    private var activeSession: PairedSession?

    init() {
        discovery.onResults = { [weak self] values in
            self?.devices = values
            if self?.selectedDevice == nil { self?.selectedDevice = values.first }
        }
        discovery.onError = { [weak self] message in self?.errorMessage = message }
        discovery.start()
    }

    func connect(pin: String) async {
        guard pin.count == 6, pin.allSatisfy(\.isNumber) else { errorMessage = "Enter the six-digit PIN shown on your PC."; return }
        guard let device = selectedDevice else { errorMessage = "Select a PC or enter its IP address."; return }
        await connect(endpoint: device.endpoint, displayName: device.name, pin: pin)
    }

    func connect(manualHost: String, pin: String) async {
        let value = manualHost.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !value.isEmpty else { errorMessage = "Enter your PC's local IP address."; return }
        let endpoint = NWEndpoint.hostPort(host: NWEndpoint.Host(value), port: 51243)
        await connect(endpoint: endpoint, displayName: value, pin: pin)
    }

    private func connect(endpoint: NWEndpoint, displayName: String, pin: String) async {
        errorMessage = nil; status = .pairing
        guard await AVAudioApplication.requestRecordPermission() else {
            status = .failed("Microphone permission denied")
            errorMessage = "Enable Microphone access in iPhone Settings → AirMic."
            return
        }
        do {
            let session = try await pairing.pair(endpoint: endpoint, pin: pin)
            activeSession = session; latencyMs = session.approximateLatencyMs
            let transport = AudioTransport(session: session); self.transport = transport
            transport.onState = { [weak self] state in
                guard let self else { return }
                switch state {
                case .ready: self.status = .connected(displayName)
                case .connecting: self.status = .pairing
                case .reconnecting: self.status = .reconnecting
                case .failed(let message): self.status = .failed(message); self.errorMessage = message
                case .stopped: if self.activeSession == nil { self.status = .idle }
                }
            }
            transport.start()
            try startCapture()
            UIApplication.shared.isIdleTimerDisabled = true
            discovery.stop()
        } catch {
            status = .failed("Connection failed")
            errorMessage = (error as? LocalizedError)?.errorDescription ?? "Could not connect to the PC."
            transport?.stop(); transport = nil; activeSession = nil
        }
    }

    func applyAudioSettings() {
        guard activeSession != nil else { return }
        do { try startCapture() }
        catch { errorMessage = "Could not apply the audio settings." }
    }

    private func startCapture() throws {
        let settings = CaptureSettings(sampleRate: sampleRate.rawValue, gain: Float(gain),
                                       noiseSuppression: noiseSuppression, autoGain: autoGain, noiseGate: noiseGate)
        try capture.start(settings: settings) { [weak self] pcm, samples, level in
            guard let self else { return }
            Task { @MainActor in
                self.level = self.muted ? 0 : level
                self.transport?.send(pcm: self.muted ? Data(count: pcm.count) : pcm,
                                     sampleRate: self.sampleRate.rawValue, sampleCount: samples, muted: self.muted)
            }
        }
    }

    func disconnect() {
        capture.stop(); transport?.stop(); transport = nil; activeSession = nil
        UIApplication.shared.isIdleTimerDisabled = false
        status = .discovering; level = 0; latencyMs = nil
        discovery.start()
    }

    func toggleMute() { muted.toggle(); if muted { level = 0 } }
}
