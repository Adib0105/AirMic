import SwiftUI
import UIKit

struct SettingsView: View {
    @EnvironmentObject private var model: AirMicViewModel
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationStack {
            Form {
                Section("Audio") {
                    Picker("Sample Rate", selection: $model.sampleRate) { ForEach(SampleRateOption.allCases) { option in Text(option.title).tag(option) } }
                    VStack(alignment: .leading) { Text("Gain: \(model.gain, specifier: "%.1f")×"); Slider(value: $model.gain, in: 0...3, step: 0.1) }
                    Toggle("Noise Suppression", isOn: $model.noiseSuppression)
                    Toggle("Automatic Gain", isOn: $model.autoGain)
                    Toggle("Noise Gate", isOn: $model.noiseGate)
                    Button("Apply Audio Settings") { model.applyAudioSettings() }
                }
                Section("Device") { Text(UIDevice.current.name); Text("Keep screen awake while streaming").foregroundStyle(.secondary) }
                Section("About") {
                    LabeledContent("Protocol", value: "AirMic LAN v1")
                    LabeledContent("Audio", value: "PCM16 mono")
                    Text("Free, account-free, and local-network only. AirMic never stores microphone audio.").font(.footnote).foregroundStyle(.secondary)
                }
            }.navigationTitle("Settings").toolbar { ToolbarItem(placement: .confirmationAction) { Button("Done") { dismiss() } } }
        }
    }
}
