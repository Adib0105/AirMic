import CryptoKit
import Foundation
import Network

final class AudioTransport {
    enum State { case connecting, ready, reconnecting, failed(String), stopped }
    private let session: PairedSession
    private let key: SymmetricKey
    private let queue = DispatchQueue(label: "com.airmic.audio.transport", qos: .userInteractive)
    private var connection: NWConnection?
    private var generation = UUID()
    private var sequence: UInt32 = 0
    private var retry = 0
    private var reconnectScheduled = false
    private var stopped = false
    var onState: ((State) -> Void)?

    init(session: PairedSession) {
        self.session = session
        self.key = SymmetricKey(data: session.key)
    }

    func start() { queue.async { [weak self] in self?.connect() } }

    func stop() {
        queue.async { [weak self] in
            self?.stopped = true
            self?.connection?.cancel()
            self?.connection = nil
            self?.publish(.stopped)
        }
    }

    func send(pcm: Data, sampleRate: Int, sampleCount: Int, muted: Bool) {
        queue.async { [weak self] in
            guard let self, !self.stopped, let connection = self.connection else { return }
            guard sampleCount > 0, sampleCount <= UInt16.max, pcm.count == sampleCount * 2 else { return }
            if self.sequence == UInt32.max {
                self.publish(.failed("The secure session expired. Pair again.")); self.stop(); return
            }
            do {
                let datagram = try self.makeDatagram(pcm: pcm, sampleRate: sampleRate, sampleCount: UInt16(sampleCount), muted: muted)
                connection.send(content: datagram, completion: .contentProcessed { [weak self] error in
                    if error != nil { self?.scheduleReconnect() }
                })
                self.sequence &+= 1
            } catch {
                self.publish(.failed("Audio encryption failed."))
            }
        }
    }

    private func connect() {
        guard !stopped else { return }
        reconnectScheduled = false
        let token = UUID(); generation = token
        let parameters = NWParameters.udp
        parameters.allowLocalEndpointReuse = true
        let connection = NWConnection(host: session.endpoint, port: session.audioPort, using: parameters)
        self.connection?.cancel(); self.connection = connection
        publish(retry == 0 ? .connecting : .reconnecting)
        connection.stateUpdateHandler = { [weak self] state in
            guard let self else { return }
            self.queue.async {
                guard token == self.generation, !self.stopped else { return }
                switch state {
                case .ready: self.retry = 0; self.publish(.ready)
                case .waiting, .failed: self.scheduleReconnect()
                case .cancelled: break
                default: break
                }
            }
        }
        connection.start(queue: queue)
    }

    private func scheduleReconnect() {
        guard !stopped, !reconnectScheduled else { return }
        reconnectScheduled = true
        connection?.cancel(); connection = nil
        retry += 1
        let delay = min(pow(2.0, Double(min(retry, 4))), 15)
        publish(.reconnecting)
        queue.asyncAfter(deadline: .now() + delay) { [weak self] in self?.connect() }
    }

    func makeDatagram(pcm: Data, sampleRate: Int, sampleCount: UInt16, muted: Bool) throws -> Data {
        var header = Data(); header.reserveCapacity(32)
        header.append(contentsOf: [0x41, 0x4D, 0x49, 0x43, 1, muted ? 3 : 1])
        header.appendBigEndian(UInt16(32))
        header.appendBigEndian(sequence)
        header.appendBigEndian(UInt64(Date().timeIntervalSince1970 * 1_000_000))
        header.appendBigEndian(UInt32(sampleRate))
        header.appendBigEndian(sampleCount)
        header.append(contentsOf: [1, 1])
        header.appendBigEndian(session.sessionId)

        var nonceData = Data(); nonceData.appendBigEndian(session.sessionId); nonceData.appendBigEndian(UInt64(sequence))
        let nonce = try AES.GCM.Nonce(data: nonceData)
        let sealed = try AES.GCM.seal(pcm, using: key, nonce: nonce, authenticating: header)
        var packet = header; packet.append(sealed.ciphertext); packet.append(sealed.tag)
        return packet
    }

    private func publish(_ state: State) { DispatchQueue.main.async { [weak self] in self?.onState?(state) } }
}

private extension Data {
    mutating func appendBigEndian<T: FixedWidthInteger>(_ value: T) {
        var big = value.bigEndian
        Swift.withUnsafeBytes(of: &big) { append(contentsOf: $0) }
    }
}
