import SwiftUI

struct HomeView: View {
    @EnvironmentObject private var model: AirMicViewModel
    @State private var pin = ""
    @State private var showManual = false
    @State private var showSettings = false

    var body: some View {
        NavigationStack {
            ZStack {
                LinearGradient(colors: [Color(hex: 0x081018), Color(hex: 0x111B2B)], startPoint: .top, endPoint: .bottom).ignoresSafeArea()
                ScrollView {
                    VStack(alignment: .leading, spacing: 18) {
                        header
                        statusCard
                        if isConnected { meterCard; controls }
                        else { deviceCard; pairingCard }
                        trustNotice
                    }
                    .padding(20)
                }
            }
            .toolbar { ToolbarItem(placement: .topBarTrailing) { Button { showSettings = true } label: { Image(systemName: "gearshape.fill").foregroundStyle(.white) } } }
            .sheet(isPresented: $showManual) { ManualConnectionView(pin: $pin) }
            .sheet(isPresented: $showSettings) { SettingsView() }
            .alert("AirMic", isPresented: Binding(get: { model.errorMessage != nil }, set: { if !$0 { model.errorMessage = nil } })) {
                Button("OK", role: .cancel) { model.errorMessage = nil }
            } message: { Text(model.errorMessage ?? "") }
        }
        .preferredColorScheme(.dark)
    }

    private var header: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text("AIRMIC").font(.system(size: 34, weight: .black, design: .rounded)).tracking(2)
            Text("iPhone Microphone").foregroundStyle(.secondary)
        }.padding(.top, 8)
    }

    private var statusCard: some View {
        Card {
            HStack(spacing: 12) {
                Circle().fill(statusColor).frame(width: 11, height: 11).shadow(color: statusColor.opacity(0.7), radius: 8)
                VStack(alignment: .leading, spacing: 4) {
                    Text("STATUS").font(.caption2.weight(.bold)).foregroundStyle(.secondary)
                    Text(model.status.title).font(.headline)
                }
                Spacer()
                if case .pairing = model.status { ProgressView().tint(Color.airMic) }
                if case .reconnecting = model.status { ProgressView().tint(Color.airMic) }
            }
        }
    }

    private var deviceCard: some View {
        Card {
            VStack(alignment: .leading, spacing: 12) {
                Label("Windows PC", systemImage: "desktopcomputer").font(.headline)
                if model.devices.isEmpty {
                    HStack { ProgressView().tint(Color.airMic); Text("Searching on this Wi-Fi…").foregroundStyle(.secondary) }
                } else {
                    Picker("Device", selection: $model.selectedDevice) {
                        ForEach(model.devices) { device in Text(device.name).tag(Optional(device)) }
                    }.pickerStyle(.menu).tint(Color.airMic)
                }
                Button("Enter PC IP manually") { showManual = true }.font(.subheadline).foregroundStyle(Color.airMic)
            }
        }
    }

    private var pairingCard: some View {
        Card {
            VStack(alignment: .leading, spacing: 13) {
                Text("PAIRING PIN").font(.caption2.weight(.bold)).foregroundStyle(.secondary)
                TextField("6 digits shown on PC", text: $pin)
                    .keyboardType(.numberPad).textContentType(.oneTimeCode)
                    .font(.system(size: 24, weight: .bold, design: .monospaced))
                    .padding(14).background(Color.white.opacity(0.06), in: RoundedRectangle(cornerRadius: 12))
                    .onChange(of: pin) { _, value in pin = String(value.filter(\.isNumber).prefix(6)) }
                Button {
                    Task { await model.connect(pin: pin) }
                } label: {
                    Label("CONNECT SECURELY", systemImage: "lock.fill").frame(maxWidth: .infinity).padding(.vertical, 5)
                }.buttonStyle(AirMicButtonStyle()).disabled(pin.count != 6 || model.selectedDevice == nil)
            }
        }
    }

    private var meterCard: some View {
        Card {
            VStack(alignment: .leading, spacing: 12) {
                HStack { Text("MICROPHONE LEVEL").font(.caption2.weight(.bold)).foregroundStyle(.secondary); Spacer(); Text(model.latencyMs.map { "~\(Int($0)) ms" } ?? "-- ms").foregroundStyle(.secondary) }
                GeometryReader { proxy in
                    ZStack(alignment: .leading) {
                        Capsule().fill(Color.white.opacity(0.08))
                        Capsule().fill(LinearGradient(colors: [.airMic, .green], startPoint: .leading, endPoint: .trailing)).frame(width: proxy.size.width * CGFloat(min(1, model.level)))
                    }
                }.frame(height: 16).animation(.linear(duration: 0.08), value: model.level)
                Text("\(model.sampleRate.title) • Mono • Encrypted LAN audio").font(.footnote).foregroundStyle(.secondary)
            }
        }
    }

    private var controls: some View {
        HStack(spacing: 12) {
            Button { model.toggleMute() } label: { Label(model.muted ? "UNMUTE" : "MUTE", systemImage: model.muted ? "mic.fill" : "mic.slash.fill").frame(maxWidth: .infinity).padding(.vertical, 5) }.buttonStyle(AirMicButtonStyle())
            Button(role: .destructive) { model.disconnect() } label: { Text("DISCONNECT").frame(maxWidth: .infinity).padding(.vertical, 5) }.buttonStyle(DarkButtonStyle())
        }
    }

    private var trustNotice: some View {
        Label("Use AirMic only on a trusted private Wi-Fi network. Audio is not uploaded or saved.", systemImage: "shield.checkered")
            .font(.footnote).foregroundStyle(.secondary).padding(.horizontal, 4)
    }

    private var isConnected: Bool { if case .connected = model.status { return true }; if case .reconnecting = model.status { return true }; return false }
    private var statusColor: Color { if isConnected { return .airMic }; if case .failed = model.status { return .red }; return .orange }
}

private struct Card<Content: View>: View {
    @ViewBuilder let content: Content
    var body: some View { content.padding(20).frame(maxWidth: .infinity, alignment: .leading).background(Color.white.opacity(0.055), in: RoundedRectangle(cornerRadius: 18)).overlay(RoundedRectangle(cornerRadius: 18).stroke(Color.white.opacity(0.06))) }
}

private struct AirMicButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View { configuration.label.font(.subheadline.bold()).foregroundStyle(Color(hex: 0x04120F)).padding(.horizontal, 12).background(Color.airMic.opacity(configuration.isPressed ? 0.75 : 1), in: RoundedRectangle(cornerRadius: 12)) }
}
private struct DarkButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View { configuration.label.font(.subheadline.bold()).foregroundStyle(.white).padding(.horizontal, 12).background(Color.white.opacity(configuration.isPressed ? 0.15 : 0.08), in: RoundedRectangle(cornerRadius: 12)) }
}

extension Color {
    static let airMic = Color(hex: 0x61E7C3)
    init(hex: UInt32) { self.init(red: Double((hex >> 16) & 0xff) / 255, green: Double((hex >> 8) & 0xff) / 255, blue: Double(hex & 0xff) / 255) }
}
