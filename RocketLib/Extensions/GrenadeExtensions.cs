using UnityEngine;

namespace RocketLib.Extensions
{
    public static class GrenadeExtensions
    {
        /// <summary>
        /// Prints the values that have been changed in this object from the default values for the Grenade class.
        /// </summary>
        /// <param name="grenade">Object to compare to the default grenade.</param>
        public static void CompareToDefaultGrenade(this Grenade grenade)
        {
            Grenade defaultGrenade = new GameObject("TemporaryGrenade", typeof(MeshFilter), typeof(MeshRenderer), typeof(SpriteSM), typeof(Grenade)).GetComponent<Grenade>();
            defaultGrenade.PrintDifferences(grenade);
            Object.Destroy(defaultGrenade.gameObject);
        }
    }
}
