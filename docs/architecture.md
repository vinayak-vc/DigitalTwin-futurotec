# Architecture

## MQTT module (Scripts/Mqtt/)

- `IMqttClientService` - transport-agnostic contract: `ConnectAsync`,
  `DisconnectAsync`, `PublishAsync`, `SubscribeAsync`, `UnsubscribeAsync`,
  `State`, and `Connected`/`Disconnected`/`MessageReceived`/`ConnectionError`
  events. Lets the rest of the codebase depend on an interface, not MQTTnet
  directly.
- `MqttClientService` - the MQTTnet-backed implementation. Every member is
  guarded by `#if MQTTNET_ENABLED`; without it, calls log a clear error and
  no-op instead of failing to compile or failing silently. Owns:
  - **Reconnect** - exponential backoff (`ReconnectInitialDelaySeconds` ->
    doubles up to `ReconnectMaxDelaySeconds`) on any non-intentional
    disconnect, only when `MqttBrokerSettings.AutoReconnect` is true.
  - **TLS** - `MqttBrokerSettings.UseTls` toggles `WithTlsOptions` on the
    MQTTnet client options builder. `TrustedCertificatePath` pins a specific
    certificate by thumbprint (`WithCertificateValidationHandler`) - the
    correct approach for a self-signed broker certificate, which default OS
    chain validation will never accept. `AllowUntrustedCertificates` is a
    separate, explicitly-logged-as-insecure escape hatch for local testing
    only, off by default.
- `MqttBrokerSettings` (ScriptableObject) - host/port/TLS/cert/keep-alive/
  reconnect policy, plus a `Topics` array (see below). Deliberately holds
  **no credentials** since it is a committed asset - see decisions.md.
  `ApplyRuntimeOverride(MqttRuntimeConfig)` lets a StreamingAssets JSON file
  override any of these fields at runtime (see "Runtime config" below). Never
  mutated directly at runtime - see "Runtime config" below for why.
- `MqttTopicSubscription` - one configured topic: `Topic` filter,
  `QualityOfService`, `SubscribeOnConnect`. Shown as an array on
  `MqttBrokerSettings.Topics` in the Inspector; also what
  `ApplyRuntimeOverride` builds from a JSON config's `Topics` array.
- `MqttMessage` - immutable topic/payload/QoS/retain snapshot, decoupled from
  MQTTnet's own message type so callers never need a direct MQTTnet
  reference.
- `MqttTopicMatcher` - static `+`/`#` wildcard matcher (MQTT 3.1.1/5.0
  rules, including the `$`-prefix exclusion for wildcard-only filters).
- `MqttManager` (MonoBehaviour, singleton via `MqttManager.Instance`) - the
  integration point the rest of the game calls:
  - Marshals every MQTTnet callback (fired on background I/O threads) onto
    the Unity main thread via a `ConcurrentQueue<Action>` drained in
    `Update()`, bounded by `maxDispatchedPerFrame` so a burst of incoming
    messages cannot spike frame time.
  - Maintains a `topicFilter -> List<Action<MqttMessage>>` registry so
    multiple game-side listeners can subscribe to different topics; the
    broker-level `SUBSCRIBE`/`UNSUBSCRIBE` only fires on the first
    subscriber / last unsubscriber for a given filter.
  - On every successful connect, auto-subscribes (broker-level only) to
    every `MqttBrokerSettings.Topics` entry with `SubscribeOnConnect = true`
    - game code still calls `Subscribe(topicFilter, callback)` separately to
      actually receive those messages; the auto-subscribe just means the
      broker-level `SUBSCRIBE` for a configured topic doesn't wait for the
      first listener.
  - Loads and applies the StreamingAssets runtime config (see below) before
    the first real `Connect()` runs - if a `Connect()` call arrives while
    that load is still in flight, it's deferred until the load finishes
    rather than connecting against possibly-stale settings.
  - `OnApplicationPause`/`OnApplicationQuit` - graceful disconnect on
    backgrounding (mobile) and on quit, reconnect on resume if it was
    connected before pausing.
- `MqttLog` / `Scripts/MqttUtilityLogBridge.cs` - routes logging through
  `Modules.Utility.Utility` (see "Logging" below).
- `MqttDebugUI` - live on-screen connection/topic visualization (see
  "Debug visualization" below).

## Runtime config (StreamingAssets)

`MqttRuntimeConfigLoader.Load(...)` reads a JSON file and deserializes it
(via Newtonsoft.Json - `com.unity.nuget.newtonsoft-json`, already a project
dependency, precompiled DLL auto-referenced) into `MqttRuntimeConfig`.
`MqttManager` runs this in a coroutine from `Awake()` and applies the result
onto `effectiveSettings`. Every field on `MqttRuntimeConfig` is
nullable/optional - a config file only needs to list the fields it wants to
change; everything else keeps the Inspector-configured value.
`Username`/`Password` are *not* applied to `MqttBrokerSettings` (that asset
stays credential-free) - they go straight into `MqttManager.SetCredentials(...)`
instead.

**`effectiveSettings`, not `brokerSettings`, is what gets mutated.**
`MqttManager.Awake()` clones the assigned `brokerSettings` asset
(`Instantiate(brokerSettings)`) and applies the JSON override, credentials,
and all runtime reads onto that clone - `BrokerSettings` (the public
property `MqttDebugUI` and other diagnostics read) returns the clone too.
The original asset is only ever read again after `Awake()` (for the
latest-edit-wins mtime check below), never written. This was a real bug
fixed mid-session, not a defensive-only precaution: `ApplyRuntimeOverride`
used to run directly on the committed asset, and since Unity does **not**
revert `ScriptableObject` *asset* field changes made during Play Mode the
way it does for scene objects, those runtime values were getting baked into
the committed `.asset` file the moment anything called
`AssetDatabase.SaveAssetIfDirty` on it. See decisions.md for how this was
caught (inspecting the committed asset directly and finding leaked runtime
values in it) and verified fixed.

**File location: `Assets/StreamingAssets/DigitalTwinFuturotec/MqttConfig.json`
- at the base project root, not under `Assets/Games/DigitalTwin-futurotec/`.**
This is a hard Unity engine constraint, not a placement choice:
`Application.streamingAssetsPath` only ever resolves to the single,
project-root `Assets/StreamingAssets` folder - Unity does not scan for or
merge folders elsewhere in the tree that happen to be named
`StreamingAssets`. (An earlier version of this work assumed otherwise and
put the file under this repo's own `StreamingAssets/`; that was wrong and
was caught by an actual runtime check - see decisions.md.) The file is
namespaced under `DigitalTwinFuturotec/` since every game's StreamingAssets
content in this shared base project ends up merged into that one folder.

**Editing after a build only works where StreamingAssets ships as loose
files** - Standalone (Windows/Mac/Linux), where it sits next to the built
executable and can be hand-edited with no rebuild. On Android/iOS the file
is packed into the app binary at build time; `MqttRuntimeConfigLoader` still
reads it correctly there (via `UnityWebRequest`, since a plain filesystem
read doesn't work inside an APK), but changing it after that requires
repackaging, not hand-editing a text file. `MqttManager`'s inspector
tooltip on `loadConfigFromStreamingAssets` calls this out.

Verified end-to-end at runtime (not just by reading the code): entered Play
mode, confirmed `MqttBrokerSettings` picked up the config file's
`BrokerPort`/`UseTls`/`ClientIdPrefix`/`KeepAliveSeconds`/`Topics` values.

## MQTTNET_ENABLED: Player Settings define symbol, not versionDefines

`MQTTNET_ENABLED` is a Player Settings Scripting Define Symbol (Standalone/
Android/iOS), set via `PlayerSettings.SetScriptingDefineSymbols`. An earlier
version of this work used the asmdef's `versionDefines` instead, on the
assumption that Unity could resolve a "version" for the manually
NuGet-installed `MQTTnet.dll` well enough to trigger it automatically. That
never actually worked - direct reflection on `MqttClientService`'s private
`mqttClient` field (only present under `#if MQTTNET_ENABLED`) proved the
class had been silently running in no-op stub mode the whole time, despite
earlier (wrong) claims of having "verified" it via reflection - those
checks only proved the MQTTnet library loads in the domain, not that this
assembly's own `#if` branch had compiled. See decisions.md for the full
account. Re-verify with the same direct-field check
(`typeof(MqttClientService).GetField("mqttClient", NonPublic | Instance)`)
if this project's Player Settings or asmdef setup ever changes.

## Transport: TCP or WebSocket, auto-detected from `BrokerHost`

`MqttBrokerSettings.BrokerHost` doubles as either a bare hostname/IP (TCP,
uses `BrokerPort`) or a full `ws://`/`wss://` URI (WebSocket, `BrokerPort`
ignored). `MqttClientService.ConnectAsync` checks the prefix and picks
`WithTcpServer(...)` or `WithWebSocketServer(...)` accordingly. This exists
because the real broker configured for this app
(`wss://plantatree-mqtt.biocon.com/mqtt`) only exposes MQTT over WebSocket -
confirmed by an actual connection attempt over TCP failing first. Unrelated
to the earlier Desktop/Mobile-only decision (WebSocket transport works the
same on Standalone/Android/iOS as TCP does; it's a per-broker choice, not a
platform one).

## Logging (Scripts/Mqtt/MqttLog.cs + Scripts/MqttUtilityLogBridge.cs)

Every log call in this module goes through `MqttLog.Info/Warning/Error`,
which routes to `Modules.Utility.Utility.Log/LogWarning/LogError` (this base
project's colored, per-file-tagged logger) instead of raw `Debug.Log`.
`MqttLog` itself can't reference `Modules.Utility.Utility` directly -
`Modules.Utility.Utility` has no asmdef of its own so it lives in
Assembly-CSharp, which always compiles *after* any named asmdef, and
`DigitalTwinFuturotec.Mqtt.asmdef` is a named asmdef. `MqttUtilityLogBridge`
(one folder up, deliberately outside that asmdef, so it compiles into
Assembly-CSharp) wires `MqttLog`'s handler delegates to the real logger via
`[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`, guaranteeing it's wired
before `MqttManager.Awake()` ever logs. `MqttLog`'s methods capture
`[CallerFilePath]` themselves and thread it through explicitly, so log tags
correctly name the real caller (`[MqttManager]`, `[MqttClientService]`) -
not the bridge file that relays the call. Falls back to plain `Debug.Log*`
if the bridge is ever missing, so logging never silently disappears.

## Debug visualization (Scripts/Mqtt/MqttDebugUI.cs)

A `TextMeshProUGUI`-based on-screen panel (built directly via the Unity
Editor MCP bridge onto `MqttDebugCanvas` in `Boot.unity`) showing live
connection state (color-coded), the broker target, and the most recent
message received per topic. Driven by `MqttManager.AnyMessageReceived` - a
new event that fires for every message received regardless of whether any
game code has actually called `Subscribe(...)` on it, purely for
visualization/diagnostics. Binds to `MqttManager.Instance` lazily in
`Update()` since script execution order between GameObjects in the same
scene isn't guaranteed. Not end-user UI.

Building this also surfaced that TextMeshPro's essential resources
(fonts, `TMP Settings.asset`) had never been imported anywhere in the base
project - `Assets/TextMesh Pro/` was an empty placeholder folder, so no
`TextMeshProUGUI` anywhere in the project could have rendered text before
this. Imported via `AssetDatabase.ImportPackage` on `com.unity.ugui`'s
bundled `TMP Essential Resources.unitypackage` - a base-project-wide fix,
not specific to this game. See decisions.md.

## Connect/Disconnect controls (Scripts/Mqtt/MqttConnectionPanelUI.cs)

Connect/Disconnect buttons plus a manual settings form (host/port/TLS/
username/password/client ID/cert path), all built the same way as
`MqttDebugUI` (directly via the Editor MCP bridge onto `MqttDebugCanvas`).
The form is shown whenever `MqttManager.State != Connected` and hidden once
connected - covers both "auto-connect hasn't succeeded yet" and "just
disconnected" with one rule, pre-filled with whatever `effectiveSettings`
currently holds so a failed auto-connect attempt shows exactly what it
tried. Pressing Connect calls `MqttManager.ApplyManualOverride(...)` (onto
the runtime clone, never `MqttBrokerSettings.asset`) then `Connect()`; a
connect made this way that succeeds gets written back to the StreamingAssets
config file via `SaveEffectiveConfigToStreamingAssets()` (see
`MqttManager.saveConfigOnSuccessfulConnect`), so a manual correction
persists across restarts.

Known cosmetic issue, deliberately left as-is once the MQTT module was
sealed: the outer panel's `VerticalLayoutGroup` has `childControlHeight =
false`, so nested layout groups' preferred heights aren't fully respected -
the buttons/feedback row can visually overlap the bottom of the form when
it's expanded. Functionally everything still works (buttons are
interactable, fields are editable, feedback text updates) - this is a
layout-only issue.

`MqttManager.autoConnectOnStart` (default `true`) connects automatically
once the StreamingAssets config load finishes, so this panel's form is the
first thing shown if that auto-connect fails.

## MQTT module sealed - new work goes elsewhere

As of this session, `Scripts/Mqtt/` is not being actively extended further
(one bug fix landed after sealing - see "Subscribe() before connect" in
decisions.md, made necessary by the lights feature below). New
project-specific logic that consumes MQTT lives in its own folder and
references `MqttManager`/`MqttLog` (both public) rather than being added
into the Mqtt module itself.

## Lights (Scripts/Lights/MqttLightController.cs)

Bridges the "on-light"/"off-light" topics to 4 scene lights
(`LightsRoot` -> `Light1`..`Light4`, each a `GameObject` with a `Light`
component). `LightsRoot` starts inactive and only activates on
`MqttManager.Connected` - no dependency on `MqttDebugUI`/
`MqttConnectionPanelUI`. Each light additionally starts `SetActive(false)`
individually (activating the root does not imply any child is "on" -
`GameObject.activeSelf` is independent per-object state). Payload is a
1-based light index as a string ("1".."4"); `on-light` ->
`lights[index].SetActive(true)`, `off-light` -> `SetActive(false)`. Indices
outside `1..lights.Length` log a warning and are otherwise ignored - no
crash on bad input.

Compiles into `Assembly-CSharp` (no dedicated asmdef) - same pattern as
`MqttUtilityLogBridge.cs` - since it only needs to reference the public
`MqttManager`/`MqttLog`/`MqttMessage` API, not depend on anything inside the
Mqtt module's internals.

Verified via direct dispatch simulation (invoking `MqttManager`'s private
`DispatchMessage` via reflection with a hand-built `MqttMessage`) rather
than a live broker message, since the configured local test broker was
unreachable at verification time - confirmed `on-light "2"` turns on only
`Light2`, `off-light "2"` turns it back off, `on-light "4"` turns on only
`Light4`, and an out-of-range index (`"9"`) is safely ignored with a
warning logged, no exception.
