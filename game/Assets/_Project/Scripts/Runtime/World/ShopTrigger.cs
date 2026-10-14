using System;
using NightWorld.Data;
using UnityEngine;

namespace NightWorld.World
{
    /// <summary>
    /// Trigger volume around a shop. Raises events when the Player enters or leaves;
    /// the shop UI (a future card) listens to these.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ShopTrigger : MonoBehaviour
    {
        public static event Action<ShopTrigger> PlayerEntered;
        public static event Action<ShopTrigger> PlayerExited;

        public ShopDefinition shop;

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player")) PlayerEntered?.Invoke(this);
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag("Player")) PlayerExited?.Invoke(this);
        }
    }
}
