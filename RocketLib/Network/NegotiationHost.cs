using UnityEngine;

namespace RocketLib.Network
{
    internal sealed class NegotiationHost : MonoBehaviour
    {
        private static NegotiationHost instance;
        internal static void EnsureInstance()
        {
            if (instance != null) return;
            GameObject host = new GameObject("RocketLib Negotiation");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<NegotiationHost>();
        }
        private void LateUpdate()
        {
            ModNegotiation.TickTimeout();
            ModNegotiation.Flush();
        }
        private void OnDestroy() { instance = null; }
    }
}
