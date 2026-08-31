import SwiftUI

struct ManualConnectionView: View {
    @EnvironmentObject private var model: AirMicViewModel
    @Environment(\.dismiss) private var dismiss
    @Binding var pin: String
    @State private var address = ""

    var body: some View {
        NavigationStack {
            Form {
                Section("Windows PC") {
                    TextField("Local IP, e.g. 192.168.1.25", text: $address).textInputAutocapitalization(.never).keyboardType(.numbersAndPunctuation)
                    TextField("6-digit PIN", text: $pin).keyboardType(.numberPad).textContentType(.oneTimeCode)
                        .onChange(of: pin) { _, value in pin = String(value.filter(\.isNumber).prefix(6)) }
                }
                Section { Text("Find the IP in Windows Settings → Network & internet → Wi-Fi → Properties → IPv4 address.").font(.footnote).foregroundStyle(.secondary) }
                Button("Connect") { dismiss(); Task { await model.connect(manualHost: address, pin: pin) } }.disabled(address.isEmpty || pin.count != 6)
            }.navigationTitle("Manual Connection").toolbar { ToolbarItem(placement: .cancellationAction) { Button("Cancel") { dismiss() } } }
        }
    }
}
