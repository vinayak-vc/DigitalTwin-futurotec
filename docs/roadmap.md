# Roadmap

## MQTT integration - sealed (see below for active work)

- [x] `IMqttClientService` / `MqttClientService` (MQTTnet-backed, reconnect,
      TLS, guarded by `MQTTNET_ENABLED`).
- [x] `MqttBrokerSettings` config ScriptableObject (no stored credentials).
- [x] `MqttManager` bootstrap MonoBehaviour - main-thread dispatch, topic
      routing, pause/quit lifecycle.
- [x] `MqttTopicMatcher` (`+`/`#` wildcard matching).
- [x] Install MQTTnet 4.3.7.1207 via NuGetForUnity - verified compiling
      clean against the real assembly via live reflection.
- [x] `MqttBrokerSettings.asset` created + `MqttManager` wired onto
      `Boot.unity`.
- [x] `MqttTopicSubscription` + `MqttBrokerSettings.Topics` array -
      declarative topic/QoS/auto-subscribe config.
- [x] Runtime config override from
      `Assets/StreamingAssets/DigitalTwinFuturotec/MqttConfig.json` -
      `MqttRuntimeConfig`/`MqttRuntimeConfigLoader`, verified end-to-end in
      Play Mode.
- [x] Latest-edit-wins between `MqttBrokerSettings.asset` and
      `MqttConfig.json` (Editor-only), verified both directions live.
- [x] Fixed `MQTTNET_ENABLED` actually never being active (was silently
      running in stub mode) - switched to a Player Settings define symbol,
      verified via direct reflection this time, not just by inspecting the
      library.
- [x] WebSocket transport - the real configured broker only exposes MQTT
      over `wss://`, not TCP.
- [x] Real broker connection verified live: connect + auto-subscribe to
      both configured topics succeeded.
- [x] Logging routed through `Modules.Utility.Utility` (`MqttLog` +
      `MqttUtilityLogBridge.cs`).
- [x] `MqttDebugUI` - live on-screen connection/topic visualization panel,
      verified via Play Mode screenshots.
- [x] TLS certificate pinning (`TrustedCertificatePath`) +
      `AllowUntrustedCertificates` escape hatch - verified live against a
      local broker with a self-signed certificate.
- [x] Fixed a real data-corruption bug where runtime JSON config values
      were getting baked into the committed `MqttBrokerSettings.asset` -
      see decisions.md and ai_handoff.md before touching settings handling.
- [x] `MqttConnectionPanelUI` - Connect/Disconnect buttons + manual settings
      form, shown whenever not connected. Known cosmetic layout overlap
      left unfixed (low priority, module sealed - see decisions.md).
- [x] `MqttManager.autoConnectOnStart` + `saveConfigOnSuccessfulConnect`.
- [x] **User confirmed connection + topic delivery working - module sealed.**
- [x] Fixed `Subscribe()` never sending the broker-level SUBSCRIBE when
      called before the first connect (or after a reconnect) -
      `ResubscribeAllTopics()`.
- [ ] Fill in real credentials on a *deployed build's* copy of
      `MqttConfig.json` for any non-local broker (the committed config
      currently points at a local LAN test broker with test credentials).
- [ ] Typed topic/payload helpers once more device/twin topics are known
      (out of scope for the initial setup; the lights feature below is the
      first consumer).

## Active - custom project logic (Scripts/Lights/, etc.)

- [x] `MqttLightController` - `on-light`/`off-light` -> 4 scene lights by
      1-based index, `LightsRoot` gated on `MqttManager.Connected`.
      Verified via direct dispatch simulation (local broker unreachable at
      verification time).
- [ ] Confirm the lights respond to a *real* published message once the
      local broker is reachable again.

## Backlog / not started

- Consent/telemetry gating consistent with the base project's
  `AnalyticsManager.SetCollectionEnabled` pattern, if MQTT traffic is ever
  considered PII-sensitive for a given deployment.
- WebGL support, if this app ever needs it - current scope is Desktop/
  Mobile only (see decisions.md); WebSocket transport (now implemented) is
  actually the right transport for WebGL too, but WebGL builds have other
  constraints (no background threads for MQTTnet's internals) not evaluated
  here.
- Fix `MqttConnectionPanelUI`'s cosmetic layout overlap, if ever revisited.
