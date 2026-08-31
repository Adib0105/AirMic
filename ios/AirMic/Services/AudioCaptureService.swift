import AVFoundation
import Foundation

struct CaptureSettings {
    let sampleRate: Int
    let gain: Float
    let noiseSuppression: Bool
    let autoGain: Bool
    let noiseGate: Bool
}

final class AudioCaptureService {
    typealias FrameHandler = (_ pcm: Data, _ samples: Int, _ level: Float) -> Void
    private let engine = AVAudioEngine()
    private let processingQueue = DispatchQueue(label: "com.airmic.audio.capture", qos: .userInteractive)
    private var converter: AVAudioConverter?
    private var pending = Data()
    private var settings: CaptureSettings?
    private var handler: FrameHandler?
    private var running = false

    func start(settings: CaptureSettings, handler: @escaping FrameHandler) throws {
        stop()
        self.settings = settings; self.handler = handler
        let session = AVAudioSession.sharedInstance()
        try session.setCategory(.record, mode: .voiceChat, options: [.allowBluetoothHFP])
        try session.setPreferredSampleRate(Double(settings.sampleRate))
        try session.setPreferredIOBufferDuration(settings.sampleRate == 48_000 ? 0.01 : 0.02)
        try session.setActive(true)

        let input = engine.inputNode
        do { try input.setVoiceProcessingEnabled(settings.noiseSuppression || settings.autoGain) } catch { /* device/route may not support voice processing */ }
        let inputFormat = input.outputFormat(forBus: 0)
        guard let outputFormat = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: Double(settings.sampleRate), channels: 1, interleaved: false),
              let converter = AVAudioConverter(from: inputFormat, to: outputFormat) else {
            throw NSError(domain: "AirMic.Audio", code: 1, userInfo: [NSLocalizedDescriptionKey: "The selected microphone format is not supported."])
        }
        self.converter = converter
        input.installTap(onBus: 0, bufferSize: 960, format: inputFormat) { [weak self] buffer, _ in
            guard let self, let copy = Self.copy(buffer) else { return }
            self.processingQueue.async { self.process(copy, outputFormat: outputFormat) }
        }
        engine.prepare(); try engine.start(); running = true
    }

    func stop() {
        if running { engine.inputNode.removeTap(onBus: 0) }
        engine.stop(); running = false; converter = nil; pending.removeAll(keepingCapacity: false)
        try? AVAudioSession.sharedInstance().setActive(false, options: .notifyOthersOnDeactivation)
    }

    private func process(_ input: AVAudioPCMBuffer, outputFormat: AVAudioFormat) {
        guard let converter, let settings else { return }
        let ratio = outputFormat.sampleRate / input.format.sampleRate
        let capacity = AVAudioFrameCount(ceil(Double(input.frameLength) * ratio)) + 8
        guard let output = AVAudioPCMBuffer(pcmFormat: outputFormat, frameCapacity: capacity) else { return }
        var supplied = false; var conversionError: NSError?
        converter.convert(to: output, error: &conversionError) { _, status in
            if supplied { status.pointee = .noDataNow; return nil }
            supplied = true; status.pointee = .haveData; return input
        }
        guard conversionError == nil, let channel = output.floatChannelData?[0] else { return }
        let count = Int(output.frameLength)
        var peak: Float = 0
        var samples = [Int16](); samples.reserveCapacity(count)
        let gateThreshold: Float = settings.noiseGate ? powf(10, -55.0 / 20.0) : 0
        for index in 0..<count {
            var value = max(-1, min(1, channel[index] * settings.gain))
            if abs(value) < gateThreshold { value = 0 }
            peak = max(peak, abs(value))
            samples.append(Int16(max(Float(Int16.min), min(Float(Int16.max), value * Float(Int16.max)))))
        }
        samples.withUnsafeBytes { pending.append(contentsOf: $0) }
        let packetSamples = settings.sampleRate == 48_000 ? 480 : settings.sampleRate / 50
        let packetBytes = packetSamples * 2
        while pending.count >= packetBytes {
            let frame = pending.prefix(packetBytes); pending.removeFirst(packetBytes)
            handler?(Data(frame), packetSamples, peak)
        }
    }

    private static func copy(_ source: AVAudioPCMBuffer) -> AVAudioPCMBuffer? {
        guard let destination = AVAudioPCMBuffer(pcmFormat: source.format, frameCapacity: source.frameLength) else { return nil }
        destination.frameLength = source.frameLength
        let buffers = UnsafeMutableAudioBufferListPointer(destination.mutableAudioBufferList)
        let sourceBuffers = UnsafeMutableAudioBufferListPointer(source.mutableAudioBufferList)
        for index in 0..<min(buffers.count, sourceBuffers.count) {
            guard let destinationData = buffers[index].mData, let sourceData = sourceBuffers[index].mData else { continue }
            memcpy(destinationData, sourceData, Int(sourceBuffers[index].mDataByteSize))
            buffers[index].mDataByteSize = sourceBuffers[index].mDataByteSize
        }
        return destination
    }
}
