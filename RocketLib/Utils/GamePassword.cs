using System;
using HarmonyLib;

namespace RocketLib
{
    /// <summary>
    /// Add password to the game
    /// </summary>
    public class GamePassword
    {
        public static GamePassword[] Passwords
        {
            get { return _passwords; }
        }
        private static readonly GamePassword[] _passwords = new GamePassword[0];

        public readonly string password = string.Empty;
        public readonly Action action;

        /// <summary>
        /// Create the game password.
        /// </summary>
        /// <param name="_password"></param>
        /// <param name="_action"></param>
        public GamePassword(string _password, Action _action)
        {
            password = _password.ToLower();
            action = _action;
            AddPassword(this);
        }

        /// <summary>
        /// Return password
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return password;
        }

        private static void AddPassword(GamePassword password)
        {
            _passwords.Append(password);
        }
    }

    [HarmonyPatch(typeof(MainMenu))]
    public class MainMenuPatch
    {
        [HarmonyPatch("ProcessCharacter")]
        [HarmonyPostfix]
        private static void CheckCustomPassword(MainMenu __instance)
        {
            foreach (GamePassword password in GamePassword.Passwords)
            {
                try
                {
                    if (__instance.CallMethod<bool>("CheckCheatString", new object[] { password.password }))
                    {
                        Sound sound7 = Sound.GetInstance();
                        sound7.PlaySoundEffect(__instance.drumSounds.specialSounds[0], 0.75f);
                        password.action?.Invoke();
                    }
                }
                catch (Exception ex)
                {
                    RocketMain.Logger.Exception($"Failed to check the password: {password.password}", ex);
                }
            }
        }
    }
}
