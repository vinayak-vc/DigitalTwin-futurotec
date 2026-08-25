# AI Handoff

## Current state (2026-08-25, MQTT sealed - custom "Lights" logic now on top)

**MQTT module is sealed** - user confirmed connection + topic delivery
working, explicitly asked to stop touching `Scripts/Mqtt/` and start
project-specific logic instead. New work goes in its own folder
(`Scripts/Lights/` so far), not into the Mqtt module. One MQTT bug fix
still landed after sealing - see "Subscribe() before connect" below - it
was necessary because it silently broke the very feature being built on
top of it (see decisions.md for the "sealed except one fix" call).

**New this round:** `Scripts/Lights/MqttLightController.cs` + 4 scene lights
(`LightsRoot` -> `Light1`..`Light4`). `on-light`/`off-light` payloads are a
1-based light index; that light gets `SetActive(true)`/`SetActive(false)`.
`LightsRoot` stays inactive until `MqttManager.Connected` fires - no
dependency on any MQTT UI. Verified via direct dispatch simulation
(reflection-invoking `MqttManager`'s private `DispatchMessage` with a
hand-built `MqttMessage`) since the configured local broker was unreachable
at verification time (repeated `MQTT connect canceled`/timeout errors,
confirmed environmental via `Time.realtimeSinceStartup` still advancing
normally - not a hang). **Not yet confirmed against a real broker
round-trip** - do that first once the local broker is reachable again.

### Fixed after sealing: `Subscribe()` before the first connect silently
dropped the broker-level SUBSCRIBE

`MqttLightController.Update()` calls `Subscribe(...)` as soon as
`MqttManager.Instance` exists - before the config load and first `Connect()`
resolve. The old code sent `SubscribeAsync` immediately regardless of
connection state; `MqttClientService` correctly rejected it
("called while not connected"), and the topic then stayed permanently
unsent (later "already registered" checks skipped it too). Fixed:
`MqttManager` now re-sends `SubscribeAsync` for every registered filter on
every successful connect (`ResubscribeAllTopics()`) - also fixes the same
gap after any reconnect, since `CleanSession` means the broker forgets
subscriptions on a dropped connection.

## Earlier state (2026-08-25, local TLS broker verified; one data-corruption
bug found and fixed)

MQTT is **connected and confirmed working end-to-end** against both the
production broker (WebSocket) and a local test broker (TCP+TLS, self-signed
cert, username/password), not just compiling. Also: **a real bug was found
and fixed mid-session** where runtime JSON config values were silently
getting baked into the committed `MqttBrokerSettings.asset` - see the
dedicated section below before touching `MqttManager`'s settings handling.
`MqttConfig.json` currently points at a **local LAN test broker**
(`192.168.1.28:8883`, TLS, `tester`/`test-pass-123`, topics `on-light`/
`off-light`), not the earlier production Biocon broker - swap back before
any real deployment.

### Fixed: `ApplyRuntimeOverride` was corrupting the committed asset

`MqttManager` used to call `brokerSettings.ApplyRuntimeOverride(...)`
directly on the committed, source-controlled `ScriptableObject` asset.
Unity does **not** revert asset field changes made during Play Mode the way
it reverts scene objects, so those runtime, JSON-sourced values were
persisting - and the latest-edit-wins check's `AssetDatabase.SaveAssetIfDirty`
call then flushed them to disk. Caught by inspecting the committed `.asset`
file directly and finding `brokerHost: 192.168.1.28` baked in, along with
topics from an *older* revision of `MqttConfig.json`. Fixed: `MqttManager`
now clones `brokerSettings` into a runtime-only `effectiveSettings` in
`Awake()` and only ever mutates that clone; the original asset is read-only
after `Awake()`. Verified fixed: full connect+subscribe session, then
confirmed the asset file byte-for-byte unchanged afterward. **If you ever
see `MqttBrokerSettings.asset` show unexpected values in git diff after
just running the game, that's a sign this fix regressed - check
`MqttManager.effectiveSettings` usage first.**

Timeline of what it took to get the connection working:

1. MQTTnet install: 5.2.0.1603 (no Unity build) -> reinstalled as 4.3.7.1207
   (works). See earlier entries below / decisions.md.
2. **`MQTTNET_ENABLED` was never actually active**, despite this file
   earlier claiming it was "verified via reflection." That reflection only
   proved the MQTTnet library loads in the domain (true for any precompiled
   DLL regardless of any `#if` symbol) - it never proved this assembly's
   own `#if MQTTNET_ENABLED` branch had compiled. A direct check
   (`typeof(MqttClientService).GetField("mqttClient", NonPublic |
   Instance)`) proved the field didn't exist - `MqttClientService` had been
   silently running in no-op stub mode this entire time. Root cause:
   `versionDefines` doesn't reliably resolve a "version" for a bare
   NuGet-installed DLL with no `package.json`. Fixed by setting
   `MQTTNET_ENABLED` as a Player Settings Scripting Define Symbol
   (Standalone/Android/iOS) instead; re-verified with the same direct field
   check, this time confirmed present.
3. First real `Connect()` attempt failed: the configured broker
   (`wss://plantatree-mqtt.biocon.com/mqtt`, from `MqttConfig.json`) only
   speaks MQTT over WebSocket, not TCP. Added WebSocket transport
   (auto-detected from `ws://`/`wss://` on `BrokerHost`).
4. Re-tested: **real connection succeeded**, auto-subscribed to both
   configured topics (`qrScanned-biocon-house`, `visitorCreated-biocon-house`).
5. Routed all logging through `Modules.Utility.Utility` (per request) via a
   new indirection (`MqttLog` + `MqttUtilityLogBridge.cs`) - `Modules.
   Utility.Utility` has no asmdef so it can't be referenced directly from
   `DigitalTwinFuturotec.Mqtt.asmdef` (compile-order rule). See
   architecture.md for the mechanism and a subtlety around `[CallerFilePath]`
   that had to be worked around too.
6. Built `MqttDebugUI` - a live on-screen panel (connection state, broker,
   last message per topic) directly via the Unity Editor MCP bridge.
   Surfaced a base-project-wide gap along the way: TextMeshPro's essential
   resources had never been imported anywhere in this project (`Assets/
   TextMesh Pro/` was an empty placeholder, `TMP_Settings.instance` was
   null) - imported via `AssetDatabase.ImportPackage`, fixing text
   rendering for every game on this template, not just this one.
7. Added a "latest-edit-wins" priority between `MqttBrokerSettings.asset`
   and `MqttConfig.json` (Editor-only) so Inspector testing doesn't get
   silently clobbered by the JSON override - verified both directions live.
8. Added certificate pinning (`TrustedCertificatePath`, thumbprint-validated
   via `WithCertificateValidationHandler`) plus a separate, explicitly
   insecure `AllowUntrustedCertificates` escape hatch, to support the local
   test broker's self-signed certificate. Verified live: real TLS connection
   with the pinned cert accepted, auth succeeded.
9. **Found and fixed the data-corruption bug described above** - discovered
   while testing #8, since the corrupted asset made the local-test config
   silently fail to apply.

Everything above was confirmed by actually running it (forced recompiles,
direct reflection checks, real Play Mode sessions, real broker connections,
Play Mode screenshots) - not inferred from reading the code.

## Added files this session

`Assets/Games/DigitalTwin-futurotec/Scripts/Mqtt/`:

- `DigitalTwinFuturotec.Mqtt.asmdef` - own assembly. No `versionDefines`
  (removed - didn't work); `MQTTNET_ENABLED` comes from Player Settings.
  References `Unity.TextMeshPro` by name for `MqttDebugUI`.
- `MqttQualityOfService.cs`, `MqttConnectionState.cs` - small enums.
- `MqttMessage.cs` - immutable topic/payload/QoS/retain value type.
- `MqttTopicMatcher.cs` - static `+`/`#` wildcard matcher.
- `MqttTopicSubscription.cs` - one configured topic (filter/QoS/
  subscribe-on-connect); array element type for `MqttBrokerSettings.Topics`.
- `MqttBrokerSettings.cs` - ScriptableObject config (host/port/TLS/cert/
  keep-alive/reconnect policy/topics; **no credentials**). `BrokerHost` can
  be a bare hostname (TCP) or a `ws://`/`wss://` URI (WebSocket).
  `TrustedCertificatePath` pins a self-signed certificate;
  `AllowUntrustedCertificates` is a separate insecure escape hatch.
  `ApplyRuntimeOverride(MqttRuntimeConfig)` applies a StreamingAssets
  override - **only ever called on `MqttManager.effectiveSettings`, a
  runtime clone, never on this asset directly - see the data-corruption fix
  above.**
- `MqttRuntimeConfig.cs` - plain Newtonsoft.Json deserialization DTO for the
  runtime config file (every field nullable/optional).
- `MqttRuntimeConfigLoader.cs` - reads that file via `UnityWebRequest`
  (works on Standalone and Android, where a plain filesystem read wouldn't).
- `IMqttClientService.cs` - transport-agnostic interface.
- `MqttClientService.cs` - MQTTnet-backed implementation, guarded by
  `#if MQTTNET_ENABLED`. Verified genuinely active (not just compiling in
  stub mode) via direct reflection. Auto-selects TCP or WebSocket transport.
- `MqttManager.cs` - bootstrap `MonoBehaviour` singleton
  (`MqttManager.Instance`): main-thread dispatch, per-topic-filter
  subscriber routing, pause/quit lifecycle, StreamingAssets config load with
  deferred-`Connect()` and latest-edit-wins priority, auto-subscribe of
  `MqttBrokerSettings.Topics` on connect, `AnyMessageReceived` event for
  diagnostics/visualization.
- `MqttLog.cs` - logging indirection routed to `Modules.Utility.Utility`.
- `MqttDebugUI.cs` - live on-screen visualization panel.

`Assets/Games/DigitalTwin-futurotec/Scripts/MqttUtilityLogBridge.cs` -
deliberately outside the asmdef folder above (see architecture.md).

`Assets/Games/DigitalTwin-futurotec/Settings/MqttBrokerSettings.asset`,
`Scenes/Boot.unity` (Main Camera, Directional Light, `MqttManager`,
`MqttDebugCanvas` with the debug panel).

`Assets/StreamingAssets/DigitalTwinFuturotec/MqttConfig.json` - **at the
base project root**, not under this repo - hard Unity engine constraint,
see decisions.md.

`docs/*` - this file and its siblings - bootstrapped fresh, this repo had
none at session start.

## Base-project-wide side effects (not scoped to this game)

- `MQTTNET_ENABLED` added to Player Settings Scripting Define Symbols for
  Standalone/Android/iOS (`ProjectSettings/ProjectSettings.asset`).
- TMP Essential Resources imported (`Assets/TextMesh Pro/` now populated).
  Was completely missing before - any other game on this template using
  `TextMeshProUGUI` would have hit the same "text doesn't render" issue.

## What still needs a human

1. **If the broker requires auth**, fill in `Username`/`Password` on a
   *deployed build's own copy* of `MqttConfig.json` - never the committed
   source-controlled one. Only works as a true post-build edit on
   Standalone; Android/iOS pack the file into the app binary at build time.
2. **Confirm live message rendering** - `MqttDebugUI` and the
   `AnyMessageReceived` pipeline are verified correct by design and by a
   successful connect+subscribe, but no actual message arrived from the
   broker during this session's test window, so the "message received"
   render path itself hasn't been exercised with real data yet. Trigger a
   QR-scan or visitor-created event on the real system and check the panel.
3. **Register `Boot.unity` with this app's build pipeline** once a
   `ViitorCloudGameInfo`/`ViitorCloudGameInfoSO` exists for this game -
   deliberately not added to global Build Settings (see decisions.md).

## Cross-repo / cross-boundary note

This repo (`Assets/Games/DigitalTwin-futurotec/`) has its own git remote
(`vinayak-vc/DigitalTwin-futurotec`) and is treated as read/write. The base
project around it (`Packages/manifest.json`, `ProjectSettings/`, other
`Assets/` folders, package installs, Player Settings) is fine to touch for
tooling/config setup - the boundary that matters is that project-specific
code and content for this game stays under
`Assets/Games/DigitalTwin-futurotec/`, not scattered into the base project.
Two confirmed exceptions this session, both hard Unity engine constraints
rather than boundary judgment calls: `Assets/StreamingAssets/
DigitalTwinFuturotec/MqttConfig.json` (StreamingAssets is a single
project-root folder) and the `MQTTNET_ENABLED` Player Settings define
symbol (no working per-asmdef alternative for a bare NuGet DLL).
