# AirMic virtual audio driver

## Release status

The signed virtual-audio driver is **not included in this source milestone**.
The Windows app already contains the real WASAPI bridge that writes received
PCM to a render endpoint named `AirMic Network Input`, but the paired capture
endpoint named `AirMic Virtual Microphone` must be implemented and signed
before the installer can be released.

This is an explicit release gate, not a placeholder device. The application
reports `Driver unavailable` when the endpoint does not exist and never claims
that a virtual microphone is ready.

## Required implementation

Use Microsoft's current SysVAD sample as the architecture reference and retain
its license notices. Build one minimal WaveRT virtual cable:

- Render endpoint: `AirMic Network Input`, 16-bit PCM, mono, 16/24/48 kHz.
- Capture endpoint: `AirMic Virtual Microphone`, same formats.
- Copy render packets to a nonpaged cyclic capture buffer.
- On underrun, emit digital silence rather than repeating old microphone data.
- Reject non-PCM formats and more than one channel.
- Expose no speaker endpoint and perform no file/network I/O in kernel mode.
- Stop and zero buffers when the render client closes.

The user-mode app opens the render endpoint in shared-mode WASAPI. Discord,
Meet, Zoom, Teams, OBS, browsers, and games open the capture endpoint normally;
no application-specific integration is required.

## Signing and validation gate

Development builds may use a test certificate only on a dedicated test PC.
Never ask end users to disable Secure Boot or enable test signing. A distributable
x64 package requires Microsoft signing and should pass the relevant HLK audio,
PnP, power, sleep/resume, stress, and uninstall tests.

Required evidence before changing this status:

1. WDK Release x64 build log.
2. Driver Verifier run with no bugcheck.
3. HLK result set and signed catalog.
4. 30-minute capture test in every app listed in `docs/MANUAL_TESTS.md`.
5. Clean install/upgrade/uninstall on a fresh Windows 11 VM and physical PC.
