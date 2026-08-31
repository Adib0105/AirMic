import SwiftUI

@main
struct AirMicApp: App {
    @StateObject private var model = AirMicViewModel()
    var body: some Scene { WindowGroup { HomeView().environmentObject(model) } }
}
