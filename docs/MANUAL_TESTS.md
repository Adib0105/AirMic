# Manual release checklist

Record Windows build, iOS build, hardware, Wi-Fi access point, and measured
latency for every run. Do not mark a release complete with an untested item.

## iPhone to Windows

- [ ] Fresh install prompts for Microphone and Local Network permissions.
- [ ] PC appears through Bonjour within 10 seconds.
- [ ] Manual IPv4 entry works when mDNS is blocked.
- [ ] Wrong PIN is rejected with a human-readable message.
- [ ] Five wrong PINs trigger rate limiting.
- [ ] Correct PIN establishes encrypted audio.
- [ ] Meter follows speech and mute produces digital silence.
- [ ] 16, 24, and 48 kHz settings reconnect cleanly.
- [ ] Wi-Fi interruption reconnects without app restart.
- [ ] No WAN traffic is observed during a 10-minute session.

## Windows endpoint

- [ ] `AirMic Virtual Microphone` appears under Recording devices.
- [ ] No phone audio is played through physical speakers unless Preview is on.
- [ ] Driver removal removes both AirMic endpoints.
- [ ] Uninstall removes app, service/task, firewall rules, and shortcuts.

## Application compatibility

- [ ] Discord detects and records speech from AirMic.
- [ ] Google Meet browser microphone test passes.
- [ ] Zoom microphone test passes.
- [ ] Microsoft Teams microphone test passes.
- [ ] OBS Audio Input Capture receives AirMic.
- [ ] A game using the default communications device receives AirMic.

## Faults

- [ ] Firewall blocked, microphone denied, driver missing, unsupported format,
  and Wi-Fi loss show friendly errors and no raw stack trace.
- [ ] Diagnostics copy contains no PIN, key, or audio bytes.
