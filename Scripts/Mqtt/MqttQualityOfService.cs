namespace ViitorCloud.Games.DigitalTwinFuturotec.Mqtt {
    /// <summary> Mirrors the three MQTT quality-of-service levels defined by the MQTT 3.1.1/5.0 spec. </summary>
    public enum MqttQualityOfService {
        AtMostOnce = 0,
        AtLeastOnce = 1,
        ExactlyOnce = 2
    }
}
