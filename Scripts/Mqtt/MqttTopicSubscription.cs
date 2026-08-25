using System;

using UnityEngine;

namespace ViitorCloud.Games.DigitalTwinFuturotec.Mqtt {
    /// <summary>
    /// One configured topic filter: what to subscribe to, at what QoS, and whether
    /// MqttManager should subscribe automatically as soon as it connects. Shown as
    /// an array element in the MqttBrokerSettings inspector, and produced from
    /// MqttRuntimeConfig when a StreamingAssets config file overrides it.
    /// </summary>
    [Serializable]
    public class MqttTopicSubscription {
        [SerializeField] private string topic = string.Empty;
        [SerializeField] private MqttQualityOfService qualityOfService = MqttQualityOfService.AtLeastOnce;
        [Tooltip("If true, MqttManager subscribes to this topic filter automatically once connected.")]
        [SerializeField] private bool subscribeOnConnect = true;

        public string Topic {
            get { return topic; }
        }

        public MqttQualityOfService QualityOfService {
            get { return qualityOfService; }
        }

        public bool SubscribeOnConnect {
            get { return subscribeOnConnect; }
        }

        public MqttTopicSubscription() {
        }

        public MqttTopicSubscription(string topic, MqttQualityOfService qualityOfService, bool subscribeOnConnect) {
            this.topic = topic;
            this.qualityOfService = qualityOfService;
            this.subscribeOnConnect = subscribeOnConnect;
        }
    }
}
