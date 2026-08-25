# Tasks

## Done (this session)

- Bootstrapped `docs/` for this repo (this file and its siblings), per
  AGENTS.md section 16 - this repo had no docs/ before.
- Added `Assets/Games/DigitalTwin-futurotec/Scripts/Mqtt/`:
  - `DigitalTwinFuturotec.Mqtt.asmdef` (defines `MQTTNET_ENABLED` via
    versionDefines once MQTTnet is installed)
  - `MqttQualityOfService.cs`, `MqttConnectionState.cs`, `MqttMessage.cs`
  - `MqttTopicMatcher.cs`
  - `MqttBrokerSettings.cs` (ScriptableObject, no stored credentials)
  - `IMqttClientService.cs`
  - `MqttClientService.cs` (MQTTnet-backed, guarded by `MQTTNET_ENABLED`)
  - `MqttManager.cs` (bootstrap MonoBehaviour, main-thread dispatch, topic
    routing, pause/quit lifecycle)

## Done (follow-up checks)

- Caught MQTTnet 5.2.0.1603 as unusable in Unity (no netstandard build);
  user reinstalled as 4.3.7.1207.
- Verified the 4.3.7.1207 install compiles clean (0 errors/warnings after a
  forced recompile) and confirmed every MQTTnet API `MqttClientService.cs`
  calls matches the real assembly via live reflection.
- Found `MqttApplicationMessage.Payload` is obsolete in 4.3.7.1207; switched
  the inbound message path to `PayloadSegment`. Re-verified clean.

## Done (this follow-up)

- Created `Settings/MqttBrokerSettings.asset` (placeholder values - empty
  host, port 8883, TLS on).
- Created `Scenes/Boot.unity` (Main Camera, Directional Light, `MqttManager`
  GameObject) and wired `MqttManager.brokerSettings` to the settings asset -
  verified as a real object reference via `SerializedProperty`, not just an
  accepted string. Scene saved, 0 console errors/warnings.

## Done (topics + runtime config)

- Added `MqttTopicSubscription.cs` (topic/QoS/subscribeOnConnect), exposed
  as `MqttBrokerSettings.Topics` array in the Inspector.
- Added `MqttRuntimeConfig.cs` + `MqttRuntimeConfigLoader.cs` - reads a JSON
  file via `UnityWebRequest` (cross-platform-safe for StreamingAssets),
  parses with Newtonsoft.Json (already a project dependency), applies onto
  `MqttBrokerSettings` via the new `ApplyRuntimeOverride(...)` method.
  `Username`/`Password` route to `MqttManager.SetCredentials(...)` instead
  of the settings asset.
- `MqttManager` now loads that config in `Awake()`, defers `Connect()` if
  called before the load finishes, and auto-subscribes (broker-level) to
  every configured topic with `SubscribeOnConnect = true` once connected.
- Caught and fixed a wrong assumption mid-task: first put the config file
  under this repo's own `StreamingAssets/` folder, then verified via
  `File.Exists` against the real resolved path that Unity's
  `Application.streamingAssetsPath` only ever resolves to the project-root
  `Assets/StreamingAssets` - moved the file there
  (`Assets/StreamingAssets/DigitalTwinFuturotec/MqttConfig.json`) and
  deleted the dead nested copy + its orphaned `.meta`. See decisions.md.
- Verified the whole path end-to-end by actually entering Play Mode:
  `MqttBrokerSettings` picked up `BrokerPort`/`UseTls`/`ClientIdPrefix`/
  `KeepAliveSeconds`/`Topics` from the committed config file. Forced
  recompile also came back with 0 errors/warnings.

## Done (latest-edit-wins config priority)

- `MqttManager` now compares `MqttBrokerSettings.asset`'s save time against
  `MqttConfig.json`'s save time (Editor-only; forces the asset to save first
  via `AssetDatabase.SaveAssetIfDirty` so an unsaved Inspector edit still
  counts) and skips the JSON override if the asset was saved more recently.
  Verified live: edited `brokerPort` in the Inspector, saved, entered Play -
  Inspector value stuck; touched `MqttConfig.json`'s mtime, entered Play
  again - JSON value won back.

## Done (real broker verification + logging + visualization)

- **Found and fixed: `MQTTNET_ENABLED` was never actually active.** Earlier
  claims that `versionDefines` had been "verified" were wrong - a direct
  reflection check on `MqttClientService`'s private `mqttClient` field
  (only present under that `#if`) proved it didn't exist. Switched to a
  Player Settings Scripting Define Symbol instead (Standalone/Android/iOS);
  re-verified the field now exists. See decisions.md and architecture.md.
- Connected to the real broker configured in `MqttConfig.json`
  (`wss://plantatree-mqtt.biocon.com/mqtt`) and hit a real failure: TCP
  transport can't reach a `wss://` URL. Added WebSocket transport support
  (`MqttClientOptionsBuilder.WithWebSocketServer`, auto-detected from the
  `ws://`/`wss://` prefix on `BrokerHost`). Re-verified: real connect +
  auto-subscribe to both configured topics succeeded live.
- Routed all logging through `Modules.Utility.Utility` (per request) via a
  new `MqttLog` indirection type + `MqttUtilityLogBridge.cs` bootstrap file
  - see architecture.md for why a direct reference isn't possible. Verified
  log tags correctly show the real calling file, not the bridge.
- Built `MqttDebugUI` - a live on-screen panel (connection state, broker,
  last message per topic) - directly via the Unity Editor MCP bridge onto
  `Boot.unity`. Along the way found and fixed a base-project-wide gap: TMP
  Essential Resources had never been imported (empty `Assets/TextMesh Pro/`
  folder, `TMP_Settings.instance` null, no font asset existed anywhere) -
  imported via `AssetDatabase.ImportPackage`, confirmed fixed with a Play
  Mode screenshot showing readable text.
- Full pipeline verified live end-to-end via screenshot: panel shows
  "MQTT: Connected", the real broker URL, connect timestamp, and (once
  traffic arrives) per-topic message previews.

## Done (local TLS broker testing + a real data-corruption bug fixed)

- Added `MqttBrokerSettings.TrustedCertificatePath` (certificate pinning by
  thumbprint via `WithCertificateValidationHandler`) and
  `AllowUntrustedCertificates` (explicit, logged-as-insecure escape hatch)
  to support the local test broker's self-signed certificate
  (`certs/server.crt`, `CN=localhost`). Verified live: real TLS connection
  over port 8883 to `192.168.1.28` with the pinned certificate, correct
  username/password auth (`tester`/`test-pass-123`), auto-subscribed to
  `on-light`/`off-light`.
- **Found and fixed a real data-corruption bug**: `ApplyRuntimeOverride` was
  mutating the committed `MqttBrokerSettings.asset` directly, and because
  Unity does not revert `ScriptableObject` asset changes made during Play
  Mode, those runtime JSON-sourced values were getting permanently baked
  into the committed asset - caught by inspecting the `.asset` file on disk
  and finding `brokerHost: 192.168.1.28` and topics from an *older* config
  revision sitting in it. Fixed by cloning `brokerSettings` into a
  runtime-only `effectiveSettings` instance in `Awake()`; the original asset
  is never written to again after that. Restored the corrupted asset to
  placeholder defaults and re-verified: full connect+subscribe session, then
  confirmed the asset file was byte-for-byte unchanged afterward.

## Done (Connect/Disconnect UI, auto-connect, sealed MQTT, lights feature)

- Added `MqttManager.autoConnectOnStart` (connects automatically once the
  config load finishes) and `saveConfigOnSuccessfulConnect` (persists a
  successful manual correction back to `MqttConfig.json`).
- Added `MqttConnectionPanelUI` - Connect/Disconnect buttons + a manual
  settings form shown whenever not connected, built directly via the Editor
  MCP bridge. Known cosmetic layout overlap left as-is (see decisions.md /
  architecture.md) - functionally verified working.
- User confirmed live connection + topic delivery working - **MQTT module
  sealed** per explicit instruction; no further changes to `Scripts/Mqtt/`
  except one necessary bug fix (below), made while building the next
  feature on top of it.
- **Found and fixed:** `MqttManager.Subscribe()` silently never sent the
  broker-level SUBSCRIBE if called before the first successful connect (or
  after any reconnect) - the exact case `MqttLightController` hit
  immediately. Added `ResubscribeAllTopics()`, called on every successful
  connect. See decisions.md.
- Added `Scripts/Lights/MqttLightController.cs` + 4 scene lights
  (`LightsRoot` -> `Light1`..`Light4`) - `on-light`/`off-light` topics drive
  individual lights by 1-based index via `SetActive`. `LightsRoot` starts
  inactive, only activates on `MqttManager.Connected`; each light also
  starts individually inactive (caught and fixed: activating the root alone
  left every light defaulting to "on").
- Verified the lights logic via direct dispatch simulation (the configured
  local broker was unreachable at verification time - confirmed environmental,
  not a code bug, via repeated timeouts/`MQTT connect canceled` after the
  configured `ConnectTimeoutSeconds`): `on-light "2"` -> only `Light2` on,
  `off-light "2"` -> back off, `on-light "4"` -> only `Light4` on,
  `on-light "9"` (out of range) -> safely ignored with a warning, no crash.

## Next up

- Get the local test broker reachable again and confirm the lights respond
  to *real* published messages (only simulated dispatch was verified this
  session, not an actual broker round-trip for on-light/off-light).
- Fill in `Username`/`Password` on a *deployed build's own copy* of
  `MqttConfig.json` for any non-local broker that requires auth - never in
  the committed source-controlled one (the current committed values are
  clearly local-test-only credentials for a LAN broker, see decisions.md).
- Register `Boot.unity` with this app's build pipeline once a
  `ViitorCloudGameInfo`/`ViitorCloudGameInfoSO` exists for this game -
  deliberately not added to global Build Settings, since this template
  drives per-app scene lists from `ViitorCloudGameInfo` instead (no such
  GameInfo asset exists yet for this game). See decisions.md.
- Optional: fix `MqttConnectionPanelUI`'s cosmetic layout overlap (set the
  outer `VerticalLayoutGroup.childControlHeight = true`, already identified
  as the fix in decisions.md) - low priority, MQTT module is sealed.

## Backlog

See roadmap.md "Backlog / not started".
