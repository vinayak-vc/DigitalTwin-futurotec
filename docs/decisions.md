# Decisions

## 2026-08-25 - MQTTnet over M2Mqtt

**Decision:** Build the MQTT client on MQTTnet rather than M2Mqtt.

**Why:** User chose MQTTnet explicitly. It is actively maintained, supports
async/await, TLS, and MQTT 5.0, versus M2Mqtt which is thread-based (no
async/await) and largely unmaintained. Both are TCP-only; since WebGL is out
of scope (next decision) M2Mqtt's lack of a WebSocket transport wasn't the
deciding factor here, but it would have been if WebGL were in scope.

## 2026-08-25 - Desktop/Mobile only, no WebGL transport

**Decision:** Scope the MQTT client to Standalone/Android/iOS. Did not build
a WebSocket transport path.

**Why:** User confirmed WebGL is not a target platform for this app's MQTT
connection. Raw TCP sockets (what MQTTnet's default transport uses) are
unavailable in WebGL builds - supporting it later would need MQTTnet's
WebSocket channel or a JS-interop bridge, a materially different code path.
Documented in roadmap.md as backlog in case this changes.

## 2026-08-25 - No credentials stored on MqttBrokerSettings

**Decision:** `MqttBrokerSettings` (ScriptableObject) holds host/port/TLS/
keep-alive/reconnect policy only. Username/password are passed in at runtime
via `MqttManager.SetCredentials(...)` and held in memory only.

**Why:** `MqttBrokerSettings` assets are committed to source control (Unity
`.asset` YAML is plain text). Storing credentials on them would leak secrets
into git history. User chose to scaffold with placeholder config now and
wire real broker/credential values in later - this decision keeps that door
open without ever giving the asset a place to put a secret in the meantime.

## 2026-08-25 - MQTTNET_ENABLED via asmdef versionDefines, not a Player
Settings scripting define symbol

**Decision:** `DigitalTwinFuturotec.Mqtt.asmdef` defines `MQTTNET_ENABLED`
via `versionDefines` (matching on the `MQTTnet` assembly name, any version)
rather than requiring the symbol be added to Player Settings' scripting
define symbols list.

**Why:** Player Settings live in `ProjectSettings/`, which is part of the
base project and read-only from this repo. `versionDefines` is scoped to
this one asmdef and needs no edit outside `Assets/Games/DigitalTwin-futurotec/`.
This mirrors the base project's own precedent -
`FirebaseAnalyticsProvider.cs` uses the same "compiles as a no-op until a
define is set" pattern, just via a different mechanism for defining the
symbol.

## 2026-08-25 - MQTTnet installation left as a human step

**Decision:** Did not attempt to fetch/vendor an MQTTnet DLL or edit
`Packages/manifest.json` (NuGetForUnity or a direct UPM entry) to bring in
MQTTnet automatically.

**Why:** `Packages/manifest.json` sits in the base project root and is
read-only from this repo. Fetching and dropping in a compiled third-party
binary without the user reviewing it first, from an environment with
unconfirmed internet access, was judged not appropriate to do unattended.
Installing MQTTnet is documented as an explicit human step in
ai_handoff.md, consistent with how the base project already handles the
Firebase Unity SDK.

## 2026-08-25 - Pin MQTTnet to 4.3.7.1207, not latest (5.x)

**Decision:** The user installed MQTTnet via NuGetForUnity, which pulled
the latest version, **5.2.0.1603**. Flagged this as wrong and require
**4.3.7.1207** instead.

**Why:** Verified on nuget.org that MQTTnet 5.x's `.nuspec` declares only
`net8.0`/`net10.0` target framework groups - no `netstandard2.x` build
exists in that package at all, so NuGetForUnity has nothing Unity-compatible
to extract (confirmed: `Assets/Packages/MQTTnet.5.2.0.1603/` contains only
`LICENSE`/`nuget.png`/`.nuspec`, no `.dll`). MQTTnet 4.3.7.1207 (the last 4.x
release) still targets `netstandard2.0`/`netstandard2.1`, which Unity's
Mono/IL2CPP runtimes can consume, and matches the API `MqttClientService.cs`
was written against.

## 2026-08-25 - Boot.unity not added to global Build Settings

**Decision:** Created `Scenes/Boot.unity` with the `MqttManager` bootstrap
and wired it up, but did not add it to `EditorBuildSettings`
(`ProjectSettings/EditorBuildSettings.asset`).

**Why:** This base project's build pipeline (`BuildContainer`/`BuildBase`,
per architecture docs) drives each app's scene list from that app's own
`ViitorCloudGameInfo`/`ViitorCloudGameInfoSO` asset, not the global Build
Settings scene list - editing `EditorBuildSettings` directly wouldn't match
how any other app in this template actually gets built, and no
`ViitorCloudGameInfoSO` exists yet for this game to hook into. Standing up
that GameInfo asset (gameID, build properties, package list) is a separate,
larger task than wiring up MQTT and is left for when this game's build
config is actually set up.

## 2026-08-25 - Runtime config file lives at project-root
`Assets/StreamingAssets/`, not this repo's own folder

**Decision:** `MqttRuntimeConfigLoader` reads
`Assets/StreamingAssets/DigitalTwinFuturotec/MqttConfig.json` - a location
inside the base project, outside `Assets/Games/DigitalTwin-futurotec/`.

**Why:** `Application.streamingAssetsPath` is hardcoded by Unity to
`Application.dataPath + "/StreamingAssets"` - it does not scan for or merge
folders elsewhere in the project tree named `StreamingAssets`. The first
version of this work put the config file under this repo's own
`StreamingAssets/` folder on the (wrong) assumption that Unity merges any
folder with that name; a live runtime check (`File.Exists` against the
resolved path, then an actual Play Mode run) caught that it would never be
found there. This isn't a boundary judgment call - it's a hard engine
constraint, confirmed by running it, not just by reasoning about it. The
file is namespaced under a `DigitalTwinFuturotec/` subfolder since every
game built on this shared base project necessarily shares the one
`Assets/StreamingAssets` output folder.

## 2026-08-25 - Credentials belong in the StreamingAssets config file, not
`MqttBrokerSettings`

**Decision:** `MqttRuntimeConfig.Username`/`Password` are read from the
config file and passed straight to `MqttManager.SetCredentials(...)` -
never written onto the `MqttBrokerSettings` asset.

**Why:** Keeps the earlier "no credentials on a committed asset" decision
intact while still giving ops a real place to put real credentials: the
*committed* `MqttConfig.json` ships with blank `Username`/`Password`, and a
deployed build's own on-disk copy (Standalone only - see architecture.md)
is what actually gets edited with real values, outside source control
entirely.

## 2026-08-25 - `versionDefines` replaced with a Player Settings scripting
define symbol for `MQTTNET_ENABLED`

**Decision:** Removed the `versionDefines` entry from
`DigitalTwinFuturotec.Mqtt.asmdef` and instead added `MQTTNET_ENABLED`
directly to Player Settings' Scripting Define Symbols (Standalone, Android,
iOS) via `PlayerSettings.SetScriptingDefineSymbols`.

**Why:** `versionDefines` **never actually worked** for the manually
NuGet-installed `MQTTnet.dll` - this was wrongly reported as verified
earlier in this session. The earlier "verification" (reflecting on MQTTnet
types via `unity_reflect`) only proved the MQTTnet assembly is loadable in
the domain, which happens automatically for any precompiled DLL regardless
of any `#if` symbol - it never actually proved `MqttClientService`'s
`#if MQTTNET_ENABLED` branch had compiled. A direct reflection check on
`MqttClientService`'s private `mqttClient` field (only present under that
`#if`) proved conclusively the field didn't exist - the class had been
running in no-op stub mode the entire time. Unity's `versionDefines`
appears to require a version Unity can resolve against a UPM package
manifest; a bare NuGet-installed DLL with no `package.json` doesn't supply
one reliably. Player Settings define symbols are the standard, always-works
mechanism (same approach the base project already uses for
`FIREBASE_ANALYTICS_ENABLED`), at the cost of the small ProjectSettings edit
`versionDefines` was originally meant to avoid - acceptable since the user
confirmed base-project tooling/config edits are fine.

## 2026-08-25 - WebSocket transport added; `BrokerHost` doubles as
hostname-or-URI

**Decision:** `MqttClientService.ConnectAsync` now detects whether
`settings.BrokerHost` starts with `ws://`/`wss://` and uses
`WithWebSocketServer(...)` instead of `WithTcpServer(...)` when it does;
`BrokerPort` is ignored in that case (the URI carries its own port).

**Why:** The real broker configured in `MqttConfig.json`
(`wss://plantatree-mqtt.biocon.com/mqtt`) only exposes MQTT over WebSocket,
not raw TCP - confirmed by an actual failed connection attempt logged as
"Error while connecting host 'Unspecified/wss://...:8883'" before this fix.
This is unrelated to the earlier Desktop/Mobile-only platform decision (no
WebGL) - WebSocket transport works fine on Standalone/Android/iOS via
`System.Net.WebSockets`, it's simply a different MQTT transport choice made
per-broker, not a platform capability question.

## 2026-08-25 - Logging routed through `Modules.Utility.Utility`, not
`Debug.Log` directly, via an indirection type

**Decision:** Every log call in the MQTT module goes through a new
`MqttLog` static class (inside `DigitalTwinFuturotec.Mqtt.asmdef`), which a
separate bootstrap file (`Scripts/MqttUtilityLogBridge.cs`, deliberately
placed outside that asmdef) wires to `Modules.Utility.Utility.Log/
LogWarning/LogError` via `[RuntimeInitializeOnLoadMethod]`.

**Why:** User asked for the base project's existing colored/tagged logger
(`Modules.Utility.Utility`) to be used instead of raw `Debug.Log`.
`Modules.Utility.Utility` has no `.asmdef` of its own, so it compiles into
Unity's default `Assembly-CSharp`, which always compiles *after* any named
asmdef - a one-way rule that means `DigitalTwinFuturotec.Mqtt.asmdef` can
never reference it directly. Rather than deleting that asmdef (losing the
compile isolation it gives the MQTT module) or reaching for reflection, a
thin bootstrap file living just outside the asmdef's folder (so it compiles
into Assembly-CSharp, where both namespaces are visible) wires the two
together at startup. Also had to fully-qualify `Modules.Utility.Utility`
in that bridge file rather than `using Modules.Utility;` - a sibling
`ViitorCloud.Utility` namespace already exists elsewhere in the base
project, and since the bridge's own namespace nests under `ViitorCloud`, an
unqualified `Utility` resolves against that enclosing namespace before ever
consulting a `using` directive, silently binding to the wrong type.
Also: `[CallerFilePath]` is captured on `MqttLog`'s own methods (not left
to `Utility.Log`'s own default), and threaded through explicitly, so the
resulting log tag names the file that actually logged (e.g.
`[MqttManager]`), not the bridge file that happens to relay the call.

## 2026-08-25 - Imported TMP Essential Resources (base-project-wide)

**Decision:** Ran `AssetDatabase.ImportPackage(...)` on `com.unity.ugui`'s
bundled `TMP Essential Resources.unitypackage`, populating the previously
placeholder-empty `Assets/TextMesh Pro/` folder (fonts, default material,
`TMP Settings.asset`).

**Why:** `MqttDebugUI`'s on-screen panel uses `TextMeshProUGUI`, which
rendered nothing at all - `TMP_Settings.instance` was null and no font
asset existed anywhere in the project. This is a base-project-wide gap
(TextMeshPro was never fully set up), not specific to this game, so the fix
benefits every game built on this template, not just this one. Confirmed
fixed by an actual Play Mode screenshot showing readable text afterward.

## 2026-08-25 - Certificate pinning for self-signed TLS, not
`AllowUntrustedCertificates`

**Decision:** Added `MqttBrokerSettings.TrustedCertificatePath` (loads a
PEM/CRT file and validates the broker's presented certificate against it by
thumbprint via `WithCertificateValidationHandler`) as the primary mechanism
for a self-signed broker certificate, with `AllowUntrustedCertificates` kept
as a separate, explicitly-logged-as-insecure escape hatch (off by default).

**Why:** The local test broker's certificate (`certs/server.crt`, `CN=
localhost`) is self-signed - default OS certificate chain validation will
never accept it, but the correct fix is to trust that *specific*
certificate, not to disable validation entirely (`AllowUntrustedCertificates`
would accept a certificate from anything, including an on-path attacker on
the same LAN during local testing). Pinning by thumbprint is the standard
approach for a known self-signed leaf certificate. Verified live: a real
TLS 1.x connection over port 8883 to `192.168.1.28` succeeded with the
pinned certificate validated correctly.

## 2026-08-25 - Fixed: `ApplyRuntimeOverride` was silently corrupting the
committed `MqttBrokerSettings.asset`

**Decision:** `MqttManager` now clones `brokerSettings` into a runtime-only
`effectiveSettings` instance (`Instantiate(brokerSettings)`) in `Awake()`
and applies the StreamingAssets JSON override, credentials, and all
day-to-day operations onto that clone. The original `brokerSettings` asset
reference is now only ever *read* (for the latest-edit-wins mtime check),
never written to again after `Awake()`.

**Why: this was a real, live data-corruption bug, not a hypothetical one.**
Unlike GameObjects/components in a loaded scene, Unity does **not** revert
field changes made to a `ScriptableObject` *asset* during Play Mode when
Play stops - a mutation made via script persists in the shared asset
instance. `ApplyRuntimeOverride` was being called directly on `brokerSettings`
(the actual committed asset, loaded via `AssetDatabase.LoadAssetAtPath`),
and the latest-edit-wins check's `AssetDatabase.SaveAssetIfDirty(brokerSettings)`
call - added in good faith to capture unsaved Inspector edits - had the side
effect of also flushing those leaked runtime values to disk. Caught by
directly inspecting the committed `.asset` file after a Play session and
finding it contained `brokerHost: 192.168.1.28` and stale topics
(`qrScanned-biocon-house`, `visitorCreated-biocon-house`) from an *earlier*
version of `MqttConfig.json` - runtime state that had been silently baked
into source-controlled data. Restored the asset to placeholder defaults and
verified the fix by re-running a full connect+subscribe session and
confirming the asset file was byte-for-byte unchanged afterward. Recurred
once more from the same root cause (`AssetDatabase.SaveAssetIfDirty` still
present in the latest-edit-wins check) before being fully removed - see the
next decision.

## 2026-08-25 - Removed `AssetDatabase.SaveAssetIfDirty` from the
latest-edit-wins check entirely

**Decision:** `IsBrokerSettingsAssetNewerThanConfigFile()` now compares
`brokerSettings.asset`'s on-disk save time as-is against the config file's -
it no longer calls `AssetDatabase.SaveAssetIfDirty(brokerSettings)` first.

**Why:** The corruption above recurred a second time even after the
`effectiveSettings` clone fix, because this one remaining call could still
flush the committed asset to disk if `brokerSettings` was dirty for *any*
reason at the moment it ran - including, apparently, surviving a script
recompile that happened while Play Mode was still active from a previous
session (a domain reload mid-Play does not necessarily discard all state
the way stopping Play does). The convenience this call provided - catching
an unsaved Inspector edit before comparing timestamps - was not worth a
repeated, real risk of writing runtime data into committed source. Losing
that convenience (an *unsaved* Inspector edit no longer counts as "just
edited" until you explicitly save it) is the correct trade against ever
writing to this asset from runtime code again.

## 2026-08-25 - MQTT module sealed; new work goes in `Scripts/Lights/`

**Decision:** After the user confirmed live connection + topic delivery
worked, stopped further changes to `Scripts/Mqtt/` (including leaving a
known cosmetic layout overlap on `MqttConnectionPanelUI`'s form unfixed) and
started new "on-light"/"off-light" -> 4 scene lights logic in a separate
`Scripts/Lights/` folder instead, compiling into `Assembly-CSharp` (same
one-way compile-order pattern as `MqttUtilityLogBridge.cs` - it references
`MqttManager`/`MqttLog`, both public, without needing a new asmdef).

**Why:** Explicit user instruction ("seal the mqtt implementation for now
... custom project logic begins"). One exception was made: `MqttManager.
Subscribe(...)` had a real bug (see next decision) that would have silently
broken the very feature being built on top of it - fixed that one thing,
left everything else (including the cosmetic overlap) alone.

## 2026-08-25 - Fixed: `Subscribe()` before the first successful connect
silently never sent the broker-level SUBSCRIBE

**Decision:** `MqttManager` now tracks each topic filter's QoS in a
`subscriptionQualityOfService` dictionary and re-sends `SubscribeAsync` for
*every* currently-registered filter on *every* successful connect
(`ResubscribeAllTopics()`, called from `HandleClientConnected`) -
`EnsureBrokerSubscription` itself only sends `SubscribeAsync` immediately
when already connected; otherwise it just registers the filter locally and
lets the next connect's resubscribe pass send it.

**Why:** `MqttLightController.Update()` calls `manager.Subscribe(...)` as
soon as `MqttManager.Instance` exists - typically several frames before the
StreamingAssets config load and first `Connect()` resolve. The old
`EnsureBrokerSubscription` called `client.SubscribeAsync(...)` immediately
regardless of connection state, which `MqttClientService` correctly
rejected ("called while not connected") and then silently dropped - the
topic filter stayed registered locally (so `AutoSubscribeConfiguredTopics`
later saw it as "not new" and skipped it too), meaning the broker was never
actually told about it. Caught live: after connecting, no
"subscribed to..." success log ever appeared for `on-light`/`off-light`,
despite `MqttLightController` reporting a successful `Subscribe()` call.
This also fixes the same gap after any reconnect (`CleanSession` means the
broker forgets every subscription on a dropped connection) -
`ResubscribeAllTopics()` covers both cases with one mechanism. Verified via
direct dispatch simulation (bypassing the actual broker, which was
unreachable at verification time) that `on-light`/`off-light` correctly
drive individual lights by 1-based index.

## 2026-08-25 - Lights start inactive; only `LightsRoot` gates on connection

**Decision:** All 4 light `GameObject`s are created `SetActive(false)`
individually (not just their `LightsRoot` parent). `MqttLightController`
activates `LightsRoot` on `MqttManager.Connected`, but each light's own
on/off state is driven only by actual `on-light`/`off-light` messages.

**Why:** Without this, every light defaulted to "on" the instant the
connection succeeded (a `GameObject`'s own `activeSelf` doesn't
automatically become `false` just because an ancestor is inactive - it's
independent state that only affects *hierarchy* activity). Caught by
checking `Light2.activeSelf` immediately after a successful connect, before
any real message had arrived, and finding it `true`.

## 2026-08-25 - Fixed: `MqttConnectionPanelUI`'s form showed blank fields on
disconnect

**Decision:** `PopulateFieldsFromCurrentSettings()` now runs on `Bind()`
(best-effort) and again on every `Disconnected`/`ConnectionError` event,
instead of exactly once (guarded by a `fieldsPopulated` flag) the first time
the component ever ran `Update()`. Also added `MqttManager.LastUsername` so
the form can pre-fill the username (not the password - never redisplay a
secret back into a UI field once entered).

**Why:** The one-shot population ran before the StreamingAssets config load
finished (`BrokerSettings` still held Inspector placeholder defaults - all
empty - at that point), and never ran again, so the form showed blank
fields every time it reappeared on a later disconnect, regardless of what
the actual attempted connection settings were. Refreshing on the events
that actually signal "the form is about to matter again" fixes this
directly. Verified live: connected, disconnected, and confirmed every field
(host, port, TLS, username, client ID, cert path) showed the real values
that had just been used to connect.
